#!/usr/bin/env node

// src/events.ts
var now = () => (/* @__PURE__ */ new Date()).toISOString();
var sessionStarted = (transcriptRef) => ({
  kind: "session-started",
  at: now(),
  transcript_ref: transcriptRef
});
var taskReceived = (prompt) => ({
  kind: "task-received",
  at: now(),
  prompt
});
var preRead = (path) => ({ kind: "pre-read", at: now(), path });
var postRead = (path, bytes) => ({
  kind: "post-read",
  at: now(),
  path,
  content_bytes: bytes
});
var preWrite = (path) => ({ kind: "pre-write", at: now(), path });
var postWrite = (path, bytes) => ({
  kind: "post-write",
  at: now(),
  path,
  content_bytes: bytes
});
var preSearch = (event) => ({
  kind: "pre-search",
  at: now(),
  ...event
});
var postSearch = (event) => ({
  kind: "post-search",
  at: now(),
  ...event
});
var preTool = (kind, tool, input) => ({
  kind,
  at: now(),
  tool_name: tool,
  input
});
var postTool = (kind, tool, input, bytes) => ({
  kind,
  at: now(),
  tool_name: tool,
  input,
  output_bytes: bytes
});
var responseCompleted = (usage) => ({
  kind: "response-completed",
  at: now(),
  usage
});

// src/telemetry/estimate.ts
import { extname } from "node:path";
var RATIOS = {
  code: 3.5,
  prose: 4,
  mixed: 3.75
};
var CODE_EXTENSIONS = /* @__PURE__ */ new Set([
  ".cs",
  ".csproj",
  ".sln",
  ".slnx",
  ".props",
  ".targets",
  ".ts",
  ".tsx",
  ".js",
  ".jsx",
  ".mjs",
  ".cjs",
  ".py",
  ".go",
  ".rs",
  ".java",
  ".kt",
  ".swift",
  ".c",
  ".cpp",
  ".h",
  ".hpp",
  ".sh",
  ".bash",
  ".zsh",
  ".ps1",
  ".json",
  ".yaml",
  ".yml",
  ".xml",
  ".toml"
]);
var PROSE_EXTENSIONS = /* @__PURE__ */ new Set([".md", ".txt", ".rst", ".adoc"]);
function estimateTokens(content, kind) {
  if (!content) return 0;
  return estimateTokensFromLength(content.length, kind);
}
function estimateTokensFromLength(length, kind) {
  if (length <= 0) return 0;
  return Math.round(length / RATIOS[kind]);
}
function classifyPath(path) {
  const ext = extname(path).toLowerCase();
  if (CODE_EXTENSIONS.has(ext)) return "code";
  if (PROSE_EXTENSIONS.has(ext)) return "prose";
  return "mixed";
}
function estimateFromExtension(path, content) {
  return estimateTokens(content, classifyPath(path));
}

// src/policy/routing.ts
var CS_FILE_PATTERN = /\.(cs|csproj|sln|slnx|props|targets)$/i;
var CS_GLOB_PATTERN = /(^|\/)\*\*?\/[^/]*\.(cs|csproj|sln|slnx|props|targets)$|(^|\/)[^/]*\.(cs|csproj|sln|slnx|props|targets)$/i;
function isParlanceTool(toolName) {
  return toolName.startsWith("mcp__parlance__");
}
function matchRoutingRule(event) {
  if (event.kind === "pre-read") {
    if (!CS_FILE_PATTERN.test(event.path)) return null;
    return {
      suggested_tool: "mcp__parlance__describe-type",
      message: "Use Parlance MCP tools before reading C# source directly.",
      reason: `pre-read on C# path ${event.path}`
    };
  }
  if (event.kind === "pre-search") {
    const hasCsType = event.file_type?.toLowerCase() === "cs";
    const hasCsGlob = event.glob ? CS_GLOB_PATTERN.test(event.glob) : false;
    const hasCsPattern = CS_GLOB_PATTERN.test(event.pattern);
    const softSrcPath = event.path?.includes("/src/") ?? false;
    if (!(hasCsType || hasCsGlob || hasCsPattern || softSrcPath)) return null;
    return {
      suggested_tool: "mcp__parlance__search-symbols",
      message: "Use Parlance symbol/search tools before grep/glob on C# workspace code.",
      reason: `pre-search for C# intent (${event.pattern})`
    };
  }
  return null;
}

// src/policy/fallback.ts
function classifyFallback(event) {
  const hit = matchRoutingRule(event);
  if (!hit) return null;
  return {
    native_tool: toNativeKind(event),
    intent: describeIntent(event),
    suggested: hit.suggested_tool,
    why: hit.reason
  };
}
function toNativeKind(event) {
  switch (event.kind) {
    case "pre-read":
    case "post-read":
      return "read";
    case "pre-write":
    case "post-write":
      return "write";
    case "pre-search":
    case "post-search":
      return "search";
    default:
      return "other";
  }
}
function describeIntent(event) {
  if (event.kind === "pre-read") return `read ${event.path}`;
  if (event.kind === "pre-write") return `write ${event.path}`;
  if (event.kind === "pre-search") {
    return `search ${event.pattern} (path=${event.path ?? ""} glob=${event.glob ?? ""} type=${event.file_type ?? ""})`;
  }
  return event.kind;
}

// src/policy/evaluate.ts
function emptySessionState(ctx, transcript_ref) {
  return {
    session_id: ctx.session_id,
    adapter: ctx.adapter,
    started_at: (/* @__PURE__ */ new Date()).toISOString(),
    cwd: ctx.cwd,
    transcript_ref,
    parlance_calls: 0,
    native_fallbacks: 0,
    tool_calls: [],
    read_tokens: 0,
    write_tokens: 0,
    active_bench: null
  };
}
function evaluateEvent(event, ctx, state) {
  const guidance = [];
  const effects = [];
  let next = null;
  if (event.kind.startsWith("pre-")) {
    const match = matchRoutingRule(event);
    if (match) {
      guidance.push({
        severity: "warn",
        message: match.message,
        suggested_tool: match.suggested_tool,
        reason: match.reason
      });
      const fallback = classifyFallback(event);
      if (fallback) {
        effects.push({
          kind: "persist-feedback",
          feedback: {
            date: event.at.slice(0, 10),
            adapter: ctx.adapter,
            native_tool: fallback.native_tool,
            intent: fallback.intent,
            why: fallback.why,
            suggested: fallback.suggested,
            session_id: ctx.session_id
          }
        });
      }
    }
  }
  if (event.kind === "post-read" || event.kind === "post-write" || event.kind === "post-search" || event.kind === "post-native-tool" || event.kind === "post-mcp-tool") {
    const record = toUsageRecord(event);
    effects.push({ kind: "persist-tool-usage", record });
    next = {
      ...state,
      parlance_calls: state.parlance_calls + (record.is_mcp_parlance ? 1 : 0),
      native_fallbacks: state.native_fallbacks + (record.is_native_fallback ? 1 : 0),
      read_tokens: state.read_tokens + (event.kind === "post-read" ? record.output_tokens : 0),
      write_tokens: state.write_tokens + (event.kind === "post-write" ? record.output_tokens : 0),
      tool_calls: [...state.tool_calls, record]
    };
  }
  return { guidance, effects, next_state: next };
}
function toUsageRecord(event) {
  const is_native_fallback = matchRoutingRule(flipToPre(event)) !== null;
  if (event.kind === "post-read" || event.kind === "post-write") {
    return {
      at: event.at,
      event_kind: event.kind,
      tool_name: event.kind === "post-read" ? "Read" : "Write",
      target: event.path,
      is_mcp_parlance: false,
      is_native_fallback,
      output_tokens: estimateTokensFromLength(event.content_bytes ?? 0, "code")
    };
  }
  if (event.kind === "post-search") {
    return {
      at: event.at,
      event_kind: event.kind,
      tool_name: "Search",
      target: `${event.pattern} (glob=${event.glob ?? ""} type=${event.file_type ?? ""})`,
      is_mcp_parlance: false,
      is_native_fallback,
      output_tokens: estimateTokensFromLength(event.result_bytes ?? 0, "code")
    };
  }
  const toolEvent = event;
  return {
    at: event.at,
    event_kind: event.kind,
    tool_name: toolEvent.tool_name,
    target: JSON.stringify(toolEvent.input).slice(0, 80),
    is_mcp_parlance: isParlanceTool(toolEvent.tool_name),
    is_native_fallback,
    output_tokens: estimateTokensFromLength(toolEvent.output_bytes ?? 0, "code")
  };
}
function flipToPre(event) {
  if (event.kind === "post-read") return { ...event, kind: "pre-read" };
  if (event.kind === "post-write") return { ...event, kind: "pre-write" };
  if (event.kind === "post-search") return { ...event, kind: "pre-search" };
  if (event.kind === "post-native-tool") return { ...event, kind: "pre-native-tool" };
  if (event.kind === "post-mcp-tool") return { ...event, kind: "pre-mcp-tool" };
  return event;
}

// src/commands/report.ts
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

// src/storage/paths.ts
import { join } from "node:path";
var parlanceDir = (root) => join(root, ".parlance");
var ledgerFile = (root) => join(parlanceDir(root), "ledger.jsonl");
var sessionLogFile = (root) => join(parlanceDir(root), "session-log.md");
var benchResultsFile = (root) => join(parlanceDir(root), "bench", "results.jsonl");

// src/commands/report.ts
async function runReport(argv) {
  const args = parseArgs(argv);
  const path = ledgerFile(resolve(args.project));
  if (!existsSync(path)) {
    process.stdout.write(`no ledger at ${path}
`);
    return 0;
  }
  const rows = readFileSync(path, "utf8").split("\n").filter(Boolean).map((line) => JSON.parse(line));
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
import { existsSync as existsSync2, readFileSync as readFileSync2 } from "node:fs";
import { resolve as resolve2 } from "node:path";
async function runStatus(argv) {
  let project = process.cwd();
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === "--project" && argv[index + 1]) project = argv[index + 1];
  }
  const root = resolve2(project);
  process.stdout.write(`.parlance/ dir: ${existsSync2(parlanceDir(root)) ? "present" : "missing"}
`);
  const ledgerPath = ledgerFile(root);
  if (existsSync2(ledgerPath)) {
    const lines = readFileSync2(ledgerPath, "utf8").trim().split("\n").filter(Boolean);
    process.stdout.write(`sessions logged: ${lines.length}
`);
  }
  const logPath = sessionLogFile(root);
  if (existsSync2(logPath)) {
    const tail = readFileSync2(logPath, "utf8").trim().split("\n").slice(-5);
    process.stdout.write("recent:\n");
    for (const line of tail) process.stdout.write(`  ${line}
`);
  }
  return 0;
}

// src/commands/bench.ts
import { existsSync as existsSync3, readFileSync as readFileSync3 } from "node:fs";
import { resolve as resolve3 } from "node:path";
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
  const path = benchResultsFile(resolve3(project));
  if (!existsSync3(path)) {
    process.stdout.write(`no bench data at ${path}
`);
    return 0;
  }
  const records = readFileSync3(path, "utf8").split("\n").filter(Boolean).map((line) => JSON.parse(line)).filter((record) => record.task_id === task);
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

// src/commands/routing-doc.ts
function generateRoutingDoc() {
  const samples = [
    { kind: "pre-read", at: "", path: "Foo.cs" },
    { kind: "pre-search", at: "", pattern: "x", file_type: "cs" },
    { kind: "pre-search", at: "", pattern: "x", glob: "**/*.cs" },
    { kind: "pre-search", at: "", pattern: "x", path: "/proj/src/sub" }
  ];
  const lines = ["# Parlance Tool Routing", "", "Generated from agent-core routing rules.", ""];
  for (const event of samples) {
    const hit = matchRoutingRule(event);
    if (!hit) continue;
    lines.push(`## ${describe(event)}`);
    lines.push(`- **Suggested:** \`${hit.suggested_tool}\``);
    lines.push(`- ${hit.message}`);
    lines.push("");
  }
  return lines.join("\n");
}
function describe(event) {
  if (event.kind === "pre-read") return "Reading a C# file";
  if (event.kind === "pre-search" && event.file_type === "cs") return "Searching with type=cs";
  if (event.kind === "pre-search" && event.glob?.includes(".cs")) return "Searching with C# glob";
  if (event.kind === "pre-search") return "Searching under /src/ (no filter)";
  return event.kind;
}
export {
  classifyPath,
  emptySessionState,
  estimateFromExtension,
  estimateTokens,
  estimateTokensFromLength,
  evaluateEvent,
  generateRoutingDoc,
  now,
  postRead,
  postSearch,
  postTool,
  postWrite,
  preRead,
  preSearch,
  preTool,
  preWrite,
  responseCompleted,
  runBench,
  runReport,
  runStatus,
  sessionStarted,
  taskReceived
};
