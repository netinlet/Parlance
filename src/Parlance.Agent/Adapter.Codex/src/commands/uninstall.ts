import { existsSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { parlanceDir } from '@parlance/agent-core/storage/paths.js';

const HOOK_MARKER = '.parlance/hooks/';

export async function runUninstall(argv: string[]): Promise<number> {
  let project = process.cwd();
  let purge = false;
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === '--project' && argv[index + 1]) project = argv[index + 1];
    if (argv[index] === '--purge') purge = true;
  }

  const root = resolve(project);
  const hooksPath = join(root, '.codex/hooks.json');
  if (existsSync(hooksPath)) {
    const settings = JSON.parse(readFileSync(hooksPath, 'utf8')) as { hooks?: Record<string, { hooks: { command?: string }[] }[]> };
    if (settings.hooks) {
      for (const key of Object.keys(settings.hooks)) {
        settings.hooks[key] = settings.hooks[key].filter((entry) => !entry.hooks.some((hook) => (hook.command ?? '').includes(HOOK_MARKER)));
        if (settings.hooks[key].length === 0) delete settings.hooks[key];
      }
      if (Object.keys(settings.hooks).length === 0) delete settings.hooks;
    }

    if (Object.keys(settings).length === 0) rmSync(hooksPath, { force: true });
    else writeFileSync(hooksPath, JSON.stringify(settings, null, 2));
  }

  if (purge && existsSync(parlanceDir(root))) {
    rmSync(parlanceDir(root), { recursive: true, force: true });
  }

  process.stderr.write('parlance agent (codex) uninstalled\n');
  return 0;
}
