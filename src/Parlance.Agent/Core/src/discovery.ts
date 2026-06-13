import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { generateSessionContext } from './commands/routing-doc.js';

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

/**
 * Cheap heuristic for "is this a C# project?", run at session start so it must
 * stay fast: solution/global.json/Directory.Build.props/.csproj at the root, or
 * a `.csproj` one level down in `src/`.
 */
export function looksLikeCsharp(root: string): boolean {
  let entries: string[];
  try {
    entries = readdirSync(root);
  } catch {
    return false;
  }

  const csAtRoot = entries.some((e) =>
    /\.(slnx|sln|csproj)$/i.test(e) || e === 'global.json' || e === 'Directory.Build.props');
  if (csAtRoot) return true;

  try {
    return readdirSync(join(root, 'src')).some((e) => /\.csproj$/i.test(e));
  } catch {
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

export type SessionStartPlan =
  | { kind: 'wired'; context: string }
  | { kind: 'suggest-install'; context: string }
  | { kind: 'idle' };

/**
 * What a session-start hook should do for a project root. Lets the same hook run
 * globally without per-worktree setup: track + prime where Parlance is wired,
 * remind where it's a C# project that isn't, and stay silent (writing nothing)
 * everywhere else so unrelated repos aren't littered.
 */
export function planSessionStart(root: string): SessionStartPlan {
  if (parlanceMcpWired(root)) {
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
