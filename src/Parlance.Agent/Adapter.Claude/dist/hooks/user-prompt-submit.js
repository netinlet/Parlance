#!/usr/bin/env node

// src/hooks/user-prompt-submit.ts
import { appendFileSync as appendFileSync2, existsSync as existsSync3, mkdirSync as mkdirSync2, unlinkSync, writeFileSync as writeFileSync2 } from "node:fs";
import { dirname as dirname2 } from "node:path";

// ../Core/src/storage/paths.ts
import { join } from "node:path";
var parlanceDir = (root) => join(root, ".parlance");
var sessionFile = (root) => join(parlanceDir(root), "_session.json");
var benchStateFile = (root) => join(parlanceDir(root), "bench", "_active.json");
var benchResultsFile = (root) => join(parlanceDir(root), "bench", "results.jsonl");

// ../Core/src/storage/session-state.ts
import { appendFileSync, existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { dirname } from "node:path";
function readSessionState(root) {
  const path = sessionFile(root);
  if (!existsSync(path)) return null;
  try {
    return JSON.parse(readFileSync(path, "utf8"));
  } catch {
    return null;
  }
}
function writeSessionState(root, state) {
  const path = sessionFile(root);
  mkdirSync(dirname(path), { recursive: true });
  const temp = `${path}.tmp`;
  writeFileSync(temp, JSON.stringify(state, null, 2));
  renameSync(temp, path);
}

// src/transcript.ts
import { existsSync as existsSync2, readFileSync as readFileSync2 } from "node:fs";
function parseTranscript(path) {
  if (!existsSync2(path)) return null;
  const records = readFileSync2(path, "utf8").split("\n").filter(Boolean).map((line) => JSON.parse(line));
  const branch = records.find((record) => typeof record.gitBranch === "string")?.gitBranch ?? null;
  return { branch, records };
}
function aggregateUsageBetween(records, start, end) {
  const startMs = start ? Date.parse(start) : Number.NEGATIVE_INFINITY;
  const endMs = end ? Date.parse(end) : Number.POSITIVE_INFINITY;
  return records.reduce((totals, record) => {
    const ts = record.timestamp ? Date.parse(record.timestamp) : Number.NaN;
    if (Number.isNaN(ts) || ts < startMs || ts > endMs) return totals;
    const usage = record.message?.usage;
    if (!usage) return totals;
    totals.input_tokens += usage.input_tokens ?? 0;
    totals.output_tokens += usage.output_tokens ?? 0;
    totals.cache_read_tokens += usage.cache_read_input_tokens ?? 0;
    totals.cache_write_tokens += usage.cache_creation_input_tokens ?? 0;
    return totals;
  }, {
    input_tokens: 0,
    output_tokens: 0,
    cache_read_tokens: 0,
    cache_write_tokens: 0
  });
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

// src/hooks/user-prompt-submit.ts
async function main() {
  try {
    const raw = await readStdin();
    const env = JSON.parse(raw);
    const translated = translate(env);
    if (!translated || translated.event.kind !== "task-received") return;
    const prompt = translated.event.prompt.trim();
    if (!prompt.startsWith("/parlance")) return;
    const parts = prompt.split(/\s+/);
    if (parts[1] !== "bench") return;
    const action = parts[2];
    if (action === "start" && parts[3] && parts[4]) {
      startBench(translated.context.project_root, parts[3], parts[4]);
    } else if (action === "end") {
      endBench(translated.context.project_root, translated.transcript_path ?? null);
    } else {
      return;
    }
    process.stderr.write(`parlance bench ${action} acknowledged
`);
  } catch {
  }
}
function startBench(root, taskId, variant) {
  const marker = {
    task_id: taskId,
    variant,
    started_at: (/* @__PURE__ */ new Date()).toISOString()
  };
  const markerPath = benchStateFile(root);
  mkdirSync2(dirname2(markerPath), { recursive: true });
  writeFileSync2(markerPath, JSON.stringify(marker));
  const state = readSessionState(root);
  if (!state) return;
  writeSessionState(root, { ...state, active_bench: marker });
}
function endBench(root, transcriptPath) {
  const state = readSessionState(root);
  if (!state?.active_bench) return;
  const endedAt = (/* @__PURE__ */ new Date()).toISOString();
  let usage = {
    input_tokens: 0,
    output_tokens: 0,
    cache_read_tokens: 0,
    cache_write_tokens: 0
  };
  const path = transcriptPath ?? state.transcript_ref;
  if (path) {
    const parsed = parseTranscript(path);
    if (parsed) {
      usage = aggregateUsageBetween(parsed.records, state.active_bench.started_at, endedAt);
    }
  }
  const record = {
    task_id: state.active_bench.task_id,
    variant: state.active_bench.variant,
    started_at: state.active_bench.started_at,
    ended_at: endedAt,
    session_id: state.session_id,
    adapter: state.adapter,
    usage
  };
  const resultsPath = benchResultsFile(root);
  mkdirSync2(dirname2(resultsPath), { recursive: true });
  appendFileSync2(resultsPath, `${JSON.stringify(record)}
`);
  const markerPath = benchStateFile(root);
  if (existsSync3(markerPath)) unlinkSync(markerPath);
  writeSessionState(root, { ...state, active_bench: null });
}
void main().then(() => process.exit(0));
