import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { mkdtempSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { appendFeedbackRecord } from '../../src/storage/kibble.js';
import { kibbleDir } from '../../src/storage/paths.js';

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'core-kibble-'));
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe('kibble', () => {
  it('writes entry 001', () => {
    const path = appendFeedbackRecord(root, {
      date: '2026-04-22',
      adapter: 'claude-code',
      native_tool: 'read',
      intent: 'read Foo.cs',
      why: 'pre-read .cs',
      suggested: 'mcp__parlance__describe-type',
      session_id: 'a',
    });

    expect(path).toMatch(/2026-04-22\/001-.*\.md$/);
    expect(readFileSync(path, 'utf8')).toContain('mcp__parlance__describe-type');
  });

  it('increments sequence', () => {
    appendFeedbackRecord(root, {
      date: '2026-04-22',
      adapter: 'claude-code',
      native_tool: 'read',
      intent: 'a',
      why: 'b',
      suggested: 'c',
      session_id: 'x',
    });

    const path = appendFeedbackRecord(root, {
      date: '2026-04-22',
      adapter: 'claude-code',
      native_tool: 'search',
      intent: 'c',
      why: 'd',
      suggested: 'e',
      session_id: 'x',
    });

    expect(path).toMatch(/002-/);
  });

  it('dedups by native_tool + intent', () => {
    const first = appendFeedbackRecord(root, {
      date: '2026-04-22',
      adapter: 'claude-code',
      native_tool: 'read',
      intent: 'look at Foo',
      why: 'a',
      suggested: 'b',
      session_id: 'x',
    });

    const second = appendFeedbackRecord(root, {
      date: '2026-04-22',
      adapter: 'claude-code',
      native_tool: 'read',
      intent: 'look at Foo',
      why: 'c',
      suggested: 'd',
      session_id: 'y',
    });

    expect(second).toBe(first);
    expect(readdirSync(join(kibbleDir(root), '2026-04-22'))).toHaveLength(1);
  });
});
