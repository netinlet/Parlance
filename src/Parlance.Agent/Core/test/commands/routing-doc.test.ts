import { describe, expect, it } from 'vitest';
import { generateRoutingDoc } from '../../src/commands/routing-doc.js';

describe('routing doc', () => {
  it('documents the current routing examples', () => {
    const body = generateRoutingDoc();

    expect(body).toContain('# Parlance Tool Routing');
    expect(body).toContain('mcp__parlance__describe-type');
    expect(body).toContain('mcp__parlance__search-symbols');
  });
});
