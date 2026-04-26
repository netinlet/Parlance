import type { AdapterCapabilities } from '@parlance/agent-core';

export const capabilities: AdapterCapabilities = {
  name: 'codex',
  events: {
    'session-started': 'supported',
    'task-received': 'supported',
    'pre-read': 'best-effort',
    'post-read': 'best-effort',
    'pre-write': 'best-effort',
    'post-write': 'best-effort',
    'pre-search': 'best-effort',
    'post-search': 'best-effort',
    'pre-native-tool': 'supported',
    'post-native-tool': 'supported',
    'pre-mcp-tool': 'supported',
    'post-mcp-tool': 'supported',
    'response-completed': 'supported',
  },
  outputs: {
    can_warn: true,
    can_block: true,
    can_inject_context: true,
  },
};
