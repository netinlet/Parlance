import { mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import type { FeedbackRecord } from '../types.js';
import { kibbleDir } from './paths.js';

export function appendFeedbackRecord(root: string, record: FeedbackRecord & { session_context?: string }): string {
  const dayDir = join(kibbleDir(root), record.date);
  mkdirSync(dayDir, { recursive: true });

  const existing = readdirSync(dayDir).filter((file) => file.endsWith('.md'));
  for (const file of existing) {
    const body = readFileSync(join(dayDir, file), 'utf8');
    if (body.includes(`**Native tool:** ${record.native_tool}`) && body.includes(`**Intent:** ${record.intent}`)) {
      return join(dayDir, file);
    }
  }

  const seq = String(existing.length + 1).padStart(3, '0');
  const slug = slugify(`${record.native_tool}-${record.intent}`).slice(0, 40) || 'entry';
  const path = join(dayDir, `${seq}-${slug}.md`);

  const body = [
    `# ${record.native_tool} fallback: ${record.intent}`,
    '',
    `**Date:** ${record.date}`,
    `**Adapter:** ${record.adapter}`,
    `**Session:** ${record.session_id}`,
    `**Native tool:** ${record.native_tool}`,
    `**Intent:** ${record.intent}`,
    '',
    '## Why Parlance did not cover it',
    record.why,
    '',
    '## Suggested',
    record.suggested || 'Needs investigation',
    '',
    '## Session context',
    record.session_context ?? '(none)',
    '',
  ].join('\n');

  writeFileSync(path, body);
  return path;
}

function slugify(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}
