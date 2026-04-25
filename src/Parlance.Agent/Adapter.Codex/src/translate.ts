import {
  postSearch,
  postTool,
  postWrite,
  preSearch,
  preTool,
  preWrite,
  responseCompleted,
  sessionStarted,
  taskReceived,
} from '@parlance/agent-core';
import type { AgentContext, AgentEvent } from '@parlance/agent-core';
import { capabilities } from './capabilities.js';

export type CodexHookEventName =
  | 'SessionStart'
  | 'PreToolUse'
  | 'PostToolUse'
  | 'UserPromptSubmit'
  | 'Stop'
  | 'PermissionRequest';

export interface CodexHookEnvelope {
  hook_event_name: CodexHookEventName;
  session_id: string;
  transcript_path?: string | null;
  cwd: string;
  model?: string;
  turn_id?: string;
  tool_use_id?: string;
  source?: string;
  tool_name?: string;
  tool_input?: Record<string, unknown>;
  tool_response?: unknown;
  prompt?: string;
  stop_hook_active?: boolean;
  last_assistant_message?: string | null;
}

export interface Translated {
  event: AgentEvent;
  context: AgentContext;
  transcript_path: string | null;
}

export type BashClassificationKind = 'search' | 'read' | 'verify' | 'build' | 'vcs-inspect' | 'unknown';

export interface BashClassification {
  kind: BashClassificationKind;
  confidence: 'high' | 'medium' | 'low';
  reason: string;
}

export function translate(env: CodexHookEnvelope): Translated | null {
  const context: AgentContext = {
    project_root: env.cwd,
    session_id: env.session_id,
    cwd: env.cwd,
    adapter: 'codex',
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
    case 'PermissionRequest':
      return null;
  }
}

function fromPre(env: CodexHookEnvelope): AgentEvent | null {
  const tool = env.tool_name ?? '';
  const input = env.tool_input ?? {};

  if (tool === 'Bash') return preBash(input);
  if (tool === 'apply_patch') {
    const paths = pathsFromPatchCommand(commandFromInput(input));
    if (paths.length === 1) return preWrite(paths[0]);
    return preTool('pre-native-tool', tool, input);
  }
  if (tool.startsWith('mcp__parlance__')) return preTool('pre-mcp-tool', tool, input);
  if (tool.startsWith('mcp__')) return preTool('pre-native-tool', tool, input);
  if (tool) return preTool('pre-native-tool', tool, input);
  return null;
}

function fromPost(env: CodexHookEnvelope): AgentEvent | null {
  const tool = env.tool_name ?? '';
  const input = env.tool_input ?? {};
  const outputBytes = outputLength(env.tool_response);

  if (tool === 'Bash') return postBash(input, outputBytes);
  if (tool === 'apply_patch') {
    const paths = pathsFromPatchCommand(commandFromInput(input));
    if (paths.length === 1) return postWrite(paths[0], 0);
    return postTool('post-native-tool', tool, input, outputBytes);
  }
  if (tool.startsWith('mcp__parlance__')) return postTool('post-mcp-tool', tool, input, outputBytes);
  if (tool.startsWith('mcp__')) return postTool('post-native-tool', tool, input, outputBytes);
  if (tool) return postTool('post-native-tool', tool, input, outputBytes);
  return null;
}

function preBash(input: Record<string, unknown>): AgentEvent {
  const command = commandFromInput(input);
  const classification = classifyBashCommand(command);
  if (classification.kind === 'search') {
    return preSearch({ pattern: command, path: undefined, glob: undefined, file_type: undefined });
  }
  if (classification.kind === 'read') {
    return preTool('pre-native-tool', 'Bash', input);
  }
  return preTool('pre-native-tool', 'Bash', input);
}

function postBash(input: Record<string, unknown>, outputBytes: number): AgentEvent {
  const command = commandFromInput(input);
  const classification = classifyBashCommand(command);
  if (classification.kind === 'search') {
    return postSearch({ pattern: command, result_bytes: outputBytes });
  }
  return postTool('post-native-tool', 'Bash', input, outputBytes);
}

export function classifyBashCommand(command: string): BashClassification {
  const normalized = command.trim();
  const first = normalized.split(/\s+/)[0] ?? '';

  if (/^(rg|grep|find|fd)\b/.test(first)) {
    return { kind: 'search', confidence: 'high', reason: `${first} command` };
  }
  if (/^(cat|head|tail|nl|wc)\b/.test(first) || /^sed\s+-n\b/.test(normalized)) {
    return { kind: 'read', confidence: 'high', reason: `${first || 'sed -n'} command` };
  }
  if (/^(dotnet\s+test|npm\s+test|make\s+test)\b/.test(normalized)) {
    return { kind: 'verify', confidence: 'high', reason: 'test command' };
  }
  if (/^(dotnet\s+build|npm\s+run\s+build|make\s+build)\b/.test(normalized)) {
    return { kind: 'build', confidence: 'high', reason: 'build command' };
  }
  if (/^git\s+(status|diff|log|show)\b/.test(normalized)) {
    return { kind: 'vcs-inspect', confidence: 'high', reason: 'git inspection command' };
  }
  return { kind: 'unknown', confidence: 'low', reason: 'no classifier matched' };
}

export function commandFromInput(input: Record<string, unknown>): string {
  return typeof input.command === 'string' ? input.command : '';
}

function outputLength(output: unknown): number {
  if (typeof output === 'string') return output.length;
  if (output && typeof output === 'object') {
    const record = output as Record<string, unknown>;
    if (typeof record.content === 'string') return record.content.length;
    if (typeof record.output === 'string') return record.output.length;
    if (typeof record.stdout === 'string' || typeof record.stderr === 'string') {
      return (typeof record.stdout === 'string' ? record.stdout.length : 0)
        + (typeof record.stderr === 'string' ? record.stderr.length : 0);
    }
  }
  return 0;
}

function pathsFromPatchCommand(command: string): string[] {
  const paths = new Set<string>();
  for (const line of command.split('\n')) {
    const match = /^(?:\*\*\* (?:Update|Delete|Add) File:|--- a\/|\+\+\+ b\/)\s*(.+)$/.exec(line.trim());
    if (!match) continue;
    const path = match[1].trim();
    if (path && path !== '/dev/null') paths.add(path);
  }
  return [...paths];
}
