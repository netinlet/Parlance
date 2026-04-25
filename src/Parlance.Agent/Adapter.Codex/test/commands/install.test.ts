import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runInstall, withCodexHooksFeature } from '../../src/commands/install.js';

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'codex-install-'));
  writeFileSync(join(root, 'App.sln'), 'dummy');
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe('install command', () => {
  it('creates .codex config, hooks, .parlance hooks, routing doc, and codex event dir', async () => {
    expect(await runInstall(['--project', root, '--solution', 'App.sln'])).toBe(0);

    expect(existsSync(join(root, '.codex/hooks.json'))).toBe(true);
    expect(existsSync(join(root, '.codex/config.toml'))).toBe(true);
    expect(existsSync(join(root, '.parlance/hooks/session-start.js'))).toBe(true);
    expect(existsSync(join(root, '.parlance/codex/events'))).toBe(true);
    expect(existsSync(join(root, '.parlance/tool-routing.md'))).toBe(true);
  });

  it('preserves foreign hooks and is idempotent', async () => {
    mkdirSync(join(root, '.codex'), { recursive: true });
    writeFileSync(join(root, '.codex/hooks.json'), JSON.stringify({
      hooks: {
        PreToolUse: [{
          matcher: 'Bash',
          hooks: [{ type: 'command', command: 'echo hi' }],
        }],
      },
    }));

    await runInstall(['--project', root, '--solution', 'App.sln']);
    await runInstall(['--project', root, '--solution', 'App.sln']);

    const settings = JSON.parse(readFileSync(join(root, '.codex/hooks.json'), 'utf8'));
    const pre = settings.hooks.PreToolUse as { matcher: string; hooks: { command?: string }[] }[];
    expect(pre.some((entry) => entry.hooks.some((hook) => hook.command === 'echo hi'))).toBe(true);
    expect(pre.filter((entry) => entry.hooks.some((hook) => hook.command?.includes('.parlance/hooks/pre-tool.js')))).toHaveLength(1);
  });

  it('enables codex_hooks without clobbering existing config', async () => {
    mkdirSync(join(root, '.codex'), { recursive: true });
    writeFileSync(join(root, '.codex/config.toml'), 'model = "gpt-5.4"\n\n[features]\nfoo = true\n');

    await runInstall(['--project', root, '--solution', 'App.sln']);

    const body = readFileSync(join(root, '.codex/config.toml'), 'utf8');
    expect(body).toContain('model = "gpt-5.4"');
    expect(body).toContain('[features]\ncodex_hooks = true\nfoo = true');
  });

  it('fails if .codex exists as a file', async () => {
    writeFileSync(join(root, '.codex'), '');

    expect(await runInstall(['--project', root, '--solution', 'App.sln'])).toBe(1);
    expect(existsSync(join(root, '.codex/hooks.json'))).toBe(false);
  });
});

describe('withCodexHooksFeature', () => {
  it('adds features section when missing', () => {
    expect(withCodexHooksFeature('model = "x"\n')).toBe('model = "x"\n\n[features]\ncodex_hooks = true\n');
  });

  it('replaces existing codex_hooks value', () => {
    expect(withCodexHooksFeature('[features]\ncodex_hooks = false\n')).toBe('[features]\ncodex_hooks = true\n');
  });
});
