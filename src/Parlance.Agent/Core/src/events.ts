import type {
  FileEvent,
  ResponseCompletedEvent,
  SearchEvent,
  SessionStartedEvent,
  TaskReceivedEvent,
  ToolEvent,
} from './types.js';

export const now = (): string => new Date().toISOString();

export const sessionStarted = (transcriptRef?: string): SessionStartedEvent => ({
  kind: 'session-started',
  at: now(),
  transcript_ref: transcriptRef,
});

export const taskReceived = (prompt: string): TaskReceivedEvent => ({
  kind: 'task-received',
  at: now(),
  prompt,
});

export const preRead = (path: string): FileEvent => ({ kind: 'pre-read', at: now(), path });
export const postRead = (path: string, bytes: number): FileEvent => ({
  kind: 'post-read',
  at: now(),
  path,
  content_bytes: bytes,
});

export const preWrite = (path: string): FileEvent => ({ kind: 'pre-write', at: now(), path });
export const postWrite = (path: string, bytes: number): FileEvent => ({
  kind: 'post-write',
  at: now(),
  path,
  content_bytes: bytes,
});

export const preSearch = (event: Omit<SearchEvent, 'kind' | 'at' | 'result_bytes'>): SearchEvent => ({
  kind: 'pre-search',
  at: now(),
  ...event,
});

export const postSearch = (event: Omit<SearchEvent, 'kind' | 'at'>): SearchEvent => ({
  kind: 'post-search',
  at: now(),
  ...event,
});

export const preTool = (
  kind: 'pre-native-tool' | 'pre-mcp-tool',
  tool: string,
  input: Record<string, unknown>,
): ToolEvent => ({
  kind,
  at: now(),
  tool_name: tool,
  input,
});

export const postTool = (
  kind: 'post-native-tool' | 'post-mcp-tool',
  tool: string,
  input: Record<string, unknown>,
  bytes: number,
): ToolEvent => ({
  kind,
  at: now(),
  tool_name: tool,
  input,
  output_bytes: bytes,
});

export const responseCompleted = (usage?: ResponseCompletedEvent['usage']): ResponseCompletedEvent => ({
  kind: 'response-completed',
  at: now(),
  usage,
});
