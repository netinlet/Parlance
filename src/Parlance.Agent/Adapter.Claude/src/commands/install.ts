import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { findSolution, generateRoutingDoc } from '@parlance/agent-core';
import { globalHooksDir, hooksDir, parlanceDir, routingFile } from '@parlance/agent-core/storage/paths.js';

const HOOK_MARKER = '.parlance/hooks/';
const GLOBAL_NUDGE_MARKER = 'hooks/nudge.js';

function claudeConfigDir(): string {
  return process.env.CLAUDE_CONFIG_DIR?.trim() || join(homedir(), '.claude');
}

interface InstallArgs {
  project: string;
  solution: string;
  mcpCommand?: string;
}

interface HookMatcher {
  matcher: string;
  hooks: { type: string; command: string; timeout?: number }[];
}

export async function runInstall(argv: string[]): Promise<number> {
  if (argv.includes('--global')) return runInstallGlobal();

  const args = parseArgs(argv);
  if (!args) return 2;

  const root = resolve(args.project);
  if (!existsSync(root)) {
    process.stderr.write(`project missing: ${root}\n`);
    return 1;
  }

  mkdirSync(parlanceDir(root), { recursive: true });
  mkdirSync(hooksDir(root), { recursive: true });
  copyHookBundles(hooksDir(root));
  writeFileSync(routingFile(root), generateRoutingDoc());

  writeMcpJson(root, resolve(root, args.solution), args.mcpCommand);
  mkdirSync(join(root, '.claude'), { recursive: true });
  writeSettingsJson(join(root, '.claude/settings.local.json'));
  process.stderr.write(`parlance agent (claude) installed at ${root}\n`);
  return 0;
}

function runInstallGlobal(): number {
  const hooksTarget = globalHooksDir();
  mkdirSync(hooksTarget, { recursive: true });

  const nudgeSource = join(findHookBundleDir(), 'nudge.js');
  if (!existsSync(nudgeSource)) {
    process.stderr.write(`nudge bundle missing at ${nudgeSource}\n`);
    return 1;
  }
  const nudgeTarget = join(hooksTarget, 'nudge.js');
  copyFileSync(nudgeSource, nudgeTarget);

  const settingsPath = join(claudeConfigDir(), 'settings.json');
  writeGlobalSettings(settingsPath, nudgeTarget);

  process.stderr.write(
    `parlance global nudge installed:\n  bundle: ${nudgeTarget}\n  wired into: ${settingsPath} (SessionStart, nudge-only)\n`,
  );

  const cwd = process.cwd();
  const hooksInstalled = existsSync(join(parlanceDir(cwd), 'hooks', 'session-start.js'));
  if (!hooksInstalled) {
    const sln = findSolution(cwd) ?? '<YourSolution.sln>';
    process.stderr.write(
      `\nNote: per-project hooks are not installed in the current directory.\n`
      + `      Run: parlance agent install --for claude --solution ${sln}\n`,
    );
  }

  return 0;
}

function readJsonOrEmpty<T extends Record<string, unknown>>(path: string): T {
  if (!existsSync(path)) return {} as T;
  try {
    return JSON.parse(readFileSync(path, 'utf8')) as T;
  } catch (err) {
    throw new Error(`could not parse ${path}: ${(err as Error).message}`);
  }
}

function mergeJsonFile<T extends Record<string, unknown>>(path: string, update: (data: T) => void): void {
  const data = readJsonOrEmpty<T>(path);
  update(data);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify(data, null, 2));
}

function writeGlobalSettings(path: string, nudgePath: string): void {
  mergeJsonFile<Record<string, unknown>>(path, (existing) => {
    const hooks = (existing.hooks as Record<string, HookMatcher[]> | undefined) ?? {};

    // Replace any prior global-nudge entry (idempotent); preserve foreign SessionStart hooks.
    const bucket = hooks.SessionStart ?? [];
    const preserved = bucket.filter((entry) => !entry.hooks.some((hook) => hook.command.includes(GLOBAL_NUDGE_MARKER)));
    hooks.SessionStart = [...preserved, {
      matcher: '',
      hooks: [{ type: 'command', command: `node "${nudgePath}"`, timeout: 5 }],
    }];

    existing.hooks = hooks;
  });
}

function parseArgs(argv: string[]): InstallArgs | null {
  const args: Partial<InstallArgs> = { project: process.cwd() };
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === '--project' && argv[index + 1]) args.project = argv[index + 1];
    if (argv[index] === '--solution' && argv[index + 1]) args.solution = argv[index + 1];
    if (argv[index] === '--mcp-command' && argv[index + 1]) args.mcpCommand = argv[index + 1];
  }

  if (!args.solution) {
    process.stderr.write('usage: install --solution <path> [--project <dir>]\n');
    return null;
  }

  return args as InstallArgs;
}

function copyHookBundles(target: string): void {
  const source = findHookBundleDir();
  for (const entry of readdirSync(source)) {
    if (entry.endsWith('.js')) copyFileSync(join(source, entry), join(target, entry));
  }
}

function findHookBundleDir(): string {
  const here = dirname(fileURLToPath(import.meta.url));
  const candidates = [
    join(here, 'hooks'),
    join(here, '..', '..', 'dist', 'hooks'),
  ];

  for (const candidate of candidates) {
    if (existsSync(candidate)) return candidate;
  }

  throw new Error('hook bundle directory not found');
}

function writeMcpJson(root: string, solutionAbs: string, mcpCommand?: string): void {
  const path = join(root, '.mcp.json');
  mergeJsonFile<{ mcpServers?: Record<string, unknown> }>(path, (existing) => {
    existing.mcpServers ??= {};
    existing.mcpServers.parlance = {
      type: 'stdio',
      command: mcpCommand ?? 'parlance',
      args: ['mcp', '--solution-path', solutionAbs],
    };
  });
}

function writeSettingsJson(path: string): void {
  mergeJsonFile<Record<string, unknown>>(path, (existing) => {
    const hooks = (existing.hooks as Record<string, HookMatcher[]> | undefined) ?? {};
    const ours: Record<string, HookMatcher[]> = {
      SessionStart: [matcher('', 'session-start.js', 5)],
      PreToolUse: [matcher('Read|Grep|Glob|Write|Edit|MultiEdit|Bash', 'pre-tool.js', 5)],
      PostToolUse: [matcher('', 'post-tool.js', 5)],
      UserPromptSubmit: [matcher('', 'user-prompt-submit.js', 3)],
      Stop: [matcher('', 'stop.js', 10)],
    };

    for (const [event, nextMatchers] of Object.entries(ours)) {
      const bucket = hooks[event] ?? [];
      const preserved = bucket.filter((entry) => !entry.hooks.some((hook) => hook.command.includes(HOOK_MARKER)));
      hooks[event] = [...preserved, ...nextMatchers];
    }

    existing.hooks = hooks;
  });
}

function matcher(matcherValue: string, script: string, timeout: number): HookMatcher {
  return {
    matcher: matcherValue,
    hooks: [{
      type: 'command',
      command: `node "$CLAUDE_PROJECT_DIR/.parlance/hooks/${script}"`,
      timeout,
    }],
  };
}
