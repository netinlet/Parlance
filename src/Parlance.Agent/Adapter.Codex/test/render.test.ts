import { describe, expect, it } from 'vitest';
import type { EventEvaluation } from '@parlance/agent-core';
import { renderForCodex, writeCodexOutput } from '../src/render.js';

const warning: EventEvaluation = {
  guidance: [{ severity: 'warn', message: 'Use Parlance first', suggested_tool: 'mcp__parlance__search-symbols' }],
  effects: [],
  next_state: null,
};

describe('renderForCodex', () => {
  it('SessionStart guidance becomes additionalContext', () => {
    const output = renderForCodex('SessionStart', warning);

    expect(output?.hookSpecificOutput).toEqual({
      hookEventName: 'SessionStart',
      additionalContext: 'parlance: Use Parlance first Suggested: mcp__parlance__search-symbols.',
    });
  });

  it('UserPromptSubmit guidance becomes additionalContext', () => {
    const output = renderForCodex('UserPromptSubmit', warning);

    expect(output?.hookSpecificOutput?.hookEventName).toBe('UserPromptSubmit');
  });

  it('PreToolUse guidance becomes systemMessage without blocking', () => {
    const output = renderForCodex('PreToolUse', warning);

    expect(output).toEqual({
      systemMessage: 'parlance: Use Parlance first Suggested: mcp__parlance__search-symbols.',
    });
    expect(output).not.toHaveProperty('decision');
    expect(output).not.toHaveProperty('hookSpecificOutput');
  });

  it('PostToolUse guidance does not replace tool output', () => {
    const output = renderForCodex('PostToolUse', warning);

    expect(output).toEqual({
      systemMessage: 'parlance: Use Parlance first Suggested: mcp__parlance__search-symbols.',
    });
    expect(output).not.toHaveProperty('decision');
    expect(output).not.toHaveProperty('continue', false);
    expect(output).not.toHaveProperty('hookSpecificOutput');
  });

  it('returns null when there is no guidance', () => {
    expect(renderForCodex('PreToolUse', { guidance: [], effects: [], next_state: null })).toBeNull();
  });
});

describe('writeCodexOutput', () => {
  it('writes newline-delimited JSON', () => {
    const chunks: string[] = [];
    writeCodexOutput({ systemMessage: 'x' }, (chunk) => { chunks.push(chunk); });

    expect(chunks.join('')).toBe('{"systemMessage":"x"}\n');
  });

  it('writes nothing for null output', () => {
    const chunks: string[] = [];
    writeCodexOutput(null, (chunk) => { chunks.push(chunk); });

    expect(chunks).toEqual([]);
  });
});
