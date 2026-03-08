# RoslynMcp Evaluation for Parlance

Date: 2026-03-08

Repository evaluated:

- GitHub: https://github.com/chrismo80/RoslynMcp

## Scope

This note evaluates what is useful, idea-wise, from RoslynMcp for Parlance under two different product assumptions:

1. Parlance remains language-agnostic and is not C#-exclusive.
2. Parlance becomes explicitly C#-specific.

The goal is not to decide whether to copy RoslynMcp. The goal is to identify which ideas are strategically useful for Parlance's later AI tool and MCP direction.

## Verification performed

- Reviewed RoslynMcp repository metadata with `gh repo view`.
- Cloned the repository locally and reviewed the host, workspace, navigation, analysis, and refactoring code.
- Ran:

```bash
dotnet test RoslynMcp.slnx -maxcpucount:1 /nodeReuse:false
```

Result:

- Passed: `121`
- Failed: `0`
- Skipped: `0`

That means the assessment below is based on both source review and a fresh passing test run, not just a README scan.

## RoslynMcp in one sentence

RoslynMcp is a C#-specific MCP server built around a loaded Roslyn `MSBuildWorkspace`, with tools for solution loading, semantic navigation, symbol resolution, call flow, code smell discovery, and a guarded set of refactoring and formatting operations.

## Scenario 1: Parlance remains language-agnostic

### Overall fit

In this scenario, RoslynMcp is useful mostly as a host and workflow reference, not as a product model.

The main mismatch is strategic:

- RoslynMcp is unapologetically C#-specific.
- Parlance's blueprint says the future MCP layer should be language-agnostic and should wrap per-language engines behind a common tool contract.

So RoslynMcp is still valuable, but mainly for patterns around session safety, mutation safety, and deterministic tool behavior.

### Most useful ideas

#### 1. Explicit workspace bootstrap

RoslynMcp treats solution loading as a first-class operation instead of an implicit side effect. That is a good MCP idea even for a language-agnostic system.

Useful takeaway:

- make "which workspace is active?" explicit
- avoid hidden auto-discovery in later mutation or navigation calls

#### 2. Session state with workspace versioning

RoslynMcp keeps a current solution session and increments a workspace version whenever the active solution changes.

Useful takeaway:

- any future Parlance MCP mutation flow should attach requests to a specific workspace snapshot
- preview/apply should reject stale actions instead of guessing

This is one of the strongest ideas in the RoslynMcp codebase.

#### 3. Stable symbol identity

RoslynMcp uses Roslyn `SymbolKey`-style identity to move between MCP requests and Roslyn semantic symbols.

Useful takeaway:

- if Parlance later exposes language-engine-specific semantic handles, stable symbol identity is a better primitive than raw spans or file-line-column alone
- for C#, Roslyn `SymbolKey` is the right model
- for other languages, Parlance would want equivalent stable semantic identifiers

#### 4. Versioned action IDs for deferred mutation

RoslynMcp does not try to keep live `CodeAction` instances around. It discovers an action, encodes enough identity to reconstruct it later, and rehydrates it against the current workspace for preview/apply.

Useful takeaway:

- later MCP actions should be discoverable, replayable, and safe against stale state
- action IDs should be tied to workspace version and semantic identity, not just a title string

#### 5. Policy-gated mutation

RoslynMcp has a simple but useful policy layer that marks actions as allowed, blocked, or review-required.

Useful takeaway:

- AI-facing tools should not expose every available refactoring blindly
- Parlance should separate "can Roslyn do this?" from "should the agent be allowed to do this automatically?"

#### 6. Readiness and health reporting

RoslynMcp checks for stale workspaces, missing generated artifacts, and restore-related degradation, and returns explicit readiness signals.

Useful takeaway:

- agents need explicit health states
- "workspace degraded" is much better than "operation failed"
- this is especially important for a tool that may be invoked in half-restored worktrees, CI sandboxes, or detached branches

#### 7. Deterministic output contracts

RoslynMcp is deliberate about canonical ordering, deduplication, normalized paths, and stable return shapes.

Useful takeaway:

- Parlance MCP responses should be optimized for testability and agent reliability
- deterministic ordering matters more than it first appears

#### 8. Sandboxed integration tests

RoslynMcp's approach of copying a small test solution into a temp sandbox for feature tests is very good.

Useful takeaway:

- if Parlance adds an MCP server later, this is the right testing model for end-to-end tool behavior

### Useful, but adapt rather than copy

#### Semantic navigation tools

RoslynMcp includes tools such as:

- symbol resolution
- usages
- implementations
- type hierarchy
- call flow

These are useful ideas, but for a language-agnostic Parlance they should remain secondary to the quality-gate core. They are valuable host capabilities, but not the strategic center of the product.

#### Thin host over reusable internals

RoslynMcp's `Core` / `Infrastructure` / `Features` / `Host` split is directionally sound. For Parlance, the reusable engine boundary should remain even stricter:

- the per-language analysis engine stays the source of truth
- the MCP host remains transport and orchestration

### Ideas that are not a strong fit

#### Copying the full C#-specific tool surface

RoslynMcp's tool list is shaped around deep C# semantic assistance. A language-agnostic Parlance should not inherit that surface wholesale because it would make the public contract C#-shaped too early.

#### Letting the MCP layer own too much language logic

RoslynMcp loads analyzers, symbol logic, and refactoring logic directly inside the server stack. That makes sense for a C#-only tool. It is not the cleanest strategic direction for a language-agnostic Parlance.

### Bottom line for the language-agnostic case

RoslynMcp is useful as a design reference for:

- explicit session bootstrap
- workspace versioning
- stable semantic IDs
- action identity and replay
- policy-gated mutation
- readiness and health signaling
- deterministic outputs
- MCP integration testing

It is not a strong template for Parlance's eventual product shape if Parlance intends to stay broader than C#.

## Scenario 2: Parlance becomes explicitly C#-specific

### Overall fit

If Parlance becomes clearly C#-specific, RoslynMcp becomes substantially more relevant.

In that scenario, RoslynMcp is no longer just an adjacent example of "interesting MCP plumbing." It becomes a meaningful reference architecture for the later MCP phase.

The previous major mismatch disappears:

- RoslynMcp is C#-only by design
- a C#-only Parlance could directly benefit from the same assumptions about Roslyn workspaces, symbols, and refactoring semantics

### Highest-value ideas

#### 1. Persistent `MSBuildWorkspace` session

For a C#-specific AI tool, a real loaded solution is a much stronger foundation than rebuilding ad hoc compilations for each request.

Benefits:

- project-to-project references
- real document/project identity
- solution-wide symbol ownership
- correct navigation and rename behavior

#### 2. Stable symbol IDs as a first-class primitive

If Parlance stays in C#, Roslyn `SymbolKey`-style IDs should probably become a foundational concept.

That would enable:

- explain-symbol style workflows
- symbol-aware diagnostics
- usages and impact analysis
- targeted refactoring and fix application

#### 3. Symbol-centric MCP tools

In a C#-specific version of Parlance, tools like these become strategically plausible:

- resolve symbol
- find usages
- find implementations
- get type hierarchy
- trace call flow

They would complement quality diagnostics rather than distract from them.

#### 4. Action discovery -> preview -> apply

RoslynMcp's refactoring pipeline is one of its strongest ideas:

1. discover actions at a specific position
2. assign a stable action identity
3. preview against the current solution
4. apply only if the workspace snapshot is still valid

That flow is very well suited to agent tooling.

#### 5. Policy-gated safe mutation

If Parlance becomes C#-specific, this is likely essential rather than optional.

Useful principle:

- not every Roslyn refactoring or code fix should be agent-applied
- the product should define a curated, trusted mutation subset

That matches Parlance's broader philosophy of curated analysis rather than raw exhaustiveness.

#### 6. Cleanup and formatting as explicit operations

RoslynMcp's cleanup and format flows show a useful middle ground between:

- "just expose every provider action"
- "never let the agent mutate anything"

For a C#-specific Parlance, named operations like:

- cleanup
- format
- safe fix batch

could be much more valuable than a giant open-ended refactoring catalog.

#### 7. Workspace health and stale-snapshot handling

This becomes even more important in the C#-specific case because more operations would depend on a live workspace.

RoslynMcp's readiness and stale workspace handling is worth reusing conceptually.

#### 8. End-to-end feature tests over a real solution

If Parlance eventually exposes semantic navigation or safe mutation for C#, RoslynMcp's sandboxed solution tests are the correct testing pattern to emulate.

### What becomes realistically in scope

If Parlance is C#-specific, RoslynMcp makes the following look reasonable for later phases:

- semantic navigation around diagnostics
- explain-symbol and impact-analysis tools
- solution-aware fix and refactoring previews
- controlled rename / cleanup / formatting flows
- solution-scoped code-quality workflows instead of only file-scoped CLI analysis

### What still should not be copied blindly

#### The current product shape

Even in the C#-specific case, Parlance should not simply become "RoslynMcp plus custom analyzers."

Parlance's differentiator would still be:

- curation of rules
- trusted diagnostics and fix guidance
- quality-gate behavior

RoslynMcp is more of a semantic workspace assistant than a rules-curation product.

#### Large orchestration classes

RoslynMcp's mutation and navigation logic works, but several orchestrator files are already large. If Parlance borrows the ideas, it should not borrow the same file-size and orchestration sprawl.

#### Host-owned analysis truth

Even for a C#-only Parlance, the cleaner model is still:

- analyzer engine stays the source of truth
- MCP host wraps that engine and adds session/navigation/mutation behavior around it

### Bottom line for the C#-specific case

If Parlance becomes explicitly C#-specific, RoslynMcp becomes much more relevant.

The most valuable ideas to borrow would be:

- persistent solution session
- `MSBuildWorkspace` as the semantic host
- stable symbol IDs
- versioned action IDs
- preview/apply over a specific workspace snapshot
- policy-gated mutation
- readiness and stale-workspace reporting
- sandboxed end-to-end solution tests

In this scenario, RoslynMcp stops being just a useful inspiration source and becomes a fairly serious reference design for the MCP-era host layer.

## Final recommendation

### If Parlance stays language-agnostic

Borrow the safety and workflow ideas, but do not copy the product shape.

Priority ideas:

- session bootstrap
- workspace versioning
- semantic identity
- action identity
- mutation policy
- readiness states
- deterministic contracts

### If Parlance becomes C#-specific

Borrow both the safety ideas and much more of the host architecture.

Priority ideas:

- real solution session management
- symbol-centric workflows
- solution-aware navigation
- safe mutation preview/apply
- explicit cleanup and formatting operations

## Short conclusion

RoslynMcp is useful in both scenarios, but in different ways.

- For a language-agnostic Parlance, it is primarily a source of host and safety patterns.
- For a C#-specific Parlance, it is a meaningful reference architecture for the eventual MCP/server layer.

The strongest reusable ideas across both cases are:

- explicit workspace/session lifecycle
- stable semantic identities
- versioned deferred actions
- policy-gated mutation
- explicit readiness and health reporting
- deterministic, integration-tested MCP behavior
