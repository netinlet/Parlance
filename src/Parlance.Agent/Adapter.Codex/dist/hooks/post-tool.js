#!/usr/bin/env node

// ../Core/src/storage/kibble.ts
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { join as join2 } from "node:path";

// ../Core/src/storage/paths.ts
import { join } from "node:path";
var parlanceDir = (root) => join(root, ".parlance");
var sessionFile = (root) => join(parlanceDir(root), "_session.json");
var kibbleDir = (root) => join(parlanceDir(root), "kibble");

// ../Core/src/storage/kibble.ts
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

// src/bash-events.ts
import { appendFileSync as appendFileSync2, mkdirSync as mkdirSync3 } from "node:fs";
import { dirname as dirname2, join as join3 } from "node:path";

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
    if (paths.length === 1) return postWrite(paths[0], 0);
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
    return preSearch({ pattern: command, path: void 0, glob: void 0, file_type: void 0 });
  }
  if (classification.kind === "read") {
    return preTool("pre-native-tool", "Bash", input);
  }
  return preTool("pre-native-tool", "Bash", input);
}
function postBash(input, outputBytes) {
  const command = commandFromInput(input);
  const classification = classifyBashCommand(command);
  if (classification.kind === "search") {
    return postSearch({ pattern: command, result_bytes: outputBytes });
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

// src/bash-events.ts
var MAX_PREVIEW_CHARS = 1e3;
function bashEventsFile(root) {
  return join3(parlanceDir(root), "codex", "events", "bash.jsonl");
}
function appendBashEvent(root, record) {
  const path = bashEventsFile(root);
  mkdirSync3(dirname2(path), { recursive: true });
  appendFileSync2(path, `${JSON.stringify(record)}
`);
}
function bashEventFromEnvelope(env, phase, now2 = /* @__PURE__ */ new Date()) {
  if (env.tool_name !== "Bash") return null;
  const input = env.tool_input ?? {};
  const commandRedaction = redact(commandFromInput(input));
  const record = {
    schema: 1,
    at: now2.toISOString(),
    adapter: "codex",
    phase,
    session_id: env.session_id,
    turn_id: env.turn_id,
    tool_use_id: env.tool_use_id,
    cwd: env.cwd,
    command: commandRedaction.value,
    redacted: commandRedaction.redacted,
    classification: classifyBashCommand(commandFromInput(input))
  };
  if (phase === "post") {
    const output = extractOutput(env.tool_response);
    if (typeof output.exit_code === "number") record.exit_code = output.exit_code;
    if (typeof output.output_bytes === "number") record.output_bytes = output.output_bytes;
    if (output.preview) {
      const previewRedaction = redact(truncate(output.preview, MAX_PREVIEW_CHARS));
      record.output_preview = previewRedaction.value;
      record.redacted ||= previewRedaction.redacted;
    }
  }
  return record;
}
function redact(value) {
  let redacted = false;
  let next = value;
  next = next.replace(
    /\b([A-Z0-9_]*(?:TOKEN|KEY|SECRET|PASSWORD|PASS|AUTH|CONNECTION)[A-Z0-9_]*)=([^\s"'`]+)/gi,
    (_match, name) => {
      redacted = true;
      return `${name}=[REDACTED]`;
    }
  );
  next = next.replace(/Authorization:\s*Bearer\s+[A-Za-z0-9._~+/=-]+/gi, () => {
    redacted = true;
    return "Authorization: Bearer [REDACTED]";
  });
  next = next.replace(/\b[A-Za-z0-9+/=_-]{40,}\b/g, () => {
    redacted = true;
    return "[REDACTED]";
  });
  return { value: next, redacted };
}
function extractOutput(output) {
  if (typeof output === "string") {
    return { output_bytes: output.length, preview: output };
  }
  if (!output || typeof output !== "object") return {};
  const record = output;
  const stdout = typeof record.stdout === "string" ? record.stdout : "";
  const stderr = typeof record.stderr === "string" ? record.stderr : "";
  const content = typeof record.content === "string" ? record.content : "";
  const outputText = stdout || stderr ? `${stdout}${stderr}` : content;
  const exit_code = typeof record.exit_code === "number" ? record.exit_code : typeof record.exitCode === "number" ? record.exitCode : void 0;
  return {
    exit_code,
    output_bytes: outputText ? outputText.length : void 0,
    preview: outputText || void 0
  };
}
function truncate(value, max) {
  return value.length > max ? `${value.slice(0, max)}...` : value;
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
function handleEvaluatedEvent(env, bashPhase) {
  const translated = translate(env);
  if (!translated) return;
  const current = readSessionState(translated.context.project_root) ?? emptySessionState(translated.context, translated.transcript_path);
  const evaluation = evaluateEvent(translated.event, translated.context, current);
  for (const effect of evaluation.effects) {
    if (effect.kind === "persist-feedback") {
      appendFeedbackRecord(translated.context.project_root, effect.feedback);
    }
  }
  if (evaluation.next_state) {
    writeSessionState(translated.context.project_root, evaluation.next_state);
  }
  if (bashPhase) {
    const bashEvent = bashEventFromEnvelope(env, bashPhase);
    if (bashEvent) appendBashEvent(translated.context.project_root, bashEvent);
  }
  writeCodexOutput(renderForCodex(env.hook_event_name, evaluation));
}

// src/hooks/post-tool.ts
async function main() {
  try {
    handleEvaluatedEvent(await readEnvelope(), "post");
  } catch {
  }
}
void main().then(() => process.exit(0));
