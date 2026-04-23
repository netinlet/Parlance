import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { generateRoutingDoc } from '@parlance/agent-core';
import { hooksDir, parlanceDir, routingFile } from '@parlance/agent-core/storage/paths.js';

const MARKER_BEGIN = '<!-- parlance-agent:begin -->';
const MARKER_END = '<!-- parlance-agent:end -->';
const HOOK_MARKER = '.parlance/hooks/';

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
  writeSettingsJson(join(root, '.claude/settings.json'));
  writeClaudeMdSnippet(join(root, 'CLAUDE.md'));

  process.stderr.write(`parlance agent (claude) installed at ${root}\n`);
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

function writeMcpJson(root: string, solutionAbs: string, mcpCommand?: string): void {
  const path = join(root, '.mcp.json');
  const existing = existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) as { mcpServers?: Record<string, unknown> } : {};
  existing.mcpServers ??= {};
  existing.mcpServers.parlance = {
    type: 'stdio',
    command: mcpCommand ?? 'parlance',
    args: ['mcp', '--solution-path', solutionAbs],
  };
  writeFileSync(path, JSON.stringify(existing, null, 2));
}

function writeSettingsJson(path: string): void {
  const existing = existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) as Record<string, unknown> : {};
  const hooks = (existing.hooks as Record<string, HookMatcher[]> | undefined) ?? {};
  const ours: Record<string, HookMatcher[]> = {
    SessionStart: [matcher('', 'session-start.js', 5)],
    PreToolUse: [matcher('Read|Grep|Glob|Write|Edit|MultiEdit', 'pre-tool.js', 5)],
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
  writeFileSync(path, JSON.stringify(existing, null, 2));
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

function writeClaudeMdSnippet(path: string): void {
  const snippet = [
    MARKER_BEGIN,
    '',
    '## Parlance Agent',
    '',
    'This project uses `parlance agent` (Claude Code adapter). Claude hooks steer',
    'tool choice toward Parlance MCP tools on C# targets; routing rules live at',
    '`.parlance/tool-routing.md`, session summaries at `.parlance/session-log.md`,',
    'gap journal at `.parlance/kibble/`. Reports: `parlance agent report`.',
    '',
    MARKER_END,
    '',
  ].join('\n');

  if (!existsSync(path)) {
    writeFileSync(path, snippet);
    return;
  }

  const body = readFileSync(path, 'utf8');
  if (body.includes(MARKER_BEGIN)) return;
  writeFileSync(path, body.endsWith('\n') ? body + snippet : `${body}\n${snippet}`);
}
