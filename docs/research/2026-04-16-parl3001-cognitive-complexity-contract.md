# PARL3001 Cognitive Complexity Contract

## Rule Identity

- ID: `PARL3001`
- Title: Cognitive complexity exceeds threshold
- Category: Maintainability
- Default severity: Warning
- Default threshold: `15`
- Message: `'{0}' has cognitive complexity {1}, exceeding threshold {2}`
- Diagnostic location: declaration identifier when available; otherwise the
  declaration syntax node.

## Purpose

`PARL3001` reports declarations whose control-flow nesting and interruption make
the code harder to understand. The rule is a quality-gate analyzer, not a code
style analyzer, and it does not provide a code fix.

## External Sources Reviewed

- `artifacts/jetbrains-plugin-cognitivecomplexity`, MIT license, Matthias Koch.
  Reviewed `CognitiveComplexityElementProcessor.cs`,
  `CognitiveComplexityAnalysisSettings.cs`, and the C# fixture layout.
- `artifacts/resharper-cyclomatic-complexity`, Apache-2.0 license, JetBrains.
  Reviewed for contrast only; cyclomatic behavior belongs to a later rule.

The implementation should be clean-room Roslyn code based on documented behavior
and fixture expectations, not copied ReSharper PSI code.

## Supported Declarations

Analyze method-like and accessor-like bodies:

- Methods, constructors, destructors, operators, and conversion operators.
- Property, indexer, and event accessors.
- Local functions (see split below).
- Block bodies and expression bodies.

Lambdas and anonymous methods contribute to the containing declaration score but
are not reported independently in the first implementation.

**Local function scoping split.** Non-static local functions are folded into
their containing declaration: their body scores at parent nesting+1, matching
the original JetBrains plugin behavior and reflecting their shared-closure
semantics. Static local functions are scored *independently* — the parent
declaration's score excludes the static local's body entirely, and PARL3001
reports against the static local as its own analysis target (with its own
threshold via the method threshold option). This matches Sonar S3776 and
prevents double-counting code the user explicitly isolated with the `static`
modifier.

## Scoring Rules

- `if`: `+1 + nesting` when it starts a new decision.
- `else if`: `+1`, without adding a second nesting penalty for the chain.
- `else`: `+1`.
- `for`, `foreach`, `while`, `do`: `+1 + nesting`.
- `switch` statement and switch expression: `+1 + nesting` for the switch, not
  per case or arm.
- Conditional expression: `+1 + nesting`.
- `catch`: `+1 + nesting`; exception filters add `+1`.
- Boolean operator groups: `+1` for each contiguous `&&` or `||` decision group,
  including alternation between groups.
- Pattern operator groups: `+1` for each contiguous `and` or `or` pattern-operator
  group under a binary pattern (`is … and …`, `x is … or …`). The scoring mirrors
  the boolean-operator rule — one group per maximal run of the same pattern
  operator — so a pattern shape and a logically-equivalent boolean shape produce
  the same increment count.
- Recursion: `+1` when semantic analysis can prove the declaration calls itself.
- `break`: `+1`. See "Break scoring decision" below for why this is `+1` and
  not `0`.
- `goto` (all forms: `goto label`, `goto case`, `goto default`): `+1 + nesting`
  (hybrid), matching Sonar. Unlike `break`, which only leaves the immediately
  enclosing construct, `goto` can jump across nesting boundaries, so the
  cognitive cost grows with the depth the reader has to unwind.
- `continue`: not counted in v1 to preserve reviewed ReSharper fixture behavior.

## Break scoring decision

Sonar's S3776 scores `break` as `0`; the JetBrains Cognitive Complexity plugin
(Parlance's source-use base for PARL3001) scores it `+1`. Both are defensible
readings of the Sonar white paper: Sonar argues `break` is a simple control
transfer with no extra cognitive load, while JetBrains treats it as a small
interruption to linear reading comparable to a `goto`.

Parlance chose **`+1`** (JetBrains parity) on 2026-04-17 as part of the
hardening plan's D1 decision. The value lives as the named constant
`ComplexityDefaults.BreakIncrement` so the choice is surfaced at the one site
that controls it, not scattered through the walker. The constant carries a
source comment recording the alternative, so a future change of heart can be
made by editing one value rather than hunting through `VisitBreakStatement`.
If the Parlance project later decides to match Sonar instead, flip the
constant to `0` and update the affected fixture scores in the port matrix.

Nesting increases while visiting nested bodies for decisions, loops, switches,
local functions, lambdas, and anonymous methods. The analyzer must not throw on
missing bodies or incomplete syntax.

## Configuration

PARL3001 uses a dual threshold: one for method-shaped declarations (methods,
constructors, destructors, operators, local functions) and a smaller one for
property-shaped declarations (property/indexer arrow bodies and every accessor
kind — get/set/init/add/remove, regardless of whether the owner is a property,
indexer, or event). Accessors are supposed to be trivial, so their default
threshold is intentionally much lower.

```ini
# Method-shaped declarations. Default: 15.
dotnet_code_quality.PARL3001.max_cognitive_complexity = 15

# Property-shaped declarations. Default: 3.
dotnet_code_quality.PARL3001.max_cognitive_complexity.property = 3
```

Invalid, missing, or non-positive values fall back to the shape-specific default
(`15` for methods, `3` for properties).

## Generated And Invalid Code

Generated code is excluded by analyzer configuration. Incomplete or invalid code
is analyzed best-effort and should produce no diagnostic unless the declaration
and computed score are reliable.

## Fixture Port Requirements

Every reviewed upstream cognitive-complexity fixture must be ported to Parlance
tests or listed in a fixture port matrix with a skip rationale. Fixture names
should preserve enough upstream context to make attribution traceable.

## Open Follow-Ups

- Decide whether lambdas should receive independent diagnostics after the first
  implementation.
- Revisit `continue` if Sonar-style parity becomes more important than the
  reviewed ReSharper fixture behavior.
- Create a later `PARL3002` cyclomatic-complexity contract only after comparing
  it with platform rule `CA1502`.
