import { emptySessionState } from '@parlance/agent-core';
import { writeSessionState } from '@parlance/agent-core/storage/session-state.js';
import { translate } from '../translate.js';
import { readStdin } from './_shared.js';

async function main(): Promise<void> {
  try {
    const raw = await readStdin();
    const env = JSON.parse(raw);
    const translated = translate(env);
    if (!translated || translated.event.kind !== 'session-started') return;
    writeSessionState(
      translated.context.project_root,
      emptySessionState(translated.context, translated.transcript_path),
    );
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
