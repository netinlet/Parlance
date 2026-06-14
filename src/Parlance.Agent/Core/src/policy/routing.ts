import type { AgentEvent } from '../types.js';

export interface RoutingMatch {
  suggested_tool: string;
  message: string;
  reason: string;
}

const CS_FILE_PATTERN = /\.(cs|csproj|sln|slnx|props|targets)$/i;
const CS_GLOB_PATTERN =
  /(^|\/)\*\*?\/[^/]*\.(cs|csproj|sln|slnx|props|targets)$|(^|\/)[^/]*\.(cs|csproj|sln|slnx|props|targets)$/i;

// Bash is the dominant way the model does C# code intelligence off the books —
// grep/rg/find/cat over .cs slips past the Read|Grep|Glob matcher entirely.
// These let us classify (and nudge) those shell commands like any other fallback.
const BASH_SEARCH_UTIL = /\b(grep|egrep|fgrep|rg|ag|ack|ripgrep)\b/;
const BASH_READ_UTIL = /\b(cat|head|tail|less|more|bat)\b/;
const BASH_FIND_UTIL = /\bfind\b/;
const BASH_MENTIONS_CS =
  /\.(cs|csproj|sln|slnx|props|targets)\b|--include=[^\s]*\.cs|--type[ =]cs\b|-tcs\b|-g\s+["']?[^"'\s]*\.cs/i;

export function isParlanceTool(toolName: string): boolean {
  return toolName.startsWith('mcp__parlance__');
}

function matchBashCodeIntel(command: string): RoutingMatch | null {
  const searches =
    BASH_SEARCH_UTIL.test(command) || BASH_FIND_UTIL.test(command);
  const reads = BASH_READ_UTIL.test(command);
  if (!(searches || reads)) return null;
  if (!BASH_MENTIONS_CS.test(command)) return null;

  const snippet = command.length > 60 ? `${command.slice(0, 60)}…` : command;
  // A pure read util (cat Foo.cs) maps to describe-type; anything searching maps to search-symbols.
  return reads && !searches
    ? {
        suggested_tool: 'mcp__parlance__describe-type',
        message:
          'Use Parlance MCP tools before cat/head-ing C# source in bash.',
        reason: `bash read of C# (${snippet})`,
      }
    : {
        suggested_tool: 'mcp__parlance__search-symbols',
        message:
          'Use Parlance symbol/search tools before grep/find on C# code in bash.',
        reason: `bash search of C# (${snippet})`,
      };
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
      message:
        'Use Parlance symbol/search tools before grep/glob on C# workspace code.',
      reason: `pre-search for C# intent (${event.pattern})`,
    };
  }

  if (event.kind === 'pre-native-tool' && event.tool_name === 'Bash') {
    const command =
      typeof event.input.command === 'string' ? event.input.command : '';
    return matchBashCodeIntel(command);
  }

  return null;
}
