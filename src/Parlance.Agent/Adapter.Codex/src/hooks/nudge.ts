import { parlanceCodexWired, planSessionStart, runNudge } from '@parlance/agent-core';
import { capabilities } from '../capabilities.js';
import { writeCodexOutput } from '../render.js';
import { translate } from '../translate.js';
import { readEnvelope } from './_shared.js';

/**
 * The *global* hook (wired once at the user level). It does exactly one thing:
 * when you open a C# project with no Parlance Codex hooks installed, it reminds
 * you to install them. It never writes state, never injects routing, and is
 * silent for non-C# and already-wired projects — all real tracking/routing is
 * per-project. Nudge-only is what keeps it from double-firing against the
 * per-project hooks.
 *
 * Uses parlanceCodexWired (not parlanceAgentInstalled) so that a Claude-only
 * .mcp.json in the repo doesn't suppress the nudge for Codex users.
 */
async function main(): Promise<void> {
  try {
    const env = await readEnvelope();
    const translated = translate(env);
    if (!translated || translated.event.kind !== 'session-started') return;

    const plan = planSessionStart(translated.context.project_root, parlanceCodexWired);
    runNudge(plan, capabilities.outputs.can_inject_context, (ctx) =>
      writeCodexOutput({
        hookSpecificOutput: { hookEventName: 'SessionStart', additionalContext: ctx },
      }),
    );
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
