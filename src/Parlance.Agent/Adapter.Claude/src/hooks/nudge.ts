import { planSessionStart, runNudge } from '@parlance/agent-core';
import { capabilities } from '../capabilities.js';
import { writeContextOutput } from '../render.js';
import { translate } from '../translate.js';
import { readStdin } from './_shared.js';

/**
 * The *global* hook (wired once in ~/.claude/settings.json). It does exactly one
 * thing: when you open a C# project that has no Parlance MCP wired, it reminds
 * you to install it. It never writes state, never injects routing, and stays
 * silent for non-C# projects and already-wired ones — all the real tracking and
 * routing is per-project, installed by `parlance agent install`. Keeping global
 * to nudge-only is what avoids double-firing against the per-project hooks.
 */
async function main(): Promise<void> {
  try {
    const raw = await readStdin();
    const env = JSON.parse(raw);
    const translated = translate(env);
    if (!translated || translated.event.kind !== 'session-started') return;

    const plan = planSessionStart(translated.context.project_root);
    runNudge(plan, capabilities.outputs.can_inject_context, writeContextOutput);
  } catch {
    // never block the host
  }
}

void main().then(() => process.exit(0));
