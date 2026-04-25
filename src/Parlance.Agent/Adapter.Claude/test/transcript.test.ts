import { describe, expect, it } from 'vitest';
import { fileURLToPath } from 'node:url';
import { aggregateUsageBetween, parseTranscript } from '../src/transcript.js';

const fixture = fileURLToPath(new URL('./fixtures/transcript-sample.jsonl', import.meta.url));

describe('transcript', () => {
  it('parses transcript records and branch', () => {
    const parsed = parseTranscript(fixture);
    expect(parsed?.branch).toBe('main');
    expect(parsed?.records).toHaveLength(3);
  });

  it('aggregates usage across all assistant records', () => {
    const parsed = parseTranscript(fixture)!;
    const usage = aggregateUsageBetween(parsed.records);
    expect(usage.input_tokens).toBe(2200);
    expect(usage.output_tokens).toBe(650);
    expect(usage.cache_read_tokens).toBe(5000);
    expect(usage.cache_write_tokens).toBe(0);
  });

  it('aggregates usage within a time window', () => {
    const parsed = parseTranscript(fixture)!;
    const usage = aggregateUsageBetween(parsed.records, '2026-04-22T14:30:15Z', '2026-04-22T14:31:00Z');
    expect(usage.input_tokens).toBe(1200);
    expect(usage.output_tokens).toBe(400);
  });
});
