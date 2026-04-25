import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { appendToolUsageRecord, persistSessionSummary, readSessionState, writeSessionState } from '../../src/storage/session-state.js';
import { ledgerFile, sessionFile } from '../../src/storage/paths.js';

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'core-session-state-'));
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe('session-state', () => {
  it('writeSessionState writes a fresh file', () => {
    writeSessionState(root, {
      session_id: 's1',
      adapter: 'claude-code',
      started_at: '2026-04-22T00:00:00Z',
      cwd: root,
      transcript_ref: null,
      parlance_calls: 0,
      native_fallbacks: 0,
      tool_calls: [],
      read_tokens: 0,
      write_tokens: 0,
      active_bench: null,
    });

    const state = JSON.parse(readFileSync(sessionFile(root), 'utf8'));
    expect(state.session_id).toBe('s1');
    expect(state.adapter).toBe('claude-code');
    expect(state.tool_calls).toEqual([]);
  });

  it('appendToolUsageRecord increments parlance_calls for parlance MCP tools', () => {
    writeSessionState(root, {
      session_id: 's1',
      adapter: 'claude-code',
      started_at: '2026-04-22T00:00:00Z',
      cwd: root,
      transcript_ref: null,
      parlance_calls: 0,
      native_fallbacks: 0,
      tool_calls: [],
      read_tokens: 0,
      write_tokens: 0,
      active_bench: null,
    });

    appendToolUsageRecord(root, {
      at: '2026-04-22T00:00:00Z',
      event_kind: 'post-mcp-tool',
      tool_name: 'mcp__parlance__search-symbols',
      target: 'X',
      is_mcp_parlance: true,
      is_native_fallback: false,
      output_tokens: 50,
    });

    const state = readSessionState(root)!;
    expect(state.parlance_calls).toBe(1);
    expect(state.native_fallbacks).toBe(0);
  });

  it('counts native_fallbacks', () => {
    writeSessionState(root, {
      session_id: 's1',
      adapter: 'claude-code',
      started_at: '2026-04-22T00:00:00Z',
      cwd: root,
      transcript_ref: null,
      parlance_calls: 0,
      native_fallbacks: 0,
      tool_calls: [],
      read_tokens: 0,
      write_tokens: 0,
      active_bench: null,
    });

    appendToolUsageRecord(root, {
      at: '2026-04-22T00:00:00Z',
      event_kind: 'post-read',
      tool_name: 'Read',
      target: 'Foo.cs',
      is_mcp_parlance: false,
      is_native_fallback: true,
      output_tokens: 700,
    });

    expect(readSessionState(root)!.native_fallbacks).toBe(1);
  });

  it('persistSessionSummary appends line to ledger.jsonl', () => {
    persistSessionSummary(root, {
      session_id: 's1',
      date: '2026-04-22',
      adapter: 'claude-code',
      started_at: '2026-04-22T00:00:00Z',
      ended_at: '2026-04-22T00:10:00Z',
      duration_s: 600,
      branch: 'main',
      parlance_calls: 0,
      native_fallbacks: 1,
      tool_call_count: 1,
      read_tokens: 200,
      write_tokens: 0,
      usage: {
        input_tokens: 1000,
        output_tokens: 200,
        cache_read_tokens: 5000,
        cache_write_tokens: 100,
      },
    });

    const entry = JSON.parse(readFileSync(ledgerFile(root), 'utf8').trim());
    expect(entry.session_id).toBe('s1');
    expect(entry.usage.input_tokens).toBe(1000);
    expect(entry.read_tokens).toBe(200);
  });

  it('readSessionState returns null when no file', () => {
    expect(readSessionState(root)).toBeNull();
    expect(existsSync(sessionFile(root))).toBe(false);
  });
});
