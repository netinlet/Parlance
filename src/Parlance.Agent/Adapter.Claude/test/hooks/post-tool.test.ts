import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const hook = fileURLToPath(new URL('../../dist/hooks/post-tool.js', import.meta.url));

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ac-post-'));
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

function run(stdin: string): number {
  try {
    execFileSync('node', [hook], { input: stdin, stdio: ['pipe', 'pipe', 'pipe'] });
    return 0;
  } catch (error) {
    return (error as { status?: number }).status ?? 1;
  }
}

describe('post-tool hook', () => {
  it('records post-read with native_fallback=true based on routing inference', () => {
    run(JSON.stringify({
      hook_event_name: 'PostToolUse',
      session_id: 's1',
      cwd: root,
      tool_name: 'Read',
      tool_input: { file_path: 'Foo.cs' },
      tool_response: { content: 'x'.repeat(3500) },
    }));

    const session = JSON.parse(readFileSync(join(root, '.parlance/_session.json'), 'utf8'));
    expect(session.tool_calls).toHaveLength(1);
    expect(session.tool_calls[0].is_native_fallback).toBe(true);
    expect(session.read_tokens).toBe(1000);
  });

  it('parlance MCP tool bumps parlance_calls', () => {
    run(JSON.stringify({
      hook_event_name: 'PostToolUse',
      session_id: 's1',
      cwd: root,
      tool_name: 'mcp__parlance__search-symbols',
      tool_input: { query: 'x' },
      tool_response: { content: '...' },
    }));

    const session = JSON.parse(readFileSync(join(root, '.parlance/_session.json'), 'utf8'));
    expect(session.parlance_calls).toBe(1);
  });
});
