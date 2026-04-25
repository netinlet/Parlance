import type { EventEvaluation } from '@parlance/agent-core';

export type Writer = (chunk: string) => void;

export function renderToStderr(evaluation: EventEvaluation, write: Writer = (s) => process.stderr.write(s)): void {
  for (const guidance of evaluation.guidance) {
    const prefix = guidance.severity === 'info'
      ? 'ℹ parlance'
      : guidance.severity === 'block'
        ? '⛔ parlance'
        : '⚡ parlance';
    write(`${prefix}: ${guidance.message}\n`);
  }
}

export function exitCodeForEvaluation(_evaluation: EventEvaluation): number {
  return 0;
}
