import type { AgentEvent, FileEvent, SearchEvent, ToolEvent } from '../types.js';
import { matchRoutingRule } from '../policy/routing.js';

export function generateRoutingDoc(): string {
  const samples: AgentEvent[] = [
    { kind: 'pre-read', at: '', path: 'Foo.cs' } satisfies FileEvent,
    { kind: 'pre-search', at: '', pattern: 'x', file_type: 'cs' } satisfies SearchEvent,
    { kind: 'pre-search', at: '', pattern: 'x', glob: '**/*.cs' } satisfies SearchEvent,
    { kind: 'pre-search', at: '', pattern: 'x', path: '/proj/src/sub' } satisfies SearchEvent,
    { kind: 'pre-native-tool', at: '', tool_name: 'Bash', input: { command: 'grep -rn Foo --include=*.cs' } } satisfies ToolEvent,
  ];

  const lines = ['# Parlance Tool Routing', '', 'Generated from agent-core routing rules.', ''];
  for (const event of samples) {
    const hit = matchRoutingRule(event);
    if (!hit) continue;
    lines.push(`## ${describe(event)}`);
    lines.push(`- **Suggested:** \`${hit.suggested_tool}\``);
    lines.push(`- ${hit.message}`);
    lines.push('');
  }

  return lines.join('\n');
}

/**
 * Session-start context payload. A short, imperative tool-first preamble
 * followed by the generated routing rules, so the model is primed to prefer
 * Parlance MCP tools the moment a session begins — no reliance on the model
 * choosing to read CLAUDE.md or invoke a skill. Reuses generateRoutingDoc so
 * the rules keep a single source of truth.
 */
export function generateSessionContext(): string {
  return [
    'Parlance MCP code-intelligence tools are available in this workspace.',
    'Prefer them over native Read/Grep/Glob when working with C# code.',
    '',
    generateRoutingDoc(),
  ].join('\n');
}

function describe(event: AgentEvent): string {
  if (event.kind === 'pre-read') return 'Reading a C# file';
  if (event.kind === 'pre-search' && event.file_type === 'cs') return 'Searching with type=cs';
  if (event.kind === 'pre-search' && event.glob?.includes('.cs')) return 'Searching with C# glob';
  if (event.kind === 'pre-search') return 'Searching under /src/ (no filter)';
  if (event.kind === 'pre-native-tool') return 'grep/find/cat over C# in bash';
  return event.kind;
}
