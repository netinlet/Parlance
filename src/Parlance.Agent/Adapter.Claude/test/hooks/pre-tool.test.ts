import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const hook = fileURLToPath(new URL('../../dist/hooks/pre-tool.js', import.meta.url));

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ac-pre-'));
  mkdirSync(join(root, '.parlance'), { recursive: true });
  writeFileSync(join(root, '.parlance/_session.json'), JSON.stringify({
    session_id: 's1',
    adapter: 'claude-code',
    started_at: new Date().toISOString(),
    cwd: root,
    transcript_ref: null,
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

function run(stdin: string): { status: number; stderr: string } {
  try {
    const stderr = execFileSync('node', [hook], { input: stdin, stdio: ['pipe', 'pipe', 'pipe'] });
    return { status: 0, stderr: String(stderr) };
  } catch (error) {
    return {
      status: (error as { status?: number }).status ?? 1,
      stderr: String((error as { stderr?: Buffer }).stderr ?? ''),
    };
  }
}

describe('pre-tool hook', () => {
  it('Read on .cs writes kibble (counter increments on post-tool, not pre-tool)', () => {
    run(JSON.stringify({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: root,
      tool_name: 'Read',
      tool_input: { file_path: '/proj/Foo.cs' },
    }));

    const session = JSON.parse(readFileSync(join(root, '.parlance/_session.json'), 'utf8'));
    expect(session.native_fallbacks).toBe(0);
    const dirs = readdirSync(join(root, '.parlance/kibble'));
    expect(dirs.length).toBe(1);
  });

  it('Read on .md does not warn', () => {
    run(JSON.stringify({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: root,
      tool_name: 'Read',
      tool_input: { file_path: 'README.md' },
    }));

    const session = JSON.parse(readFileSync(join(root, '.parlance/_session.json'), 'utf8'));
    expect(session.native_fallbacks).toBe(0);
  });
});
