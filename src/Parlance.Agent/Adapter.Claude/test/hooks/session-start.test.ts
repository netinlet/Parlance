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

function run(stdin: string): { status: number; stdout: string } {
  try {
    const stdout = execFileSync('node', [hook], { input: stdin, stdio: ['pipe', 'pipe', 'pipe'] });
    return { status: 0, stdout: stdout.toString('utf8') };
  } catch (error) {
    return { status: (error as { status?: number }).status ?? 1, stdout: '' };
  }
}

const envelope = JSON.stringify({
  hook_event_name: 'SessionStart',
  session_id: 'abc',
  cwd: '__ROOT__',
  transcript_path: '/t',
});

describe('session-start hook', () => {
  it('creates _session.json with adapter=claude-code', () => {
    const { status } = run(envelope.replace('__ROOT__', root));

    expect(status).toBe(0);
    expect(existsSync(join(root, '.parlance/_session.json'))).toBe(true);
    const doc = JSON.parse(readFileSync(join(root, '.parlance/_session.json'), 'utf8'));
    expect(doc.session_id).toBe('abc');
    expect(doc.adapter).toBe('claude-code');
  });

  it('injects SessionStart additionalContext priming Parlance tool-first routing', () => {
    const { status, stdout } = run(envelope.replace('__ROOT__', root));

    expect(status).toBe(0);
    const payload = JSON.parse(stdout);
    expect(payload.hookSpecificOutput.hookEventName).toBe('SessionStart');
    expect(payload.hookSpecificOutput.additionalContext).toContain('Prefer them over native');
    expect(payload.hookSpecificOutput.additionalContext).toContain('mcp__parlance__describe-type');
  });
});
