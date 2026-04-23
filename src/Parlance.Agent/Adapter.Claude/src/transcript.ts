import { existsSync, readFileSync } from 'node:fs';
import type { UsageTotals } from '@parlance/agent-core';

interface RawRecord {
  type?: string;
  timestamp?: string;
  gitBranch?: string;
  message?: {
    usage?: {
      input_tokens?: number;
      output_tokens?: number;
      cache_creation_input_tokens?: number;
      cache_read_input_tokens?: number;
    };
  };
}

export interface ParsedTranscript {
  branch: string | null;
  records: RawRecord[];
}

export function parseTranscript(path: string): ParsedTranscript | null {
  if (!existsSync(path)) return null;

  const records = readFileSync(path, 'utf8')
    .split('\n')
    .filter(Boolean)
    .map((line) => JSON.parse(line) as RawRecord);

  const branch = records.find((record) => typeof record.gitBranch === 'string')?.gitBranch ?? null;
  return { branch, records };
}

export function aggregateUsageBetween(
  records: RawRecord[],
  start?: string,
  end?: string,
): UsageTotals {
  const startMs = start ? Date.parse(start) : Number.NEGATIVE_INFINITY;
  const endMs = end ? Date.parse(end) : Number.POSITIVE_INFINITY;

  return records.reduce<UsageTotals>((totals, record) => {
    const ts = record.timestamp ? Date.parse(record.timestamp) : Number.NaN;
    if (Number.isNaN(ts) || ts < startMs || ts > endMs) return totals;

    const usage = record.message?.usage;
    if (!usage) return totals;

    totals.input_tokens += usage.input_tokens ?? 0;
    totals.output_tokens += usage.output_tokens ?? 0;
    totals.cache_read_tokens += usage.cache_read_input_tokens ?? 0;
    totals.cache_write_tokens += usage.cache_creation_input_tokens ?? 0;
    return totals;
  }, {
    input_tokens: 0,
    output_tokens: 0,
    cache_read_tokens: 0,
    cache_write_tokens: 0,
  });
}
