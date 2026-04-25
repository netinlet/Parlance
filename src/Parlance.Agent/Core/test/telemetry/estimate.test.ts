import { describe, expect, it } from 'vitest';
import { estimateFromExtension, estimateTokens } from '../../src/telemetry/estimate.js';

describe('estimate', () => {
  it('returns 0 for empty', () => {
    expect(estimateTokens('', 'code')).toBe(0);
  });

  it('uses 3.5 chars/token for code', () => {
    expect(estimateTokens('x'.repeat(350), 'code')).toBe(100);
  });

  it('uses 4.0 chars/token for prose', () => {
    expect(estimateTokens('x'.repeat(400), 'prose')).toBe(100);
  });

  it('uses 3.75 chars/token for mixed', () => {
    expect(estimateTokens('x'.repeat(375), 'mixed')).toBe(100);
  });

  it('classifies .cs as code', () => {
    expect(estimateFromExtension('foo.cs', 'x'.repeat(350))).toBe(100);
  });

  it('classifies .md as prose', () => {
    expect(estimateFromExtension('README.md', 'x'.repeat(400))).toBe(100);
  });

  it('unknown extension falls back to mixed', () => {
    expect(estimateFromExtension('data.xyz', 'x'.repeat(375))).toBe(100);
  });
});
