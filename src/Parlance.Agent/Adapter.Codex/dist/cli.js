#!/usr/bin/env node

// src/commands/install.ts
import { copyFileSync, existsSync as existsSync2, lstatSync, mkdirSync, readFileSync as readFileSync2, readdirSync as readdirSync2, writeFileSync } from "node:fs";
import { homedir as homedir2 } from "node:os";
import { dirname, join as join2, resolve } from "node:path";
import { fileURLToPath } from "node:url";

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

// ../Core/src/storage/paths.ts
import { homedir } from "node:os";
import { join } from "node:path";
var parlanceDir = (root) => join(root, ".parlance");
var parlanceHome = () => process.env.PARLANCE_HOME?.trim() || join(homedir(), ".parlance");
var globalHooksDir = () => join(parlanceHome(), "hooks");
var hooksDir = (root) => join(parlanceDir(root), "hooks");
var routingFile = (root) => join(parlanceDir(root), "tool-routing.md");

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
function describe(event) {
  if (event.kind === "pre-read") return "Reading a C# file";
  if (event.kind === "pre-search" && event.file_type === "cs") return "Searching with type=cs";
  if (event.kind === "pre-search" && event.glob?.includes(".cs")) return "Searching with C# glob";
  if (event.kind === "pre-search") return "Searching under /src/ (no filter)";
  if (event.kind === "pre-native-tool") return "grep/find/cat over C# in bash";
  return event.kind;
}

// ../Core/src/discovery.ts
import { existsSync, readFileSync, readdirSync } from "node:fs";
function findSolution(root) {
  let entries;
  try {
    entries = readdirSync(root);
  } catch {
    return null;
  }
  return entries.find((e) => /\.slnx$/i.test(e)) ?? entries.find((e) => /\.sln$/i.test(e)) ?? null;
}

// src/commands/install.ts
var HOOK_MARKER = ".parlance/hooks/";
var GLOBAL_NUDGE_MARKER = "hooks/nudge.js";
function codexConfigDir() {
  return process.env.CODEX_CONFIG_DIR?.trim() || join2(homedir2(), ".codex");
}
async function runInstall(argv) {
  if (argv.includes("--global")) return runInstallGlobal();
  const args = parseArgs(argv);
  if (!args) return 2;
  const root = resolve(args.project);
  if (!existsSync2(root)) {
    process.stderr.write(`project missing: ${root}
`);
    return 1;
  }
  const codexDir = join2(root, ".codex");
  if (existsSync2(codexDir) && !lstatSync(codexDir).isDirectory()) {
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
  writeMcpSetupDoc(root, resolve(root, args.solution), args.mcpCommand ?? "parlance");
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
  for (const entry of readdirSync2(source)) {
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
    if (existsSync2(candidate)) return candidate;
  }
  throw new Error("hook bundle directory not found");
}
function readJsonOrEmpty(path) {
  if (!existsSync2(path)) return {};
  try {
    return JSON.parse(readFileSync2(path, "utf8"));
  } catch (err) {
    throw new Error(`could not parse ${path}: ${err.message}`);
  }
}
function mergeJsonFile(path, update) {
  const data = readJsonOrEmpty(path);
  update(data);
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, JSON.stringify(data, null, 2));
}
function runInstallGlobal() {
  const hooksTarget = globalHooksDir();
  mkdirSync(hooksTarget, { recursive: true });
  const nudgeSource = join2(findHookBundleDir(), "nudge.js");
  if (!existsSync2(nudgeSource)) {
    process.stderr.write(`nudge bundle missing at ${nudgeSource}
`);
    return 1;
  }
  const nudgeTarget = join2(hooksTarget, "nudge.js");
  copyFileSync(nudgeSource, nudgeTarget);
  const hooksPath = join2(codexConfigDir(), "hooks.json");
  writeGlobalHooksJson(hooksPath, nudgeTarget);
  writeConfigToml(join2(codexConfigDir(), "config.toml"));
  process.stderr.write(
    `parlance global nudge installed:
  bundle: ${nudgeTarget}
  wired into: ${hooksPath} (SessionStart, nudge-only)
`
  );
  const cwd = process.cwd();
  const hooksInstalled = existsSync2(join2(cwd, ".codex", "hooks.json"));
  if (!hooksInstalled) {
    const sln = findSolution(cwd) ?? "<YourSolution.sln>";
    process.stderr.write(
      `
Note: per-project hooks are not installed in the current directory.
      Run: parlance agent install --for codex --solution ${sln}
`
    );
  }
  return 0;
}
function writeGlobalHooksJson(path, nudgePath) {
  mergeJsonFile(path, (existing) => {
    const hooks = existing.hooks ?? {};
    const bucket = hooks.SessionStart ?? [];
    const preserved = bucket.filter((entry) => !entry.hooks.some((hook) => hook.command.includes(GLOBAL_NUDGE_MARKER)));
    hooks.SessionStart = [...preserved, {
      hooks: [{
        type: "command",
        command: `node "${nudgePath}"`,
        timeout: 5,
        statusMessage: "Checking Parlance setup"
      }]
    }];
    existing.hooks = hooks;
  });
}
function writeHooksJson(path) {
  const existing = readJsonOrEmpty(path);
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
    const preserved = bucket.filter((entry) => !entry.hooks.some((hook) => (hook.command ?? "").includes(HOOK_MARKER)));
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
  const existing = existsSync2(path) ? readFileSync2(path, "utf8") : "";
  const next = withHooksFeature(existing);
  writeFileSync(path, next);
}
function writeMcpSetupDoc(root, solutionAbs, mcpCommand) {
  const path = join2(parlanceDir(root), "codex", "mcp-setup.md");
  const command = `codex mcp add parlance -- ${shellQuote(mcpCommand)} mcp --solution-path ${shellQuote(solutionAbs)}`;
  const body = [
    "# Parlance MCP Setup for Codex",
    "",
    "Run this command in your Codex shell to register the Parlance MCP server:",
    "",
    "```bash",
    command,
    "```",
    "",
    "After registration, restart Codex or follow any instructions printed by the Codex CLI.",
    ""
  ].join("\n");
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, body);
}
function shellQuote(value) {
  if (/^[A-Za-z0-9_./:-]+$/.test(value)) return value;
  return `'${value.replace(/'/g, `'\\''`)}'`;
}
function withHooksFeature(existing) {
  const normalized = existing.replace(/\r\n/g, "\n");
  if (/^\s*\[features]\s*$/m.test(normalized)) {
    if (/^\s*hooks\s*=/m.test(normalized)) {
      return ensureTrailingNewline(normalized.replace(/^\s*hooks\s*=.*$/m, "hooks = true"));
    }
    if (/^\s*codex_hooks\s*=/m.test(normalized)) {
      return ensureTrailingNewline(normalized.replace(/^\s*codex_hooks\s*=.*$/m, "hooks = true"));
    }
    const lines = normalized.split("\n");
    const index = lines.findIndex((line) => /^\s*\[features]\s*$/.test(line));
    lines.splice(index + 1, 0, "hooks = true");
    return ensureTrailingNewline(lines.join("\n"));
  }
  const prefix = normalized.trim().length === 0 ? "" : `${ensureTrailingNewline(normalized)}
`;
  return `${prefix}[features]
hooks = true
`;
}
function ensureTrailingNewline(value) {
  return value.endsWith("\n") ? value : `${value}
`;
}

// src/commands/uninstall.ts
import { existsSync as existsSync3, readFileSync as readFileSync3, rmSync, writeFileSync as writeFileSync2 } from "node:fs";
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
  if (existsSync3(hooksPath)) {
    const settings = JSON.parse(readFileSync3(hooksPath, "utf8"));
    if (settings.hooks) {
      for (const key of Object.keys(settings.hooks)) {
        settings.hooks[key] = settings.hooks[key].map((entry) => ({
          ...entry,
          hooks: entry.hooks.filter((hook) => !(hook.command ?? "").includes(HOOK_MARKER2))
        })).filter((entry) => entry.hooks.length > 0);
        if (settings.hooks[key].length === 0) delete settings.hooks[key];
      }
      if (Object.keys(settings.hooks).length === 0) delete settings.hooks;
    }
    if (Object.keys(settings).length === 0) rmSync(hooksPath, { force: true });
    else writeFileSync2(hooksPath, JSON.stringify(settings, null, 2));
  }
  if (purge && existsSync3(parlanceDir(root))) {
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
    "  install --global",
    "  uninstall [--project <dir>] [--purge]",
    ""
  ].join("\n"));
}
void main();
