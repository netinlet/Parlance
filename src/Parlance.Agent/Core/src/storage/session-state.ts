import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import type { SessionState, SessionSummary, ToolUsageRecord } from '../types.js';
import { ledgerFile, sessionFile } from './paths.js';

export function readSessionState(root: string): SessionState | null {
  const path = sessionFile(root);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, 'utf8')) as SessionState;
  } catch {
    return null;
  }
}

export function writeSessionState(root: string, state: SessionState): void {
  const path = sessionFile(root);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify(state, null, 2));
}

export function appendToolUsageRecord(root: string, record: ToolUsageRecord): void {
  const state = readSessionState(root);
  if (!state) return;

  state.tool_calls.push(record);
  if (record.is_mcp_parlance) state.parlance_calls += 1;
  if (record.is_native_fallback) state.native_fallbacks += 1;
  if (record.event_kind === 'post-read') state.read_tokens += record.output_tokens;
  if (record.event_kind === 'post-write') state.write_tokens += record.output_tokens;

  writeSessionState(root, state);
}

export function persistSessionSummary(root: string, summary: SessionSummary): SessionSummary {
  mkdirSync(dirname(ledgerFile(root)), { recursive: true });
  appendFileSync(ledgerFile(root), `${JSON.stringify(summary)}\n`);
  return summary;
}
