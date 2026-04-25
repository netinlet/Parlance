import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const hook = fileURLToPath(new URL('../../dist/hooks/session-start.js', import.meta.url));

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ac-ss-'));
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

describe('session-start hook', () => {
  it('creates _session.json with adapter=claude-code', () => {
    const status = run(JSON.stringify({
      hook_event_name: 'SessionStart',
      session_id: 'abc',
      cwd: root,
      transcript_path: '/t',
    }));

    expect(status).toBe(0);
    expect(existsSync(join(root, '.parlance/_session.json'))).toBe(true);
    const doc = JSON.parse(readFileSync(join(root, '.parlance/_session.json'), 'utf8'));
    expect(doc.session_id).toBe('abc');
    expect(doc.adapter).toBe('claude-code');
  });
});
