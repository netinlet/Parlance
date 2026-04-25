# @parlance/agent-adapter-claude

Claude Code adapter for `@parlance/agent-core`.

Responsibilities:
- translate Claude Code hook JSON into `AgentEvent` values
- render core `EventEvaluation` guidance into Claude-visible outputs (stderr + exit code)
- parse Claude Code JSONL transcripts into agent-neutral `UsageTotals`
- write/remove the Claude Code-specific install artifacts (`.claude/settings.local.json`,
  `.mcp.json`, `.parlance/hooks/*.js`)

This package is the **only** place in the repo that knows about Claude Code
hook event names, `.claude/settings.local.json`, or `~/.claude/projects/*.jsonl`.

## Build

    npm install && npm test && npm run build

## Capability manifest

See `src/capabilities.ts` for which agent-neutral events this adapter emits,
and with what fidelity.
