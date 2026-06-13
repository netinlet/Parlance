import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runStatus } from '../../src/commands/status.js';

let root: string;
let out: string;
const originalHome = process.env.PARLANCE_HOME;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'core-status-'));
  process.env.PARLANCE_HOME = join(root, 'central');
  mkdirSync(join(root, '.parlance'), { recursive: true });
  mkdirSync(join(root, 'central'), { recursive: true });
  writeFileSync(join(root, 'central/ledger.jsonl'), `${JSON.stringify({ session_id: 'abc' })}\n`);
  writeFileSync(join(root, 'central/session-log.md'), '- 2026-04-22 `abc` (claude-code)\n');
  out = '';
  vi.spyOn(process.stdout, 'write').mockImplementation((chunk: unknown) => {
    out += String(chunk);
    return true;
  });
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
  vi.restoreAllMocks();
  if (originalHome === undefined) delete process.env.PARLANCE_HOME;
  else process.env.PARLANCE_HOME = originalHome;
});

describe('status', () => {
  it('prints local install status and central log tail', async () => {
    await runStatus(['--project', root]);

    expect(out).toContain('project .parlance/ (install): present');
    expect(out).toContain('sessions logged (all worktrees): 1');
    expect(out).toContain('recent:');
    expect(out).toContain('claude-code');
  });
});
