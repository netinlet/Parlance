import { copyFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { generateRoutingDoc } from '@parlance/agent-core';
import { hooksDir, parlanceDir, routingFile } from '@parlance/agent-core/storage/paths.js';

const HOOK_MARKER = '.parlance/hooks/';

interface InstallArgs {
  project: string;
  solution: string;
  mcpCommand?: string;
}

interface HookMatcher {
  matcher?: string;
  hooks: { type: string; command: string; timeout?: number; statusMessage?: string }[];
}

export async function runInstall(argv: string[]): Promise<number> {
  const args = parseArgs(argv);
  if (!args) return 2;

  const root = resolve(args.project);
  if (!existsSync(root)) {
    process.stderr.write(`project missing: ${root}\n`);
    return 1;
  }

  const codexDir = join(root, '.codex');
  if (existsSync(codexDir) && !lstatSync(codexDir).isDirectory()) {
    process.stderr.write(`cannot install codex hooks: ${codexDir} exists and is not a directory\n`);
    return 1;
  }

  mkdirSync(parlanceDir(root), { recursive: true });
  mkdirSync(hooksDir(root), { recursive: true });
  mkdirSync(join(parlanceDir(root), 'codex', 'events'), { recursive: true });
  mkdirSync(codexDir, { recursive: true });

  copyHookBundles(hooksDir(root));
  writeFileSync(routingFile(root), generateRoutingDoc());
  writeHooksJson(join(codexDir, 'hooks.json'));
  writeConfigToml(join(codexDir, 'config.toml'));

  process.stderr.write(`parlance agent (codex) installed at ${root}\n`);
  return 0;
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

function writeHooksJson(path: string): void {
  const existing = existsSync(path)
    ? JSON.parse(readFileSync(path, 'utf8')) as { hooks?: Record<string, HookMatcher[]> }
    : {};
  existing.hooks ??= {};

  const ours: Record<string, HookMatcher[]> = {
    SessionStart: [matcher('startup|resume|clear', 'session-start.js', 5, 'Loading Parlance session state')],
    UserPromptSubmit: [matcher(undefined, 'user-prompt-submit.js', 3, 'Checking Parlance prompt context')],
    PreToolUse: [matcher('Bash|apply_patch|Edit|Write|mcp__.*', 'pre-tool.js', 5, 'Checking Parlance routing')],
    PostToolUse: [matcher('Bash|apply_patch|Edit|Write|mcp__.*', 'post-tool.js', 5, 'Recording Parlance tool telemetry')],
    Stop: [matcher(undefined, 'stop.js', 10, 'Recording Parlance session summary')],
  };

  for (const [event, nextMatchers] of Object.entries(ours)) {
    const bucket = existing.hooks[event] ?? [];
    const preserved = bucket.filter((entry) => !entry.hooks.some((hook) => hook.command.includes(HOOK_MARKER)));
    existing.hooks[event] = [...preserved, ...nextMatchers];
  }

  writeFileSync(path, JSON.stringify(existing, null, 2));
}

function matcher(matcherValue: string | undefined, script: string, timeout: number, statusMessage: string): HookMatcher {
  const entry: HookMatcher = {
    hooks: [{
      type: 'command',
      command: `node "$(git rev-parse --show-toplevel)/.parlance/hooks/${script}"`,
      timeout,
      statusMessage,
    }],
  };
  if (matcherValue !== undefined) entry.matcher = matcherValue;
  return entry;
}

function writeConfigToml(path: string): void {
  const existing = existsSync(path) ? readFileSync(path, 'utf8') : '';
  const next = withCodexHooksFeature(existing);
  writeFileSync(path, next);
}

export function withCodexHooksFeature(existing: string): string {
  const normalized = existing.replace(/\r\n/g, '\n');
  if (/^\s*\[features]\s*$/m.test(normalized)) {
    if (/^\s*codex_hooks\s*=/m.test(normalized)) {
      return ensureTrailingNewline(normalized.replace(/^\s*codex_hooks\s*=.*$/m, 'codex_hooks = true'));
    }

    const lines = normalized.split('\n');
    const index = lines.findIndex((line) => /^\s*\[features]\s*$/.test(line));
    lines.splice(index + 1, 0, 'codex_hooks = true');
    return ensureTrailingNewline(lines.join('\n'));
  }

  const prefix = normalized.trim().length === 0 ? '' : `${ensureTrailingNewline(normalized)}\n`;
  return `${prefix}[features]\ncodex_hooks = true\n`;
}

function ensureTrailingNewline(value: string): string {
  return value.endsWith('\n') ? value : `${value}\n`;
}
