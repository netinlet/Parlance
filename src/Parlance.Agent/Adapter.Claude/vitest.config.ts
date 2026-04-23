import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  resolve: {
    alias: [
      {
        find: /^@parlance\/agent-core$/,
        replacement: fileURLToPath(new URL('../Core/src/api.ts', import.meta.url)),
      },
      {
        find: /^@parlance\/agent-core\/(.*)$/,
        replacement: fileURLToPath(new URL('../Core/src/$1', import.meta.url)),
      },
    ],
  },
  test: {
    include: ['test/**/*.test.ts'],
    environment: 'node',
    testTimeout: 5000,
  },
});
