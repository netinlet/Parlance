import { estimateTokens } from '../telemetry/estimate.js';
import { classifyFallback } from './fallback.js';
import { isParlanceTool, matchRoutingRule } from './routing.js';
import type { AgentContext, AgentEvent, EventEvaluation, SessionState, ToolEvent, ToolUsageRecord } from '../types.js';

export function emptySessionState(ctx: AgentContext, transcript_ref: string | null): SessionState {
  return {
    session_id: ctx.session_id,
    adapter: ctx.adapter,
    started_at: new Date().toISOString(),
    cwd: ctx.cwd,
    transcript_ref,
    parlance_calls: 0,
    native_fallbacks: 0,
    tool_calls: [],
    read_tokens: 0,
    write_tokens: 0,
    active_bench: null,
  };
}

export function evaluateEvent(event: AgentEvent, ctx: AgentContext, state: SessionState): EventEvaluation {
  const guidance: EventEvaluation['guidance'] = [];
  const effects: EventEvaluation['effects'] = [];
  let next = state;

  if (event.kind.startsWith('pre-')) {
    const match = matchRoutingRule(event);
    if (match) {
      guidance.push({
        severity: 'warn',
        message: match.message,
        suggested_tool: match.suggested_tool,
        reason: match.reason,
      });
      const fallback = classifyFallback(event);
      if (fallback) {
        effects.push({
          kind: 'persist-feedback',
          feedback: {
            date: event.at.slice(0, 10),
            adapter: ctx.adapter,
            native_tool: fallback.native_tool,
            intent: fallback.intent,
            why: fallback.why,
            suggested: fallback.suggested,
            session_id: ctx.session_id,
          },
        });
        next = { ...next, native_fallbacks: next.native_fallbacks + 1 };
      }
    }
  }

  if (
    event.kind === 'post-read' ||
    event.kind === 'post-write' ||
    event.kind === 'post-search' ||
    event.kind === 'post-native-tool' ||
    event.kind === 'post-mcp-tool'
  ) {
    const record = toUsageRecord(event);
    effects.push({ kind: 'persist-tool-usage', record });
    next = {
      ...next,
      parlance_calls: next.parlance_calls + (record.is_mcp_parlance ? 1 : 0),
      read_tokens: next.read_tokens + (event.kind === 'post-read' ? record.output_tokens : 0),
      write_tokens: next.write_tokens + (event.kind === 'post-write' ? record.output_tokens : 0),
      tool_calls: [...next.tool_calls, record],
    };
  }

  return { guidance, effects, next_state: next };
}

function toUsageRecord(event: AgentEvent): ToolUsageRecord {
  const is_native_fallback = matchRoutingRule(flipToPre(event)) !== null;

  if (event.kind === 'post-read' || event.kind === 'post-write') {
    const bytes = event.content_bytes ?? 0;
    return {
      at: event.at,
      event_kind: event.kind,
      tool_name: event.kind === 'post-read' ? 'Read' : 'Write',
      target: event.path,
      is_mcp_parlance: false,
      is_native_fallback,
      output_tokens: estimateTokens('x'.repeat(bytes), 'code'),
    };
  }

  if (event.kind === 'post-search') {
    return {
      at: event.at,
      event_kind: event.kind,
      tool_name: 'Search',
      target: `${event.pattern} (glob=${event.glob ?? ''} type=${event.file_type ?? ''})`,
      is_mcp_parlance: false,
      is_native_fallback,
      output_tokens: estimateTokens('x'.repeat(event.result_bytes ?? 0), 'code'),
    };
  }

  const toolEvent = event as ToolEvent;
  return {
    at: event.at,
    event_kind: event.kind,
    tool_name: toolEvent.tool_name,
    target: JSON.stringify(toolEvent.input).slice(0, 80),
    is_mcp_parlance: isParlanceTool(toolEvent.tool_name),
    is_native_fallback,
    output_tokens: estimateTokens('x'.repeat(toolEvent.output_bytes ?? 0), 'code'),
  };
}

function flipToPre(event: AgentEvent): AgentEvent {
  if (event.kind === 'post-read') return { ...event, kind: 'pre-read' };
  if (event.kind === 'post-write') return { ...event, kind: 'pre-write' };
  if (event.kind === 'post-search') return { ...event, kind: 'pre-search' };
  if (event.kind === 'post-native-tool') return { ...event, kind: 'pre-native-tool' };
  if (event.kind === 'post-mcp-tool') return { ...event, kind: 'pre-mcp-tool' };
  return event;
}
