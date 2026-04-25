import { appendFeedbackRecord } from '@parlance/agent-core/storage/kibble.js';
import { emptySessionState, evaluateEvent } from '@parlance/agent-core';
import { readSessionState, writeSessionState } from '@parlance/agent-core/storage/session-state.js';
import { appendBashEvent, bashEventFromEnvelope } from '../bash-events.js';
import { renderForCodex, writeCodexOutput } from '../render.js';
import { translate } from '../translate.js';
import type { BashEventPhase } from '../bash-events.js';
import type { CodexHookEnvelope, CodexHookEventName } from '../translate.js';

export async function readStdin(): Promise<string> {
  const chunks: Buffer[] = [];
  for await (const chunk of process.stdin) {
    chunks.push(typeof chunk === 'string' ? Buffer.from(chunk) : chunk);
  }
  return Buffer.concat(chunks).toString('utf8');
}

export async function readEnvelope(): Promise<CodexHookEnvelope> {
  return JSON.parse(await readStdin()) as CodexHookEnvelope;
}

export function handleEvaluatedEvent(env: CodexHookEnvelope, bashPhase?: BashEventPhase): void {
  const translated = translate(env);
  if (!translated) return;

  const current = readSessionState(translated.context.project_root)
    ?? emptySessionState(translated.context, translated.transcript_path);
  const evaluation = evaluateEvent(translated.event, translated.context, current);

  for (const effect of evaluation.effects) {
    if (effect.kind === 'persist-feedback') {
      appendFeedbackRecord(translated.context.project_root, effect.feedback);
    }
  }

  if (evaluation.next_state) {
    writeSessionState(translated.context.project_root, evaluation.next_state);
  }

  if (bashPhase) {
    const bashEvent = bashEventFromEnvelope(env, bashPhase);
    if (bashEvent) appendBashEvent(translated.context.project_root, bashEvent);
  }

  writeCodexOutput(renderForCodex(env.hook_event_name as CodexHookEventName, evaluation));
}
