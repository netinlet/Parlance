import { describe, expect, it } from 'vitest';
import { classifyBashCommand, translate } from '../src/translate.js';

describe('translate', () => {
  it('SessionStart -> session-started', () => {
    const translated = translate({
      hook_event_name: 'SessionStart',
      session_id: 's1',
      cwd: '/p',
      transcript_path: '/t.jsonl',
    });

    expect(translated?.event.kind).toBe('session-started');
    expect(translated?.context.adapter).toBe('codex');
    expect(translated?.context.project_root).toBe('/p');
  });

  it('UserPromptSubmit -> task-received', () => {
    const translated = translate({
      hook_event_name: 'UserPromptSubmit',
      session_id: 's1',
      cwd: '/p',
      prompt: 'fix it',
    });

    expect(translated?.event.kind).toBe('task-received');
    expect(translated && 'prompt' in translated.event && translated.event.prompt).toBe('fix it');
  });

  it('Stop -> response-completed', () => {
    const translated = translate({
      hook_event_name: 'Stop',
      session_id: 's1',
      cwd: '/p',
    });

    expect(translated?.event.kind).toBe('response-completed');
  });

  it('PreToolUse Bash rg -> pre-search', () => {
    const translated = translate({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'Bash',
      tool_input: { command: 'rg "Foo" src' },
    });

    expect(translated?.event.kind).toBe('pre-search');
    expect(translated && 'pattern' in translated.event && translated.event.pattern).toBe('Foo');
    expect(translated && 'path' in translated.event && translated.event.path).toBe('src');
  });

  it('PreToolUse Bash rg with glob -> pre-search with glob', () => {
    const translated = translate({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'Bash',
      tool_input: { command: 'rg Foo --glob "*.cs" src' },
    });

    expect(translated?.event.kind).toBe('pre-search');
    expect(translated && 'glob' in translated.event && translated.event.glob).toBe('*.cs');
  });

  it('PreToolUse Bash cat C# file -> pre-read', () => {
    const translated = translate({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'Bash',
      tool_input: { command: 'cat src/Foo.cs' },
    });

    expect(translated?.event.kind).toBe('pre-read');
    expect(translated && 'path' in translated.event && translated.event.path).toBe('src/Foo.cs');
  });

  it('PreToolUse Bash unknown -> pre-native-tool', () => {
    const translated = translate({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'Bash',
      tool_input: { command: 'awk {print}' },
    });

    expect(translated?.event.kind).toBe('pre-native-tool');
    expect(translated && 'tool_name' in translated.event && translated.event.tool_name).toBe('Bash');
  });

  it('PostToolUse Bash rg -> post-search', () => {
    const translated = translate({
      hook_event_name: 'PostToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'Bash',
      tool_input: { command: 'rg "Foo" src' },
      tool_response: { stdout: 'Foo.cs:1' },
    });

    expect(translated?.event.kind).toBe('post-search');
    expect(translated && 'pattern' in translated.event && translated.event.pattern).toBe('Foo');
    expect(translated && 'path' in translated.event && translated.event.path).toBe('src');
    expect(translated && 'result_bytes' in translated.event && translated.event.result_bytes).toBe(8);
  });

  it('PreToolUse parlance MCP -> pre-mcp-tool', () => {
    const translated = translate({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'mcp__parlance__search-symbols',
      tool_input: {},
    });

    expect(translated?.event.kind).toBe('pre-mcp-tool');
  });

  it('PostToolUse parlance MCP -> post-mcp-tool', () => {
    const translated = translate({
      hook_event_name: 'PostToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'mcp__parlance__search-symbols',
      tool_input: {},
      tool_response: { content: 'ok' },
    });

    expect(translated?.event.kind).toBe('post-mcp-tool');
  });

  it('apply_patch with one path -> write event', () => {
    const translated = translate({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'apply_patch',
      tool_input: { command: '*** Begin Patch\n*** Update File: Foo.cs\n@@\n x\n*** End Patch' },
    });

    expect(translated?.event.kind).toBe('pre-write');
    expect(translated && 'path' in translated.event && translated.event.path).toBe('Foo.cs');
  });

  it('PostToolUse apply_patch with one path estimates written bytes', () => {
    const translated = translate({
      hook_event_name: 'PostToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'apply_patch',
      tool_input: { command: '*** Begin Patch\n*** Update File: Foo.cs\n@@\n+hello\n+world\n*** End Patch' },
    });

    expect(translated?.event.kind).toBe('post-write');
    expect(translated && 'content_bytes' in translated.event && translated.event.content_bytes).toBe(12);
  });

  it('PostToolUse apply_patch with unknown bytes falls back to native tool', () => {
    const translated = translate({
      hook_event_name: 'PostToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'apply_patch',
      tool_input: { command: '*** Begin Patch\n*** Update File: Foo.cs\n@@\n context only\n*** End Patch' },
      tool_response: { content: 'ok' },
    });

    expect(translated?.event.kind).toBe('post-native-tool');
    expect(translated && 'tool_name' in translated.event && translated.event.tool_name).toBe('apply_patch');
  });

  it('classifies common Bash commands', () => {
    expect(classifyBashCommand('rg Foo src').kind).toBe('search');
    expect(classifyBashCommand('sed -n "1,10p" Foo.cs').kind).toBe('read');
    expect(classifyBashCommand('dotnet test Parlance.sln').kind).toBe('verify');
    expect(classifyBashCommand('git status --short').kind).toBe('vcs-inspect');
    expect(classifyBashCommand('awk "{print}" file').kind).toBe('unknown');
  });
});
