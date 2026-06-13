import { existsSync, readFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { ledgerFile, parlanceDir } from '../storage/paths.js';
import type { SessionSummary } from '../types.js';

interface ReportArgs {
  project?: string;
  days: number;
  since?: string;
  until?: string;
}

export async function runReport(argv: string[]): Promise<number> {
  const args = parseArgs(argv);
  const path = ledgerFile();

  const rows: SessionSummary[] = [];

  // Central ledger (new layout).
  if (existsSync(path)) {
    rows.push(
      ...readFileSync(path, 'utf8')
        .split('\n')
        .filter(Boolean)
        .map((line) => JSON.parse(line) as SessionSummary),
    );
  }

  // Legacy per-project ledger (old layout: .parlance/ledger.jsonl in cwd).
  const legacyPath = join(parlanceDir(process.cwd()), 'ledger.jsonl');
  if (existsSync(legacyPath)) {
    rows.push(
      ...readFileSync(legacyPath, 'utf8')
        .split('\n')
        .filter(Boolean)
        .map((line) => JSON.parse(line) as SessionSummary),
    );
  }

  if (rows.length === 0) {
    process.stdout.write(`no ledger at ${path}\n`);
    return 0;
  }

  const range = resolveRange(args);
  const filtered = rows.filter((row) =>
    row.date >= range.start && row.date <= range.end
    && (!args.project || basename(row.project ?? '') === args.project));

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

  const tools = aggregateToolBreakdown(filtered);
  if (tools.length > 0) {
    lines.push('Top tools (calls):');
    for (const [tool, count] of tools.slice(0, 12)) {
      const tag = tool.startsWith('mcp__parlance__') ? 'parlance' : 'native  ';
      lines.push(`  ${String(count).padStart(6)}  ${tag}  ${tool}`);
    }
    lines.push('');
  }

  lines.push(
    `${'Date'.padEnd(12)}${'Session'.padEnd(10)}${'Project'.padEnd(18)}${'Adapter'.padEnd(13)}${'Parlance'.padStart(9)}${'Fallback'.padStart(9)}${'Output'.padStart(9)}`,
  );
  lines.push('-'.repeat(89));
  for (const row of filtered) {
    lines.push(
      row.date.padEnd(12)
      + row.session_id.slice(0, 8).padEnd(10)
      + basename(row.project ?? '').slice(0, 17).padEnd(18)
      + row.adapter.slice(0, 12).padEnd(13)
      + String(row.parlance_calls).padStart(9)
      + String(row.native_fallbacks).padStart(9)
      + String(row.usage.output_tokens).padStart(9),
    );
  }

  process.stdout.write(`${lines.join('\n')}\n`);
  return 0;
}

function aggregateToolBreakdown(rows: SessionSummary[]): [string, number][] {
  const totals: Record<string, number> = {};
  for (const row of rows) {
    for (const [tool, count] of Object.entries(row.tool_breakdown ?? {})) {
      totals[tool] = (totals[tool] ?? 0) + count;
    }
  }
  return Object.entries(totals).sort((a, b) => b[1] - a[1]);
}

function parseArgs(argv: string[]): ReportArgs {
  const args: ReportArgs = { days: 7 };
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
