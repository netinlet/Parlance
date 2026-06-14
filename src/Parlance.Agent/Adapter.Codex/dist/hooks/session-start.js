#!/usr/bin/env node

// ../Core/src/storage/paths.ts
import { join } from "node:path";
var parlanceDir = (root) => join(root, ".parlance");
var sessionFile = (root) => join(parlanceDir(root), "_session.json");

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
    {
      kind: "pre-search",
      at: "",
      pattern: "x",
      file_type: "cs"
    },
    {
      kind: "pre-search",
      at: "",
      pattern: "x",
      glob: "**/*.cs"
    },
    {
      kind: "pre-search",
      at: "",
      pattern: "x",
      path: "/proj/src/sub"
    },
    {
      kind: "pre-native-tool",
      at: "",
      tool_name: "Bash",
      input: { command: "grep -rn Foo --include=*.cs" }
    }
  ];
  const lines = [
    "# Parlance Tool Routing",
    "",
    "Generated from agent-core routing rules.",
    ""
  ];
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
  if (event.kind === "pre-search" && event.file_type === "cs")
    return "Searching with type=cs";
  if (event.kind === "pre-search" && event.glob?.includes(".cs"))
    return "Searching with C# glob";
  if (event.kind === "pre-search") return "Searching under /src/ (no filter)";
  if (event.kind === "pre-native-tool") return "grep/find/cat over C# in bash";
  return event.kind;
}

// ../Core/src/discovery.ts
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join as join2 } from "node:path";
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
  const csAtRoot = entries.some(
    (e) => /\.(slnx|sln|csproj)$/i.test(e) || e === "Directory.Build.props"
  );
  if (csAtRoot) {
    csharpCache.set(root, true);
    return true;
  }
  try {
    const result = readdirSync(join2(root, "src")).some(
      (e) => /\.csproj$/i.test(e)
    );
    csharpCache.set(root, result);
    return result;
  } catch {
    csharpCache.set(root, false);
    return false;
  }
}
function parlanceMcpWired(root) {
  try {
    const config = JSON.parse(
      readFileSync(join2(root, ".mcp.json"), "utf8")
    );
    return Boolean(config.mcpServers && "parlance" in config.mcpServers);
  } catch {
    return false;
  }
}
function parlanceAgentInstalled(root) {
  return parlanceMcpWired(root) || existsSync(join2(root, ".parlance", "hooks", "session-start.js"));
}
function planSessionStart(root, wiredFn = parlanceAgentInstalled) {
  if (wiredFn(root)) {
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
var preRead = (path) => ({
  kind: "pre-read",
  at: now(),
  path
});
var preWrite = (path) => ({
  kind: "pre-write",
  at: now(),
  path
});
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

// ../Core/src/storage/session-state.ts
import {
  appendFileSync,
  existsSync as existsSync2,
  mkdirSync,
  readFileSync as readFileSync2,
  writeFileSync
} from "node:fs";
import { dirname } from "node:path";
function writeSessionState(root, state) {
  const path = sessionFile(root);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify(state, null, 2));
}

// src/capabilities.ts
var capabilities = {
  name: "codex",
  events: {
    "session-started": "supported",
    "task-received": "supported",
    "pre-read": "best-effort",
    "post-read": "best-effort",
    "pre-write": "best-effort",
    "post-write": "best-effort",
    "pre-search": "best-effort",
    "post-search": "best-effort",
    "pre-native-tool": "supported",
    "post-native-tool": "supported",
    "pre-mcp-tool": "supported",
    "post-mcp-tool": "supported",
    "response-completed": "supported"
  },
  outputs: {
    can_warn: true,
    can_block: true,
    can_inject_context: true
  }
};

// src/render.ts
function writeCodexOutput(output, write = (s) => process.stdout.write(s)) {
  if (!output) return;
  write(`${JSON.stringify(output)}
`);
}

// src/translate.ts
function translate(env) {
  const context = {
    project_root: env.cwd,
    session_id: env.session_id,
    cwd: env.cwd,
    adapter: "codex",
    capabilities
  };
  const transcript_path = env.transcript_path ?? null;
  switch (env.hook_event_name) {
    case "SessionStart":
      return {
        event: sessionStarted(transcript_path ?? void 0),
        context,
        transcript_path
      };
    case "UserPromptSubmit":
      return {
        event: taskReceived(env.prompt ?? ""),
        context,
        transcript_path
      };
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
    case "PermissionRequest":
      return null;
  }
}
function fromPre(env) {
  const tool = env.tool_name ?? "";
  const input = env.tool_input ?? {};
  if (tool === "Bash") return preBash(input);
  if (tool === "apply_patch") {
    const paths = pathsFromPatchCommand(commandFromInput(input));
    if (paths.length === 1) return preWrite(paths[0]);
    return preTool("pre-native-tool", tool, input);
  }
  if (tool.startsWith("mcp__parlance__"))
    return preTool("pre-mcp-tool", tool, input);
  if (tool.startsWith("mcp__")) return preTool("pre-native-tool", tool, input);
  if (tool) return preTool("pre-native-tool", tool, input);
  return null;
}
function fromPost(env) {
  const tool = env.tool_name ?? "";
  const input = env.tool_input ?? {};
  const outputBytes = outputLength(env.tool_response);
  if (tool === "Bash") return postBash(input, outputBytes);
  if (tool === "apply_patch") {
    const paths = pathsFromPatchCommand(commandFromInput(input));
    if (paths.length === 1) {
      const writtenBytes = estimatePatchWrittenBytes(commandFromInput(input));
      if (writtenBytes > 0) return postWrite(paths[0], writtenBytes);
    }
    return postTool("post-native-tool", tool, input, outputBytes);
  }
  if (tool.startsWith("mcp__parlance__"))
    return postTool("post-mcp-tool", tool, input, outputBytes);
  if (tool.startsWith("mcp__"))
    return postTool("post-native-tool", tool, input, outputBytes);
  if (tool) return postTool("post-native-tool", tool, input, outputBytes);
  return null;
}
function preBash(input) {
  const command = commandFromInput(input);
  const classification = classifyBashCommand(command);
  if (classification.kind === "search") {
    return preSearch(searchFromBashCommand(command));
  }
  if (classification.kind === "read") {
    const path = readPathFromBashCommand(command);
    if (path) return preRead(path);
    return preTool("pre-native-tool", "Bash", input);
  }
  return preTool("pre-native-tool", "Bash", input);
}
function postBash(input, outputBytes) {
  const command = commandFromInput(input);
  const classification = classifyBashCommand(command);
  if (classification.kind === "search") {
    return postSearch({
      ...searchFromBashCommand(command),
      result_bytes: outputBytes
    });
  }
  return postTool("post-native-tool", "Bash", input, outputBytes);
}
function classifyBashCommand(command) {
  const normalized = command.trim();
  const first = normalized.split(/\s+/)[0] ?? "";
  if (/^(rg|grep|find|fd)\b/.test(first)) {
    return { kind: "search", confidence: "high", reason: `${first} command` };
  }
  if (/^(cat|head|tail|nl|wc)\b/.test(first) || /^sed\s+-n\b/.test(normalized)) {
    return {
      kind: "read",
      confidence: "high",
      reason: `${first || "sed -n"} command`
    };
  }
  if (/^(dotnet\s+test|npm\s+test|make\s+test)\b/.test(normalized)) {
    return { kind: "verify", confidence: "high", reason: "test command" };
  }
  if (/^(dotnet\s+build|npm\s+run\s+build|make\s+build)\b/.test(normalized)) {
    return { kind: "build", confidence: "high", reason: "build command" };
  }
  if (/^git\s+(status|diff|log|show)\b/.test(normalized)) {
    return {
      kind: "vcs-inspect",
      confidence: "high",
      reason: "git inspection command"
    };
  }
  return {
    kind: "unknown",
    confidence: "low",
    reason: "no classifier matched"
  };
}
function commandFromInput(input) {
  return typeof input.command === "string" ? input.command : "";
}
function outputLength(output) {
  if (typeof output === "string") return output.length;
  if (output && typeof output === "object") {
    const record = output;
    if (typeof record.content === "string") return record.content.length;
    if (typeof record.output === "string") return record.output.length;
    if (typeof record.stdout === "string" || typeof record.stderr === "string") {
      return (typeof record.stdout === "string" ? record.stdout.length : 0) + (typeof record.stderr === "string" ? record.stderr.length : 0);
    }
  }
  return 0;
}
function pathsFromPatchCommand(command) {
  const paths = /* @__PURE__ */ new Set();
  for (const line of command.split("\n")) {
    const match = /^(?:\*\*\* (?:Update|Delete|Add) File:|--- a\/|\+\+\+ b\/)\s*(.+)$/.exec(
      line.trim()
    );
    if (!match) continue;
    const path = match[1].trim();
    if (path && path !== "/dev/null") paths.add(path);
  }
  return [...paths];
}
function estimatePatchWrittenBytes(command) {
  let bytes = 0;
  for (const line of command.split("\n")) {
    if (line.startsWith("+++") || line.startsWith("***")) continue;
    if (!line.startsWith("+")) continue;
    bytes += Buffer.byteLength(`${line.slice(1)}
`, "utf8");
  }
  return bytes;
}
function searchFromBashCommand(command) {
  const args = shellWords(command);
  const tool = args[0];
  if ((tool === "rg" || tool === "grep") && args.length > 1) {
    let pattern = "";
    let path;
    let glob;
    for (let index = 1; index < args.length; index += 1) {
      const arg = args[index];
      if (arg === "--glob" || arg === "-g") {
        glob = args[index + 1];
        index += 1;
        continue;
      }
      if (arg.startsWith("-")) continue;
      if (!pattern) {
        pattern = arg;
        continue;
      }
      if (!path) path = arg;
    }
    return {
      pattern: pattern || command,
      path,
      glob,
      file_type: glob?.endsWith(".cs") ? "cs" : void 0
    };
  }
  return { pattern: command };
}
function readPathFromBashCommand(command) {
  const args = shellWords(command);
  const tool = args[0];
  if (!tool) return void 0;
  const candidates = args.slice(1).filter((arg) => !arg.startsWith("-") && !/^\d+,\d+p$/.test(arg));
  for (let index = candidates.length - 1; index >= 0; index -= 1) {
    if (/\.(cs|csproj|sln|slnx|props|targets)$/i.test(candidates[index]))
      return candidates[index];
  }
  return void 0;
}
function shellWords(command) {
  const words = [];
  const pattern = /"([^"]*)"|'([^']*)'|(\S+)/g;
  let match;
  while ((match = pattern.exec(command)) !== null) {
    words.push(match[1] ?? match[2] ?? match[3]);
  }
  return words;
}

// src/hooks/_shared.ts
async function readStdin() {
  const chunks = [];
  for await (const chunk of process.stdin) {
    chunks.push(typeof chunk === "string" ? Buffer.from(chunk) : chunk);
  }
  return Buffer.concat(chunks).toString("utf8");
}
async function readEnvelope() {
  return JSON.parse(await readStdin());
}

// src/hooks/session-start.ts
async function main() {
  try {
    const env = await readEnvelope();
    const translated = translate(env);
    if (!translated || translated.event.kind !== "session-started") return;
    const plan = planSessionStart(translated.context.project_root);
    if (plan.kind === "wired") {
      writeSessionState(
        translated.context.project_root,
        emptySessionState(translated.context, translated.transcript_path)
      );
    }
    if (plan.kind !== "idle" && capabilities.outputs.can_inject_context) {
      writeCodexOutput({
        hookSpecificOutput: {
          hookEventName: "SessionStart",
          additionalContext: plan.context
        }
      });
    }
  } catch {
  }
}
void main().then(() => process.exit(0));
