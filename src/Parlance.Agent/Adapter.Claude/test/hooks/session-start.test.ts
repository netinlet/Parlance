import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const hook = fileURLToPath(new URL('../../dist/hooks/session-start.js', import.meta.url));

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ac-ss-'));
});

const wire = () => writeFileSync(join(root, '.mcp.json'), JSON.stringify({ mcpServers: { parlance: { command: 'parlance' } } }));

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
  it('wired project: creates _session.json and primes tool-first routing', () => {
    wire();
    const { status, stdout } = run(envelope.replace('__ROOT__', root));

    expect(status).toBe(0);
    expect(existsSync(join(root, '.parlance/_session.json'))).toBe(true);
    const doc = JSON.parse(readFileSync(join(root, '.parlance/_session.json'), 'utf8'));
    expect(doc.session_id).toBe('abc');
    expect(doc.adapter).toBe('claude-code');

    const payload = JSON.parse(stdout);
    expect(payload.hookSpecificOutput.hookEventName).toBe('SessionStart');
    expect(payload.hookSpecificOutput.additionalContext).toContain('Prefer them over native');
    expect(payload.hookSpecificOutput.additionalContext).toContain('mcp__parlance__describe-type');
  });

  it('C# project without the MCP wired: reminds to install, writes no state', () => {
    writeFileSync(join(root, 'Widgets.slnx'), '');
    const { status, stdout } = run(envelope.replace('__ROOT__', root));

    expect(status).toBe(0);
    expect(existsSync(join(root, '.parlance/_session.json'))).toBe(false);
    const payload = JSON.parse(stdout);
    expect(payload.hookSpecificOutput.additionalContext).toContain('parlance agent install --solution Widgets.slnx');
  });

  it('non-C# project: stays silent and writes nothing', () => {
    writeFileSync(join(root, 'package.json'), '{}');
    const { status, stdout } = run(envelope.replace('__ROOT__', root));

    expect(status).toBe(0);
    expect(existsSync(join(root, '.parlance/_session.json'))).toBe(false);
    expect(stdout.trim()).toBe('');
  });
});
