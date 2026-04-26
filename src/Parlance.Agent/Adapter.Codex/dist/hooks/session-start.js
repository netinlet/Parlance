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

// ../Core/src/storage/paths.ts
import { join } from "node:path";
var parlanceDir = (root) => join(root, ".parlance");
var sessionFile = (root) => join(parlanceDir(root), "_session.json");

// ../Core/src/storage/session-state.ts
import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname } from "node:path";
function writeSessionState(root, state) {
  const path = sessionFile(root);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify(state, null, 2));
}

// src/render.ts
function renderForCodex(eventName, evaluation) {
  const message = guidanceMessage(evaluation.guidance);
  if (!message) return null;
  if (eventName === "SessionStart" || eventName === "UserPromptSubmit") {
    return {
      hookSpecificOutput: {
        hookEventName: eventName,
        additionalContext: message
      }
    };
  }
  if (eventName === "PreToolUse") {
    return { systemMessage: message };
  }
  if (eventName === "PostToolUse") {
    return { systemMessage: message };
  }
  return null;
}
function writeCodexOutput(output, write = (s) => process.stdout.write(s)) {
  if (!output) return;
  write(`${JSON.stringify(output)}
`);
}
function guidanceMessage(guidance) {
  if (guidance.length === 0) return null;
  return guidance.map((entry) => {
    const suffix = entry.suggested_tool ? ` Suggested: ${entry.suggested_tool}.` : "";
    return `parlance: ${entry.message}${suffix}`;
  }).join("\n");
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
  if (tool.startsWith("mcp__parlance__")) return preTool("pre-mcp-tool", tool, input);
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
  if (tool.startsWith("mcp__parlance__")) return postTool("post-mcp-tool", tool, input, outputBytes);
  if (tool.startsWith("mcp__")) return postTool("post-native-tool", tool, input, outputBytes);
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
    return postSearch({ ...searchFromBashCommand(command), result_bytes: outputBytes });
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
    return { kind: "read", confidence: "high", reason: `${first || "sed -n"} command` };
  }
  if (/^(dotnet\s+test|npm\s+test|make\s+test)\b/.test(normalized)) {
    return { kind: "verify", confidence: "high", reason: "test command" };
  }
  if (/^(dotnet\s+build|npm\s+run\s+build|make\s+build)\b/.test(normalized)) {
    return { kind: "build", confidence: "high", reason: "build command" };
  }
  if (/^git\s+(status|diff|log|show)\b/.test(normalized)) {
    return { kind: "vcs-inspect", confidence: "high", reason: "git inspection command" };
  }
  return { kind: "unknown", confidence: "low", reason: "no classifier matched" };
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
    const match = /^(?:\*\*\* (?:Update|Delete|Add) File:|--- a\/|\+\+\+ b\/)\s*(.+)$/.exec(line.trim());
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
    if (/\.(cs|csproj|sln|slnx|props|targets)$/i.test(candidates[index])) return candidates[index];
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
    writeSessionState(
      translated.context.project_root,
      emptySessionState(translated.context, translated.transcript_path)
    );
    writeCodexOutput(renderForCodex(env.hook_event_name, { guidance: [], effects: [], next_state: null }));
  } catch {
  }
}
void main().then(() => process.exit(0));
