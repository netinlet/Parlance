import { build } from 'esbuild';
import { existsSync, mkdirSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('..', import.meta.url));
const distDir = join(root, 'dist');

if (existsSync(distDir)) rmSync(distDir, { recursive: true });
mkdirSync(join(distDir, 'hooks'), { recursive: true });

const common = {
  bundle: true,
  platform: 'node',
  target: 'node20',
  format: 'esm',
  banner: { js: '#!/usr/bin/env node' },
};

const entries = [
  { in: 'src/cli.ts', out: 'dist/cli.js' },
  { in: 'src/hooks/session-start.ts', out: 'dist/hooks/session-start.js' },
  { in: 'src/hooks/pre-tool.ts', out: 'dist/hooks/pre-tool.js' },
  { in: 'src/hooks/post-tool.ts', out: 'dist/hooks/post-tool.js' },
  { in: 'src/hooks/stop.ts', out: 'dist/hooks/stop.js' },
  { in: 'src/hooks/user-prompt-submit.ts', out: 'dist/hooks/user-prompt-submit.js' },
];

for (const entry of entries) {
  await build({
    ...common,
    entryPoints: [join(root, entry.in)],
    outfile: join(root, entry.out),
  });
}

console.log('adapter-codex bundled');
