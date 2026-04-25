# @parlance/agent-core

Agent-neutral integration core for Parlance.

## Firewall

This package **must not** reference:
- `.claude/` or any Claude Code-specific path
- Claude Code JSONL record shapes
- Claude Code hook event names (`PreToolUse`, `PostToolUse`, `SessionStart`,
  `Stop`, `UserPromptSubmit`)

Agent-specific translation happens in adapters. Core consumes `AgentEvent`
values and produces `EventEvaluation` values — both defined in `src/types.ts`.

CI runs a grep-gate to enforce these rules (see `.github/workflows/ci.yml`).

## Build

    npm install
    npm test
    npm run build

The `dist/` directory is committed — it ships inside the Parlance dotnet tool.

## Entry points

- `dist/cli.js` — `parlance-agent-core <status|report|bench>` (agent-neutral)
- `dist/api.js` — library surface consumed by adapters
