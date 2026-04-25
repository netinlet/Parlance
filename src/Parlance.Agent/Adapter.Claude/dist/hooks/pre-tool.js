#!/usr/bin/env node

// ../Core/src/events.ts
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

// ../Core/src/telemetry/estimate.ts
var RATIOS = {
  code: 3.5,
  prose: 4,
  mixed: 3.75
};
function estimateTokensFromLength(length, kind) {
  if (length <= 0) return 0;
  return Math.round(length / RATIOS[kind]);
}

// ../Core/src/policy/routing.ts
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

// ../Core/src/policy/fallback.ts
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

// ../Core/src/policy/evaluate.ts
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

// ../Core/src/storage/paths.ts
import { join } from "node:path";
var parlanceDir = (root) => join(root, ".parlance");
var sessionFile = (root) => join(parlanceDir(root), "_session.json");
var kibbleDir = (root) => join(parlanceDir(root), "kibble");

// ../Core/src/storage/kibble.ts
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { join as join2 } from "node:path";
function appendFeedbackRecord(root, record) {
  const dayDir = join2(kibbleDir(root), record.date);
  mkdirSync(dayDir, { recursive: true });
  const existing = readdirSync(dayDir).filter((file) => file.endsWith(".md"));
  for (const file of existing) {
    const body2 = readFileSync(join2(dayDir, file), "utf8");
    if (body2.includes(`**Native tool:** ${record.native_tool}`) && body2.includes(`**Intent:** ${record.intent}`)) {
      return join2(dayDir, file);
    }
  }
  const seq = String(existing.length + 1).padStart(3, "0");
  const slug = slugify(`${record.native_tool}-${record.intent}`).slice(0, 40) || "entry";
  const path = join2(dayDir, `${seq}-${slug}.md`);
  const body = [
    `# ${record.native_tool} fallback: ${record.intent}`,
    "",
    `**Date:** ${record.date}`,
    `**Adapter:** ${record.adapter}`,
    `**Session:** ${record.session_id}`,
    `**Native tool:** ${record.native_tool}`,
    `**Intent:** ${record.intent}`,
    "",
    "## Why Parlance did not cover it",
    record.why,
    "",
    "## Suggested",
    record.suggested || "Needs investigation",
    "",
    "## Session context",
    record.session_context ?? "(none)",
    ""
  ].join("\n");
  writeFileSync(path, body);
  return path;
}
function slugify(value) {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
}

// ../Core/src/storage/session-state.ts
import { appendFileSync, existsSync, mkdirSync as mkdirSync2, readFileSync as readFileSync2, writeFileSync as writeFileSync2 } from "node:fs";
import { dirname } from "node:path";
function readSessionState(root) {
  const path = sessionFile(root);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync2(path, "utf8"));
  } catch {
    return null;
  }
}
function writeSessionState(root, state) {
  const path = sessionFile(root);
  mkdirSync2(dirname(path), { recursive: true });
  writeFileSync2(path, JSON.stringify(state, null, 2));
}

// src/render.ts
function renderToStderr(evaluation, write = (s) => process.stderr.write(s)) {
  for (const guidance of evaluation.guidance) {
    const prefix = guidance.severity === "info" ? "\u2139 parlance" : guidance.severity === "block" ? "\u26D4 parlance" : "\u26A1 parlance";
    write(`${prefix}: ${guidance.message}
`);
  }
}

// src/capabilities.ts
var capabilities = {
  name: "claude-code",
  events: {
    "session-started": "supported",
    "task-received": "supported",
    "pre-read": "supported",
    "post-read": "supported",
    "pre-write": "supported",
    "post-write": "supported",
    "pre-search": "supported",
    "post-search": "supported",
    "pre-native-tool": "supported",
    "post-native-tool": "supported",
    "pre-mcp-tool": "supported",
    "post-mcp-tool": "supported",
    "response-completed": "supported"
  },
  outputs: {
    can_warn: true,
    can_block: false,
    can_inject_context: false
  }
};

// src/translate.ts
function translate(env) {
  const context = {
    project_root: env.cwd,
    session_id: env.session_id,
    cwd: env.cwd,
    adapter: "claude-code",
    capabilities
  };
  const transcript_path = env.transcript_path ?? null;
  switch (env.hook_event_name) {
    case "SessionStart":
      return { event: sessionStarted(transcript_path ?? void 0), context, transcript_path };
    case "UserPromptSubmit":
      return { event: taskReceived(env.prompt ?? ""), context, transcript_path };
    case "Stop":
      return { event: responseCompleted(), context, transcript_path };
    case "PreToolUse": {
      const event = fromPre(env);
      return event ? { event, context, transcript_path } : null;
    }
    case "PostToolUse": {
      const event = fromPost(env);
      return event ? { event, context, transcript_path } : null;
    }
  }
}
function fromPre(env) {
  const tool = env.tool_name ?? "";
  const input = env.tool_input ?? {};
  if (tool === "Read" && typeof input.file_path === "string") return preRead(input.file_path);
  if ((tool === "Write" || tool === "Edit" || tool === "MultiEdit") && typeof input.file_path === "string") return preWrite(input.file_path);
  if (tool === "Grep" || tool === "Glob") return searchEvent(tool, input, true);
  if (tool.startsWith("mcp__parlance__")) return preTool("pre-mcp-tool", tool, input);
  if (tool) return preTool("pre-native-tool", tool, input);
  return null;
}
function fromPost(env) {
  const tool = env.tool_name ?? "";
  const input = env.tool_input ?? {};
  const output = env.tool_response ?? {};
  if (tool === "Read" && typeof input.file_path === "string") {
    return postRead(input.file_path, typeof output.content === "string" ? output.content.length : 0);
  }
  if ((tool === "Write" || tool === "Edit" || tool === "MultiEdit") && typeof input.file_path === "string") {
    return postWrite(input.file_path, contentLength(input.content));
  }
  if (tool === "Grep" || tool === "Glob") return searchEvent(tool, { ...input, result_bytes: contentLength(output.content) }, false);
  if (tool.startsWith("mcp__parlance__")) return postTool("post-mcp-tool", tool, input, contentLength(output.content));
  if (tool) return postTool("post-native-tool", tool, input, contentLength(output.content));
  return null;
}
function searchEvent(tool, input, isPre) {
  const event = {
    pattern: typeof input.pattern === "string" ? input.pattern : "",
    path: typeof input.path === "string" ? input.path : void 0,
    glob: typeof input.glob === "string" ? input.glob : typeof input.pattern === "string" && tool === "Glob" ? input.pattern : void 0,
    file_type: typeof input.type === "string" ? input.type : void 0,
    result_bytes: typeof input.result_bytes === "number" ? input.result_bytes : void 0
  };
  return isPre ? preSearch({ pattern: event.pattern, path: event.path, glob: event.glob, file_type: event.file_type }) : postSearch(event);
}
function contentLength(value) {
  return typeof value === "string" ? value.length : 0;
}

// src/hooks/_shared.ts
async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(typeof chunk === "string" ? Buffer.from(chunk) : chunk);
  }
  return Buffer.concat(chunks).toString("utf8");
}

// src/hooks/pre-tool.ts
async function main() {
  try {
    const raw = await readStdin();
    const env = JSON.parse(raw);
    const translated = translate(env);
    if (!translated) return;
    const current = readSessionState(translated.context.project_root) ?? emptySessionState(translated.context, translated.transcript_path);
    const evaluation = evaluateEvent(translated.event, translated.context, current);
    renderToStderr(evaluation);
    for (const effect of evaluation.effects) {
      if (effect.kind === "persist-feedback") {
        appendFeedbackRecord(translated.context.project_root, effect.feedback);
      }
    }
    if (evaluation.next_state) {
      writeSessionState(translated.context.project_root, evaluation.next_state);
    }
  } catch {
  }
}
void main().then(() => process.exit(0));
