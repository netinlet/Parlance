import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const hook = fileURLToPath(new URL('../../dist/hooks/nudge.js', import.meta.url));

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ac-codex-nudge-'));
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

function copyHookBundle(): void {
  mkdirSync(join(root, '.parlance', 'hooks'), { recursive: true });
  writeFileSync(join(root, '.parlance', 'hooks', 'session-start.js'), '');
}

function wireCodexHooks(): void {
  mkdirSync(join(root, '.codex'), { recursive: true });
  writeFileSync(join(root, '.codex/hooks.json'), JSON.stringify({
    hooks: {
      SessionStart: [{
        hooks: [{ type: 'command', command: 'node "$(git rev-parse --show-toplevel)/.parlance/hooks/session-start.js"' }],
      }],
    },
  }));
  copyHookBundle();
}

describe('global codex nudge hook', () => {
  it('C# project without Codex hooks: reminds to install', () => {
    writeFileSync(join(root, 'Widgets.slnx'), '');
    const payload = JSON.parse(run());
    expect(payload.hookSpecificOutput.additionalContext).toContain('parlance agent install --solution Widgets.slnx');
  });

  it('C# project with Codex hooks: stays silent', () => {
    writeFileSync(join(root, 'Widgets.slnx'), '');
    wireCodexHooks();
    expect(run().trim()).toBe('');
  });

  it('C# project with stale copied hook bundle but no Codex hooks: still nudges', () => {
    writeFileSync(join(root, 'Widgets.slnx'), '');
    copyHookBundle();
    const payload = JSON.parse(run());
    expect(payload.hookSpecificOutput.additionalContext).toContain('parlance agent install --solution Widgets.slnx');
  });

  it('C# project with Claude .mcp.json but no Codex hooks: still nudges', () => {
    writeFileSync(join(root, 'Widgets.slnx'), '');
    writeFileSync(join(root, '.mcp.json'), JSON.stringify({ mcpServers: { parlance: { command: 'parlance' } } }));
    const payload = JSON.parse(run());
    expect(payload.hookSpecificOutput.additionalContext).toContain('parlance agent install --solution Widgets.slnx');
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
