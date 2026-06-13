import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { runInstall, withHooksFeature } from '../../src/commands/install.js';

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

  it('preserves foreign hooks without command fields', async () => {
    mkdirSync(join(root, '.codex'), { recursive: true });
    writeFileSync(join(root, '.codex/hooks.json'), JSON.stringify({
      hooks: {
        PreToolUse: [{
          matcher: 'Bash',
          hooks: [{ type: 'webhook', url: 'https://example.test/hook' }],
        }],
      },
    }));

    expect(await runInstall(['--project', root, '--solution', 'App.sln'])).toBe(0);

    const settings = JSON.parse(readFileSync(join(root, '.codex/hooks.json'), 'utf8'));
    const pre = settings.hooks.PreToolUse as { hooks: { type: string; url?: string }[] }[];
    expect(pre.some((entry) => entry.hooks.some((hook) => hook.type === 'webhook' && hook.url === 'https://example.test/hook'))).toBe(true);
  });

  it('writes Codex MCP setup guidance using the solution path', async () => {
    await runInstall(['--project', root, '--solution', 'App.sln']);

    const body = readFileSync(join(root, '.parlance/codex/mcp-setup.md'), 'utf8');
    expect(body).toContain('codex mcp add parlance -- parlance mcp --solution-path');
    expect(body).toContain(join(root, 'App.sln'));
  });

  it('uses --mcp-command in Codex MCP setup guidance', async () => {
    await runInstall(['--project', root, '--solution', 'App.sln', '--mcp-command', '/opt/parlance/bin/parlance']);

    const body = readFileSync(join(root, '.parlance/codex/mcp-setup.md'), 'utf8');
    expect(body).toContain('codex mcp add parlance -- /opt/parlance/bin/parlance mcp --solution-path');
  });

  it('enables hooks without clobbering existing config', async () => {
    mkdirSync(join(root, '.codex'), { recursive: true });
    writeFileSync(join(root, '.codex/config.toml'), 'model = "gpt-5.4"\n\n[features]\nfoo = true\n');

    await runInstall(['--project', root, '--solution', 'App.sln']);

    const body = readFileSync(join(root, '.codex/config.toml'), 'utf8');
    expect(body).toContain('model = "gpt-5.4"');
    expect(body).toContain('[features]\nhooks = true\nfoo = true');
  });

  it('fails if .codex exists as a file', async () => {
    writeFileSync(join(root, '.codex'), '');

    expect(await runInstall(['--project', root, '--solution', 'App.sln'])).toBe(1);
    expect(existsSync(join(root, '.codex/hooks.json'))).toBe(false);
  });
});

describe('install --global', () => {
  const orig = { home: process.env.PARLANCE_HOME, codex: process.env.CODEX_CONFIG_DIR };

  afterEach(() => {
    for (const [key, value] of [['PARLANCE_HOME', orig.home], ['CODEX_CONFIG_DIR', orig.codex]] as const) {
      if (value === undefined) delete process.env[key];
      else process.env[key] = value;
    }
  });

  function withDirs(): { configToml: string; hooksJson: string; nudge: string } {
    process.env.PARLANCE_HOME = join(root, 'home');
    process.env.CODEX_CONFIG_DIR = join(root, 'codex');
    return {
      configToml: join(root, 'codex/config.toml'),
      hooksJson: join(root, 'codex/hooks.json'),
      nudge: join(root, 'home/hooks/nudge.js'),
    };
  }

  it('copies the nudge bundle and wires a nudge-only SessionStart hook', async () => {
    const { hooksJson, nudge } = withDirs();

    expect(await runInstall(['--global'])).toBe(0);
    expect(existsSync(nudge)).toBe(true);

    const parsed = JSON.parse(readFileSync(hooksJson, 'utf8'));
    const sessionStart = parsed.hooks.SessionStart as { hooks: { command: string; statusMessage?: string }[] }[];
    expect(sessionStart.some((entry) => entry.hooks.some((hook) => hook.command.includes('hooks/nudge.js')))).toBe(true);
    expect(sessionStart.some((entry) => entry.hooks.some((hook) => hook.statusMessage === 'Checking Parlance setup'))).toBe(true);
    // global never wires the per-project tracking hooks
    expect(JSON.stringify(parsed)).not.toContain('pre-tool.js');
    expect(JSON.stringify(parsed)).not.toContain('stop.js');
  });

  it('enables Codex hooks in the global config', async () => {
    const { configToml } = withDirs();

    expect(await runInstall(['--global'])).toBe(0);

    expect(readFileSync(configToml, 'utf8')).toContain('[features]\nhooks = true');
  });

  it('is idempotent and preserves foreign SessionStart hooks', async () => {
    const { hooksJson } = withDirs();
    mkdirSync(dirname(hooksJson), { recursive: true });
    writeFileSync(hooksJson, JSON.stringify({
      hooks: { SessionStart: [{ hooks: [{ type: 'command', command: 'echo foreign' }] }] },
    }));

    await runInstall(['--global']);
    await runInstall(['--global']);

    const parsed = JSON.parse(readFileSync(hooksJson, 'utf8'));
    const sessionStart = parsed.hooks.SessionStart as { hooks: { command: string }[] }[];
    expect(sessionStart.some((entry) => entry.hooks.some((hook) => hook.command === 'echo foreign'))).toBe(true);
    expect(sessionStart.filter((entry) => entry.hooks.some((hook) => hook.command.includes('hooks/nudge.js')))).toHaveLength(1);
  });
});

describe('withHooksFeature', () => {
  it('adds features section when missing', () => {
    expect(withHooksFeature('model = "x"\n')).toBe('model = "x"\n\n[features]\nhooks = true\n');
  });

  it('replaces existing hooks value', () => {
    expect(withHooksFeature('[features]\nhooks = false\n')).toBe('[features]\nhooks = true\n');
  });

  it('migrates existing codex_hooks alias to hooks', () => {
    expect(withHooksFeature('[features]\ncodex_hooks = false\n')).toBe('[features]\nhooks = true\n');
  });
});
