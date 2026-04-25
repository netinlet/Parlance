import { describe, expect, it } from 'vitest';
import { capabilities } from '../src/capabilities.js';

describe('capabilities', () => {
  it('reports codex adapter fidelity', () => {
    expect(capabilities.name).toBe('codex');
    expect(capabilities.events['session-started']).toBe('supported');
    expect(capabilities.events['pre-native-tool']).toBe('supported');
    expect(capabilities.events['post-mcp-tool']).toBe('supported');
    expect(capabilities.events['pre-search']).toBe('best-effort');
    expect(capabilities.events['post-read']).toBe('best-effort');
    expect(capabilities.outputs).toEqual({
      can_warn: true,
      can_block: true,
      can_inject_context: true,
    });
  });
});
