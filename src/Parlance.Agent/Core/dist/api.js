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
  return Math.round(content.length / RATIOS[kind]);
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
  let next = state;
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
        next = { ...next, native_fallbacks: next.native_fallbacks + 1 };
      }
    }
  }
  if (event.kind === "post-read" || event.kind === "post-write" || event.kind === "post-search" || event.kind === "post-native-tool" || event.kind === "post-mcp-tool") {
    const record = toUsageRecord(event);
    effects.push({ kind: "persist-tool-usage", record });
    next = {
      ...next,
      parlance_calls: next.parlance_calls + (record.is_mcp_parlance ? 1 : 0),
      read_tokens: next.read_tokens + (event.kind === "post-read" ? record.output_tokens : 0),
      write_tokens: next.write_tokens + (event.kind === "post-write" ? record.output_tokens : 0),
      tool_calls: [...next.tool_calls, record]
    };
  }
  return { guidance, effects, next_state: next };
}
function toUsageRecord(event) {
  const is_native_fallback = matchRoutingRule(flipToPre(event)) !== null;
  if (event.kind === "post-read" || event.kind === "post-write") {
    const bytes = event.content_bytes ?? 0;
    return {
      at: event.at,
      event_kind: event.kind,
      tool_name: event.kind === "post-read" ? "Read" : "Write",
      target: event.path,
      is_mcp_parlance: false,
      is_native_fallback,
      output_tokens: estimateTokens("x".repeat(bytes), "code")
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
      output_tokens: estimateTokens("x".repeat(event.result_bytes ?? 0), "code")
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
    output_tokens: estimateTokens("x".repeat(toolEvent.output_bytes ?? 0), "code")
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
export {
  classifyPath,
  emptySessionState,
  estimateFromExtension,
  estimateTokens,
  evaluateEvent,
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
  sessionStarted,
  taskReceived
};
