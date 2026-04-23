import { emptySessionState, evaluateEvent } from '@parlance/agent-core';
import { appendFeedbackRecord } from '@parlance/agent-core/storage/kibble.js';
import { readSessionState, writeSessionState } from '@parlance/agent-core/storage/session-state.js';
import { renderToStderr } from '../render.js';
import { translate } from '../translate.js';
import { readStdin } from './_shared.js';

async function main(): Promise<void> {
  try {
    const raw = await readStdin();
    const env = JSON.parse(raw);
    const translated = translate(env);
    if (!translated) return;

    const current = readSessionState(translated.context.project_root)
      ?? emptySessionState(translated.context, translated.transcript_path);

    const evaluation = evaluateEvent(translated.event, translated.context, current);
    renderToStderr(evaluation);

    for (const effect of evaluation.effects) {
      if (effect.kind === 'persist-feedback') {
        appendFeedbackRecord(translated.context.project_root, effect.feedback);
      }
    }

    if (evaluation.next_state) {
      writeSessionState(translated.context.project_root, evaluation.next_state);
    }
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
