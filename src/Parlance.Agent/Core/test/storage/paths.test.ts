import { homedir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import * as paths from '../../src/storage/paths.js';

const original = process.env.PARLANCE_HOME;

afterEach(() => {
  if (original === undefined) delete process.env.PARLANCE_HOME;
  else process.env.PARLANCE_HOME = original;
});

describe('paths — project-local install + ephemeral state', () => {
  it('parlanceDir', () =>
    expect(paths.parlanceDir('/proj')).toBe('/proj/.parlance'));
  it('sessionFile', () =>
    expect(paths.sessionFile('/proj')).toBe('/proj/.parlance/_session.json'));
  it('hooksDir', () =>
    expect(paths.hooksDir('/proj')).toBe('/proj/.parlance/hooks'));
  it('routingFile', () =>
    expect(paths.routingFile('/proj')).toBe('/proj/.parlance/tool-routing.md'));
  it('benchStateFile stays local', () =>
    expect(paths.benchStateFile('/proj')).toBe(
      '/proj/.parlance/bench/_active.json',
    ));
});

describe('paths — centralized telemetry', () => {
  it('parlanceHome honors PARLANCE_HOME and telemetry nests under it', () => {
    process.env.PARLANCE_HOME = '/central/store';
    expect(paths.parlanceHome()).toBe('/central/store');
    expect(paths.telemetryDir()).toBe('/central/store/telemetry');
    expect(paths.ledgerFile()).toBe('/central/store/telemetry/ledger.jsonl');
    expect(paths.sessionLogFile()).toBe(
      '/central/store/telemetry/session-log.md',
    );
    expect(paths.benchResultsFile()).toBe(
      '/central/store/telemetry/bench/results.jsonl',
    );
  });

  it('parlanceHome defaults to ~/.parlance', () => {
    delete process.env.PARLANCE_HOME;
    expect(paths.parlanceHome()).toBe(join(homedir(), '.parlance'));
    expect(paths.telemetryDir()).toBe(
      join(homedir(), '.parlance', 'telemetry'),
    );
  });
});
