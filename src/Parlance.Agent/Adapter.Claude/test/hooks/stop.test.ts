import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const hook = fileURLToPath(new URL('../../dist/hooks/stop.js', import.meta.url));

let root: string;
let transcript: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ac-stop-'));
  transcript = join(root, 'transcript.jsonl');
  writeFileSync(transcript, [
    JSON.stringify({ type: 'system', timestamp: '2026-04-22T14:29:00Z', gitBranch: 'main' }),
    JSON.stringify({ type: 'assistant', timestamp: '2026-04-22T14:30:00Z', message: { usage: { input_tokens: 1000, output_tokens: 250, cache_creation_input_tokens: 0, cache_read_input_tokens: 5000 } } }),
  ].join('\n'));
  mkdirSync(join(root, '.parlance'), { recursive: true });
  writeFileSync(join(root, '.parlance/_session.json'), JSON.stringify({
    session_id: 's1',
    adapter: 'claude-code',
    started_at: '2026-04-22T14:29:30Z',
    cwd: root,
    transcript_ref: transcript,
    parlance_calls: 3,
    native_fallbacks: 1,
    tool_calls: [],
    read_tokens: 200,
    write_tokens: 0,
    active_bench: null,
  }));
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

function run(stdin: string): number {
  try {
    execFileSync('node', [hook], { input: stdin, stdio: ['pipe', 'pipe', 'pipe'] });
    return 0;
  } catch (error) {
    return (error as { status?: number }).status ?? 1;
  }
}

describe('stop hook', () => {
  it('writes a ledger entry with transcript-derived usage', () => {
    run(JSON.stringify({
      hook_event_name: 'Stop',
      session_id: 's1',
      cwd: root,
      transcript_path: transcript,
    }));

    const lines = readFileSync(join(root, '.parlance/ledger.jsonl'), 'utf8').trim().split('\n');
    expect(lines).toHaveLength(1);
    const summary = JSON.parse(lines[0]);
    expect(summary.branch).toBe('main');
    expect(summary.usage.input_tokens).toBe(1000);
    expect(summary.parlance_calls).toBe(3);
  });

  it('appends a session log line', () => {
    run(JSON.stringify({
      hook_event_name: 'Stop',
      session_id: 's1',
      cwd: root,
      transcript_path: transcript,
    }));

    const body = readFileSync(join(root, '.parlance/session-log.md'), 'utf8');
    expect(body).toContain('claude-code');
    expect(body).toContain('3 Parlance');
  });
});
