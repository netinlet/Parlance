import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { execFileSync } from 'node:child_process';
import { appendFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const hook = fileURLToPath(new URL('../../dist/hooks/user-prompt-submit.js', import.meta.url));

let root: string;
let transcript: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ac-ups-'));
  transcript = join(root, 'transcript.jsonl');
  writeFileSync(transcript, '');
  mkdirSync(join(root, '.parlance'), { recursive: true });
  writeFileSync(join(root, '.parlance/_session.json'), JSON.stringify({
    session_id: 's1',
    adapter: 'claude-code',
    started_at: '2026-04-22T14:30:00Z',
    cwd: root,
    transcript_ref: transcript,
    parlance_calls: 0,
    native_fallbacks: 0,
    tool_calls: [],
    read_tokens: 0,
    write_tokens: 0,
    active_bench: null,
  }));
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

function run(prompt: string): void {
  execFileSync('node', [hook], {
    input: JSON.stringify({
      hook_event_name: 'UserPromptSubmit',
      session_id: 's1',
      cwd: root,
      prompt,
      transcript_path: transcript,
    }),
    stdio: ['pipe', 'pipe', 'pipe'],
  });
}

function appendUsageRecord(): void {
  appendFileSync(transcript, `${JSON.stringify({
    type: 'assistant',
    timestamp: new Date().toISOString(),
    message: {
      usage: {
        input_tokens: 1200,
        output_tokens: 400,
        cache_creation_input_tokens: 0,
        cache_read_input_tokens: 0,
      },
    },
  })}\n`);
}

describe('user-prompt-submit hook', () => {
  it('/parlance bench start sets active_bench', () => {
    run('/parlance bench start taskA grep');

    const state = JSON.parse(readFileSync(join(root, '.parlance/_session.json'), 'utf8'));
    expect(state.active_bench?.task_id).toBe('taskA');
    expect(state.active_bench?.variant).toBe('grep');
  });

  it('/parlance bench end writes a bench result with computed usage', () => {
    run('/parlance bench start taskA grep');
    appendUsageRecord();
    run('/parlance bench end');

    const lines = readFileSync(join(root, '.parlance/bench/results.jsonl'), 'utf8').trim().split('\n');
    expect(lines).toHaveLength(1);
    const record = JSON.parse(lines[0]);
    expect(record.task_id).toBe('taskA');
    expect(record.variant).toBe('grep');
    expect(record.adapter).toBe('claude-code');
    expect(record.usage.input_tokens).toBe(1200);

    const state = JSON.parse(readFileSync(join(root, '.parlance/_session.json'), 'utf8'));
    expect(state.active_bench).toBeNull();
  });

  it('non-parlance prompt is ignored', () => {
    run('just write some code please');

    const state = JSON.parse(readFileSync(join(root, '.parlance/_session.json'), 'utf8'));
    expect(state.active_bench).toBeNull();
    expect(existsSync(join(root, '.parlance/bench/results.jsonl'))).toBe(false);
  });
});
