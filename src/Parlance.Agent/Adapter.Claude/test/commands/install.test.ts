import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
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
    expect(await runInstall(['--project', root, '--solution', 'App.sln'])).toBe(0);
    expect(existsSync(join(root, '.claude/settings.local.json'))).toBe(true);
    expect(existsSync(join(root, '.mcp.json'))).toBe(true);
    expect(existsSync(join(root, '.parlance/hooks/session-start.js'))).toBe(true);
    expect(existsSync(join(root, '.parlance/tool-routing.md'))).toBe(true);
  });

  it('preserves foreign hooks and is idempotent', async () => {
    mkdirSync(join(root, '.claude'), { recursive: true });
    writeFileSync(join(root, '.claude/settings.local.json'), JSON.stringify({
      hooks: {
        PreToolUse: [{
          matcher: 'Bash',
          hooks: [{ type: 'command', command: 'echo hi' }],
        }],
      },
    }));

    await runInstall(['--project', root, '--solution', 'App.sln']);
    await runInstall(['--project', root, '--solution', 'App.sln']);

    const settings = JSON.parse(readFileSync(join(root, '.claude/settings.local.json'), 'utf8'));
    const pre = settings.hooks.PreToolUse as { matcher: string; hooks: { command?: string }[] }[];
    expect(pre.some((matcher) => matcher.matcher === 'Bash')).toBe(true);
    expect(pre.filter((matcher) => matcher.hooks.some((hook) => hook.command?.includes('.parlance/hooks/pre-tool.js')))).toHaveLength(1);
  });

  it('CLAUDE.md snippet appended once', async () => {
    writeFileSync(join(root, 'CLAUDE.md'), '# Project\n\nBody.\n');

    await runInstall(['--project', root, '--solution', 'App.sln']);
    await runInstall(['--project', root, '--solution', 'App.sln']);

    const body = readFileSync(join(root, 'CLAUDE.md'), 'utf8');
    expect(body).toContain('Body.');
    expect((body.match(/<!-- parlance-agent:begin -->/g) ?? []).length).toBe(1);
  });
});
