import { join } from 'node:path';

export const parlanceDir = (root: string): string => join(root, '.parlance');
export const sessionFile = (root: string): string => join(parlanceDir(root), '_session.json');
export const ledgerFile = (root: string): string => join(parlanceDir(root), 'ledger.jsonl');
export const sessionLogFile = (root: string): string => join(parlanceDir(root), 'session-log.md');
export const kibbleDir = (root: string): string => join(parlanceDir(root), 'kibble');
export const hooksDir = (root: string): string => join(parlanceDir(root), 'hooks');
export const configFile = (root: string): string => join(parlanceDir(root), 'config.json');
export const routingFile = (root: string): string => join(parlanceDir(root), 'tool-routing.md');
export const benchStateFile = (root: string): string => join(parlanceDir(root), 'bench', '_active.json');
export const benchResultsFile = (root: string): string => join(parlanceDir(root), 'bench', 'results.jsonl');
