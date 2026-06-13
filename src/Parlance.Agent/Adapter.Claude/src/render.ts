import type { EventEvaluation } from '@parlance/agent-core';

export type Writer = (chunk: string) => void;

export function renderToStderr(
  evaluation: EventEvaluation,
  write: Writer = (s) => process.stderr.write(s),
): void {
  for (const guidance of evaluation.guidance) {
    const prefix =
      guidance.severity === 'info'
        ? 'ℹ parlance'
        : guidance.severity === 'block'
          ? '⛔ parlance'
          : '⚡ parlance';
    write(`${prefix}: ${guidance.message}\n`);
  }
}

export function writeContextOutput(
  additionalContext: string,
  write: Writer = (s) => process.stdout.write(s),
): void {
  write(
    `${JSON.stringify({
      hookSpecificOutput: {
        hookEventName: 'SessionStart',
        additionalContext,
      },
    })}\n`,
  );
}
