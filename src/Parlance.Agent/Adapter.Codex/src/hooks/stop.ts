import { appendFileSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { persistSessionSummary, readSessionState } from '@parlance/agent-core/storage/session-state.js';
import { sessionLogFile } from '@parlance/agent-core/storage/paths.js';
import { translate } from '../translate.js';
import { readEnvelope } from './_shared.js';

async function main(): Promise<void> {
  try {
    const env = await readEnvelope();
    const translated = translate(env);
    if (!translated || translated.event.kind !== 'response-completed') return;

    const state = readSessionState(translated.context.project_root);
    if (!state) return;

    const endedAt = new Date();
    const startedAt = new Date(state.started_at);
    const summary = persistSessionSummary(translated.context.project_root, {
      session_id: state.session_id,
      date: endedAt.toISOString().slice(0, 10),
      adapter: state.adapter,
      started_at: state.started_at,
      ended_at: endedAt.toISOString(),
      duration_s: Math.round((endedAt.getTime() - startedAt.getTime()) / 1000),
      branch: null,
      parlance_calls: state.parlance_calls,
      native_fallbacks: state.native_fallbacks,
      tool_call_count: state.tool_calls.length,
      read_tokens: state.read_tokens,
      write_tokens: state.write_tokens,
      usage: {
        input_tokens: 0,
        output_tokens: 0,
        cache_read_tokens: 0,
        cache_write_tokens: 0,
      },
    });

    const line = `- ${summary.date} \`${summary.session_id.slice(0, 8)}\` (${summary.adapter}) - ${summary.parlance_calls} Parlance, ${summary.native_fallbacks} fallback, ${summary.tool_call_count} tools, ${summary.duration_s}s, ${summary.usage.input_tokens} in / ${summary.usage.output_tokens} out\n`;
    const logPath = sessionLogFile(translated.context.project_root);
    mkdirSync(dirname(logPath), { recursive: true });
    appendFileSync(logPath, line);
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
