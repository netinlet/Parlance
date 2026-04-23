#!/usr/bin/env node

// src/commands/install.ts
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
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
var MARKER_BEGIN = "<!-- parlance-agent:begin -->";
var MARKER_END = "<!-- parlance-agent:end -->";
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
  mkdirSync(parlanceDir(root), { recursive: true });
  mkdirSync(hooksDir(root), { recursive: true });
  copyHookBundles(hooksDir(root));
  writeFileSync(routingFile(root), generateRoutingDoc());
  writeMcpJson(root, resolve(root, args.solution), args.mcpCommand);
  mkdirSync(join2(root, ".claude"), { recursive: true });
  writeSettingsJson(join2(root, ".claude/settings.json"));
  writeClaudeMdSnippet(join2(root, "CLAUDE.md"));
  process.stderr.write(`parlance agent (claude) installed at ${root}
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
function writeMcpJson(root, solutionAbs, mcpCommand) {
  const path = join2(root, ".mcp.json");
  const existing = existsSync(path) ? JSON.parse(readFileSync(path, "utf8")) : {};
  existing.mcpServers ??= {};
  existing.mcpServers.parlance = {
    type: "stdio",
    command: mcpCommand ?? "parlance",
    args: ["mcp", "--solution-path", solutionAbs]
  };
  writeFileSync(path, JSON.stringify(existing, null, 2));
}
function writeSettingsJson(path) {
  const existing = existsSync(path) ? JSON.parse(readFileSync(path, "utf8")) : {};
  const hooks = existing.hooks ?? {};
  const ours = {
    SessionStart: [matcher("", "session-start.js", 5)],
    PreToolUse: [matcher("Read|Grep|Glob|Write|Edit|MultiEdit", "pre-tool.js", 5)],
    PostToolUse: [matcher("", "post-tool.js", 5)],
    UserPromptSubmit: [matcher("", "user-prompt-submit.js", 3)],
    Stop: [matcher("", "stop.js", 10)]
  };
  for (const [event, nextMatchers] of Object.entries(ours)) {
    const bucket = hooks[event] ?? [];
    const preserved = bucket.filter((entry) => !entry.hooks.some((hook) => hook.command.includes(HOOK_MARKER)));
    hooks[event] = [...preserved, ...nextMatchers];
  }
  existing.hooks = hooks;
  writeFileSync(path, JSON.stringify(existing, null, 2));
}
function matcher(matcherValue, script, timeout) {
  return {
    matcher: matcherValue,
    hooks: [{
      type: "command",
      command: `node "$CLAUDE_PROJECT_DIR/.parlance/hooks/${script}"`,
      timeout
    }]
  };
}
function writeClaudeMdSnippet(path) {
  const snippet = [
    MARKER_BEGIN,
    "",
    "## Parlance Agent",
    "",
    "This project uses `parlance agent` (Claude Code adapter). Claude hooks steer",
    "tool choice toward Parlance MCP tools on C# targets; routing rules live at",
    "`.parlance/tool-routing.md`, session summaries at `.parlance/session-log.md`,",
    "gap journal at `.parlance/kibble/`. Reports: `parlance agent report`.",
    "",
    MARKER_END,
    ""
  ].join("\n");
  if (!existsSync(path)) {
    writeFileSync(path, snippet);
    return;
  }
  const body = readFileSync(path, "utf8");
  if (body.includes(MARKER_BEGIN)) return;
  writeFileSync(path, body.endsWith("\n") ? body + snippet : `${body}
${snippet}`);
}

// src/commands/uninstall.ts
import { existsSync as existsSync2, readFileSync as readFileSync2, rmSync, writeFileSync as writeFileSync2 } from "node:fs";
import { join as join3, resolve as resolve2 } from "node:path";
var MARKER_BEGIN2 = "<!-- parlance-agent:begin -->";
var MARKER_END2 = "<!-- parlance-agent:end -->";
var HOOK_MARKER2 = ".parlance/hooks/";
async function runUninstall(argv) {
  let project = process.cwd();
  let purge = false;
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === "--project" && argv[index + 1]) project = argv[index + 1];
    if (argv[index] === "--purge") purge = true;
  }
  const root = resolve2(project);
  const settingsPath = join3(root, ".claude/settings.json");
  if (existsSync2(settingsPath)) {
    const settings = JSON.parse(readFileSync2(settingsPath, "utf8"));
    const hooks = settings.hooks;
    if (hooks) {
      for (const key of Object.keys(hooks)) {
        hooks[key] = hooks[key].filter((entry) => !entry.hooks.some((hook) => (hook.command ?? "").includes(HOOK_MARKER2)));
        if (hooks[key].length === 0) delete hooks[key];
      }
    }
    writeFileSync2(settingsPath, JSON.stringify(settings, null, 2));
  }
  const claudeMdPath = join3(root, "CLAUDE.md");
  if (existsSync2(claudeMdPath)) {
    const body = readFileSync2(claudeMdPath, "utf8");
    const pattern = new RegExp(`${escapeRegex(MARKER_BEGIN2)}[\\s\\S]*?${escapeRegex(MARKER_END2)}\\n?`, "g");
    writeFileSync2(claudeMdPath, body.replace(pattern, "").replace(/\n{3,}/g, "\n\n"));
  }
  const mcpPath = join3(root, ".mcp.json");
  if (existsSync2(mcpPath)) {
    const mcp = JSON.parse(readFileSync2(mcpPath, "utf8"));
    if (mcp.mcpServers) {
      delete mcp.mcpServers.parlance;
      if (Object.keys(mcp.mcpServers).length === 0) delete mcp.mcpServers;
    }
    if (Object.keys(mcp).length === 0) rmSync(mcpPath, { force: true });
    else writeFileSync2(mcpPath, JSON.stringify(mcp, null, 2));
  }
  if (purge && existsSync2(parlanceDir(root))) {
    rmSync(parlanceDir(root), { recursive: true, force: true });
  }
  process.stderr.write("parlance agent (claude) uninstalled\n");
  return 0;
}
function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
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
    "usage: parlance-agent-claude <install|uninstall> [args]",
    "  install --solution <path> [--project <dir>]",
    "  uninstall [--project <dir>] [--purge]",
    ""
  ].join("\n"));
}
void main();
