import { describe, expect, it } from 'vitest';
import { classifyFallback } from '../../src/policy/fallback.js';

describe('fallback', () => {
  it('classifies pre-read on C# as read fallback', () => {
    const result = classifyFallback({
      kind: 'pre-read',
      at: '2026-04-22T00:00:00Z',
      path: 'Foo.cs',
    });

    expect(result?.native_tool).toBe('read');
    expect(result?.intent).toContain('Foo.cs');
  });

  it('returns null when no routing rule matched', () => {
    const result = classifyFallback({
      kind: 'pre-read',
      at: '2026-04-22T00:00:00Z',
      path: 'README.md',
    });

    expect(result).toBeNull();
  });
});
