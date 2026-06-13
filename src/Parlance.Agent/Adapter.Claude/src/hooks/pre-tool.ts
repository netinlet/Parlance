import { evaluateEvent, parlanceAgentInstalled } from '@parlance/agent-core';
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

    // No tracked session (project not wired) -> no nudge, no write.
    const current = readSessionState(translated.context.project_root);
    if (!current) return;

    // Guard against a stale _session.json left behind after the project was unwired.
    if (!parlanceAgentInstalled(translated.context.project_root)) return;

    const evaluation = evaluateEvent(translated.event, translated.context, current);
    renderToStderr(evaluation);

    if (evaluation.next_state) {
      writeSessionState(translated.context.project_root, evaluation.next_state);
    }
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
