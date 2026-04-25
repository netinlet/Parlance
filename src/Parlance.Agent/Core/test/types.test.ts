import { describe, expect, it } from 'vitest';
import * as typesModule from '../src/types.js';
import type { AdapterCapabilities, AgentEvent, EventEvaluation } from '../src/types.js';

describe('types', () => {
  it('types module exists', () => {
    expect(typesModule).toBeTypeOf('object');
  });

  it('AgentEvent union accepts a pre-read event', () => {
    const event: AgentEvent = {
      kind: 'pre-read',
      at: '2026-04-22T00:00:00Z',
      path: 'Foo.cs',
    };

    expect(event.kind).toBe('pre-read');
  });

  it('EventEvaluation carries guidance, effects, and next state', () => {
    const evaluation: EventEvaluation = {
      guidance: [{ severity: 'warn', message: 'Use Parlance first' }],
      effects: [],
      next_state: null,
    };

    expect(evaluation.guidance[0].severity).toBe('warn');
  });

  it('AdapterCapabilities lists event fidelities', () => {
    const capabilities: AdapterCapabilities = {
      name: 'test',
      events: {
        'pre-read': 'supported',
        'response-completed': 'best-effort',
      },
      outputs: {
        can_warn: true,
        can_block: false,
        can_inject_context: false,
      },
    };

    expect(capabilities.name).toBe('test');
  });
});
