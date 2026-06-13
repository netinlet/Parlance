import { emptySessionState, planSessionStart } from '@parlance/agent-core';
import { writeSessionState } from '@parlance/agent-core/storage/session-state.js';
import { capabilities } from '../capabilities.js';
import { writeContextOutput } from '../render.js';
import { translate } from '../translate.js';
import { readStdin } from './_shared.js';

async function main(): Promise<void> {
  try {
    const raw = await readStdin();
    const env = JSON.parse(raw);
    const translated = translate(env);
    if (!translated || translated.event.kind !== 'session-started') return;

    const plan = planSessionStart(translated.context.project_root);

    // Only track where Parlance is actually wired — never litter unrelated repos.
    if (plan.kind === 'wired') {
      writeSessionState(
        translated.context.project_root,
        emptySessionState(translated.context, translated.transcript_path),
      );
    }

    // Inject routing guidance (wired) or an install reminder (C# but unwired).
    if (plan.kind !== 'idle' && capabilities.outputs.can_inject_context) {
      writeContextOutput(plan.context);
    }
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
