import { homedir } from 'node:os';
import { join } from 'node:path';

export const parlanceDir = (root: string): string => join(root, '.parlance');

/**
 * Centralized telemetry home, shared across every worktree so usage accrues in
 * one place instead of a per-worktree `.parlance/`. Override with PARLANCE_HOME
 * (an OS env var — not a repo `.env`, since it spans repos); defaults to
 * `~/.parlance`.
 */
export const telemetryHome = (): string => process.env.PARLANCE_HOME?.trim() || join(homedir(), '.parlance');

// --- Project-local: install artifacts + the ephemeral active-session state.
// These stay per-worktree (the hooks are wired into that worktree's settings,
// and concurrent sessions in different worktrees must not clobber each other).
export const sessionFile = (root: string): string => join(parlanceDir(root), '_session.json');
export const hooksDir = (root: string): string => join(parlanceDir(root), 'hooks');
export const configFile = (root: string): string => join(parlanceDir(root), 'config.json');
export const routingFile = (root: string): string => join(parlanceDir(root), 'tool-routing.md');
export const benchStateFile = (root: string): string => join(parlanceDir(root), 'bench', '_active.json');

// --- Centralized: the durable, append-only records you track over time.
export const ledgerFile = (): string => join(telemetryHome(), 'ledger.jsonl');
export const sessionLogFile = (): string => join(telemetryHome(), 'session-log.md');
export const benchResultsFile = (): string => join(telemetryHome(), 'bench', 'results.jsonl');
