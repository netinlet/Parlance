import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const hook = fileURLToPath(new URL('../../dist/hooks/nudge.js', import.meta.url));

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ac-nudge-'));
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

function run(): string {
  const stdout = execFileSync('node', [hook], {
    input: JSON.stringify({ hook_event_name: 'SessionStart', session_id: 'g', cwd: root }),
    stdio: ['pipe', 'pipe', 'pipe'],
  });
  return stdout.toString('utf8');
}

describe('global nudge hook', () => {
  it('C# project without the MCP wired: reminds to install', () => {
    writeFileSync(join(root, 'Widgets.slnx'), '');
    const payload = JSON.parse(run());
    expect(payload.hookSpecificOutput.additionalContext).toContain('parlance agent install --solution Widgets.slnx');
  });

  it('wired project: stays silent (per-project hooks own it)', () => {
    writeFileSync(join(root, '.mcp.json'), JSON.stringify({ mcpServers: { parlance: { command: 'parlance' } } }));
    expect(run().trim()).toBe('');
  });

  it('non-C# project: stays silent', () => {
    writeFileSync(join(root, 'package.json'), '{}');
    expect(run().trim()).toBe('');
  });

  it('never writes session state', () => {
    writeFileSync(join(root, 'Widgets.slnx'), '');
    run();
    expect(existsSync(join(root, '.parlance/_session.json'))).toBe(false);
  });
});
