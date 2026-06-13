# TypeScript review & hygiene toolchain (#149)

**Status:** Design approved — ready for implementation plan
**Date:** 2026-06-13
**Issue:** #149 — Add TypeScript review and hygiene toolchain for agent packages

## Problem

The `src/Parlance.Agent/*` TypeScript packages (`Core`, `Adapter.Claude`,
`Adapter.Codex`) have a solid baseline — `strict: true`, `tsc --noEmit`,
Vitest, esbuild bundling, Node 20 ESM, committed `dist/` bundles, and Makefile
CI targets. The missing layer is TypeScript-specific *review* tooling: linting,
formatting/import hygiene, and dead-code detection. They also carry a spread of
`JSON.parse(...) as SomeType` boundary casts that hide runtime shape errors from
the compiler.

## Decisions (locked)

| Decision | Choice |
|---|---|
| Config sharing | **Duplicated per package** — no workspace root, no shared base. Matches current package independence. |
| PR scope | **Tooling + fix violations only.** Compiler-option tightening and zod boundary validation are deferred to separate follow-up issues. |
| Knip in CI | **Blocking** — `agent-deadcode` participates in `agent-ci`. |
| `no-unsafe-*` rules | **Warn-level, non-blocking** in this PR (see below). |

## Current-state facts (verified)

- Three independent npm packages, each with its own `package-lock.json`,
  installed via `npm ci`. No root `package.json`.
- `tsconfig.json` includes only `src/**/*.ts`. Test files under `test/**` are
  **not** in the TS project; they run via Vitest's own resolution.
- Entry points are `src/cli.ts` and `src/api.ts` (matches `scripts/bundle.mjs`).
- `dist/` bundles are **committed** (verified by `agent-dist-check`), not
  gitignored. `out-ts/`, `out-dts/`, `*.tsbuildinfo`, `node_modules/` are
  gitignored.
- CI (`.github/workflows/ci.yml`) runs `make agent-ci`
  (= `agent-typecheck` → `agent-build` → `agent-test`) then `agent-dist-check`.
- No existing ESLint / Biome / Knip / Prettier config.
- Source style: 2-space indent, single quotes, ESM.

## Design

### Per-package files (×3)

**`eslint.config.mjs`** — flat config.

- `@eslint/js` recommended + `typescript-eslint` `recommendedTypeChecked`.
- `languageOptions.parserOptions = { projectService: true, tsconfigRootDir: import.meta.dirname }`.
- **Error-level** (fixed in this PR):
  - `@typescript-eslint/consistent-type-imports`
  - `@typescript-eslint/no-floating-promises`
  - `@typescript-eslint/no-misused-promises`
  - `@typescript-eslint/switch-exhaustiveness-check`
  - `no-param-reassign`
  - `@typescript-eslint/prefer-readonly`
- **Warn-level** (visible, non-blocking):
  - the `no-unsafe-*` family (assignment/member-access/call/argument/return)
  - `@typescript-eslint/restrict-template-expressions`
- Ignores: `dist/`, `out-ts/`, `out-dts/`, `node_modules/`.

**Why `no-unsafe-*` are warnings.** `recommendedTypeChecked` flags every
`JSON.parse(...) as T` boundary cast. Those cannot be cleanly resolved without
runtime validation (zod), which is explicitly deferred. Keeping them at `warn`
surfaces them now without blocking; the zod follow-up promotes them to `error`.
`make agent-lint` runs `eslint .` **without** `--max-warnings 0`, so the gate
fails on errors only.

**`tsconfig.eslint.json`** — extends `tsconfig.json`, adds `test/**` and root
config files to `include` so type-aware linting covers tests. This keeps the
`tsc --noEmit` typecheck scope unchanged while giving ESLint's project service a
tsconfig that owns the test files (avoids `allowDefaultProject` churn).

**`biome.json`** — **formatter + import organization only; linter disabled**
(ESLint owns linting, so no rule overlap). Configured to match existing style
(2-space, single quotes). `format` writes; `format:check` uses `biome ci`.
Ignores `dist/`, `out-*`, `node_modules/`.

**`knip.json`** — `entry: ["src/cli.ts", "src/api.ts"]`, project `src/**/*.ts`,
Vitest plugin so `test/**` are recognized as entry points. Tuned so the
`file:` `@parlance/agent-core` dependency and the `bin` entries are not reported
as false positives. Ignores `dist`, `out-*`.

**`package.json`** — add scripts and devDependencies.

```json
{
  "lint": "eslint .",
  "format": "biome format --write .",
  "format:check": "biome ci .",
  "deadcode": "knip",
  "review": "npm run typecheck && npm run lint && npm test && npm run format:check"
}
```

- devDeps: `eslint`, `@eslint/js`, `typescript-eslint` (caret), and
  `@biomejs/biome` + `knip` **pinned exact** (acceptance criteria: Biome pinned;
  Knip configured for the layout).
- `@types/node` (`^22`) and `typescript` (`^5.6`) are **left untouched** per the
  issue's explicit warning.
- Target versions (npm-checked 2026-06-13): `eslint 10.5.0`,
  `@eslint/js 10.0.1`, `typescript-eslint 8.61.0`, `@biomejs/biome 2.5.0`,
  `knip 6.16.1`. Verify peer compatibility at install; surface any conflict.

### Makefile

Per-package Makefiles add: `lint`, `format-check`, `deadcode`, `review`
targets (each delegating to the matching npm script).

Root Makefile adds loop-over-all targets and tightens `agent-ci`:

```
agent-lint:          # core + adapters: npm run lint
agent-format-check:  # core + adapters: npm run format:check
agent-deadcode:      # core + adapters: npm run deadcode
agent-review:        # core + adapters: npm run review

agent-ci: agent-typecheck agent-lint agent-format-check agent-deadcode agent-build agent-test
```

**No `ci.yml` change** — the `Validate agent workspaces` step already runs
`make agent-ci`; it simply becomes stricter.

### Lockfiles

Adding devDependencies regenerates all three `package-lock.json` files (CI uses
`npm ci`, which fails on drift). Refresh via `npm install` in each package and
commit the updated lockfiles.

### Fix violations

After enabling the tools:

1. `biome format --write` across all three packages (formatting/import order).
2. Fix error-level ESLint findings: add `import type`, mark fields `readonly`,
   resolve floating/misused promises, add exhaustive `switch` handling, remove
   `param` reassignment. **Behavior must remain unchanged.**
3. Resolve any Knip findings (genuine dead code removed; legitimate
   entry/dep edges added to `knip.json`).
4. `no-unsafe-*` / `restrict-template-expressions` warnings are left in place
   (tracked by the zod follow-up).

## Follow-up issues (to open)

1. **Tighten TS compiler options** — trial/enable `noUncheckedIndexedAccess`,
   `exactOptionalPropertyTypes`, `noImplicitReturns`,
   `noFallthroughCasesInSwitch`, `noPropertyAccessFromIndexSignature`,
   `noUncheckedSideEffectImports`, `verbatimModuleSyntax`, landing incrementally.
2. **zod boundary validation** — parse `unknown` at the edges (hook envelopes,
   config files, transcript rows, MCP/settings JSON, persisted session state),
   validate once, keep the interior strongly typed; then promote `no-unsafe-*`
   from `warn` to `error`.

## Acceptance criteria (from #149) — mapping

- [x] Shared **or consistently duplicated** ESLint flat config → duplicated.
- [x] `make agent-ci` runs typecheck, build, tests, lint, **and format checks**.
- [x] Biome configured and **pinned**.
- [x] Knip configured for the agent package layout and generated outputs.
- [x] Existing generated `dist` behavior unchanged (`agent-dist-check` still green).
- [x] New strict compiler options documented as **staged follow-up** (issue #1).
- [x] Unsafe JSON/input casts **tracked in a separate follow-up issue** (issue #2).

## Out of scope

- Any change to `dist/` bundling or the esbuild scripts.
- `@types/node` / `typescript` version bumps.
- `oxlint` (only if ESLint performance becomes a problem later).
- npm workspace restructuring.
