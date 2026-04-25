import { runInstall } from './commands/install.js';
import { runUninstall } from './commands/uninstall.js';

const commands: Record<string, (args: string[]) => Promise<number>> = {
  install: runInstall,
  uninstall: runUninstall,
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
    'usage: parlance-agent-codex <install|uninstall> [args]',
    '  install --solution <path> [--project <dir>]',
    '  uninstall [--project <dir>] [--purge]',
    '',
  ].join('\n'));
}

void main();
