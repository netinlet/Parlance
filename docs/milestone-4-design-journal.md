# Milestone 4 Design Journal

Running notes from the M4 brainstorming and design session.

## 2026-03-26 — Brainstorming Session

### MCP Tool Observations

Used Parlance's own MCP tools throughout to explore the codebase while designing M4. Notes on what I found:

**`workspace-status`** — 16 projects loaded, Degraded status (one warning about Analyzers project reference). Confirmed the full project graph including `Parlance.Analyzers.Upstream` multi-targeting net8.0/net10.0.

**`describe-type Diagnostic`** — Confirmed the existing Parlance diagnostic model: `RuleId`, `Category`, `Severity`, `Message`, `Location`, `Rationale?`, `SuggestedFix?`. The `Rationale` and `SuggestedFix` fields already exist but are only populated for PARL rules via `RuleMetadataProvider`. Curation sets will populate these for upstream rules too.

**`describe-type AnalysisSummary`** — `TotalDiagnostics`, `Errors`, `Warnings`, `Suggestions`, `ByCategory` (ImmutableDictionary), `IdiomaticScore`. This model works for M4 as-is.

**`describe-type WorkspaceSessionHolder`** — Confirmed the DI pattern: `Session`, `IsLoaded`, `LoadFailure`. Same pattern the analyze tool will use.

**`outline-file AnalyzerLoader.cs`** — `LoadAll(string targetFramework)` is the entry point. Returns `ImmutableArray<DiagnosticAnalyzer>`. Currently discovers PARL via reflection + loads upstream from `analyzer-dlls/`. This becomes the single loading path.

**`outline-file DiagnosticEnricher.cs`** — Single extension method `ToParlanceDiagnostics()`. Internal to `Parlance.CSharp`. Will need to evolve to accept curation context (fix classification, rationale from curation set).

**`outline-file CSharpAnalysisEngine.cs`** — Hardcodes all 8 PARL analyzers in a static array. This is one of the two loading paths that gets unified.

**`outline-file IdiomaticScoreCalculator.cs`** — `Calculate(ImmutableList<Diagnostic>)` returns `AnalysisSummary`. Internal to `Parlance.CSharp`. Reusable as-is.

### Key Design Decisions Made

1. **PARL rules are stubs** — User confirmed they're clones of standard MS analyzers, placed to "hold down the air." Trim to 1, keep simplest.

2. **Curation sets are data-driven** — JSON today, database tomorrow. User's reasoning: "One day we may query a database for custom rules — need the flexibility to have multiple inputs."

3. **Rationales have their own identity** — User caught that the same rationale applies across multiple rules. `rationaleId` lets you update the message once. "I could see a case where we had the same message across multiple rules and we wanted to update them all at the same time."

4. **Rule IDs are stable enough** — I overcomplicated this by proposing a separate Parlance key decoupled from upstream rule IDs. User pushed back: "That is not what I was saying... I don't think that is a truly realistic scenario." Upstream rule IDs are the identity. Simple.

5. **File-scoped analysis only** — Arrived at through progressive elimination:
   - Started with file/project/solution scope
   - Research showed every AI agent operates file-centric (edit → check → fix)
   - Solution scope is just "all projects" = bigger firehose
   - Project scope is just "all files in a group" = smaller firehose
   - User: "what's the difference between a project & sln at the end of the day — an array of projects or is it just a bigger list?"
   - Collapsed to: files in, diagnostics out. Claude decides which files.

6. **`ai-agent` curation set deferred** — Ship infrastructure + default set. Build named sets from empirical dogfooding observation, not guesses.

7. **New `Parlance.Analysis` project** — User asked "does it deserve its own project?" Answer: yes, because putting it in Workspace would force Workspace to depend on Analyzers.Upstream and Parlance.CSharp, muddying its focused responsibility.

### Research: Analysis Scoping for AI Agents

Conducted research on how AI agents consume diagnostics. Key findings:

- **Every major AI coding agent (Claude Code, Cursor, Copilot) operates file-centric, reactive.** Edit → check that file → fix → move on. None proactively request solution-wide diagnostics.
- **No existing Roslyn MCP server offers configurable scoping.** They either do single-file validation or skip diagnostics entirely.
- **SonarQube's "New Code" concept** — scope to what changed since a reference point — is the answer to the firehose at scale. But for an AI agent, the agent already knows what it changed.
- **Token costs are real.** pgEdge MCP server found `SELECT *` consumed tens of thousands of tokens. MCP tool definitions alone can burn 55K+ tokens. Compact, file-scoped results avoid the problem entirely.
- **Progressive disclosure pattern** — summary first, drill down on demand — is the proven approach for large result sets.

### Existing Profile System

Checked for existing profile/curation JSON: `Glob("src/Parlance.Analyzers.Upstream/profiles/**/*.json")` returned no files. `ProfileProvider.cs` exists with `GetProfileContent()` and `GetAvailableProfiles()` but the profiles directory is empty. The existing profile infrastructure is a shell — M4's curation sets replace it cleanly.

### Accessibility Concerns

Several internal types that `Parlance.Analysis` needs:
- `DiagnosticEnricher` — internal to `Parlance.CSharp`
- `IdiomaticScoreCalculator` — internal to `Parlance.CSharp`
- `AnalyzerLoader` — internal to `Parlance.Analyzers.Upstream`

Options: `InternalsVisibleTo`, make them public, or extract to Abstractions. Decision deferred to implementation plan — the spec describes the pipeline, implementation decides the access strategy.
