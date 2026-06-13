import { describe, expect, it } from 'vitest';
import { generateRoutingDoc, generateSessionContext } from '../../src/commands/routing-doc.js';

describe('routing doc', () => {
  it('documents the current routing examples', () => {
    const body = generateRoutingDoc();

    expect(body).toContain('# Parlance Tool Routing');
    expect(body).toContain('mcp__parlance__describe-type');
    expect(body).toContain('mcp__parlance__search-symbols');
  });
});

describe('session context', () => {
  it('prepends a tool-first preamble to the routing rules', () => {
    const body = generateSessionContext();

    expect(body).toContain('Prefer them over native Read/Grep/Glob');
    expect(body).toContain('# Parlance Tool Routing');
    expect(body).toContain('mcp__parlance__describe-type');
    expect(body).toContain('mcp__parlance__search-symbols');
  });
});
