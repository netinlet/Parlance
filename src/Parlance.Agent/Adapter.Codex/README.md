# @parlance/agent-adapter-codex

Codex adapter for `@parlance/agent-core`.

Responsibilities:
- translate Codex hook JSON into `AgentEvent` values
- render core `EventEvaluation` guidance into Codex-compatible hook outputs
- write/remove Codex-specific install artifacts (`.codex/hooks.json`,
  `.codex/config.toml`, `.parlance/hooks/*.js`)
- record Codex Bash telemetry in `.parlance/codex/events/bash.jsonl`

This package is the **only** place in the repo that knows about Codex hook
event names, Codex hook output shapes, or `.codex/` configuration.

## Build

    npm install && npm test && npm run build

## Capability manifest

See `src/capabilities.ts` for which agent-neutral events this adapter emits,
and with what fidelity.

## Install behavior

`parlance agent install --for codex --solution <path>` writes:

- `.codex/hooks.json`
- `.codex/config.toml`
- `.parlance/hooks/*.js`
- `.parlance/tool-routing.md`
- `.parlance/codex/mcp-setup.md`
- `.parlance/codex/events/bash.jsonl` parent directory

Codex hooks are experimental and require `[features] codex_hooks = true`. The
installer updates that feature flag while preserving unrelated config. If
`.codex` exists as a file, installation fails and leaves it untouched.

The `--solution` path is used to generate `.parlance/codex/mcp-setup.md` with a
Codex CLI command of this shape:

```bash
codex mcp add parlance -- parlance mcp --solution-path /absolute/path/to/App.sln
```

Run that command in your Codex shell after installing hooks, then restart Codex
or follow any instructions printed by the Codex CLI.

`PostToolUse` is telemetry-only for normal Parlance guidance. The adapter does
not return `decision: "block"` or `continue: false`, because those Codex outputs
replace the original tool result after the tool has already run.

Adapter-specific reports such as `parlance agent report codex` are planned as a
follow-up. V1 records raw Bash telemetry so those reports can be tuned from real
Codex usage.
