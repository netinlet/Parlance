import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runBench } from '../../src/commands/bench.js';

let root: string;
let out: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'core-bench-'));
  mkdirSync(join(root, '.parlance/bench'), { recursive: true });
  writeFileSync(join(root, '.parlance/bench/results.jsonl'), [
    JSON.stringify({
      task_id: 'find-callers',
      variant: 'grep',
      started_at: '2026-04-22T14:00:00Z',
      ended_at: '2026-04-22T14:02:00Z',
      session_id: 'sA',
      adapter: 'claude-code',
      usage: { input_tokens: 9000, output_tokens: 500, cache_read_tokens: 0, cache_write_tokens: 0 },
    }),
    JSON.stringify({
      task_id: 'find-callers',
      variant: 'parlance',
      started_at: '2026-04-22T14:05:00Z',
      ended_at: '2026-04-22T14:05:30Z',
      session_id: 'sB',
      adapter: 'claude-code',
      usage: { input_tokens: 400, output_tokens: 80, cache_read_tokens: 0, cache_write_tokens: 0 },
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

describe('bench report', () => {
  it('prints per-variant rows', async () => {
    await runBench(['report', '--project', root, '--task', 'find-callers']);

    expect(out).toContain('find-callers');
    expect(out).toContain('grep');
    expect(out).toContain('parlance');
    expect(out).toContain('9,000');
    expect(out).toContain('400');
  });
});
