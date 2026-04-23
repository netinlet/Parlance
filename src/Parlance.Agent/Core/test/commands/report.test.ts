import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runReport } from '../../src/commands/report.js';

let root: string;
let out: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'core-report-'));
  mkdirSync(join(root, '.parlance'), { recursive: true });
  writeFileSync(join(root, '.parlance/ledger.jsonl'), [
    JSON.stringify({
      session_id: 'aaa11111bb',
      date: '2026-04-20',
      adapter: 'claude-code',
      started_at: '2026-04-20T10:00:00Z',
      ended_at: '2026-04-20T10:20:00Z',
      duration_s: 1200,
      branch: 'main',
      parlance_calls: 20,
      native_fallbacks: 2,
      tool_call_count: 25,
      read_tokens: 400,
      write_tokens: 100,
      usage: { input_tokens: 5000, output_tokens: 800, cache_read_tokens: 30000, cache_write_tokens: 500 },
    }),
    JSON.stringify({
      session_id: 'bbb22222cc',
      date: '2026-04-22',
      adapter: 'claude-code',
      started_at: '2026-04-22T14:00:00Z',
      ended_at: '2026-04-22T14:30:00Z',
      duration_s: 1800,
      branch: 'main',
      parlance_calls: 40,
      native_fallbacks: 0,
      tool_call_count: 45,
      read_tokens: 200,
      write_tokens: 500,
      usage: { input_tokens: 7000, output_tokens: 1500, cache_read_tokens: 50000, cache_write_tokens: 800 },
    }),
  ].join('\n') + '\n');
  out = '';
  vi.spyOn(process.stdout, 'write').mockImplementation((chunk: unknown) => {
    out += String(chunk);
    return true;
  });
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
  vi.restoreAllMocks();
});

describe('report', () => {
  it('totals both sessions', async () => {
    await runReport(['--project', root, '--since', '2026-04-20', '--until', '2026-04-22']);

    expect(out).toContain('Sessions: 2');
    expect(out).toContain('Parlance calls: 60');
    expect(out).toContain('Native fallbacks: 2');
  });

  it('--since filters', async () => {
    await runReport(['--project', root, '--since', '2026-04-21', '--until', '2026-04-22']);

    expect(out).toContain('Sessions: 1');
    expect(out).toContain('bbb22222');
  });

  it('includes per-session rows with adapter column', async () => {
    await runReport(['--project', root, '--since', '2026-04-20', '--until', '2026-04-22']);

    expect(out).toContain('claude-code');
  });
});
