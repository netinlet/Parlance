import type { AgentEvent, FileEvent, SearchEvent } from '../types.js';
import { matchRoutingRule } from '../policy/routing.js';

export function generateRoutingDoc(): string {
  const samples: AgentEvent[] = [
    { kind: 'pre-read', at: '', path: 'Foo.cs' } satisfies FileEvent,
    { kind: 'pre-search', at: '', pattern: 'x', file_type: 'cs' } satisfies SearchEvent,
    { kind: 'pre-search', at: '', pattern: 'x', glob: '**/*.cs' } satisfies SearchEvent,
    { kind: 'pre-search', at: '', pattern: 'x', path: '/proj/src/sub' } satisfies SearchEvent,
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

function describe(event: AgentEvent): string {
  if (event.kind === 'pre-read') return 'Reading a C# file';
  if (event.kind === 'pre-search' && event.file_type === 'cs') return 'Searching with type=cs';
  if (event.kind === 'pre-search' && event.glob?.includes('.cs')) return 'Searching with C# glob';
  if (event.kind === 'pre-search') return 'Searching under /src/ (no filter)';
  return event.kind;
}
