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
        kind: 'persist-tool-usage',
        record: {
          at: '2026-04-22T00:00:00.000Z',
          event_kind: 'post-read',
          tool_name: 'Read',
          target: 'Foo.cs',
          is_mcp_parlance: false,
          is_native_fallback: true,
          output_tokens: 100,
        },
      }],
      next_state: null,
    };

    renderToStderr(evaluation, (s) => { lines.push(s); });
    expect(lines).toHaveLength(0);
  });
});
