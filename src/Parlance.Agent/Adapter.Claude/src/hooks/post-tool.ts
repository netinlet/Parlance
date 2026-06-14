import { evaluateEvent } from '@parlance/agent-core';
import {
  readSessionState,
  writeSessionState,
} from '@parlance/agent-core/storage/session-state.js';
import { translate } from '../translate.js';
import { readStdin } from './_shared.js';

async function main(): Promise<void> {
  try {
    const raw = await readStdin();
    const env = JSON.parse(raw);
    const translated = translate(env);
    if (!translated) return;

    // Only track sessions Parlance is wired into — session-start creates the
    // state file only for wired projects, so its absence means "don't track".
    const current = readSessionState(translated.context.project_root);
    if (!current) return;
    const evaluation = evaluateEvent(
      translated.event,
      translated.context,
      current,
    );

    if (evaluation.next_state) {
      writeSessionState(translated.context.project_root, evaluation.next_state);
    }
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
