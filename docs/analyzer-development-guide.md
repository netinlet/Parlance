# Analyzer Development Guide

Last updated: 2026-03-06
Status: Draft starting point

## Why this document exists

This project is building custom C# analyzers and, potentially, code fixes. Roslyn makes that possible, but it is also easy to ship analyzers that are technically correct in a happy-path test and still wrong, noisy, slow, or misleading in real code.

This guide is a practical starting point for developing analyzers in this repo. It mixes:

- Roslyn SDK fundamentals
- testing and code-fix workflow guidance
- practical pitfalls already visible in this codebase

It is intentionally opinionated. Favor fewer, more trustworthy diagnostics over more aggressive diagnostics.

## Core mental model

### 1. Syntax first, semantics second

Roslyn gives you a full-fidelity syntax tree for the source being edited. That tree includes:

- nodes
- tokens
- trivia (whitespace, comments, directives)

Syntax trees are immutable. You do not mutate a tree in place; you create a new tree or document from the old one.

In practice:

- use syntax to cheaply narrow candidates
- use semantics only after a candidate survives cheap syntactic checks
- use data-flow or symbol analysis only when syntax alone is not enough

This is both a correctness rule and a performance rule.

### 2. Analyzers run on incomplete and invalid code

Visual Studio and Roslyn invoke analyzers while a developer is typing, not only on clean, compilable programs.

That means every analyzer should expect:

- missing nodes
- partially written expressions
- invalid conversions
- unresolved symbols
- incomplete declarations

If a rule only works on valid code, it must explicitly bail out when the semantic model cannot support the conclusion.

### 3. A diagnostic is a claim

Every diagnostic is making a claim of the form:

- "this code matches pattern X"
- "pattern X is undesirable here"
- "recommendation Y is valid for this exact code"

The third part is where analyzers often go wrong.

Do not report a diagnostic unless the suggested direction is actually valid for the current code. A diagnostic that teaches an invalid transformation is worse than no diagnostic.

## Recommended development workflow

### 1. Start with a rule contract

Before writing the analyzer, define:

- what code should trigger
- what code must not trigger
- whether the rule is style, readability, modernization, correctness, or bug-risk
- whether the rule is always safe or only advisory
- whether a code fix is semantics-preserving

If the rule depends on language version, target framework, or reference availability, write that down up front.

Before you even start implementing, check whether the platform already ships the rule you want. Dennis's sample "empty lines" analyzer was partly a learning exercise; he explicitly notes that a built-in diagnostic already existed for that case. A custom analyzer has real maintenance cost, so "can we configure an existing analyzer?" should be the first gate.

### 2. Write tests before the analyzer gets clever

Roslyn's analyzer testing packages are the fastest way to shape a rule safely.

At minimum, every rule should have:

- a positive case
- a no-diagnostic case
- a false-positive regression case
- an invalid/incomplete-code case when semantics are involved

If a code fix exists, add:

- a simple fix test
- a trivia/formatting preservation test
- a "fix should not be offered" test for unsafe cases

For quick manual feedback while developing, a tiny local host project can still help. The Denace series shows the common pattern of referencing the analyzer project with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"` so the host project consumes the analyzer without treating it as a normal library reference. Use that as a smoke-test loop, not as the real validation strategy.

### 3. Register the narrowest action you can

Prefer specific registrations such as:

- `RegisterSyntaxNodeAction`
- `RegisterOperationAction`
- `RegisterCompilationStartAction` when you need one-time setup

Only register for the syntax kinds or operations you actually care about. Broad registrations create unnecessary analyzer load and make reasoning harder.

### 4. Use cheap filters before expensive analysis

The Roslyn tutorial's `MakeConst` example is a good model:

- first filter by syntax shape
- then inspect constant-ness
- then do data-flow analysis

This pattern generalizes well:

- cheap syntax gates first
- symbol lookup second
- data flow / control flow / deeper graph analysis last

### 5. Add a code fix only when you can defend it

A code fix should be more conservative than a diagnostic, not less.

Good fixes:

- preserve meaning
- preserve trivia where possible
- format the result
- work in batch

Bad fixes:

- change construction semantics
- depend on unstated project settings
- silently require new language features
- assume a target-typed context that is not present

Code-fix providers are also a different integration point from analyzers:

- they are MEF components
- they should be stateless
- they should support cancellation
- they should think about batch behavior from day one

## Analyzer design guidelines

### Be explicit about scope

For each rule, answer:

- what exact syntax/semantic pattern is matched?
- what exact symbol ownership is required?
- what language version is required?
- what project context is assumed?

Example: if a rule says "assigned to a property on the containing type", verify that the symbol actually belongs to the containing type. Do not just check that the left side is any `IPropertySymbol`.

### Prefer precise rules over aspirational ones

If the rule is trying to recommend a newer C# feature, verify that:

- the feature is available for the effective language version
- the replacement is valid in the current context
- the replacement does not rely on unstated typing behavior

Modernization rules are especially easy to overstate.

### Treat language-version-sensitive rules as first-class design concerns

If a rule needs C# 11 or C# 12, the analyzer architecture must have a way to know that.

That means one or more of:

- explicit parse options
- effective language-version inspection
- configuration passed into the analysis engine

If the system cannot know the language version, it cannot honestly claim language-version-aware behavior.

### Make diagnostic messages modest

Prefer:

- "can be simplified"
- "consider using"
- "can be converted"

Avoid overclaiming language like:

- "replace with X"
- "should always use X"

unless the rule really has proven that transformation is valid.

## Code-fix guidelines

### Treat the provider itself as infrastructure

The Denace code-fix walkthrough is a useful reminder that a code fix is not "just a helper method". It is a `CodeFixProvider` living inside the Roslyn workspace layer.

Practical implications:

- export it correctly
- keep it stateless
- wire it to the exact fixable diagnostic IDs
- use a stable action title/equivalence key
- use `WellKnownFixAllProviders.BatchFixer` only after thinking through fix-all interactions

If a fix is not safe in batch, do not hide behind `BatchFixer` and hope for the best.

### Preserve trivia and formatting

The Roslyn tutorial explicitly handles leading trivia before inserting the `const` token, then applies `Formatter.Annotation`.

That is a good baseline:

- preserve comments and whitespace
- avoid ugly output
- let Roslyn format the resulting tree

### Operate on the smallest correct span

Find the diagnostic's syntax node from the diagnostic span, then transform the smallest syntax unit that safely produces the desired result.

This reduces accidental edits and makes previews easier to trust.

If the analyzer already knows exactly what secondary location a fix needs, consider using `additionalLocations` on the diagnostic. The Denace series uses that as a bridge from analyzer to code fix. This can make the fix simpler and less guessy, but only if the extra locations are precise and stable.

### Respect batch behavior

Fixes often work for one case and fail in "Fix all".

Before shipping a fix, think about:

- ordering dependencies
- interactions between multiple diagnostics
- whether fixing one node invalidates assumptions for nearby nodes

The `MakeConst` tutorial demonstrates this exact failure mode: a fix can look correct in one declaration order and still break in another.

Also remember that Roslyn can cancel a code-fix operation at any time. Pass the cancellation token through async APIs and honor it during CPU-bound work. A correct fix that freezes the IDE is still a bad fix.

### Do not attach a fix to a rule just because a human could do it

Some rules are better as diagnostics only.

Skip the code fix when:

- the right transformation depends on API intent
- call sites must change
- required members or constructors change object creation semantics
- multiple valid fixes exist and Roslyn cannot infer the right one

## Testing guidelines

### Use analyzer tests as the real design surface

The tutorial is right: unit tests are faster and more precise than repeatedly launching another VS instance.

Use analyzer tests to encode:

- exact spans
- exact IDs
- exact severities
- exact fixed output

The Denace testing article is useful here for one more reason: it shows the full workspace plumbing with `AdhocWorkspace`, projects, documents, and changed solutions. That is valuable for understanding Roslyn, but for day-to-day analyzer work, prefer `Microsoft.CodeAnalysis.Testing` unless you specifically need to test workspace-level behavior.

### Always write negative tests

A lot of analyzer quality is defined by what it does *not* flag.

Negative tests should cover:

- already-correct code
- related but different syntax
- invalid code
- multi-declaration cases
- partially typed code when relevant
- feature-not-available cases

### Add regression tests for every bug found in review

If a review discovers a false positive, false negative, or unsafe fix:

- add the failing test first
- then patch the analyzer

That is especially important for modernization rules, where edge cases are common.

### Use Roslyn test markup aggressively

The Denace series calls out one of the best productivity features in `Microsoft.CodeAnalysis.Testing`: embedded markup in the test source.

Useful forms:

- `[| ... |]` for an expected diagnostic span
- `{|ID: ... |}` for an expected diagnostic with a specific ID
- `$$` for an expected cursor/location

Use markup whenever it makes the test easier to read than a manually constructed span object.

## Performance and reliability checklist

### Keep analyzer callbacks cheap

The Roslyn tutorial explicitly notes that analyzers should exit as quickly as possible because they run while the user edits code.

Practical implications:

- return early often
- do not allocate large objects on hot paths unless necessary
- do not do semantic analysis if syntax already rules the case out
- avoid repeated symbol lookups when one lookup can be cached locally

The Denace series also highlights a practical distinction that is easy to forget:

- analyzers often operate at compilation/syntax-tree scale
- code fixes usually operate at document/workspace scale

Keep those responsibilities separate. Do not make the analyzer do workspace-style fix logic just because the same person is writing both classes.

### Use Roslyn concurrency correctly

In general, analyzers should opt into concurrent execution when safe. Microsoft documents `EnableConcurrentExecution` as a performance feature, but it requires that analyzer actions behave correctly in parallel.

That means:

- no mutable shared state unless carefully synchronized
- prefer local variables and immutable caches
- be very cautious with static mutable collections

### Decide generated-code behavior deliberately

Microsoft recommends explicitly configuring generated-code analysis mode. Most style and modernization rules should skip generated code unless there is a strong reason not to.

## Repo-specific guidance for Parlance

### 1. Keep the analyzer assembly conservative

The current split is sensible:

- `src/Parlance.CSharp.Analyzers` for analyzers
- `src/Parlance.CSharp` for engine/integration logic

Keep analyzer logic independent and conservative. If a rule needs environment-specific behavior, keep that policy in the engine or configuration layer, not buried in rule implementation.

The Denace setup article also reinforces why the analyzer project itself should stay broadly loadable. Targeting `netstandard2.0` for the analyzer assembly remains the safest default for Roslyn analyzer distribution.

### 2. Treat reference assemblies as part of analyzer correctness

For source analysis, reference assemblies matter. Do not assume runtime assemblies are equivalent to target-pack reference assemblies for all analyzer scenarios.

If the engine claims to analyze against a specific TFM or language environment, it should load the matching reference assemblies and expose that choice clearly.

### 3. Add explicit language-version plumbing before expanding modernization rules

Rules like:

- primary constructors
- collection expressions
- required members

all depend on modern C# features and context-sensitive semantics.

Before adding more such rules, the engine should be able to answer:

- what C# language version is in effect?
- what target framework/reference set is in effect?
- is the suggested replacement legal in this context?

### 4. Validate ownership, not just kind

Several analyzer bugs come from checking "field or property" and stopping there.

For any rule that reasons about assignments into the current type, verify:

- containing symbol
- declared accessibility when relevant
- setter/init accessibility
- whether the symbol is actually part of the pattern the rule claims

### 5. Be wary of contradictory modernization guidance

Two individually plausible rules can still conflict.

Examples:

- "prefer primary constructor"
- "prefer required properties"

If both can fire on the same type, that is a product-design problem, not just an implementation detail. Add arbitration rules or suppress the broader rule when the narrower one applies.

### 6. Turn on analyzer analyzers and keep them on

Roslyn ships analyzer-authoring analyzers. The Denace series explicitly recommends enabling them through `<EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>`.

For this repo, that should remain the default for analyzer projects because it catches:

- API misuse
- threading/performance issues
- release-tracking gaps
- other analyzer-authoring mistakes that normal project analyzers will not catch

## Common pitfalls to watch for

### Target-typed features

Some modern syntax is target-typed and has no natural type on its own. Do not recommend it unless the current context provides the needed target type.

### Constructor and object-creation semantics

Features like `required` are not just syntax sugar. They change object creation requirements and may require object initializers or `SetsRequiredMembers` constructors.

Do not suggest these transformations casually.

### Incomplete semantic checks

If a rule uses semantic analysis, confirm all the semantic facts you actually depend on:

- exact symbol
- exact containing type
- exact conversion
- exact constant-ness
- exact control-flow/data-flow property

### Overly broad pattern matching

A rule that says it handles "simple assignments" but matches any field/property symbol is not a simple-assignment rule. It is a broad symbol-kind rule masquerading as a conversion rule.

The implementation should match the product claim exactly.

## Useful tools

- Syntax Visualizer for understanding tree shape
- RoslynQuoter for constructing syntax nodes
- `Microsoft.CodeAnalysis.Testing` for analyzer and code-fix tests
- a second VS instance or equivalent interactive host for exploratory testing

## Suggested checklist for a new rule

1. Write the rule contract in plain English.
2. List required language/version/context assumptions.
3. Create positive and negative tests first.
4. Register the narrowest action possible.
5. Add cheap syntax filters.
6. Add semantic and data-flow checks only as needed.
7. Verify the diagnostic message does not overclaim.
8. Add a code fix only if it is defensible and batch-safe.
9. Test invalid code and multi-node interactions.
10. Add at least one regression test for a tricky edge case.

## Sources

Read and used:

- https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix
- https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/syntax-analysis
- https://raw.githubusercontent.com/dotnet/roslyn/main/docs/wiki/Getting-Started-Writing-a-Custom-Analyzer-%26-Code-Fix.md
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.analysiscontext.enableconcurrentexecution
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.analysiscontext.configuregeneratedcodeanalysis
- https://denace.dev/exploring-roslyn-net-compiler-platform-sdk
- https://denace.dev/getting-started-with-roslyn-analyzers
- https://denace.dev/fixing-mistakes-with-roslyn-code-fixes
- https://denace.dev/testing-roslyn-analyzers-and-code-fixes
