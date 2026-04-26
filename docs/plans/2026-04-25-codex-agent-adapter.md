# Codex Agent Adapter Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a Codex adapter that mirrors the current Claude adapter behavior while capturing Codex-specific Bash telemetry for later tuning.

**Architecture:** Add `src/Parlance.Agent/Adapter.Codex` as a sibling package to `Adapter.Claude`. The adapter translates Codex hook envelopes into `@parlance/agent-core` events, renders Codex-compatible soft guidance, installs Codex hook config, and records raw Codex Bash events under `.parlance/codex/events/`. Shared session summaries remain in `.parlance/_session.json`, `.parlance/ledger.jsonl`, and `.parlance/session-log.md`.

**Tech Stack:** TypeScript, Node 20, esbuild, vitest, .NET CLI packaging via `src/Parlance.Cli`.

## Decisions

- Codex support is a new adapter package, not changes inside `Adapter.Claude`.
- V1 mirrors Claude lifecycle coverage: session start, user prompt submit, pre-tool, post-tool, and stop.
- Codex diverges for host-specific behavior: `.codex` config, Bash-heavy hook surface, context injection, and raw Bash event capture.
- Raw hook/event ledgers are adapter-specific, not adapter-neutral.
- Codex V1 writes `.parlance/codex/events/bash.jsonl`.
- Claude raw event storage is a follow-up story under `.parlance/claude/events/tool-use.jsonl`.
- `PostToolUse` is telemetry/state only for normal Parlance guidance. It must not replace tool output for routing nudges.
- `PreToolUse` may emit soft routing guidance. Blocking is reserved for a future explicit policy.
- `SessionStart` and `UserPromptSubmit` are the preferred Codex places for `additionalContext` guidance.
- User-facing reports should be adapter-level: `parlance agent report codex` and `parlance agent report claude`, not `report codex-bash`.

## V1 Installed Layout

```text
.parlance/
  _session.json
  ledger.jsonl
  session-log.md
  kibble/
  tool-routing.md
  hooks/
    session-start.js
    user-prompt-submit.js
    pre-tool.js
    post-tool.js
    stop.js
  codex/
    mcp-setup.md
    events/
      bash.jsonl
.codex/
  config.toml
  hooks.json
```

If `.codex` exists as a file, the installer must fail clearly and must not delete or replace it.

## Task 1: Scaffold `Adapter.Codex`

**Files:**
- Create: `src/Parlance.Agent/Adapter.Codex/package.json`
- Create: `src/Parlance.Agent/Adapter.Codex/package-lock.json`
- Create: `src/Parlance.Agent/Adapter.Codex/Makefile`
- Create: `src/Parlance.Agent/Adapter.Codex/tsconfig.json`
- Create: `src/Parlance.Agent/Adapter.Codex/tsconfig.build.json`
- Create: `src/Parlance.Agent/Adapter.Codex/scripts/bundle.mjs`
- Create: `src/Parlance.Agent/Adapter.Codex/src/cli.ts`
- Create: `src/Parlance.Agent/Adapter.Codex/README.md`

**Steps:**

1. Copy the package/build shape from `src/Parlance.Agent/Adapter.Claude`.
2. Rename package/bin to `@parlance/agent-adapter-codex` and `parlance-agent-codex`.
3. Keep the same scripts: `typecheck`, `test`, `build`.
4. Bundle `cli.js` and the five hook entrypoints under `dist/hooks/`.
5. Add README notes that this adapter is the only package that knows Codex hook envelope names and `.codex` config.

**Verification:**

Run:

```bash
cd src/Parlance.Agent/Adapter.Codex
npm install
npm run typecheck
npm test
npm run build
```

Expected: commands pass after later tasks add implementation/tests.

## Task 2: Add Codex Capabilities

**Files:**
- Create: `src/Parlance.Agent/Adapter.Codex/src/capabilities.ts`
- Test: `src/Parlance.Agent/Adapter.Codex/test/capabilities.test.ts`

**Implementation:**

Codex capabilities should set:

- `name: "codex"`
- lifecycle and tool events as `supported` where Codex hooks provide enough signal
- read/search events as `best-effort` because Codex collapses many operations into `Bash`
- `can_warn: true`
- `can_block: true`, but V1 policy does not block
- `can_inject_context: true`

**Verification:**

Run:

```bash
cd src/Parlance.Agent/Adapter.Codex
npm test -- capabilities
```

Expected: capabilities match the agreed fidelity.

## Task 3: Translate Codex Hook Envelopes

**Files:**
- Create: `src/Parlance.Agent/Adapter.Codex/src/translate.ts`
- Test: `src/Parlance.Agent/Adapter.Codex/test/translate.test.ts`

**Expected translations:**

- `SessionStart` -> `sessionStarted`
- `UserPromptSubmit` -> `taskReceived`
- `Stop` -> `responseCompleted`
- `PreToolUse` for Parlance MCP tool -> `pre-mcp-tool`
- `PostToolUse` for Parlance MCP tool -> `post-mcp-tool`
- `PreToolUse Bash` -> `pre-native-tool`
- `PostToolUse Bash` -> `post-native-tool`
- `apply_patch` -> best-effort write event only when paths can be identified confidently; otherwise native tool event

**Bash classification in V1:**

- `rg`, `grep`, `find`, `fd` -> `search`
- `cat`, `sed -n`, `head`, `tail`, `nl`, `wc` -> `read`
- `dotnet test`, `npm test`, `make test` -> `verify`
- `dotnet build`, `npm run build`, `make build` -> `build`
- `git status`, `git diff`, `git log`, `git show` -> `vcs-inspect`
- anything else -> `unknown`

Only `search` and `read` should feed routing/fallback nudges initially.

**Verification:**

Run:

```bash
cd src/Parlance.Agent/Adapter.Codex
npm test -- translate
```

Expected: all lifecycle, Bash, MCP, and apply-patch translation tests pass.

## Task 4: Add Codex Rendering

**Files:**
- Create: `src/Parlance.Agent/Adapter.Codex/src/render.ts`
- Test: `src/Parlance.Agent/Adapter.Codex/test/render.test.ts`

**Behavior:**

- `SessionStart` and `UserPromptSubmit` may return Codex-compatible `additionalContext`.
- `PreToolUse` may return Codex-compatible soft guidance.
- `PostToolUse` must not replace the original tool result for normal Parlance routing guidance.
- Effects are persisted by hook entrypoints, not rendered directly.

**Verification:**

Run:

```bash
cd src/Parlance.Agent/Adapter.Codex
npm test -- render
```

Expected: output shape is Codex-compatible and post-tool guidance does not hide tool output.

## Task 5: Add Codex Bash Event Store

**Files:**
- Create: `src/Parlance.Agent/Adapter.Codex/src/bash-events.ts`
- Test: `src/Parlance.Agent/Adapter.Codex/test/bash-events.test.ts`

**Path:**

```text
.parlance/codex/events/bash.jsonl
```

**Record shape:**

```json
{
  "schema": 1,
  "at": "2026-04-25T18:30:00.000Z",
  "adapter": "codex",
  "phase": "pre",
  "session_id": "s1",
  "turn_id": "t1",
  "tool_use_id": "u1",
  "cwd": "/repo",
  "command": "rg \"Foo\" src",
  "redacted": false,
  "classification": {
    "kind": "search",
    "confidence": "high",
    "reason": "rg command"
  }
}
```

For post records, include bounded output metadata only when available:

```json
{
  "schema": 1,
  "phase": "post",
  "exit_code": 0,
  "output_bytes": 1842,
  "output_preview": "first bounded redacted preview"
}
```

**Redaction:**

Redact at least:

- `KEY=value` patterns for names containing `TOKEN`, `KEY`, `SECRET`, `PASSWORD`, `PASS`, `AUTH`, or `CONNECTION`
- `Authorization: Bearer ...`
- long high-entropy strings

**Verification:**

Run:

```bash
cd src/Parlance.Agent/Adapter.Codex
npm test -- bash-events
```

Expected: pre/post records append, directories are created, secrets are redacted, and long previews are truncated.

## Task 6: Add Hook Entrypoints

**Files:**
- Create: `src/Parlance.Agent/Adapter.Codex/src/hooks/_shared.ts`
- Create: `src/Parlance.Agent/Adapter.Codex/src/hooks/session-start.ts`
- Create: `src/Parlance.Agent/Adapter.Codex/src/hooks/user-prompt-submit.ts`
- Create: `src/Parlance.Agent/Adapter.Codex/src/hooks/pre-tool.ts`
- Create: `src/Parlance.Agent/Adapter.Codex/src/hooks/post-tool.ts`
- Create: `src/Parlance.Agent/Adapter.Codex/src/hooks/stop.ts`

**Behavior:**

- Match Claude adapter error policy: hooks must not crash the host.
- Read JSON from stdin.
- Translate envelope.
- Load or initialize session state.
- Evaluate event through `agent-core`.
- Persist feedback/session state effects.
- Record every Bash pre/post event to `.parlance/codex/events/bash.jsonl`.
- Stop hook persists session summary like Claude, but Codex transcript/usage may be zero or best-effort in V1.

**Verification:**

Run:

```bash
cd src/Parlance.Agent/Adapter.Codex
npm test
npm run build
```

Expected: hook entrypoints bundle into `dist/hooks/`.

## Task 7: Add Install Command

**Files:**
- Create: `src/Parlance.Agent/Adapter.Codex/src/commands/install.ts`
- Test: `src/Parlance.Agent/Adapter.Codex/test/commands/install.test.ts`

**Behavior:**

- Parse `install --solution <path> [--project <dir>] [--mcp-command <command>]`.
- Create `.parlance/`, `.parlance/hooks/`, and `.parlance/codex/events/`.
- Copy hook bundles into `.parlance/hooks/`.
- Write `.parlance/tool-routing.md`.
- Write `.parlance/codex/mcp-setup.md` with `codex mcp add parlance -- parlance mcp --solution-path <solution>`.
- Create or update `.codex/hooks.json`.
- Create or update `.codex/config.toml` with `codex_hooks = true`.
- Preserve foreign hooks.
- Be idempotent.
- Fail if `.codex` exists and is not a directory.

**Verification:**

Run:

```bash
cd src/Parlance.Agent/Adapter.Codex
npm test -- commands/install
```

Expected: install tests pass, including `.codex` file collision.

## Task 8: Add Uninstall Command

**Files:**
- Create: `src/Parlance.Agent/Adapter.Codex/src/commands/uninstall.ts`
- Test: `src/Parlance.Agent/Adapter.Codex/test/commands/uninstall.test.ts`

**Behavior:**

- Remove only Parlance hook entries from `.codex/hooks.json`.
- Leave foreign hooks intact.
- Do not remove `.codex/config.toml` by default.
- Remove Codex MCP config only if V1 installs it.
- `--purge` removes `.parlance/`.
- Do not remove `.parlance/codex/events/bash.jsonl` unless `--purge` is used.

**Verification:**

Run:

```bash
cd src/Parlance.Agent/Adapter.Codex
npm test -- commands/uninstall
```

Expected: uninstall removes only Parlance-owned config and preserves event history unless purged.

## Task 9: Wire .NET CLI and Packaging

**Files:**
- Modify: `src/Parlance.Cli/Commands/AgentCommand.cs`
- Modify: `src/Parlance.Cli/Parlance.Cli.csproj`
- Modify: `Makefile`
- Modify: `.github/workflows/ci.yml`

**Behavior:**

- Add `"codex" => "Parlance.Agent.Adapter.Codex"` in adapter dispatch.
- Include `Adapter.Codex/dist/**/*.js` in CLI output/publish content.
- Add Codex adapter directory to agent install, typecheck, test, build, dist-check, lock-refresh, and lock-check targets.
- Add Codex package lock to CI cache paths.
- Update helper command output if needed.

**Verification:**

Run:

```bash
make agent-typecheck
make agent-test
make agent-build
make agent-dist-check
dotnet build src/Parlance.Cli/Parlance.Cli.csproj --configuration Release
```

Expected: Codex adapter participates in agent and CLI build workflows.

## Task 10: Documentation

**Files:**
- Modify: `README.md`
- Modify: `src/Parlance.Agent/Adapter.Codex/README.md`
- Optional: `docs/analyzer-development-guide.md`

**Content:**

- Document `parlance agent install --for codex --solution <path>`.
- Explain `.codex` directory requirement and file collision failure.
- Explain experimental Codex hook dependency and `codex_hooks = true`.
- Explain that `PostToolUse` records telemetry but does not replace tool output.
- Explain `.parlance/codex/events/bash.jsonl` and redaction.
- Note that adapter-specific reports are future work.

**Verification:**

Read docs for command accuracy and no outdated Claude-only claims.

## Final Verification

Run:

```bash
make agent-ci
dotnet build Parlance.sln --configuration Release
dotnet test Parlance.sln --configuration Release --no-build
```

If full solution tests are too slow, at minimum run:

```bash
make agent-ci
dotnet test tests/Parlance.Cli.Tests/Parlance.Cli.Tests.csproj --configuration Release
```

## Follow-Up Story: Adapter Event Stores and Reports

Name: `Adapter Event Stores and Reports`

Goals:

- Add `.parlance/claude/events/tool-use.jsonl`.
- Add `parlance agent report codex`.
- Add `parlance agent report claude`.
- Keep reports adapter-level, not event-type-level.
- Add adapter-owned raw event parsers.
- Share report rendering only after adapter parsers produce report rows.
- Tune Codex Bash classification from real `.parlance/codex/events/bash.jsonl` data.

Expected CLI:

```bash
parlance agent report
parlance agent report codex
parlance agent report claude
parlance agent report codex --events bash
parlance agent report codex --unknown-only
parlance agent report codex --since 2026-04-01 --until 2026-04-25
```

Example Codex report:

```text
=== Parlance Codex Report: 2026-04-19 -> 2026-04-25 ===
Sessions: 8  |  Bash commands: 143  |  Unknown: 37  |  Native fallback candidates: 22

Command families:
  search        41
  read          29
  verify        18
  build         9
  vcs-inspect   9
  unknown       37

Top unknown commands:
  awk ...                    7
  perl ...                   4
  dotnet format ...          3

Fallback candidates:
  rg *.cs / src              18  -> consider Parlance search-symbols/outline-file routing
  sed -n *.cs                4   -> consider describe-type/outline-file routing
```

Out of scope for Codex V1:

- Claude event-store migration.
- Adapter-specific reports.
- Hard blocking policy.
- Tool-result replacement from `PostToolUse`.
- Full Codex transcript usage parsing unless an obvious stable transcript schema is confirmed.
