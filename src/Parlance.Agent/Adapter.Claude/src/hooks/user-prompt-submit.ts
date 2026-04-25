import { appendFileSync, existsSync, mkdirSync, unlinkSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { benchResultsFile, benchStateFile } from '@parlance/agent-core/storage/paths.js';
import { readSessionState, writeSessionState } from '@parlance/agent-core/storage/session-state.js';
import { aggregateUsageBetween, parseTranscript } from '../transcript.js';
import { translate } from '../translate.js';
import { readStdin } from './_shared.js';

async function main(): Promise<void> {
  try {
    const raw = await readStdin();
    const env = JSON.parse(raw);
    const translated = translate(env);
    if (!translated || translated.event.kind !== 'task-received') return;

    const prompt = translated.event.prompt.trim();
    if (!prompt.startsWith('/parlance')) return;

    const parts = prompt.split(/\s+/);
    if (parts[1] !== 'bench') return;

    const action = parts[2];
    if (action === 'start' && parts[3] && parts[4]) {
      startBench(translated.context.project_root, parts[3], parts[4]);
    } else if (action === 'end') {
      endBench(translated.context.project_root, translated.transcript_path ?? null);
    } else {
      return;
    }

    process.stderr.write(`parlance bench ${action} acknowledged\n`);
  } catch {
    // never block the host
  }
}

function startBench(root: string, taskId: string, variant: string): void {
  const marker = {
    task_id: taskId,
    variant,
    started_at: new Date().toISOString(),
  };

  const markerPath = benchStateFile(root);
  mkdirSync(dirname(markerPath), { recursive: true });
  writeFileSync(markerPath, JSON.stringify(marker));

  const state = readSessionState(root);
  if (!state) return;

  writeSessionState(root, { ...state, active_bench: marker });
}

function endBench(root: string, transcriptPath: string | null): void {
  const state = readSessionState(root);
  if (!state?.active_bench) return;

  const endedAt = new Date().toISOString();
  let usage = {
    input_tokens: 0,
    output_tokens: 0,
    cache_read_tokens: 0,
    cache_write_tokens: 0,
  };

  const path = transcriptPath ?? state.transcript_ref;
  if (path) {
    const parsed = parseTranscript(path);
    if (parsed) {
      usage = aggregateUsageBetween(parsed.records, state.active_bench.started_at, endedAt);
    }
  }

  const record = {
    task_id: state.active_bench.task_id,
    variant: state.active_bench.variant,
    started_at: state.active_bench.started_at,
    ended_at: endedAt,
    session_id: state.session_id,
    adapter: state.adapter,
    usage,
  };

  const resultsPath = benchResultsFile(root);
  mkdirSync(dirname(resultsPath), { recursive: true });
  appendFileSync(resultsPath, `${JSON.stringify(record)}\n`);

  const markerPath = benchStateFile(root);
  if (existsSync(markerPath)) unlinkSync(markerPath);

  writeSessionState(root, { ...state, active_bench: null });
}

void main().then(() => process.exit(0));
