import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { runInstall } from '../../src/commands/install.js';

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'ac-install-'));
  writeFileSync(join(root, 'App.sln'), 'dummy');
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe('install command', () => {
  it('creates .claude/settings.local.json, .mcp.json, .parlance/hooks/', async () => {
    expect(await runInstall(['--project', root, '--solution', 'App.sln'])).toBe(
      0,
    );
    expect(existsSync(join(root, '.claude/settings.local.json'))).toBe(true);
    expect(existsSync(join(root, '.mcp.json'))).toBe(true);
    expect(existsSync(join(root, '.parlance/hooks/session-start.js'))).toBe(
      true,
    );
    expect(existsSync(join(root, '.parlance/tool-routing.md'))).toBe(true);
  });

  it('preserves foreign hooks and is idempotent', async () => {
    mkdirSync(join(root, '.claude'), { recursive: true });
    writeFileSync(
      join(root, '.claude/settings.local.json'),
      JSON.stringify({
        hooks: {
          PreToolUse: [
            {
              matcher: 'Bash',
              hooks: [{ type: 'command', command: 'echo hi' }],
            },
          ],
        },
      }),
    );

    await runInstall(['--project', root, '--solution', 'App.sln']);
    await runInstall(['--project', root, '--solution', 'App.sln']);

    const settings = JSON.parse(
      readFileSync(join(root, '.claude/settings.local.json'), 'utf8'),
    );
    const pre = settings.hooks.PreToolUse as {
      matcher: string;
      hooks: { command?: string }[];
    }[];
    expect(pre.some((matcher) => matcher.matcher === 'Bash')).toBe(true);
    expect(
      pre.filter((matcher) =>
        matcher.hooks.some((hook) =>
          hook.command?.includes('.parlance/hooks/pre-tool.js'),
        ),
      ),
    ).toHaveLength(1);
  });

  it('does not modify an existing CLAUDE.md', async () => {
    const original = '# Project\n\nBody.\n';
    writeFileSync(join(root, 'CLAUDE.md'), original);

    await runInstall(['--project', root, '--solution', 'App.sln']);
    await runInstall(['--project', root, '--solution', 'App.sln']);

    const body = readFileSync(join(root, 'CLAUDE.md'), 'utf8');
    expect(body).toBe(original);
  });

  it('does not create CLAUDE.md when missing', async () => {
    await runInstall(['--project', root, '--solution', 'App.sln']);

    expect(existsSync(join(root, 'CLAUDE.md'))).toBe(false);
  });
});

describe('install --global', () => {
  const orig = {
    home: process.env.PARLANCE_HOME,
    claude: process.env.CLAUDE_CONFIG_DIR,
  };

  afterEach(() => {
    for (const [key, value] of [
      ['PARLANCE_HOME', orig.home],
      ['CLAUDE_CONFIG_DIR', orig.claude],
    ] as const) {
      if (value === undefined) delete process.env[key];
      else process.env[key] = value;
    }
  });

  function withDirs(): { settings: string; nudge: string } {
    process.env.PARLANCE_HOME = join(root, 'home');
    process.env.CLAUDE_CONFIG_DIR = join(root, 'claude');
    return {
      settings: join(root, 'claude/settings.json'),
      nudge: join(root, 'home/hooks/nudge.js'),
    };
  }

  it('copies the nudge bundle and wires a nudge-only SessionStart hook', async () => {
    const { settings, nudge } = withDirs();

    expect(await runInstall(['--global'])).toBe(0);
    expect(existsSync(nudge)).toBe(true);

    const parsed = JSON.parse(readFileSync(settings, 'utf8'));
    const sessionStart = parsed.hooks.SessionStart as {
      hooks: { command: string }[];
    }[];
    expect(
      sessionStart.some((entry) =>
        entry.hooks.some((hook) => hook.command.includes('hooks/nudge.js')),
      ),
    ).toBe(true);
    // global never wires the per-project tracking hooks
    expect(JSON.stringify(parsed)).not.toContain('pre-tool.js');
    expect(JSON.stringify(parsed)).not.toContain('stop.js');
  });

  it('is idempotent and preserves foreign SessionStart hooks', async () => {
    const { settings } = withDirs();
    mkdirSync(dirname(settings), { recursive: true });
    writeFileSync(
      settings,
      JSON.stringify({
        hooks: {
          SessionStart: [
            {
              matcher: '',
              hooks: [{ type: 'command', command: 'echo foreign' }],
            },
          ],
        },
      }),
    );

    await runInstall(['--global']);
    await runInstall(['--global']);

    const parsed = JSON.parse(readFileSync(settings, 'utf8'));
    const sessionStart = parsed.hooks.SessionStart as {
      hooks: { command: string }[];
    }[];
    expect(
      sessionStart.some((entry) =>
        entry.hooks.some((hook) => hook.command === 'echo foreign'),
      ),
    ).toBe(true);
    expect(
      sessionStart.filter((entry) =>
        entry.hooks.some((hook) => hook.command.includes('hooks/nudge.js')),
      ),
    ).toHaveLength(1);
  });
});
