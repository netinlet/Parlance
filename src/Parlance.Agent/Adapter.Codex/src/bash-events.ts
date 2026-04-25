import { appendFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { parlanceDir } from '@parlance/agent-core/storage/paths.js';
import { classifyBashCommand, commandFromInput } from './translate.js';
import type { BashClassification, CodexHookEnvelope } from './translate.js';

const MAX_PREVIEW_CHARS = 1000;

export type BashEventPhase = 'pre' | 'post';

export interface CodexBashEvent {
  schema: 1;
  at: string;
  adapter: 'codex';
  phase: BashEventPhase;
  session_id: string;
  turn_id?: string;
  tool_use_id?: string;
  cwd: string;
  command: string;
  redacted: boolean;
  classification: BashClassification;
  exit_code?: number;
  output_bytes?: number;
  output_preview?: string;
}

export function bashEventsFile(root: string): string {
  return join(parlanceDir(root), 'codex', 'events', 'bash.jsonl');
}

export function appendBashEvent(root: string, record: CodexBashEvent): void {
  const path = bashEventsFile(root);
  mkdirSync(dirname(path), { recursive: true });
  appendFileSync(path, `${JSON.stringify(record)}\n`);
}

export function bashEventFromEnvelope(env: CodexHookEnvelope, phase: BashEventPhase, now = new Date()): CodexBashEvent | null {
  if (env.tool_name !== 'Bash') return null;

  const input = env.tool_input ?? {};
  const commandRedaction = redact(commandFromInput(input));
  const record: CodexBashEvent = {
    schema: 1,
    at: now.toISOString(),
    adapter: 'codex',
    phase,
    session_id: env.session_id,
    turn_id: env.turn_id,
    tool_use_id: env.tool_use_id,
    cwd: env.cwd,
    command: commandRedaction.value,
    redacted: commandRedaction.redacted,
    classification: classifyBashCommand(commandFromInput(input)),
  };

  if (phase === 'post') {
    const output = extractOutput(env.tool_response);
    if (typeof output.exit_code === 'number') record.exit_code = output.exit_code;
    if (typeof output.output_bytes === 'number') record.output_bytes = output.output_bytes;
    if (output.preview) {
      const previewRedaction = redact(truncate(output.preview, MAX_PREVIEW_CHARS));
      record.output_preview = previewRedaction.value;
      record.redacted ||= previewRedaction.redacted;
    }
  }

  return record;
}

export function redact(value: string): { value: string; redacted: boolean } {
  let redacted = false;
  let next = value;

  next = next.replace(
    /\b([A-Z0-9_]*(?:TOKEN|KEY|SECRET|PASSWORD|PASS|AUTH|CONNECTION)[A-Z0-9_]*)=([^\s"'`]+)/gi,
    (_match, name) => {
      redacted = true;
      return `${name}=[REDACTED]`;
    },
  );

  next = next.replace(/Authorization:\s*Bearer\s+[A-Za-z0-9._~+/=-]+/gi, () => {
    redacted = true;
    return 'Authorization: Bearer [REDACTED]';
  });

  next = next.replace(/\b[A-Za-z0-9+/=_-]{40,}\b/g, () => {
    redacted = true;
    return '[REDACTED]';
  });

  return { value: next, redacted };
}

function extractOutput(output: unknown): { exit_code?: number; output_bytes?: number; preview?: string } {
  if (typeof output === 'string') {
    return { output_bytes: output.length, preview: output };
  }
  if (!output || typeof output !== 'object') return {};

  const record = output as Record<string, unknown>;
  const stdout = typeof record.stdout === 'string' ? record.stdout : '';
  const stderr = typeof record.stderr === 'string' ? record.stderr : '';
  const content = typeof record.content === 'string' ? record.content : '';
  const outputText = stdout || stderr ? `${stdout}${stderr}` : content;
  const exit_code = typeof record.exit_code === 'number'
    ? record.exit_code
    : typeof record.exitCode === 'number'
      ? record.exitCode
      : undefined;

  return {
    exit_code,
    output_bytes: outputText ? outputText.length : undefined,
    preview: outputText || undefined,
  };
}

function truncate(value: string, max: number): string {
  return value.length > max ? `${value.slice(0, max)}...` : value;
}
