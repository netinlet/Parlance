# PARL3002 Cyclomatic Complexity Contract

## Decision

`PARL3002` is deferred as a visible analyzer. This work delivers an internal
cyclomatic complexity metric only. A user-facing PARL rule should be added
later only if curation shows Parlance needs behavior that is not covered by
platform rule `CA1502`.

## Purpose

The metric measures McCabe cyclomatic complexity for method-like bodies. It is
intended to support future quality gates, research, and `PARL3101`
test-adequacy work without adding duplicate diagnostics by default.

## External Sources Reviewed

- `artifacts/resharper-cyclomatic-complexity`, Apache-2.0 license, JetBrains.
  Reviewed `ComplexityAnalysisElementProblemAnalyzer.cs`,
  `CyclomaticComplexityAnalysisSettings.cs`, and the C# fixture layout.

The implementation is clean-room Roslyn code. ReSharper's PSI/CFG workarounds
are behavior references only and are not copied. Upstream fixtures are ported
as behavior references — their expected scores are mirrored where the Roslyn
metric agrees, and documented case-by-case where it does not. The port matrix
in `docs/research/2026-04-16-parl3002-fixture-port-matrix.md` tracks each
fixture's status.

## Relationship To CA1502

`CA1502` already reports excessive cyclomatic complexity, operating at IL
level. Parlance does not ship an enabled duplicate rule without a documented
distinction such as a different metric definition, curation workflow,
AI-review quality gate, or test-adequacy integration. Until then, no
`PARL3002` diagnostic is implemented.

## Metric Definition

McCabe's π + 1 formulation, computed over Roslyn's lowered
`ControlFlowGraph`:

```text
complexity = (number of basic blocks with a non-null ConditionalSuccessor) + 1
```

This is McCabe's predicate-counting definition. For structured code it is
equivalent to `E − N + 2P`, but it is immune to Roslyn's empty-branch
collapsing. Roslyn's lowered CFG merges the true and false successors of
empty `if` bodies into the same block, which makes `E − N + 2` collapse to 1
on the ReSharper `ManySequentialIfs` fixture (83 empty ifs, gold score 84).
Predicate-counting is unaffected because every conditional branch still
materializes as a basic block with a non-null `ConditionalSuccessor` even
when its two targets happen to be the same block.

The spike is recorded in `docs/research/2026-04-16-parl3002-analysis.md`
§ "Post-Spike Decision Log". All reviewed upstream fixtures match gold scores
under predicate-counting.

## Single-Path Calculation — No Syntactic Fallback

`SemanticModel` is required. When Roslyn cannot produce an `IOperation` root
for a declaration, or `ControlFlowGraph.Create` declines, the metric returns
a `Skipped` result with a reason. There is no syntax-based fallback
algorithm. Rationale:

- A syntactic walker and CFG predicate-counting produce different numbers
  for the same source in at least one documented shape. A `switch`
  expression with a discard arm scores 3 via naive syntax counting and 4
  via CFG, because the CFG models the implicit "no match throws
  `SwitchExpressionException`" branch that the discard syntactically
  swallows. The CFG answer matches CA1502's IL-level view.
- A threshold-based analyzer would cross or not cross the threshold on
  identical code depending on which algorithm happened to run, which is
  exactly the path-dependent metric bug the spike was intended to
  eliminate.
- An honest skip is better than a plausible-looking wrong answer.

## Result Shape

```csharp
public sealed record CyclomaticComplexityResult(
    int Complexity,
    int EdgeCount,
    int NodeCount,
    string? SkippedReason = null);
```

- `Complexity` is π + 1 when `SkippedReason` is null; otherwise 0 and not
  meaningful.
- `EdgeCount` is the real count of outgoing successor edges across all
  basic blocks in the CFG (conditional + fall-through).
- `NodeCount` is the real count of basic blocks in the CFG, including
  entry/exit blocks.
- Consumers must check `SkippedReason is null` before using `Complexity`.

## Supported Declarations

Calculate the metric for syntax bodies and expression bodies:

- Methods (block and expression-bodied).
- Constructors (with or without `: base(...)` / `: this(...)` initializers).
- Destructors.
- Operators and conversion operators (block and expression-bodied).
- Property and indexer accessors (`get`, `set`, `init`).
- Expression-bodied properties and indexers.
- Local functions, block and expression-bodied, including nested ones.

Lambdas are not measured independently; their body is measured as part of
the enclosing CFG via Roslyn's nested graph traversal when the caller
scores the enclosing declaration. (The cognitive metric treats lambdas as
a nesting boundary, but cyclomatic complexity folds them into the
containing method's predicate count.)

## Counting Rules (What CFG Predicate-Counting Captures)

The metric does not enumerate source constructs; it trusts Roslyn's lowered
CFG. These are the source shapes that produce predicate blocks in the CFG:

- `if` statements (including `else if`; `else` adds none).
- Loop conditions: `for`, `foreach`, `while`, `do`.
- Short-circuit operators: `&&`, `||`.
- Ternary conditional: `? :`.
- `catch` clauses (entry and `when (...)` filter are each their own
  predicate block).
- Pattern `when` guards in switch cases and switch-expression arms.
- Non-default `switch` statement case labels.
- `switch` expression arms (the implicit no-match branch is also a
  predicate even when a discard arm is present).

Simple statements, variable declarations, field access, unconditional
expressions, and `else` branches add no predicates — the `if` already
contributed its one predicate.

## Skipped Cases

- Declarations with no body (abstract methods, interface members passed in
  whole).
- Declarations where `SemanticModel.GetOperation` returns null and no
  fallback operation lookup succeeds.
- Operation shapes that are not valid CFG roots.
- `ControlFlowGraph.Create` throwing `ArgumentException` on an unexpected
  operation shape.
- Local functions whose symbol cannot be resolved or whose nested CFG
  cannot be located in the enclosing graph.

In every skip case, `SkippedReason` names the specific failure.

## Fixture Port Requirements

Every reviewed upstream cyclomatic fixture must be ported to metric tests
or listed in the shared fixture matrix with a skip or changed-score
rationale. Because the source metric used ReSharper PSI/CFG APIs, Roslyn
metric differences are allowed only when documented case-by-case.

## Analyzer Status

Task 12 is deferred. Do not implement `PARL3002_CyclomaticComplexityThreshold`
or `PARL3002Tests` in this pass. The internal metric stands on its own and
will be picked up by PARL3002, PARL3101, or curation gates as those land.
