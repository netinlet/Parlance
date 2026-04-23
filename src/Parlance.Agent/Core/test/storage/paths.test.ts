import { describe, expect, it } from 'vitest';
import * as paths from '../../src/storage/paths.js';

describe('paths', () => {
  it('parlanceDir', () => expect(paths.parlanceDir('/proj')).toBe('/proj/.parlance'));
  it('sessionFile', () => expect(paths.sessionFile('/proj')).toBe('/proj/.parlance/_session.json'));
  it('ledgerFile', () => expect(paths.ledgerFile('/proj')).toBe('/proj/.parlance/ledger.jsonl'));
  it('hooksDir', () => expect(paths.hooksDir('/proj')).toBe('/proj/.parlance/hooks'));
  it('kibbleDir', () => expect(paths.kibbleDir('/proj')).toBe('/proj/.parlance/kibble'));
  it('benchStateFile', () => expect(paths.benchStateFile('/proj')).toBe('/proj/.parlance/bench/_active.json'));
  it('routingFile', () => expect(paths.routingFile('/proj')).toBe('/proj/.parlance/tool-routing.md'));
});
