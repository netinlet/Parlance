import type { AdapterCapabilities } from '@parlance/agent-core';

export const capabilities: AdapterCapabilities = {
  name: 'claude-code',
  events: {
    'session-started': 'supported',
    'task-received': 'supported',
    'pre-read': 'supported',
    'post-read': 'supported',
    'pre-write': 'supported',
    'post-write': 'supported',
    'pre-search': 'supported',
    'post-search': 'supported',
    'pre-native-tool': 'supported',
    'post-native-tool': 'supported',
    'pre-mcp-tool': 'supported',
    'post-mcp-tool': 'supported',
    'response-completed': 'supported',
  },
  outputs: {
    can_warn: true,
    can_block: false,
    can_inject_context: false,
  },
};
