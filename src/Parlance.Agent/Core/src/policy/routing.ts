import type { AgentEvent } from '../types.js';

export interface RoutingMatch {
  suggested_tool: string;
  message: string;
  reason: string;
}

const CS_FILE_PATTERN = /\.(cs|csproj|sln|slnx|props|targets)$/i;
const CS_GLOB_PATTERN = /(^|\/)\*\*?\/[^/]*\.(cs|csproj|sln|slnx|props|targets)$|(^|\/)[^/]*\.(cs|csproj|sln|slnx|props|targets)$/i;

export function isParlanceTool(toolName: string): boolean {
  return toolName.startsWith('mcp__parlance__');
}

export function matchRoutingRule(event: AgentEvent): RoutingMatch | null {
  if (event.kind === 'pre-read') {
    if (!CS_FILE_PATTERN.test(event.path)) return null;
    return {
      suggested_tool: 'mcp__parlance__describe-type',
      message: 'Use Parlance MCP tools before reading C# source directly.',
      reason: `pre-read on C# path ${event.path}`,
    };
  }

  if (event.kind === 'pre-search') {
    const hasCsType = event.file_type?.toLowerCase() === 'cs';
    const hasCsGlob = event.glob ? CS_GLOB_PATTERN.test(event.glob) : false;
    const hasCsPattern = CS_GLOB_PATTERN.test(event.pattern);
    const softSrcPath = event.path?.includes('/src/') ?? false;
    if (!(hasCsType || hasCsGlob || hasCsPattern || softSrcPath)) return null;
    return {
      suggested_tool: 'mcp__parlance__search-symbols',
      message: 'Use Parlance symbol/search tools before grep/glob on C# workspace code.',
      reason: `pre-search for C# intent (${event.pattern})`,
    };
  }

  return null;
}
