import { describe, expect, it } from 'vitest';
import { translate } from '../src/translate.js';

describe('translate', () => {
  it('PreToolUse Read .cs -> pre-read', () => {
    const translated = translate({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'Read',
      tool_input: { file_path: 'Foo.cs' },
    });

    expect(translated?.event.kind).toBe('pre-read');
  });

  it('PreToolUse Grep type=cs -> pre-search', () => {
    const translated = translate({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'Grep',
      tool_input: { pattern: 'Foo', type: 'cs' },
    });

    expect(translated?.event.kind).toBe('pre-search');
    expect(translated && 'file_type' in translated.event && translated.event.file_type).toBe('cs');
  });

  it('PreToolUse mcp__parlance__X -> pre-mcp-tool', () => {
    const translated = translate({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'mcp__parlance__search-symbols',
      tool_input: {},
    });

    expect(translated?.event.kind).toBe('pre-mcp-tool');
  });

  it('PreToolUse Bash -> pre-native-tool', () => {
    const translated = translate({
      hook_event_name: 'PreToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'Bash',
      tool_input: { command: 'ls' },
    });

    expect(translated?.event.kind).toBe('pre-native-tool');
  });

  it('PostToolUse Read with content has content_bytes', () => {
    const translated = translate({
      hook_event_name: 'PostToolUse',
      session_id: 's1',
      cwd: '/p',
      tool_name: 'Read',
      tool_input: { file_path: 'a.cs' },
      tool_response: { content: 'hello' },
    });

    expect(translated?.event.kind).toBe('post-read');
    expect(translated && 'content_bytes' in translated.event && translated.event.content_bytes).toBe(5);
  });

  it('Stop -> response-completed', () => {
    const translated = translate({
      hook_event_name: 'Stop',
      session_id: 's1',
      cwd: '/p',
      transcript_path: '/t.jsonl',
    });

    expect(translated?.event.kind).toBe('response-completed');
    expect(translated?.context.project_root).toBe('/p');
  });

  it('UserPromptSubmit -> task-received', () => {
    const translated = translate({
      hook_event_name: 'UserPromptSubmit',
      session_id: 's1',
      cwd: '/p',
      prompt: 'hi',
    });

    expect(translated?.event.kind).toBe('task-received');
    expect(translated && 'prompt' in translated.event && translated.event.prompt).toBe('hi');
  });
});
