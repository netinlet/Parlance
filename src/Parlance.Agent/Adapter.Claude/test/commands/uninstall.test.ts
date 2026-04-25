import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runInstall } from '../../src/commands/install.js';
import { runUninstall } from '../../src/commands/uninstall.js';

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ac-uninst-'));
  writeFileSync(join(root, 'App.sln'), 'x');
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe('uninstall', () => {
  it('removes settings.local.json hook entries and CLAUDE.md snippet', async () => {
    await runInstall(['--project', root, '--solution', 'App.sln']);
    await runUninstall(['--project', root]);

    const settings = JSON.parse(readFileSync(join(root, '.claude/settings.local.json'), 'utf8'));
    for (const event of Object.keys(settings.hooks ?? {})) {
      for (const matcher of settings.hooks[event]) {
        expect(matcher.hooks.some((hook: { command?: string }) => hook.command?.includes('.parlance/hooks/'))).toBe(false);
      }
    }

    const body = readFileSync(join(root, 'CLAUDE.md'), 'utf8');
    expect(body).not.toContain('parlance-agent:begin');
  });

  it('--purge removes .parlance/', async () => {
    await runInstall(['--project', root, '--solution', 'App.sln']);
    await runUninstall(['--project', root, '--purge']);

    expect(existsSync(join(root, '.parlance'))).toBe(false);
  });
});
