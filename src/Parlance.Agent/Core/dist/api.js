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
var BASH_SEARCH_UTIL = /\b(grep|egrep|fgrep|rg|ag|ack|ripgrep)\b/;
var BASH_READ_UTIL = /\b(cat|head|tail|less|more|bat)\b/;
var BASH_FIND_UTIL = /\bfind\b/;
var BASH_MENTIONS_CS = /\.(cs|csproj|sln|slnx|props|targets)\b|--include=[^\s]*\.cs|--type[ =]cs\b|-tcs\b|-g\s+["']?[^"'\s]*\.cs/i;
function isParlanceTool(toolName) {
  return toolName.startsWith("mcp__parlance__");
}
function matchBashCodeIntel(command) {
  const searches = BASH_SEARCH_UTIL.test(command) || BASH_FIND_UTIL.test(command);
  const reads = BASH_READ_UTIL.test(command);
  if (!(searches || reads)) return null;
  if (!BASH_MENTIONS_CS.test(command)) return null;
  const snippet = command.length > 60 ? `${command.slice(0, 60)}\u2026` : command;
  return reads && !searches ? {
    suggested_tool: "mcp__parlance__describe-type",
    message: "Use Parlance MCP tools before cat/head-ing C# source in bash.",
    reason: `bash read of C# (${snippet})`
  } : {
    suggested_tool: "mcp__parlance__search-symbols",
    message: "Use Parlance symbol/search tools before grep/find on C# code in bash.",
    reason: `bash search of C# (${snippet})`
  };
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
  if (event.kind === "pre-native-tool" && event.tool_name === "Bash") {
    const command = typeof event.input.command === "string" ? event.input.command : "";
    return matchBashCodeIntel(command);
  }
  return null;
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
import { basename, join as join2 } from "node:path";

// src/storage/paths.ts
import { homedir } from "node:os";
import { join } from "node:path";
var parlanceDir = (root) => join(root, ".parlance");
var parlanceHome = () => process.env.PARLANCE_HOME?.trim() || join(homedir(), ".parlance");
var telemetryDir = () => join(parlanceHome(), "telemetry");
var ledgerFile = () => join(telemetryDir(), "ledger.jsonl");
var sessionLogFile = () => join(telemetryDir(), "session-log.md");
var benchResultsFile = () => join(telemetryDir(), "bench", "results.jsonl");

// src/commands/report.ts
async function runReport(argv) {
  const args = parseArgs(argv);
  const path = ledgerFile();
  const rows = [];
  if (existsSync(path)) {
    rows.push(
      ...readFileSync(path, "utf8").split("\n").filter(Boolean).map((line) => JSON.parse(line))
    );
  }
  const legacyPath = join2(parlanceDir(process.cwd()), "ledger.jsonl");
  if (existsSync(legacyPath)) {
    rows.push(
      ...readFileSync(legacyPath, "utf8").split("\n").filter(Boolean).map((line) => JSON.parse(line))
    );
  }
  if (rows.length === 0) {
    process.stdout.write(`no ledger at ${path}
`);
    return 0;
  }
  const range = resolveRange(args);
  const filtered = rows.filter((row) => row.date >= range.start && row.date <= range.end && (!args.project || basename(row.project ?? "") === args.project));
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
  const tools = aggregateToolBreakdown(filtered);
  if (tools.length > 0) {
    lines.push("Top tools (calls):");
    for (const [tool, count] of tools.slice(0, 12)) {
      const tag = tool.startsWith("mcp__parlance__") ? "parlance" : "native  ";
      lines.push(`  ${String(count).padStart(6)}  ${tag}  ${tool}`);
    }
    lines.push("");
  }
  lines.push(
    `${"Date".padEnd(12)}${"Session".padEnd(10)}${"Project".padEnd(18)}${"Adapter".padEnd(13)}${"Parlance".padStart(9)}${"Fallback".padStart(9)}${"Output".padStart(9)}`
  );
  lines.push("-".repeat(89));
  for (const row of filtered) {
    lines.push(
      row.date.padEnd(12) + row.session_id.slice(0, 8).padEnd(10) + basename(row.project ?? "").slice(0, 17).padEnd(18) + row.adapter.slice(0, 12).padEnd(13) + String(row.parlance_calls).padStart(9) + String(row.native_fallbacks).padStart(9) + String(row.usage.output_tokens).padStart(9)
    );
  }
  process.stdout.write(`${lines.join("\n")}
`);
  return 0;
}
function aggregateToolBreakdown(rows) {
  const totals = {};
  for (const row of rows) {
    for (const [tool, count] of Object.entries(row.tool_breakdown ?? {})) {
      totals[tool] = (totals[tool] ?? 0) + count;
    }
  }
  return Object.entries(totals).sort((a, b) => b[1] - a[1]);
}
function parseArgs(argv) {
  const args = { days: 7 };
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
import { resolve } from "node:path";
async function runStatus(argv) {
  let project = process.cwd();
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === "--project" && argv[index + 1]) project = argv[index + 1];
  }
  const root = resolve(project);
  process.stdout.write(`project .parlance/ (install): ${existsSync2(parlanceDir(root)) ? "present" : "missing"}
`);
  const ledgerPath = ledgerFile();
  process.stdout.write(`central ledger: ${ledgerPath}
`);
  if (existsSync2(ledgerPath)) {
    const lines = readFileSync2(ledgerPath, "utf8").trim().split("\n").filter(Boolean);
    process.stdout.write(`sessions logged (all worktrees): ${lines.length}
`);
  }
  const logPath = sessionLogFile();
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
async function runBench(argv) {
  const [action, ...rest] = argv;
  if (action !== "report") {
    process.stderr.write("usage: bench report --task <id>\n");
    return 2;
  }
  let task;
  for (let index = 0; index < rest.length; index += 1) {
    if (rest[index] === "--task" && rest[index + 1]) task = rest[index + 1];
  }
  if (!task) {
    process.stderr.write("--task required\n");
    return 2;
  }
  const path = benchResultsFile();
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
    { kind: "pre-search", at: "", pattern: "x", path: "/proj/src/sub" },
    { kind: "pre-native-tool", at: "", tool_name: "Bash", input: { command: "grep -rn Foo --include=*.cs" } }
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
function generateSessionContext() {
  return [
    "Parlance MCP code-intelligence tools are available in this workspace.",
    "Prefer them over native Read/Grep/Glob when working with C# code.",
    "",
    generateRoutingDoc()
  ].join("\n");
}
function describe(event) {
  if (event.kind === "pre-read") return "Reading a C# file";
  if (event.kind === "pre-search" && event.file_type === "cs") return "Searching with type=cs";
  if (event.kind === "pre-search" && event.glob?.includes(".cs")) return "Searching with C# glob";
  if (event.kind === "pre-search") return "Searching under /src/ (no filter)";
  if (event.kind === "pre-native-tool") return "grep/find/cat over C# in bash";
  return event.kind;
}

// src/discovery.ts
import { existsSync as existsSync4, readFileSync as readFileSync4, readdirSync } from "node:fs";
import { join as join3 } from "node:path";
function findSolution(root) {
  let entries;
  try {
    entries = readdirSync(root);
  } catch {
    return null;
  }
  return entries.find((e) => /\.slnx$/i.test(e)) ?? entries.find((e) => /\.sln$/i.test(e)) ?? null;
}
var csharpCache = /* @__PURE__ */ new Map();
function looksLikeCsharp(root) {
  const cached = csharpCache.get(root);
  if (cached !== void 0) return cached;
  let entries;
  try {
    entries = readdirSync(root);
  } catch {
    csharpCache.set(root, false);
    return false;
  }
  const csAtRoot = entries.some((e) => /\.(slnx|sln|csproj)$/i.test(e) || e === "Directory.Build.props");
  if (csAtRoot) {
    csharpCache.set(root, true);
    return true;
  }
  try {
    const result = readdirSync(join3(root, "src")).some((e) => /\.csproj$/i.test(e));
    csharpCache.set(root, result);
    return result;
  } catch {
    csharpCache.set(root, false);
    return false;
  }
}
function parlanceMcpWired(root) {
  try {
    const config = JSON.parse(readFileSync4(join3(root, ".mcp.json"), "utf8"));
    return Boolean(config.mcpServers && "parlance" in config.mcpServers);
  } catch {
    return false;
  }
}
function parlanceAgentInstalled(root) {
  return parlanceMcpWired(root) || existsSync4(join3(root, ".parlance", "hooks", "session-start.js"));
}
function planSessionStart(root) {
  if (parlanceAgentInstalled(root)) {
    return { kind: "wired", context: generateSessionContext() };
  }
  if (looksLikeCsharp(root)) {
    const target = findSolution(root) ?? "<YourSolution.slnx>";
    return {
      kind: "suggest-install",
      context: [
        "This looks like a C# project, but the Parlance MCP server is not wired here \u2014",
        "so there is no Parlance code intelligence and this session is not being tracked.",
        `To enable it, run:  parlance agent install --solution ${target}`
      ].join("\n")
    };
  }
  return { kind: "idle" };
}
function runNudge(plan, canInjectContext, emit) {
  if (plan.kind === "suggest-install" && canInjectContext) {
    emit(plan.context);
  }
}
export {
  classifyPath,
  emptySessionState,
  estimateFromExtension,
  estimateTokens,
  estimateTokensFromLength,
  evaluateEvent,
  findSolution,
  generateRoutingDoc,
  generateSessionContext,
  looksLikeCsharp,
  now,
  parlanceAgentInstalled,
  parlanceMcpWired,
  planSessionStart,
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
  runNudge,
  runReport,
  runStatus,
  sessionStarted,
  taskReceived
};
