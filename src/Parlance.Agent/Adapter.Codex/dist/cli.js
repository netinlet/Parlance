#!/usr/bin/env node

// src/commands/install.ts
import { copyFileSync, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { dirname, join as join2, resolve } from "node:path";
import { fileURLToPath } from "node:url";

// ../Core/src/policy/routing.ts
var CS_FILE_PATTERN = /\.(cs|csproj|sln|slnx|props|targets)$/i;
var CS_GLOB_PATTERN = /(^|\/)\*\*?\/[^/]*\.(cs|csproj|sln|slnx|props|targets)$|(^|\/)[^/]*\.(cs|csproj|sln|slnx|props|targets)$/i;
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

// ../Core/src/storage/paths.ts
import { join } from "node:path";
var parlanceDir = (root) => join(root, ".parlance");
var hooksDir = (root) => join(parlanceDir(root), "hooks");
var routingFile = (root) => join(parlanceDir(root), "tool-routing.md");

// ../Core/src/commands/routing-doc.ts
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

// src/commands/install.ts
var HOOK_MARKER = ".parlance/hooks/";
async function runInstall(argv) {
  const args = parseArgs(argv);
  if (!args) return 2;
  const root = resolve(args.project);
  if (!existsSync(root)) {
    process.stderr.write(`project missing: ${root}
`);
    return 1;
  }
  const codexDir = join2(root, ".codex");
  if (existsSync(codexDir) && !lstatSync(codexDir).isDirectory()) {
    process.stderr.write(`cannot install codex hooks: ${codexDir} exists and is not a directory
`);
    return 1;
  }
  mkdirSync(parlanceDir(root), { recursive: true });
  mkdirSync(hooksDir(root), { recursive: true });
  mkdirSync(join2(parlanceDir(root), "codex", "events"), { recursive: true });
  mkdirSync(codexDir, { recursive: true });
  copyHookBundles(hooksDir(root));
  writeFileSync(routingFile(root), generateRoutingDoc());
  writeHooksJson(join2(codexDir, "hooks.json"));
  writeConfigToml(join2(codexDir, "config.toml"));
  process.stderr.write(`parlance agent (codex) installed at ${root}
`);
  return 0;
}
function parseArgs(argv) {
  const args = { project: process.cwd() };
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === "--project" && argv[index + 1]) args.project = argv[index + 1];
    if (argv[index] === "--solution" && argv[index + 1]) args.solution = argv[index + 1];
    if (argv[index] === "--mcp-command" && argv[index + 1]) args.mcpCommand = argv[index + 1];
  }
  if (!args.solution) {
    process.stderr.write("usage: install --solution <path> [--project <dir>]\n");
    return null;
  }
  return args;
}
function copyHookBundles(target) {
  const source = findHookBundleDir();
  for (const entry of readdirSync(source)) {
    if (entry.endsWith(".js")) copyFileSync(join2(source, entry), join2(target, entry));
  }
}
function findHookBundleDir() {
  const here = dirname(fileURLToPath(import.meta.url));
  const candidates = [
    join2(here, "hooks"),
    join2(here, "..", "..", "dist", "hooks")
  ];
  for (const candidate of candidates) {
    if (existsSync(candidate)) return candidate;
  }
  throw new Error("hook bundle directory not found");
}
function writeHooksJson(path) {
  const existing = existsSync(path) ? JSON.parse(readFileSync(path, "utf8")) : {};
  existing.hooks ??= {};
  const ours = {
    SessionStart: [matcher("startup|resume|clear", "session-start.js", 5, "Loading Parlance session state")],
    UserPromptSubmit: [matcher(void 0, "user-prompt-submit.js", 3, "Checking Parlance prompt context")],
    PreToolUse: [matcher("Bash|apply_patch|Edit|Write|mcp__.*", "pre-tool.js", 5, "Checking Parlance routing")],
    PostToolUse: [matcher("Bash|apply_patch|Edit|Write|mcp__.*", "post-tool.js", 5, "Recording Parlance tool telemetry")],
    Stop: [matcher(void 0, "stop.js", 10, "Recording Parlance session summary")]
  };
  for (const [event, nextMatchers] of Object.entries(ours)) {
    const bucket = existing.hooks[event] ?? [];
    const preserved = bucket.filter((entry) => !entry.hooks.some((hook) => hook.command.includes(HOOK_MARKER)));
    existing.hooks[event] = [...preserved, ...nextMatchers];
  }
  writeFileSync(path, JSON.stringify(existing, null, 2));
}
function matcher(matcherValue, script, timeout, statusMessage) {
  const entry = {
    hooks: [{
      type: "command",
      command: `node "$(git rev-parse --show-toplevel)/.parlance/hooks/${script}"`,
      timeout,
      statusMessage
    }]
  };
  if (matcherValue !== void 0) entry.matcher = matcherValue;
  return entry;
}
function writeConfigToml(path) {
  const existing = existsSync(path) ? readFileSync(path, "utf8") : "";
  const next = withCodexHooksFeature(existing);
  writeFileSync(path, next);
}
function withCodexHooksFeature(existing) {
  const normalized = existing.replace(/\r\n/g, "\n");
  if (/^\s*\[features]\s*$/m.test(normalized)) {
    if (/^\s*codex_hooks\s*=/m.test(normalized)) {
      return ensureTrailingNewline(normalized.replace(/^\s*codex_hooks\s*=.*$/m, "codex_hooks = true"));
    }
    const lines = normalized.split("\n");
    const index = lines.findIndex((line) => /^\s*\[features]\s*$/.test(line));
    lines.splice(index + 1, 0, "codex_hooks = true");
    return ensureTrailingNewline(lines.join("\n"));
  }
  const prefix = normalized.trim().length === 0 ? "" : `${ensureTrailingNewline(normalized)}
`;
  return `${prefix}[features]
codex_hooks = true
`;
}
function ensureTrailingNewline(value) {
  return value.endsWith("\n") ? value : `${value}
`;
}

// src/commands/uninstall.ts
import { existsSync as existsSync2, readFileSync as readFileSync2, rmSync, writeFileSync as writeFileSync2 } from "node:fs";
import { join as join3, resolve as resolve2 } from "node:path";
var HOOK_MARKER2 = ".parlance/hooks/";
async function runUninstall(argv) {
  let project = process.cwd();
  let purge = false;
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === "--project" && argv[index + 1]) project = argv[index + 1];
    if (argv[index] === "--purge") purge = true;
  }
  const root = resolve2(project);
  const hooksPath = join3(root, ".codex/hooks.json");
  if (existsSync2(hooksPath)) {
    const settings = JSON.parse(readFileSync2(hooksPath, "utf8"));
    if (settings.hooks) {
      for (const key of Object.keys(settings.hooks)) {
        settings.hooks[key] = settings.hooks[key].filter((entry) => !entry.hooks.some((hook) => (hook.command ?? "").includes(HOOK_MARKER2)));
        if (settings.hooks[key].length === 0) delete settings.hooks[key];
      }
      if (Object.keys(settings.hooks).length === 0) delete settings.hooks;
    }
    if (Object.keys(settings).length === 0) rmSync(hooksPath, { force: true });
    else writeFileSync2(hooksPath, JSON.stringify(settings, null, 2));
  }
  if (purge && existsSync2(parlanceDir(root))) {
    rmSync(parlanceDir(root), { recursive: true, force: true });
  }
  process.stderr.write("parlance agent (codex) uninstalled\n");
  return 0;
}

// src/cli.ts
var commands = {
  install: runInstall,
  uninstall: runUninstall
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
    "usage: parlance-agent-codex <install|uninstall> [args]",
    "  install --solution <path> [--project <dir>]",
    "  uninstall [--project <dir>] [--purge]",
    ""
  ].join("\n"));
}
void main();
