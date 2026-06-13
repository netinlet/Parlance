#!/usr/bin/env node

// src/commands/bench.ts
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

// src/storage/paths.ts
import { join } from "node:path";
var parlanceDir = (root) => join(root, ".parlance");
var ledgerFile = (root) => join(parlanceDir(root), "ledger.jsonl");
var sessionLogFile = (root) => join(parlanceDir(root), "session-log.md");
var benchResultsFile = (root) => join(parlanceDir(root), "bench", "results.jsonl");

// src/commands/bench.ts
async function runBench(argv) {
  const [action, ...rest] = argv;
  if (action !== "report") {
    process.stderr.write("usage: bench report --task <id> [--project <path>]\n");
    return 2;
  }
  let project = process.cwd();
  let task;
  for (let index = 0; index < rest.length; index += 1) {
    if (rest[index] === "--project" && rest[index + 1]) project = rest[index + 1];
    if (rest[index] === "--task" && rest[index + 1]) task = rest[index + 1];
  }
  if (!task) {
    process.stderr.write("--task required\n");
    return 2;
  }
  const path = benchResultsFile(resolve(project));
  if (!existsSync(path)) {
    process.stdout.write(`no bench data at ${path}
`);
    return 0;
  }
  const records = readFileSync(path, "utf8").split("\n").filter(Boolean).map((line) => JSON.parse(line)).filter((record) => record.task_id === task);
  if (records.length === 0) {
    process.stdout.write(`no bench records for task ${task}
`);
    return 0;
  }
  const lines = [];
  lines.push(`=== Bench: ${task} ===`);
  lines.push(
    `${"Variant".padEnd(14)}${"Adapter".padEnd(14)}${"Input".padStart(12)}${"Output".padStart(12)}${"Cache-read".padStart(14)}${"Duration".padStart(12)}`
  );
  lines.push("-".repeat(78));
  for (const record of records) {
    const durationS = Math.round(
      (new Date(record.ended_at).getTime() - new Date(record.started_at).getTime()) / 1e3
    );
    lines.push(
      record.variant.padEnd(14) + record.adapter.padEnd(14) + record.usage.input_tokens.toLocaleString().padStart(12) + record.usage.output_tokens.toLocaleString().padStart(12) + record.usage.cache_read_tokens.toLocaleString().padStart(14) + `${durationS}s`.padStart(12)
    );
  }
  process.stdout.write(`${lines.join("\n")}
`);
  return 0;
}

// src/commands/report.ts
import { existsSync as existsSync2, readFileSync as readFileSync2 } from "node:fs";
import { resolve as resolve2 } from "node:path";
async function runReport(argv) {
  const args = parseArgs(argv);
  const path = ledgerFile(resolve2(args.project));
  if (!existsSync2(path)) {
    process.stdout.write(`no ledger at ${path}
`);
    return 0;
  }
  const rows = readFileSync2(path, "utf8").split("\n").filter(Boolean).map((line) => JSON.parse(line));
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
    duration: acc.duration + row.duration_s
  }), {
    parlance: 0,
    fallback: 0,
    input: 0,
    output: 0,
    cacheRead: 0,
    reads: 0,
    writes: 0,
    duration: 0
  });
  const lines = [];
  lines.push(`=== Parlance Agent Report: ${range.start} -> ${range.end} ===`);
  lines.push(
    `Sessions: ${filtered.length}  |  Parlance calls: ${totals.parlance}  |  Native fallbacks: ${totals.fallback}  |  Duration: ${Math.round(totals.duration / 60)}m`
  );
  lines.push(
    `LLM tokens - input: ${totals.input.toLocaleString()}  output: ${totals.output.toLocaleString()}  cache-read: ${totals.cacheRead.toLocaleString()}`
  );
  lines.push(`Estimated file content - read: ${totals.reads}  write: ${totals.writes}`);
  lines.push("");
  lines.push(
    `${"Date".padEnd(12)}${"Session".padEnd(10)}${"Adapter".padEnd(14)}${"Branch".padEnd(16)}${"Parlance".padStart(10)}${"Fallback".padStart(10)}${"Input".padStart(10)}${"Output".padStart(10)}`
  );
  lines.push("-".repeat(92));
  for (const row of filtered) {
    lines.push(
      row.date.padEnd(12) + row.session_id.slice(0, 8).padEnd(10) + row.adapter.slice(0, 13).padEnd(14) + (row.branch ?? "").slice(0, 15).padEnd(16) + String(row.parlance_calls).padStart(10) + String(row.native_fallbacks).padStart(10) + String(row.usage.input_tokens).padStart(10) + String(row.usage.output_tokens).padStart(10)
    );
  }
  process.stdout.write(`${lines.join("\n")}
`);
  return 0;
}
function parseArgs(argv) {
  const args = { project: process.cwd(), days: 7 };
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === "--project" && argv[index + 1]) args.project = argv[index + 1];
    if (argv[index] === "--days" && argv[index + 1]) args.days = parseInt(argv[index + 1], 10);
    if (argv[index] === "--since" && argv[index + 1]) args.since = argv[index + 1];
    if (argv[index] === "--until" && argv[index + 1]) args.until = argv[index + 1];
  }
  return args;
}
function resolveRange(args) {
  const end = args.until ?? (/* @__PURE__ */ new Date()).toISOString().slice(0, 10);
  const start = args.since ?? shiftDays(end, -(args.days - 1));
  return { start, end };
}
function shiftDays(iso, days) {
  const date = /* @__PURE__ */ new Date(`${iso}T00:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

// src/commands/status.ts
import { existsSync as existsSync3, readFileSync as readFileSync3 } from "node:fs";
import { resolve as resolve3 } from "node:path";
async function runStatus(argv) {
  let project = process.cwd();
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === "--project" && argv[index + 1]) project = argv[index + 1];
  }
  const root = resolve3(project);
  process.stdout.write(`.parlance/ dir: ${existsSync3(parlanceDir(root)) ? "present" : "missing"}
`);
  const ledgerPath = ledgerFile(root);
  if (existsSync3(ledgerPath)) {
    const lines = readFileSync3(ledgerPath, "utf8").trim().split("\n").filter(Boolean);
    process.stdout.write(`sessions logged: ${lines.length}
`);
  }
  const logPath = sessionLogFile(root);
  if (existsSync3(logPath)) {
    const tail = readFileSync3(logPath, "utf8").trim().split("\n").slice(-5);
    process.stdout.write("recent:\n");
    for (const line of tail) process.stdout.write(`  ${line}
`);
  }
  return 0;
}

// src/cli.ts
var commands = {
  status: runStatus,
  report: runReport,
  bench: runBench
};
async function main() {
  const [, , subcommand, ...rest] = process.argv;
  if (!subcommand || subcommand === "--help" || subcommand === "-h") {
    help();
    process.exit(subcommand ? 0 : 1);
  }
  const command = commands[subcommand];
  if (!command) {
    process.stderr.write(`unknown: ${subcommand}
`);
    help();
    process.exit(2);
  }
  process.exit(await command(rest));
}
function help() {
  process.stderr.write([
    "usage: parlance-agent-core <command> [args]",
    "",
    "  status                       install state + recent ledger summary",
    "  report [--days N]            session analysis",
    "  bench report --task <id>     variant comparison",
    ""
  ].join("\n"));
}
void main();
