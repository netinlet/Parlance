import { copyFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { findSolution, generateRoutingDoc } from '@parlance/agent-core';
import { globalHooksDir, hooksDir, parlanceDir, routingFile } from '@parlance/agent-core/storage/paths.js';

const HOOK_MARKER = '.parlance/hooks/';
const GLOBAL_NUDGE_MARKER = 'hooks/nudge.js';

function codexConfigDir(): string {
  return process.env.CODEX_CONFIG_DIR?.trim() || join(homedir(), '.codex');
}

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
  if (argv.includes('--global')) return runInstallGlobal();

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
  writeMcpSetupDoc(root, resolve(root, args.solution), args.mcpCommand ?? 'parlance');
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

  const hooksPath = join(codexConfigDir(), 'hooks.json');
  writeGlobalHooksJson(hooksPath, nudgeTarget);

  process.stderr.write(
    `parlance global nudge installed:\n  bundle: ${nudgeTarget}\n  wired into: ${hooksPath} (SessionStart, nudge-only)\n`,
  );

  const cwd = process.cwd();
  const hooksInstalled = existsSync(join(cwd, '.codex', 'hooks.json'));
  if (!hooksInstalled) {
    const sln = findSolution(cwd) ?? '<YourSolution.sln>';
    process.stderr.write(
      `\nNote: per-project hooks are not installed in the current directory.\n`
      + `      Run: parlance agent install --for codex --solution ${sln}\n`,
    );
  }

  return 0;
}

function writeGlobalHooksJson(path: string, nudgePath: string): void {
  mergeJsonFile<{ hooks?: Record<string, HookMatcher[]> }>(path, (existing) => {
    const hooks = existing.hooks ?? {};

    // Replace any prior global-nudge entry (idempotent); preserve foreign SessionStart hooks.
    const bucket = hooks.SessionStart ?? [];
    const preserved = bucket.filter((entry) => !entry.hooks.some((hook) => hook.command.includes(GLOBAL_NUDGE_MARKER)));
    hooks.SessionStart = [...preserved, {
      hooks: [{ type: 'command', command: `node "${nudgePath}"`, timeout: 5 }],
    }];

    existing.hooks = hooks;
  });
}

function writeHooksJson(path: string): void {
  const existing = readJsonOrEmpty<{ hooks?: Record<string, HookMatcher[]> }>(path);
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
    const preserved = bucket.filter((entry) => !entry.hooks.some((hook) => (hook.command ?? '').includes(HOOK_MARKER)));
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

function writeMcpSetupDoc(root: string, solutionAbs: string, mcpCommand: string): void {
  const path = join(parlanceDir(root), 'codex', 'mcp-setup.md');
  const command = `codex mcp add parlance -- ${shellQuote(mcpCommand)} mcp --solution-path ${shellQuote(solutionAbs)}`;
  const body = [
    '# Parlance MCP Setup for Codex',
    '',
    'Run this command in your Codex shell to register the Parlance MCP server:',
    '',
    '```bash',
    command,
    '```',
    '',
    'After registration, restart Codex or follow any instructions printed by the Codex CLI.',
    '',
  ].join('\n');
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, body);
}

function shellQuote(value: string): string {
  if (/^[A-Za-z0-9_./:-]+$/.test(value)) return value;
  return `'${value.replace(/'/g, `'\\''`)}'`;
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
