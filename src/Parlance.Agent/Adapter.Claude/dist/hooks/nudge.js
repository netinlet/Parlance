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

// ../Core/src/policy/routing.ts
var CS_FILE_PATTERN = /\.(cs|csproj|sln|slnx|props|targets)$/i;
var CS_GLOB_PATTERN = /(^|\/)\*\*?\/[^/]*\.(cs|csproj|sln|slnx|props|targets)$|(^|\/)[^/]*\.(cs|csproj|sln|slnx|props|targets)$/i;
var BASH_SEARCH_UTIL = /\b(grep|egrep|fgrep|rg|ag|ack|ripgrep)\b/;
var BASH_READ_UTIL = /\b(cat|head|tail|less|more|bat)\b/;
var BASH_FIND_UTIL = /\bfind\b/;
var BASH_MENTIONS_CS = /\.(cs|csproj|sln|slnx|props|targets)\b|--include=[^\s]*\.cs|--type[ =]cs\b|-tcs\b|-g\s+["']?[^"'\s]*\.cs/i;
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

// ../Core/src/commands/routing-doc.ts
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

// ../Core/src/discovery.ts
import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";
function findSolution(root) {
  let entries;
  try {
    entries = readdirSync(root);
  } catch {
    return null;
  }
  return entries.find((e) => /\.slnx$/i.test(e)) ?? entries.find((e) => /\.sln$/i.test(e)) ?? null;
}
function looksLikeCsharp(root) {
  let entries;
  try {
    entries = readdirSync(root);
  } catch {
    return false;
  }
  const csAtRoot = entries.some((e) => /\.(slnx|sln|csproj)$/i.test(e) || e === "global.json" || e === "Directory.Build.props");
  if (csAtRoot) return true;
  try {
    return readdirSync(join(root, "src")).some((e) => /\.csproj$/i.test(e));
  } catch {
    return false;
  }
}
function parlanceMcpWired(root) {
  try {
    const config = JSON.parse(readFileSync(join(root, ".mcp.json"), "utf8"));
    return Boolean(config.mcpServers && "parlance" in config.mcpServers);
  } catch {
    return false;
  }
}
function planSessionStart(root) {
  if (parlanceMcpWired(root)) {
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
    can_inject_context: true
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

// src/hooks/nudge.ts
async function main() {
  try {
    const raw = await readStdin();
    const env = JSON.parse(raw);
    const translated = translate(env);
    if (!translated || translated.event.kind !== "session-started") return;
    const plan = planSessionStart(translated.context.project_root);
    if (plan.kind === "suggest-install" && capabilities.outputs.can_inject_context) {
      process.stdout.write(`${JSON.stringify({
        hookSpecificOutput: {
          hookEventName: "SessionStart",
          additionalContext: plan.context
        }
      })}
`);
    }
  } catch {
  }
}
void main().then(() => process.exit(0));
