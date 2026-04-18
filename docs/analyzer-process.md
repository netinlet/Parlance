# Analyzer Process

This document describes how Parlance creates official analyzers. It complements
`docs/analyzer-development-guide.md`; read that guide before designing or
implementing an analyzer.

## Research Intake

Start from a concrete problem statement, issue, research note, or existing tool.
Record the source material in a rule-specific research document under
`docs/research/`. Prefer rule IDs in the `PARL3XXX` range for maintainability and
quality-gate analyzers.

## External Source Review

When external projects inform a rule, review the relevant code before planning
implementation. Identify which parts are reusable behavior, which parts are
platform-specific integration, and which parts should not be carried forward.
Attribute the reviewed projects in the research document with repository links,
license names, and the files or fixtures that influenced the rule.

## License And Attribution

Use a clean-room Roslyn implementation unless direct copying or substantial
adaptation is explicitly justified. If code or test data is copied or closely
adapted, add or update `THIRD_PARTY_NOTICES.md`. If the external project only
informed behavior or test coverage, document that attribution in the rule
research note.

## Rule Contract

Create a rule contract before implementation. The contract must define the rule
ID, title, category, default severity, diagnostic message, diagnostic location,
configuration options, supported syntax, excluded syntax, generated-code
behavior, and incomplete-code behavior.

## Fixture Port Matrix

Every upstream fixture or test case from reviewed projects must be represented
in Parlance tests or listed as skipped with a rationale. Keep the matrix in the
rule research document or a nearby test-plan document. Skips should be narrow,
for example "ReSharper PSI-only behavior" or "not relevant to Roslyn syntax".

## Implementation

Implement the smallest analyzer that satisfies the contract. Prefer syntax
actions and cheap filters first. Use semantic analysis only when the diagnostic
claim requires it. Do not add a code fix unless the transformation is safe,
obvious, and testable.

## Packaging And Discovery

Ensure the analyzer is included in the analyzer package and discoverable by the
test harness. Add rule metadata in the same location as existing descriptors so
severity, category, help links, and IDs remain consistent.

## Curation

After implementation, run the analyzer against representative Parlance code and
record observed diagnostics. Use curation results to tune thresholds, wording,
and false-positive handling before enabling the rule broadly.

## Verification

Add unit tests for threshold boundaries, positive cases, negative cases,
configuration, generated code, and incomplete syntax. Run the solution tests
before considering the rule complete.

## Follow-Up Issues

Create follow-up issues for deferred metric differences, additional syntax
support, configuration improvements, or source projects that need deeper review.
