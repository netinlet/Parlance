import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { generateSessionContext } from './commands/routing-doc.js';

interface HookMatcher {
  hooks?: { command?: unknown }[];
}

/** First solution file (basename) at the project root — `.slnx` preferred over `.sln`. */
export function findSolution(root: string): string | null {
  let entries: string[];
  try {
    entries = readdirSync(root);
  } catch {
    return null;
  }
  return entries.find((e) => /\.slnx$/i.test(e)) ?? entries.find((e) => /\.sln$/i.test(e)) ?? null;
}

const csharpCache = new Map<string, boolean>();

/**
 * Cheap heuristic for "is this a C# project?", run at session start so it must
 * stay fast: solution/Directory.Build.props/.csproj at the root, or a `.csproj`
 * one level down in `src/`. Result is cached per root for the process lifetime.
 */
export function looksLikeCsharp(root: string): boolean {
  const cached = csharpCache.get(root);
  if (cached !== undefined) return cached;

  let entries: string[];
  try {
    entries = readdirSync(root);
  } catch {
    csharpCache.set(root, false);
    return false;
  }

  const csAtRoot = entries.some((e) =>
    /\.(slnx|sln|csproj)$/i.test(e) || e === 'Directory.Build.props');
  if (csAtRoot) {
    csharpCache.set(root, true);
    return true;
  }

  try {
    const result = readdirSync(join(root, 'src')).some((e) => /\.csproj$/i.test(e));
    csharpCache.set(root, result);
    return result;
  } catch {
    csharpCache.set(root, false);
    return false;
  }
}

/** Is the Parlance MCP server wired in this worktree's `.mcp.json`? */
export function parlanceMcpWired(root: string): boolean {
  try {
    const config = JSON.parse(readFileSync(join(root, '.mcp.json'), 'utf8')) as { mcpServers?: Record<string, unknown> };
    return Boolean(config.mcpServers && 'parlance' in config.mcpServers);
  } catch {
    return false;
  }
}

/**
 * True when `parlance agent install` has been run: either the MCP server is in
 * `.mcp.json` (Claude path) or hook bundles are present (Codex path, where the
 * user separately runs `codex mcp add parlance`).
 */
export function parlanceAgentInstalled(root: string): boolean {
  return parlanceMcpWired(root)
    || existsSync(join(root, '.parlance', 'hooks', 'session-start.js'));
}

/** Is the Parlance Codex agent installed? Requires Codex to actually point at the per-project hook bundle. */
export function parlanceCodexWired(root: string): boolean {
  return existsSync(join(root, '.parlance', 'hooks', 'session-start.js'))
    && codexHooksJsonReferencesSessionStart(root);
}

function codexHooksJsonReferencesSessionStart(root: string): boolean {
  try {
    const config = JSON.parse(readFileSync(join(root, '.codex', 'hooks.json'), 'utf8')) as { hooks?: Record<string, HookMatcher[]> };
    const sessionStart = config.hooks?.SessionStart ?? [];
    return sessionStart.some((entry) =>
      entry.hooks?.some((hook) =>
        typeof hook.command === 'string' && hook.command.includes('.parlance/hooks/session-start.js')) ?? false);
  } catch {
    return false;
  }
}

export type SessionStartPlan =
  | { kind: 'wired'; context: string }
  | { kind: 'suggest-install'; context: string }
  | { kind: 'idle' };

/**
 * What a session-start hook should do for a project root. Lets the same hook run
 * globally without per-worktree setup: track + prime where Parlance is wired,
 * remind where it's a C# project that isn't, and stay silent (writing nothing)
 * everywhere else so unrelated repos aren't littered.
 *
 * Pass a custom `wiredFn` to check agent-specific wiring (e.g. `parlanceCodexWired`
 * for the Codex global nudge, so a Claude-only `.mcp.json` doesn't suppress it).
 */
export function planSessionStart(root: string, wiredFn: (r: string) => boolean = parlanceAgentInstalled): SessionStartPlan {
  if (wiredFn(root)) {
    return { kind: 'wired', context: generateSessionContext() };
  }

  if (looksLikeCsharp(root)) {
    const target = findSolution(root) ?? '<YourSolution.slnx>';
    return {
      kind: 'suggest-install',
      context: [
        'This looks like a C# project, but the Parlance MCP server is not wired here —',
        'so there is no Parlance code intelligence and this session is not being tracked.',
        `To enable it, run:  parlance agent install --solution ${target}`,
      ].join('\n'),
    };
  }

  return { kind: 'idle' };
}

/**
 * Emit a nudge context string when the plan is `suggest-install` and the adapter
 * supports context injection. Centralises the guard so adapter `nudge.ts` files
 * don't each duplicate the if-condition.
 */
export function runNudge(
  plan: SessionStartPlan,
  canInjectContext: boolean,
  emit: (context: string) => void,
): void {
  if (plan.kind === 'suggest-install' && canInjectContext) {
    emit(plan.context);
  }
}
