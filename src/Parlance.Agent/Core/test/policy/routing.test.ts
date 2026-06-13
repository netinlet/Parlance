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
    expect(
      matchRoutingRule({
        kind: 'pre-read',
        at: '2026-04-22T00:00:00Z',
        path: 'README.md',
      }),
    ).toBeNull();
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
    expect(
      matchRoutingRule({
        kind: 'pre-search',
        at: '2026-04-22T00:00:00Z',
        pattern: 'X',
        path: '/proj/docs',
      }),
    ).toBeNull();
  });

  it('Glob with *.cs pattern matches', () => {
    const match = matchRoutingRule({
      kind: 'pre-search',
      at: '2026-04-22T00:00:00Z',
      pattern: 'src/**/*.cs',
    });
    expect(match).not.toBeNull();
  });

  it('Bash with no code-intel util returns null', () => {
    expect(
      matchRoutingRule({
        kind: 'pre-native-tool',
        at: '2026-04-22T00:00:00Z',
        tool_name: 'Bash',
        input: { command: 'ls' },
      }),
    ).toBeNull();
  });

  const bash = (command: string) =>
    matchRoutingRule({
      kind: 'pre-native-tool',
      at: '2026-04-22T00:00:00Z',
      tool_name: 'Bash',
      input: { command },
    });

  it('Bash grep over .cs matches search-symbols', () => {
    expect(
      bash('grep -rn "class Foo" --include=*.cs src')?.suggested_tool,
    ).toContain('search-symbols');
  });

  it('Bash rg -tcs matches search-symbols', () => {
    expect(bash('rg -tcs IAnalysisEngine')?.suggested_tool).toContain(
      'search-symbols',
    );
  });

  it('Bash find -name *.cs matches search-symbols', () => {
    expect(bash('find . -name "*.cs"')?.suggested_tool).toContain(
      'search-symbols',
    );
  });

  it('Bash cat of a .cs file maps to describe-type', () => {
    expect(bash('cat src/Foo.cs')?.suggested_tool).toContain('describe-type');
  });

  it('Bash grep over non-C# does not match', () => {
    expect(bash('grep -rn TODO README.md')).toBeNull();
  });

  it('Bash build command (no read/search util) does not match', () => {
    expect(bash('dotnet build Parlance.slnx')).toBeNull();
  });

  it('isParlanceTool true for mcp__parlance__ prefix', () => {
    expect(isParlanceTool('mcp__parlance__search-symbols')).toBe(true);
  });

  it('isParlanceTool false for Read', () => {
    expect(isParlanceTool('Read')).toBe(false);
  });
});
