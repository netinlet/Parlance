import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runStatus } from '../../src/commands/status.js';

let root: string;
let out: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'core-status-'));
  mkdirSync(join(root, '.parlance'), { recursive: true });
  writeFileSync(join(root, '.parlance/ledger.jsonl'), `${JSON.stringify({ session_id: 'abc' })}\n`);
  writeFileSync(join(root, '.parlance/session-log.md'), '- 2026-04-22 `abc` (claude-code)\n');
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

describe('status', () => {
  it('prints install and recent log status', async () => {
    await runStatus(['--project', root]);

    expect(out).toContain('.parlance/ dir: present');
    expect(out).toContain('sessions logged: 1');
    expect(out).toContain('recent:');
    expect(out).toContain('claude-code');
  });
});
