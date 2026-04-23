import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { parlanceDir } from '@parlance/agent-core/storage/paths.js';

const MARKER_BEGIN = '<!-- parlance-agent:begin -->';
const MARKER_END = '<!-- parlance-agent:end -->';
const HOOK_MARKER = '.parlance/hooks/';

export async function runUninstall(argv: string[]): Promise<number> {
  let project = process.cwd();
  let purge = false;
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === '--project' && argv[index + 1]) project = argv[index + 1];
    if (argv[index] === '--purge') purge = true;
  }

  const root = resolve(project);
  const settingsPath = join(root, '.claude/settings.json');
  if (existsSync(settingsPath)) {
    const settings = JSON.parse(readFileSync(settingsPath, 'utf8')) as Record<string, unknown>;
    const hooks = settings.hooks as Record<string, { hooks: { command?: string }[] }[]> | undefined;
    if (hooks) {
      for (const key of Object.keys(hooks)) {
        hooks[key] = hooks[key].filter((entry) => !entry.hooks.some((hook) => (hook.command ?? '').includes(HOOK_MARKER)));
        if (hooks[key].length === 0) delete hooks[key];
      }
    }
    writeFileSync(settingsPath, JSON.stringify(settings, null, 2));
  }

  const claudeMdPath = join(root, 'CLAUDE.md');
  if (existsSync(claudeMdPath)) {
    const body = readFileSync(claudeMdPath, 'utf8');
    const pattern = new RegExp(`${escapeRegex(MARKER_BEGIN)}[\\s\\S]*?${escapeRegex(MARKER_END)}\\n?`, 'g');
    writeFileSync(claudeMdPath, body.replace(pattern, '').replace(/\n{3,}/g, '\n\n'));
  }

  const mcpPath = join(root, '.mcp.json');
  if (existsSync(mcpPath)) {
    const mcp = JSON.parse(readFileSync(mcpPath, 'utf8')) as { mcpServers?: Record<string, unknown> };
    if (mcp.mcpServers) {
      delete mcp.mcpServers.parlance;
      if (Object.keys(mcp.mcpServers).length === 0) delete mcp.mcpServers;
    }

    if (Object.keys(mcp).length === 0) rmSync(mcpPath, { force: true });
    else writeFileSync(mcpPath, JSON.stringify(mcp, null, 2));
  }

  if (purge && existsSync(parlanceDir(root))) {
    rmSync(parlanceDir(root), { recursive: true, force: true });
  }

  process.stderr.write('parlance agent (claude) uninstalled\n');
  return 0;
}

function escapeRegex(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}
