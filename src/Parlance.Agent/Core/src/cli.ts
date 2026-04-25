import { runBench } from './commands/bench.js';
import { runReport } from './commands/report.js';
import { runStatus } from './commands/status.js';

const commands: Record<string, (args: string[]) => Promise<number>> = {
  status: runStatus,
  report: runReport,
  bench: runBench,
};

async function main(): Promise<void> {
  const [, , subcommand, ...rest] = process.argv;
  if (!subcommand || subcommand === '--help' || subcommand === '-h') {
    help();
    process.exit(subcommand ? 0 : 1);
  }

  const command = commands[subcommand];
  if (!command) {
    process.stderr.write(`unknown: ${subcommand}\n`);
    help();
    process.exit(2);
  }

  process.exit(await command(rest));
}

function help(): void {
  process.stderr.write([
    'usage: parlance-agent-core <command> [args]',
    '',
    '  status                       install state + recent ledger summary',
    '  report [--days N]            session analysis',
    '  bench report --task <id>     variant comparison',
    '',
  ].join('\n'));
}

void main();
