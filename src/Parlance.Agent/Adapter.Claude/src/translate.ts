import {
  postRead,
  postSearch,
  postTool,
  postWrite,
  preRead,
  preSearch,
  preTool,
  preWrite,
  responseCompleted,
  sessionStarted,
  taskReceived,
} from '@parlance/agent-core';
import type { AgentContext, AgentEvent, SearchEvent } from '@parlance/agent-core';
import { capabilities } from './capabilities.js';

export interface ClaudeHookEnvelope {
  hook_event_name: 'SessionStart' | 'PreToolUse' | 'PostToolUse' | 'UserPromptSubmit' | 'Stop';
  session_id: string;
  transcript_path?: string;
  cwd: string;
  tool_name?: string;
  tool_input?: Record<string, unknown>;
  tool_response?: { content?: string; [k: string]: unknown };
  prompt?: string;
}

export interface Translated {
  event: AgentEvent;
  context: AgentContext;
  transcript_path: string | null;
}

export function translate(env: ClaudeHookEnvelope): Translated | null {
  const context: AgentContext = {
    project_root: env.cwd,
    session_id: env.session_id,
    cwd: env.cwd,
    adapter: 'claude-code',
    capabilities,
  };
  const transcript_path = env.transcript_path ?? null;

  switch (env.hook_event_name) {
    case 'SessionStart':
      return { event: sessionStarted(transcript_path ?? undefined), context, transcript_path };
    case 'UserPromptSubmit':
      return { event: taskReceived(env.prompt ?? ''), context, transcript_path };
    case 'Stop':
      return { event: responseCompleted(), context, transcript_path };
    case 'PreToolUse': {
      const event = fromPre(env);
      return event ? { event, context, transcript_path } : null;
    }
    case 'PostToolUse': {
      const event = fromPost(env);
      return event ? { event, context, transcript_path } : null;
    }
  }
}

function fromPre(env: ClaudeHookEnvelope): AgentEvent | null {
  const tool = env.tool_name ?? '';
  const input = env.tool_input ?? {};

  if (tool === 'Read' && typeof input.file_path === 'string') return preRead(input.file_path);
  if ((tool === 'Write' || tool === 'Edit' || tool === 'MultiEdit') && typeof input.file_path === 'string') return preWrite(input.file_path);
  if (tool === 'Grep' || tool === 'Glob') return searchEvent(tool, input, true);
  if (tool.startsWith('mcp__parlance__')) return preTool('pre-mcp-tool', tool, input);
  if (tool) return preTool('pre-native-tool', tool, input);
  return null;
}

function fromPost(env: ClaudeHookEnvelope): AgentEvent | null {
  const tool = env.tool_name ?? '';
  const input = env.tool_input ?? {};
  const output = env.tool_response ?? {};

  if (tool === 'Read' && typeof input.file_path === 'string') {
    return postRead(input.file_path, typeof output.content === 'string' ? output.content.length : 0);
  }
  if ((tool === 'Write' || tool === 'Edit' || tool === 'MultiEdit') && typeof input.file_path === 'string') {
    return postWrite(input.file_path, contentLength(input.content));
  }
  if (tool === 'Grep' || tool === 'Glob') return searchEvent(tool, { ...input, result_bytes: contentLength(output.content) }, false);
  if (tool.startsWith('mcp__parlance__')) return postTool('post-mcp-tool', tool, input, contentLength(output.content));
  if (tool) return postTool('post-native-tool', tool, input, contentLength(output.content));
  return null;
}

function searchEvent(tool: string, input: Record<string, unknown>, isPre: boolean): AgentEvent {
  const event: Omit<SearchEvent, 'kind' | 'at'> = {
    pattern: typeof input.pattern === 'string' ? input.pattern : '',
    path: typeof input.path === 'string' ? input.path : undefined,
    glob: typeof input.glob === 'string' ? input.glob : typeof input.pattern === 'string' && tool === 'Glob' ? input.pattern : undefined,
    file_type: typeof input.type === 'string' ? input.type : undefined,
    result_bytes: typeof input.result_bytes === 'number' ? input.result_bytes : undefined,
  };

  return isPre
    ? preSearch({ pattern: event.pattern, path: event.path, glob: event.glob, file_type: event.file_type })
    : postSearch(event);
}

function contentLength(value: unknown): number {
  return typeof value === 'string' ? value.length : 0;
}
