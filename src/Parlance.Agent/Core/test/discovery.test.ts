import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { findSolution, looksLikeCsharp, parlanceAgentInstalled, parlanceCodexWired, parlanceMcpWired, planSessionStart } from '../src/discovery.js';

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'core-discovery-'));
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

const wireMcp = () => writeFileSync(join(root, '.mcp.json'), JSON.stringify({ mcpServers: { parlance: { command: 'parlance' } } }));
const copyCodexBundle = () => {
  mkdirSync(join(root, '.parlance', 'hooks'), { recursive: true });
  writeFileSync(join(root, '.parlance', 'hooks', 'session-start.js'), '');
};
const wireCodexHooks = () => {
  mkdirSync(join(root, '.codex'), { recursive: true });
  writeFileSync(join(root, '.codex/hooks.json'), JSON.stringify({
    hooks: {
      SessionStart: [{
        hooks: [{ type: 'command', command: 'node "$(git rev-parse --show-toplevel)/.parlance/hooks/session-start.js"' }],
      }],
    },
  }));
  writeFileSync(join(root, '.codex/config.toml'), '[features]\nhooks = true\n');
  copyCodexBundle();
};

describe('looksLikeCsharp', () => {
  it('true for a solution at root', () => {
    writeFileSync(join(root, 'App.slnx'), '');
    expect(looksLikeCsharp(root)).toBe(true);
  });

  it('false for global.json alone (Volta uses this file too)', () => {
    writeFileSync(join(root, 'global.json'), '{}');
    expect(looksLikeCsharp(root)).toBe(false);
  });

  it('true for a .csproj under src/', () => {
    mkdirSync(join(root, 'src'), { recursive: true });
    writeFileSync(join(root, 'src/Thing.csproj'), '');
    expect(looksLikeCsharp(root)).toBe(true);
  });

  it('false for a non-C# project', () => {
    writeFileSync(join(root, 'package.json'), '{}');
    expect(looksLikeCsharp(root)).toBe(false);
  });
});

describe('findSolution', () => {
  it('prefers .slnx over .sln', () => {
    writeFileSync(join(root, 'A.sln'), '');
    writeFileSync(join(root, 'B.slnx'), '');
    expect(findSolution(root)).toBe('B.slnx');
  });

  it('null when none', () => {
    expect(findSolution(root)).toBeNull();
  });
});

describe('parlanceMcpWired', () => {
  it('true when .mcp.json has a parlance server', () => {
    wireMcp();
    expect(parlanceMcpWired(root)).toBe(true);
  });

  it('false when .mcp.json lacks parlance', () => {
    writeFileSync(join(root, '.mcp.json'), JSON.stringify({ mcpServers: { other: {} } }));
    expect(parlanceMcpWired(root)).toBe(false);
  });

  it('false when no .mcp.json', () => {
    expect(parlanceMcpWired(root)).toBe(false);
  });
});

describe('parlanceCodexWired', () => {
  it('true when Codex hook config points at the Parlance hook bundle', () => {
    wireCodexHooks();
    expect(parlanceCodexWired(root)).toBe(true);
  });

  it('false when only the hook bundle is present', () => {
    copyCodexBundle();
    expect(parlanceCodexWired(root)).toBe(false);
  });

  it('false when only .mcp.json is present (Claude path, not Codex)', () => {
    wireMcp();
    expect(parlanceCodexWired(root)).toBe(false);
  });

  it('false when nothing is installed', () => {
    expect(parlanceCodexWired(root)).toBe(false);
  });
});

describe('parlanceAgentInstalled', () => {
  it('true when .mcp.json has a parlance server', () => {
    wireMcp();
    expect(parlanceAgentInstalled(root)).toBe(true);
  });

  it('true when hook bundle is present (Codex install path)', () => {
    copyCodexBundle();
    expect(parlanceAgentInstalled(root)).toBe(true);
  });

  it('false when neither .mcp.json nor hooks are present', () => {
    expect(parlanceAgentInstalled(root)).toBe(false);
  });
});

describe('planSessionStart', () => {
  it('wired (Claude) -> routing context', () => {
    wireMcp();
    const plan = planSessionStart(root);
    expect(plan.kind).toBe('wired');
    expect(plan.kind === 'wired' && plan.context).toContain('mcp__parlance__describe-type');
  });

  it('wired (Codex hook bundle) -> routing context', () => {
    copyCodexBundle();
    const plan = planSessionStart(root);
    expect(plan.kind).toBe('wired');
  });

  it('C# but unwired -> install suggestion naming the solution', () => {
    writeFileSync(join(root, 'Widgets.slnx'), '');
    const plan = planSessionStart(root);
    expect(plan.kind).toBe('suggest-install');
    expect(plan.kind === 'suggest-install' && plan.context).toContain('parlance agent install --solution Widgets.slnx');
  });

  it('neither C# nor wired -> idle', () => {
    writeFileSync(join(root, 'package.json'), '{}');
    expect(planSessionStart(root).kind).toBe('idle');
  });

  it('custom wiredFn: Claude .mcp.json does not suppress suggest-install when Codex hooks absent', () => {
    wireMcp();
    writeFileSync(join(root, 'App.slnx'), '');
    const plan = planSessionStart(root, parlanceCodexWired);
    expect(plan.kind).toBe('suggest-install');
  });

  it('custom wiredFn: stale copied Codex bundle does not suppress suggest-install when hooks.json is absent', () => {
    copyCodexBundle();
    writeFileSync(join(root, 'App.slnx'), '');
    const plan = planSessionStart(root, parlanceCodexWired);
    expect(plan.kind).toBe('suggest-install');
  });
});
