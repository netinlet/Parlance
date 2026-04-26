import { emptySessionState } from '@parlance/agent-core';
import { writeSessionState } from '@parlance/agent-core/storage/session-state.js';
import { renderForCodex, writeCodexOutput } from '../render.js';
import { translate } from '../translate.js';
import { readEnvelope } from './_shared.js';

async function main(): Promise<void> {
  try {
    const env = await readEnvelope();
    const translated = translate(env);
    if (!translated || translated.event.kind !== 'session-started') return;
    writeSessionState(
      translated.context.project_root,
      emptySessionState(translated.context, translated.transcript_path),
    );
    writeCodexOutput(renderForCodex(env.hook_event_name, { guidance: [], effects: [], next_state: null }));
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
