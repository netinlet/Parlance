import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runInstall } from '../../src/commands/install.js';
import { runUninstall } from '../../src/commands/uninstall.js';

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'codex-uninstall-'));
  writeFileSync(join(root, 'App.sln'), 'dummy');
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe('uninstall command', () => {
  it('removes only Parlance hook entries from hooks.json', async () => {
    await runInstall(['--project', root, '--solution', 'App.sln']);

    const hooksPath = join(root, '.codex/hooks.json');
    const hooks = JSON.parse(readFileSync(hooksPath, 'utf8'));
    hooks.hooks.PreToolUse.unshift({
      matcher: 'Bash',
      hooks: [{ type: 'command', command: 'echo hi' }],
    });
    writeFileSync(hooksPath, JSON.stringify(hooks, null, 2));

    await runUninstall(['--project', root]);

    const settings = JSON.parse(readFileSync(hooksPath, 'utf8'));
    for (const event of Object.keys(settings.hooks ?? {})) {
      for (const matcher of settings.hooks[event]) {
        expect(matcher.hooks.some((hook: { command?: string }) => hook.command?.includes('.parlance/hooks/'))).toBe(false);
      }
    }
    expect(settings.hooks.PreToolUse.some((entry: { hooks: { command?: string }[] }) => entry.hooks.some((hook) => hook.command === 'echo hi'))).toBe(true);
    expect(existsSync(join(root, '.parlance/codex/events'))).toBe(true);
  });

  it('--purge removes .parlance', async () => {
    await runInstall(['--project', root, '--solution', 'App.sln']);
    await runUninstall(['--project', root, '--purge']);

    expect(existsSync(join(root, '.parlance'))).toBe(false);
  });

  it('leaves config.toml intact', async () => {
    await runInstall(['--project', root, '--solution', 'App.sln']);
    await runUninstall(['--project', root]);

    expect(existsSync(join(root, '.codex/config.toml'))).toBe(true);
  });
});
