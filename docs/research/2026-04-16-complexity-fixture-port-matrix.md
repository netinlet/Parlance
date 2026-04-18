# Complexity Fixture Port Matrix

This matrix tracks external fixture coverage for the complexity quality-gate
work. Every reviewed upstream case must be ported to Parlance tests or skipped
with a rationale.

## Cognitive Complexity Fixtures

Source: `artifacts/jetbrains-plugin-cognitivecomplexity/src/dotnet/ReSharperPlugin.CognitiveComplexity.Tests/test/data/CSharp/`

License attribution: MIT license, Matthias Koch. These fixtures are behavioral
references; Parlance tests should recreate the scenarios without copying the
source files wholesale.

| Upstream file | Scenario | Upstream score | Parlance test name | Status | Rationale |
| --- | --- | ---: | --- | --- | --- |
| `ConditionTest.cs` | `M1`: `if` with `else` | 2 | `Condition_IfElse` | port | Basic branch and `else` behavior. |
| `ConditionTest.cs` | `M2`: nested `if`, `else if`, `else` | 5 | `Condition_NestedIfElseIfElse` | port | Covers nesting and `else if` chain handling. |
| `LoopingTest.cs` | `M1`: deeply nested loops, nested `if`, `goto` | 16 | `Looping_NestedLoopsAndGoto` | changed | Parlance adopts Sonar's hybrid `+nesting+1` for `goto` (T4.5). The same nesting structure scores 21 in Parlance (goto at nesting 5 contributes +6 instead of the upstream flat +1). |
| `LoopingTest.cs` | `M2`: flat loops and `goto` | 5 | `Looping_FlatLoopsAndGoto` | port | Confirms flat loops do not add nesting penalties. |
| `LoopingTest.cs` | `M3`: `foreach` with `continue` and `break` | 6 | `Looping_ContinueAndBreak` | port | Follow `.gold`: `break` counts, `continue` does not affect the score. |
| `LogicalOperatorTest.cs` | `M1`: flat `||`, flat `&&`, negated `&&` group | 3 | `LogicalOperators_FlatGroups` | port | Covers boolean operator grouping outside branches. |
| `LogicalOperatorTest.cs` | `M2`: mixed `&&` and `||` in `if` condition | 4 | `LogicalOperators_MixedGroupsInIf` | port | Covers group alternation plus branch increment. |
| `SwitchTest.cs` | `M1`: switch statement only | 1 | `Switch_SimpleStatement` | port | Confirms switch cases do not each increment score. |
| `SwitchTest.cs` | `M2`: switch with nested `if` in case | 3 | `Switch_NestedIfInCase` | port | Covers nesting inside switch sections. |
| `TryCatchTest.cs` | `M1`: try with nested `if`/loops and catch branch | 9 | `TryCatch_NestedTryAndCatch` | port | Covers catch and nested body scoring. |
| `TryCatchTest.cs` | `M2`: `catch` nested under `if` | 3 | `TryCatch_CatchInsideIf` | port | Confirms catch receives nesting penalty. |
| `TryCatchTest.cs` | `M3`: catch filter | 4 | `TryCatch_FilterAddsComplexity` | port | Covers exception filter increment. |
| `TryCatchTest.cs` | `M4`: nested `if` inside catch | 6 | `TryCatch_NestedIfInsideCatch` | port | Covers deeper nesting after catch. |
| `LambdaTest.cs` | `M1`: lambda containing `if` | 2 | `Lambda_LambdaAddsNesting` | port | Lambda contributes nesting to containing method. |
| `LambdaTest.cs` | `M2`: anonymous delegate containing `if` | 2 | `Lambda_AnonymousMethodAddsNesting` | port | Anonymous method contributes nesting to containing method. |
| `RecursiveTest.cs` | `M1`: direct recursive call | 1 | `Recursive_DirectCall` | port | Requires semantic recursion detection. |
| `RecursiveTest.cs` | `M2`: recursive call in `if` condition with `&&` | 3 | `Recursive_CallInsideLogicalIf` | port | Covers recursion plus branch plus boolean group. |
| `RecursiveTest.cs` | `M3`: recursive call in return expression with `&&` | 2 | `Recursive_CallInsideReturnLogicalGroup` | port | Covers recursion outside a branch. |
| `NullCheckingTest.cs` | `M1`: explicit null check | 1 | `NullChecking_ExplicitIf` | port | Confirms normal `if` behavior. |
| `NullCheckingTest.cs` | `M2`: null-conditional access | 0 | `NullChecking_NullConditionalAccess` | port | Confirms null propagation does not increment score. |

All reviewed cognitive-complexity cases are classified. No cognitive cases are
skipped.

Cyclomatic-complexity fixture ports (for the internal `CyclomaticComplexityMetric`
and future `PARL3002` work) are tracked in a separate matrix on the
`feature/curation-start` branch and land with that work, not here.
