import { homedir } from 'node:os';
import { join } from 'node:path';

export const parlanceDir = (root: string): string => join(root, '.parlance');

/**
 * Umbrella root for all Parlance global state, shared across every worktree.
 * Override with PARLANCE_HOME (an OS env var — not a repo `.env`, since it spans
 * repos); defaults to `~/.parlance`. Kept deliberately empty at the top level so
 * distinct concerns (telemetry, and later config/cache/logs) each get their own
 * subdirectory rather than colliding at the root.
 */
export const parlanceHome = (): string => process.env.PARLANCE_HOME?.trim() || join(homedir(), '.parlance');

/** Durable, cross-worktree telemetry — its own subtree under the home root. */
export const telemetryDir = (): string => join(parlanceHome(), 'telemetry');

/** Central bundle for the global (user-level) nudge hook — sibling of telemetry/. */
export const globalHooksDir = (): string => join(parlanceHome(), 'hooks');

// --- Project-local: install artifacts + the ephemeral active-session state.
// These stay per-worktree (the hooks are wired into that worktree's settings,
// and concurrent sessions in different worktrees must not clobber each other).
export const sessionFile = (root: string): string => join(parlanceDir(root), '_session.json');
export const hooksDir = (root: string): string => join(parlanceDir(root), 'hooks');
export const configFile = (root: string): string => join(parlanceDir(root), 'config.json');
export const routingFile = (root: string): string => join(parlanceDir(root), 'tool-routing.md');
export const benchStateFile = (root: string): string => join(parlanceDir(root), 'bench', '_active.json');

// --- Centralized: the durable, append-only records you track over time.
export const ledgerFile = (): string => join(telemetryDir(), 'ledger.jsonl');
export const sessionLogFile = (): string => join(telemetryDir(), 'session-log.md');
export const benchResultsFile = (): string => join(telemetryDir(), 'bench', 'results.jsonl');
