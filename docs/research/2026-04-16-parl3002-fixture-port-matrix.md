# PARL3002 Fixture Port Matrix

This matrix tracks external fixture coverage for the internal cyclomatic
complexity metric that ships on this branch. Every reviewed upstream case must
be ported to Parlance tests or listed here with a skip rationale.

## Scope

This branch ships only the **internal** `CyclomaticComplexityMetric` under
`src/Parlance.CSharp.Analyzers/Metrics/Experimental/`. There is no user-facing
`PARL3002` diagnostic — platform rule `CA1502` already reports excessive
cyclomatic complexity at the IL level, and Parlance does not duplicate it.
See `docs/research/2026-04-16-parl3002-cyclomatic-complexity-contract.md`
§ "Relationship To CA1502" for the deferral decision.

Fixtures below therefore exercise the *metric* (is the computed number right?),
not a diagnostic pipeline.

## Cyclomatic Complexity Fixtures

**Upstream source:** JetBrains ReSharper Cyclomatic Complexity PowerToy,
Apache-2.0. Vendored at `artifacts/resharper-cyclomatic-complexity/`. Each port
rewrites the upstream scenario in a Parlance-owned test string; `.cs` files
and gold-file contents are not copied wholesale. Attribution in
`THIRD_PARTY_NOTICES.md`; source-use record in
`docs/research/2026-04-16-parl3002-quality-gates.md`.

| Upstream fixture | Scenario | Upstream gold | Parlance test name | Status | Rationale |
| --- | --- | ---: | --- | --- | --- |
| `ComplexMethodWithDefaultSettings.cs` | 9 `if`, 1 `\|\|`, 1 `&&` across nested branches | 12 | `ComplexMethod_DefaultSettingsMethod` | port | CFG predicate-counting matches upstream gold exactly. |
| `ComplexMethodWithDefaultSettings.cs` (alternate threshold mode) | Same source, different upstream threshold label | 12 | `ComplexMethod_ModifiedThresholdMethodScore` | port | Parlance does not model thresholds in the metric; verifies score is stable across runs. |
| `ManySequentialIfs.cs` | 83 empty `if` statements | 84 | `ManySequentialIfs_HighComplexity` | port | CFG π + 1 = 84. `E − N + 2P` would collapse to 1 here — see the metric's file header for the algorithm choice. |
| `ManyDeclarations.cs` | 85 `var` declarations, no branches | 1 | `ManyDeclarations_LowComplexity` | port | Confirms unconditional statements add no predicates. |
| `BoolAssignments.cs` | Repeated `Blah = val;` / `Blah = !val;` | 1 | `BoolAssignments_DoNotIncreaseComplexity` | port | Confirms boolean assignments and negations do not add predicates. |

All reviewed upstream cyclomatic fixtures are ported with gold parity. No
upstream cyclomatic fixtures are skipped.

## Parlance-owned cases

These cases cover shapes that the upstream fixtures do not exercise but that
the contract (`docs/research/2026-04-16-parl3002-cyclomatic-complexity-contract.md`)
says the metric must handle.

| Scenario | Parlance test name | Expected | Rationale |
| --- | --- | ---: | --- |
| Expression-bodied method with ternary | `Calculates_ParlanceSpecificCases/ExpressionBodiedMethod` | 2 | Confirms ternary-only methods score via CFG. |
| `switch` expression with discard arm | `Calculates_ParlanceSpecificCases/SwitchExpression` | 4 | Documents the CFG-vs-syntax divergence: CFG models the implicit "no match throws" branch (score 4); naive syntactic counting would say 3. The CFG answer matches CA1502. |
| Block-bodied property accessor | `Calculates_ParlanceSpecificCases/Accessor` | 2 | Confirms accessor-level scoring works. |

## Declaration-shape coverage

`CyclomaticDeclarationShapeTests` covers the set of declaration shapes the
metric contract claims to support. Each test constructs a body with exactly one
predicate and asserts `Complexity == 2` and `SkippedReason is null`.

| Shape | Test name |
| --- | --- |
| Method, block body | `Method_BlockBody` |
| Method, expression body | `Method_ExpressionBody` |
| Constructor, block body | `Constructor_BlockBody` |
| Destructor | `Destructor` |
| Binary operator, expression body | `BinaryOperator_ExpressionBody` |
| Conversion operator, expression body | `ConversionOperator_ExpressionBody` |
| Property `get` accessor, block body | `PropertyAccessor_Get_BlockBody` |
| Property `get` accessor, expression body | `PropertyAccessor_Get_ExpressionBody` |
| Property `set` accessor, block body | `PropertyAccessor_Set_BlockBody` |
| Property `init` accessor, block body | `PropertyAccessor_Init_BlockBody` |
| Indexer `get` accessor, block body | `IndexerAccessor_Get_BlockBody` |
| Expression-bodied property | `ExpressionBodiedProperty` |
| Expression-bodied indexer | `ExpressionBodiedIndexer` |
| Local function, block body | `LocalFunction_BlockBody` |
| Local function, expression body | `LocalFunction_ExpressionBody` |

These shape tests are not fixture ports — they are the metric's own guard
against Roslyn CFG construction failures across the declared-supported
declaration surface.
