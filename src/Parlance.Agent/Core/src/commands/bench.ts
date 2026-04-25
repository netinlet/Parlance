import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { benchResultsFile } from '../storage/paths.js';
import type { BenchResultRecord } from '../types.js';

export async function runBench(argv: string[]): Promise<number> {
  const [action, ...rest] = argv;
  if (action !== 'report') {
    process.stderr.write('usage: bench report --task <id> [--project <path>]\n');
    return 2;
  }

  let project = process.cwd();
  let task: string | undefined;
  for (let index = 0; index < rest.length; index += 1) {
    if (rest[index] === '--project' && rest[index + 1]) project = rest[index + 1];
    if (rest[index] === '--task' && rest[index + 1]) task = rest[index + 1];
  }

  if (!task) {
    process.stderr.write('--task required\n');
    return 2;
  }

  const path = benchResultsFile(resolve(project));
  if (!existsSync(path)) {
    process.stdout.write(`no bench data at ${path}\n`);
    return 0;
  }

  const records = readFileSync(path, 'utf8')
    .split('\n')
    .filter(Boolean)
    .map((line) => JSON.parse(line) as BenchResultRecord)
    .filter((record) => record.task_id === task);
  if (records.length === 0) {
    process.stdout.write(`no bench records for task ${task}\n`);
    return 0;
  }

  const lines: string[] = [];
  lines.push(`=== Bench: ${task} ===`);
  lines.push(
    `${'Variant'.padEnd(14)}${'Adapter'.padEnd(14)}${'Input'.padStart(12)}${'Output'.padStart(12)}${'Cache-read'.padStart(14)}${'Duration'.padStart(12)}`,
  );
  lines.push('-'.repeat(78));
  for (const record of records) {
    const durationS = Math.round(
      (new Date(record.ended_at).getTime() - new Date(record.started_at).getTime()) / 1000,
    );
    lines.push(
      record.variant.padEnd(14)
      + record.adapter.padEnd(14)
      + record.usage.input_tokens.toLocaleString().padStart(12)
      + record.usage.output_tokens.toLocaleString().padStart(12)
      + record.usage.cache_read_tokens.toLocaleString().padStart(14)
      + `${durationS}s`.padStart(12),
    );
  }

  process.stdout.write(`${lines.join('\n')}\n`);
  return 0;
}
