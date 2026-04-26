import type { EventEvaluation, Guidance } from '@parlance/agent-core';
import type { CodexHookEventName } from './translate.js';

export interface CodexHookOutput {
  continue?: boolean;
  stopReason?: string;
  systemMessage?: string;
  suppressOutput?: boolean;
  hookSpecificOutput?: {
    hookEventName: 'SessionStart' | 'UserPromptSubmit';
    additionalContext: string;
  };
}

export function renderForCodex(eventName: CodexHookEventName, evaluation: EventEvaluation): CodexHookOutput | null {
  const message = guidanceMessage(evaluation.guidance);
  if (!message) return null;

  if (eventName === 'SessionStart' || eventName === 'UserPromptSubmit') {
    return {
      hookSpecificOutput: {
        hookEventName: eventName,
        additionalContext: message,
      },
    };
  }

  if (eventName === 'PreToolUse') {
    return { systemMessage: message };
  }

  if (eventName === 'PostToolUse') {
    return { systemMessage: message };
  }

  return null;
}

export function writeCodexOutput(output: CodexHookOutput | null, write: (chunk: string) => void = (s) => process.stdout.write(s)): void {
  if (!output) return;
  write(`${JSON.stringify(output)}\n`);
}

function guidanceMessage(guidance: Guidance[]): string | null {
  if (guidance.length === 0) return null;
  return guidance.map((entry) => {
    const suffix = entry.suggested_tool ? ` Suggested: ${entry.suggested_tool}.` : '';
    return `parlance: ${entry.message}${suffix}`;
  }).join('\n');
}
