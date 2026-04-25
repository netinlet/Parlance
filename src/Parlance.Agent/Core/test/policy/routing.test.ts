import { describe, expect, it } from 'vitest';
import { isParlanceTool, matchRoutingRule } from '../../src/policy/routing.js';

describe('routing', () => {
  it('Read on .cs file matches', () => {
    const match = matchRoutingRule({
      kind: 'pre-read',
      at: '2026-04-22T00:00:00Z',
      path: '/proj/src/Foo.cs',
    });
    expect(match?.suggested_tool).toMatch(/describe-type|outline-file/);
  });

  it('Read on .md file does not match', () => {
    expect(matchRoutingRule({
      kind: 'pre-read',
      at: '2026-04-22T00:00:00Z',
      path: 'README.md',
    })).toBeNull();
  });

  it('Grep with type=cs matches', () => {
    const match = matchRoutingRule({
      kind: 'pre-search',
      at: '2026-04-22T00:00:00Z',
      pattern: 'class Foo',
      file_type: 'cs',
    });
    expect(match?.suggested_tool).toContain('search-symbols');
  });

  it('Grep with glob *.cs matches', () => {
    const match = matchRoutingRule({
      kind: 'pre-search',
      at: '2026-04-22T00:00:00Z',
      pattern: 'X',
      glob: '**/*.cs',
    });
    expect(match).not.toBeNull();
  });

  it('Grep in /src/ with no glob/type matches as soft fallback', () => {
    const match = matchRoutingRule({
      kind: 'pre-search',
      at: '2026-04-22T00:00:00Z',
      pattern: 'X',
      path: '/proj/src/',
    });
    expect(match).not.toBeNull();
  });

  it('Grep on non-C# path does not match', () => {
    expect(matchRoutingRule({
      kind: 'pre-search',
      at: '2026-04-22T00:00:00Z',
      pattern: 'X',
      path: '/proj/docs',
    })).toBeNull();
  });

  it('Glob with *.cs pattern matches', () => {
    const match = matchRoutingRule({
      kind: 'pre-search',
      at: '2026-04-22T00:00:00Z',
      pattern: 'src/**/*.cs',
    });
    expect(match).not.toBeNull();
  });

  it('Unknown tool returns null', () => {
    expect(matchRoutingRule({
      kind: 'pre-native-tool',
      at: '2026-04-22T00:00:00Z',
      tool_name: 'Bash',
      input: { command: 'ls' },
    })).toBeNull();
  });

  it('isParlanceTool true for mcp__parlance__ prefix', () => {
    expect(isParlanceTool('mcp__parlance__search-symbols')).toBe(true);
  });

  it('isParlanceTool false for Read', () => {
    expect(isParlanceTool('Read')).toBe(false);
  });
});
