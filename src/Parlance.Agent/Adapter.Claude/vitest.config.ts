import { defineConfig } from 'vitest/config';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  test: {
    include: ['test/**/*.test.ts'],
    environment: 'node',
    testTimeout: 5000,
    alias: {
      '@parlance/agent-core': fileURLToPath(new URL('../Core/src/api.ts', import.meta.url)),
    },
  },
});
