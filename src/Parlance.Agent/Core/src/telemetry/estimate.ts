import { extname } from 'node:path';

export type ContentKind = 'code' | 'prose' | 'mixed';

const RATIOS: Record<ContentKind, number> = {
  code: 3.5,
  prose: 4.0,
  mixed: 3.75,
};

const CODE_EXTENSIONS = new Set([
  '.cs', '.csproj', '.sln', '.slnx', '.props', '.targets',
  '.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs',
  '.py', '.go', '.rs', '.java', '.kt', '.swift',
  '.c', '.cpp', '.h', '.hpp',
  '.sh', '.bash', '.zsh', '.ps1',
  '.json', '.yaml', '.yml', '.xml', '.toml',
]);

const PROSE_EXTENSIONS = new Set(['.md', '.txt', '.rst', '.adoc']);

export function estimateTokens(content: string, kind: ContentKind): number {
  if (!content) return 0;
  return Math.round(content.length / RATIOS[kind]);
}

export function classifyPath(path: string): ContentKind {
  const ext = extname(path).toLowerCase();
  if (CODE_EXTENSIONS.has(ext)) return 'code';
  if (PROSE_EXTENSIONS.has(ext)) return 'prose';
  return 'mixed';
}

export function estimateFromExtension(path: string, content: string): number {
  return estimateTokens(content, classifyPath(path));
}
