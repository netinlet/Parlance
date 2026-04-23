export type EventKind =
  | 'session-started'
  | 'task-received'
  | 'pre-read'
  | 'post-read'
  | 'pre-write'
  | 'post-write'
  | 'pre-search'
  | 'post-search'
  | 'pre-native-tool'
  | 'post-native-tool'
  | 'pre-mcp-tool'
  | 'post-mcp-tool'
  | 'response-completed';

export type CapabilityFidelity = 'supported' | 'best-effort' | 'unavailable';

export interface AdapterCapabilities {
  name: string;
  events: Partial<Record<EventKind, CapabilityFidelity>>;
  outputs: {
    can_warn: boolean;
    can_block: boolean;
    can_inject_context: boolean;
  };
}

export interface AgentContext {
  project_root: string;
  session_id: string;
  cwd: string;
  adapter: string;
  capabilities: AdapterCapabilities;
}

export interface BaseEvent {
  kind: EventKind;
  at: string;
}

export interface SessionStartedEvent extends BaseEvent {
  kind: 'session-started';
  transcript_ref?: string;
}

export interface TaskReceivedEvent extends BaseEvent {
  kind: 'task-received';
  prompt: string;
}

export interface FileEvent extends BaseEvent {
  kind: 'pre-read' | 'post-read' | 'pre-write' | 'post-write';
  path: string;
  content_bytes?: number;
}

export interface SearchEvent extends BaseEvent {
  kind: 'pre-search' | 'post-search';
  pattern: string;
  path?: string;
  glob?: string;
  file_type?: string;
  result_bytes?: number;
}

export interface ToolEvent extends BaseEvent {
  kind: 'pre-native-tool' | 'post-native-tool' | 'pre-mcp-tool' | 'post-mcp-tool';
  tool_name: string;
  input: Record<string, unknown>;
  output_bytes?: number;
}

export interface ResponseCompletedEvent extends BaseEvent {
  kind: 'response-completed';
  usage?: UsageTotals;
}

export type AgentEvent =
  | SessionStartedEvent
  | TaskReceivedEvent
  | FileEvent
  | SearchEvent
  | ToolEvent
  | ResponseCompletedEvent;

export type GuidanceSeverity = 'info' | 'warn' | 'block';

export interface Guidance {
  severity: GuidanceSeverity;
  message: string;
  suggested_tool?: string;
  reason?: string;
}

export interface PersistToolUsageEffect {
  kind: 'persist-tool-usage';
  record: ToolUsageRecord;
}

export interface PersistFeedbackEffect {
  kind: 'persist-feedback';
  feedback: FeedbackRecord;
}

export interface PersistSessionSummaryEffect {
  kind: 'persist-session-summary';
  summary: SessionSummary;
}

export type EvaluationEffect =
  | PersistToolUsageEffect
  | PersistFeedbackEffect
  | PersistSessionSummaryEffect;

export interface SessionState {
  session_id: string;
  adapter: string;
  started_at: string;
  cwd: string;
  transcript_ref: string | null;
  parlance_calls: number;
  native_fallbacks: number;
  tool_calls: ToolUsageRecord[];
  read_tokens: number;
  write_tokens: number;
  active_bench: { task_id: string; variant: string; started_at: string } | null;
}

export interface EventEvaluation {
  guidance: Guidance[];
  effects: EvaluationEffect[];
  next_state: SessionState | null;
}

export interface UsageTotals {
  input_tokens: number;
  output_tokens: number;
  cache_read_tokens: number;
  cache_write_tokens: number;
}

export interface ToolUsageRecord {
  at: string;
  event_kind: EventKind;
  tool_name: string;
  target: string;
  is_mcp_parlance: boolean;
  is_native_fallback: boolean;
  output_tokens: number;
}

export interface FeedbackRecord {
  date: string;
  adapter: string;
  native_tool: string;
  intent: string;
  why: string;
  suggested: string;
  session_id: string;
}

export interface SessionSummary {
  session_id: string;
  date: string;
  adapter: string;
  started_at: string;
  ended_at: string;
  duration_s: number;
  branch: string | null;
  parlance_calls: number;
  native_fallbacks: number;
  tool_call_count: number;
  read_tokens: number;
  write_tokens: number;
  usage: UsageTotals;
}

export {};
