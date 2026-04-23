import { build } from 'esbuild';
import { existsSync, mkdirSync, rmSync } from 'node:fs';
import { join } from 'node:path';

const root = new URL('..', import.meta.url).pathname;
const distDir = join(root, 'dist');

if (existsSync(distDir)) rmSync(distDir, { recursive: true });
mkdirSync(distDir, { recursive: true });

const entries = [
  { in: 'src/cli.ts', out: 'dist/cli.js' },
  { in: 'src/api.ts', out: 'dist/api.js' },
];

for (const entry of entries) {
  await build({
    entryPoints: [join(root, entry.in)],
    outfile: join(root, entry.out),
    bundle: true,
    platform: 'node',
    target: 'node20',
    format: 'esm',
    banner: { js: '#!/usr/bin/env node' },
  });
}

console.log('agent-core bundled');
