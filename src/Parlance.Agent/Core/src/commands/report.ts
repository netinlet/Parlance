import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { ledgerFile } from '../storage/paths.js';
import type { SessionSummary } from '../types.js';

interface ReportArgs {
  project: string;
  days: number;
  since?: string;
  until?: string;
}

export async function runReport(argv: string[]): Promise<number> {
  const args = parseArgs(argv);
  const path = ledgerFile(resolve(args.project));
  if (!existsSync(path)) {
    process.stdout.write(`no ledger at ${path}\n`);
    return 0;
  }

  const rows = readFileSync(path, 'utf8')
    .split('\n')
    .filter(Boolean)
    .map((line) => JSON.parse(line) as SessionSummary);
  const range = resolveRange(args);
  const filtered = rows.filter((row) => row.date >= range.start && row.date <= range.end);

  const totals = filtered.reduce((acc, row) => ({
    parlance: acc.parlance + row.parlance_calls,
    fallback: acc.fallback + row.native_fallbacks,
    input: acc.input + row.usage.input_tokens,
    output: acc.output + row.usage.output_tokens,
    cacheRead: acc.cacheRead + row.usage.cache_read_tokens,
    reads: acc.reads + row.read_tokens,
    writes: acc.writes + row.write_tokens,
    duration: acc.duration + row.duration_s,
  }), {
    parlance: 0,
    fallback: 0,
    input: 0,
    output: 0,
    cacheRead: 0,
    reads: 0,
    writes: 0,
    duration: 0,
  });

  const lines: string[] = [];
  lines.push(`=== Parlance Agent Report: ${range.start} -> ${range.end} ===`);
  lines.push(
    `Sessions: ${filtered.length}  |  Parlance calls: ${totals.parlance}  |  Native fallbacks: ${totals.fallback}  |  Duration: ${Math.round(totals.duration / 60)}m`,
  );
  lines.push(
    `LLM tokens - input: ${totals.input.toLocaleString()}  output: ${totals.output.toLocaleString()}  cache-read: ${totals.cacheRead.toLocaleString()}`,
  );
  lines.push(`Estimated file content - read: ${totals.reads}  write: ${totals.writes}`);
  lines.push('');
  lines.push(
    `${'Date'.padEnd(12)}${'Session'.padEnd(10)}${'Adapter'.padEnd(14)}${'Branch'.padEnd(16)}${'Parlance'.padStart(10)}${'Fallback'.padStart(10)}${'Input'.padStart(10)}${'Output'.padStart(10)}`,
  );
  lines.push('-'.repeat(92));
  for (const row of filtered) {
    lines.push(
      row.date.padEnd(12)
      + row.session_id.slice(0, 8).padEnd(10)
      + row.adapter.slice(0, 13).padEnd(14)
      + (row.branch ?? '').slice(0, 15).padEnd(16)
      + String(row.parlance_calls).padStart(10)
      + String(row.native_fallbacks).padStart(10)
      + String(row.usage.input_tokens).padStart(10)
      + String(row.usage.output_tokens).padStart(10),
    );
  }

  process.stdout.write(`${lines.join('\n')}\n`);
  return 0;
}

function parseArgs(argv: string[]): ReportArgs {
  const args: ReportArgs = { project: process.cwd(), days: 7 };
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === '--project' && argv[index + 1]) args.project = argv[index + 1];
    if (argv[index] === '--days' && argv[index + 1]) args.days = parseInt(argv[index + 1], 10);
    if (argv[index] === '--since' && argv[index + 1]) args.since = argv[index + 1];
    if (argv[index] === '--until' && argv[index + 1]) args.until = argv[index + 1];
  }
  return args;
}

function resolveRange(args: ReportArgs): { start: string; end: string } {
  const end = args.until ?? new Date().toISOString().slice(0, 10);
  const start = args.since ?? shiftDays(end, -(args.days - 1));
  return { start, end };
}

function shiftDays(iso: string, days: number): string {
  const date = new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}
