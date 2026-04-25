import { describe, expect, it } from 'vitest';
import type { EventEvaluation } from '@parlance/agent-core';
import { renderToStderr } from '../src/render.js';

describe('render', () => {
  it('guidance warn emits prefix + message', () => {
    const lines: string[] = [];
    const evaluation: EventEvaluation = {
      guidance: [{ severity: 'warn', message: 'Use X', suggested_tool: 'X' }],
      effects: [],
      next_state: null,
    };

    renderToStderr(evaluation, (s) => { lines.push(s); });
    expect(lines.join('')).toContain('⚡ parlance');
    expect(lines.join('')).toContain('Use X');
  });

  it('ignores effects entirely and only renders guidance', () => {
    const lines: string[] = [];
    const evaluation: EventEvaluation = {
      guidance: [],
      effects: [{
        kind: 'persist-feedback',
        feedback: {
          date: '2026-04-22',
          adapter: 'claude-code',
          native_tool: 'read',
          intent: 'x',
          why: 'y',
          suggested: 'z',
          session_id: 's',
        },
      }],
      next_state: null,
    };

    renderToStderr(evaluation, (s) => { lines.push(s); });
    expect(lines).toHaveLength(0);
  });
});
