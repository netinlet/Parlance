import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { findSolution, looksLikeCsharp, parlanceAgentInstalled, parlanceMcpWired, planSessionStart } from '../src/discovery.js';

let root: string;

beforeEach(() => {
  root = mkdtempSync(join(tmpdir(), 'core-discovery-'));
});

afterEach(() => {
  rmSync(root, { recursive: true, force: true });
});

const wireMcp = () => writeFileSync(join(root, '.mcp.json'), JSON.stringify({ mcpServers: { parlance: { command: 'parlance' } } }));

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

describe('parlanceAgentInstalled', () => {
  it('true when .mcp.json has a parlance server', () => {
    wireMcp();
    expect(parlanceAgentInstalled(root)).toBe(true);
  });

  it('true when hook bundle is present (Codex install path)', () => {
    mkdirSync(join(root, '.parlance', 'hooks'), { recursive: true });
    writeFileSync(join(root, '.parlance', 'hooks', 'session-start.js'), '');
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
    mkdirSync(join(root, '.parlance', 'hooks'), { recursive: true });
    writeFileSync(join(root, '.parlance', 'hooks', 'session-start.js'), '');
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
});
