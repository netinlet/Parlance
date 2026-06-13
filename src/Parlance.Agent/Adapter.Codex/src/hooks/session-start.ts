import { emptySessionState, generateSessionContext } from '@parlance/agent-core';
import { writeSessionState } from '@parlance/agent-core/storage/session-state.js';
import { capabilities } from '../capabilities.js';
import { writeCodexOutput } from '../render.js';
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
    if (capabilities.outputs.can_inject_context) {
      writeCodexOutput({
        hookSpecificOutput: {
          hookEventName: 'SessionStart',
          additionalContext: generateSessionContext(),
        },
      });
    }
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
