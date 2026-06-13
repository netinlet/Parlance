import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { ledgerFile, parlanceDir, sessionLogFile } from '../storage/paths.js';

export async function runStatus(argv: string[]): Promise<number> {
  let project = process.cwd();
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === '--project' && argv[index + 1]) project = argv[index + 1];
  }

  const root = resolve(project);
  process.stdout.write(`project .parlance/ (install): ${existsSync(parlanceDir(root)) ? 'present' : 'missing'}\n`);

  const ledgerPath = ledgerFile();
  process.stdout.write(`central ledger: ${ledgerPath}\n`);
  if (existsSync(ledgerPath)) {
    const lines = readFileSync(ledgerPath, 'utf8').trim().split('\n').filter(Boolean);
    process.stdout.write(`sessions logged (all worktrees): ${lines.length}\n`);
  }

  const logPath = sessionLogFile();
  if (existsSync(logPath)) {
    const tail = readFileSync(logPath, 'utf8').trim().split('\n').slice(-5);
    process.stdout.write('recent:\n');
    for (const line of tail) process.stdout.write(`  ${line}\n`);
  }

  return 0;
}
