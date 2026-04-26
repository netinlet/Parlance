import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { appendBashEvent, bashEventFromEnvelope, bashEventsFile, redact } from '../src/bash-events.js';

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'codex-bash-events-'));
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

describe('bash events', () => {
  it('appends pre records under .parlance/codex/events/bash.jsonl', () => {
    const record = bashEventFromEnvelope({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      turn_id: 't1',
      tool_use_id: 'u1',
      cwd: root,
      tool_name: 'Bash',
      tool_input: { command: 'rg Foo src' },
    }, 'pre', new Date('2026-04-25T18:30:00Z'));

    expect(record).not.toBeNull();
    appendBashEvent(root, record!);

    expect(existsSync(bashEventsFile(root))).toBe(true);
    const line = JSON.parse(readFileSync(bashEventsFile(root), 'utf8').trim());
    expect(line.phase).toBe('pre');
    expect(line.command).toBe('rg Foo src');
    expect(line.classification.kind).toBe('search');
  });

  it('appends post records with bounded output metadata', () => {
    const record = bashEventFromEnvelope({
      hook_event_name: 'PostToolUse',
      session_id: 's1',
      cwd: root,
      tool_name: 'Bash',
      tool_input: { command: 'dotnet test' },
      tool_response: { exit_code: 1, stdout: 'x'.repeat(1200) },
    }, 'post', new Date('2026-04-25T18:30:00Z'));

    expect(record?.exit_code).toBe(1);
    expect(record?.output_bytes).toBe(1200);
    expect(record?.output_preview?.length).toBeLessThanOrEqual(1003);
    expect(record?.classification.kind).toBe('verify');
  });

  it('redacts secret-looking command content', () => {
    const record = bashEventFromEnvelope({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: root,
      tool_name: 'Bash',
      tool_input: {
        command: 'curl -H "Authorization: Bearer abcdefghijklmnopqrstuvwxyz0123456789" API_TOKEN=secretvalue',
      },
    }, 'pre', new Date('2026-04-25T18:30:00Z'));

    expect(record?.redacted).toBe(true);
    expect(record?.command).toContain('Authorization: Bearer [REDACTED]');
    expect(record?.command).toContain('API_TOKEN=[REDACTED]');
  });

  it('redacts long high-entropy strings', () => {
    const result = redact('token abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789');

    expect(result.redacted).toBe(true);
    expect(result.value).toBe('token [REDACTED]');
  });

  it('returns null for non-Bash tools', () => {
    expect(bashEventFromEnvelope({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: root,
      tool_name: 'apply_patch',
      tool_input: {},
    }, 'pre')).toBeNull();
  });
});
