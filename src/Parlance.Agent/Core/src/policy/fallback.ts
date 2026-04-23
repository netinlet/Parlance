import { matchRoutingRule } from './routing.js';
import type { AgentEvent } from '../types.js';

export interface FallbackClassification {
  native_tool: 'read' | 'write' | 'search' | 'other';
  intent: string;
  suggested: string;
  why: string;
}

export function classifyFallback(event: AgentEvent): FallbackClassification | null {
  const hit = matchRoutingRule(event);
  if (!hit) return null;

  return {
    native_tool: toNativeKind(event),
    intent: describeIntent(event),
    suggested: hit.suggested_tool,
    why: hit.reason,
  };
}

function toNativeKind(event: AgentEvent): FallbackClassification['native_tool'] {
  switch (event.kind) {
    case 'pre-read':
    case 'post-read':
      return 'read';
    case 'pre-write':
    case 'post-write':
      return 'write';
    case 'pre-search':
    case 'post-search':
      return 'search';
    default:
      return 'other';
  }
}

function describeIntent(event: AgentEvent): string {
  if (event.kind === 'pre-read') return `read ${event.path}`;
  if (event.kind === 'pre-write') return `write ${event.path}`;
  if (event.kind === 'pre-search') {
    return `search ${event.pattern} (path=${event.path ?? ''} glob=${event.glob ?? ''} type=${event.file_type ?? ''})`;
  }
  return event.kind;
}
