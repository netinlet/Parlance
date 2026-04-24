import { describe, expect, it } from 'vitest';
import { postRead, postTool, preRead, preTool } from '../../src/events.js';
import { emptySessionState, evaluateEvent } from '../../src/policy/evaluate.js';
import type { AgentContext } from '../../src/types.js';

const ctx: AgentContext = {
  project_root: '/proj',
  session_id: 's1',
  cwd: '/proj',
  adapter: 'claude-code',
  capabilities: {
    name: 'claude-code',
    events: {
      'pre-read': 'supported',
      'post-read': 'supported',
      'pre-native-tool': 'supported',
      'post-native-tool': 'supported',
      'pre-mcp-tool': 'supported',
      'post-mcp-tool': 'supported',
    },
    outputs: { can_warn: true, can_block: false, can_inject_context: false },
  },
};

const state = emptySessionState(ctx, null);

describe('evaluateEvent', () => {
  it('pre-read on .cs emits warn guidance + persist-feedback effect', () => {
    const evaluation = evaluateEvent(preRead('Foo.cs'), ctx, state);
    expect(evaluation.guidance.some((g) => g.severity === 'warn')).toBe(true);
    expect(evaluation.effects.some((e) => e.kind === 'persist-feedback')).toBe(true);
  });

  it('pre-mcp-tool on parlance emits no guidance', () => {
    const evaluation = evaluateEvent(preTool('pre-mcp-tool', 'mcp__parlance__describe-type', {}), ctx, state);
    expect(evaluation.guidance.length).toBe(0);
  });

  it('post-native-tool emits a persist-tool-usage effect', () => {
    const evaluation = evaluateEvent(postTool('post-native-tool', 'Read', { file_path: 'a.cs' }, 700), ctx, state);
    expect(evaluation.effects.some((e) => e.kind === 'persist-tool-usage')).toBe(true);
  });

  it('post-mcp-tool on parlance sets is_mcp_parlance=true on usage effect', () => {
    const evaluation = evaluateEvent(postTool('post-mcp-tool', 'mcp__parlance__describe-type', {}, 120), ctx, state);
    const effect = evaluation.effects.find((e) => e.kind === 'persist-tool-usage');
    expect(effect && effect.kind === 'persist-tool-usage' && effect.record.is_mcp_parlance).toBe(true);
  });

  it('post-read on .cs increments native_fallbacks in next_state', () => {
    const evaluation = evaluateEvent(postRead('Foo.cs', 700), ctx, state);
    expect(evaluation.next_state?.native_fallbacks).toBe(1);
  });

  it('returns next_state: null for events that do not change state', () => {
    const evaluation = evaluateEvent(preRead('README.md'), ctx, state);
    expect(evaluation.next_state).toBeNull();
  });

  it('pre-read does not mutate state (counting happens on post-read only)', () => {
    const evaluation = evaluateEvent(preRead('Foo.cs'), ctx, state);
    expect(evaluation.next_state).toBeNull();
  });

  it('pre+post pair for a .cs Read increments native_fallbacks exactly once', () => {
    const pre = evaluateEvent(preRead('Foo.cs'), ctx, state);
    const afterPre = pre.next_state ?? state;
    const post = evaluateEvent(postRead('Foo.cs', 700), ctx, afterPre);
    expect(post.next_state?.native_fallbacks).toBe(1);
  });
});
