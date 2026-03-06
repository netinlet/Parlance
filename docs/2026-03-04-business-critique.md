# Parlance Product Blueprint — Business Critique

Date: 2026-03-04

## Reality Check
- Scope spans static analysis core, IDE/CLI, MCP for agents, configurator, and analytics SaaS — risk of dilution and long time-to-first-revenue.
- Open-sourcing core without a license decision invites fast-follow and forks; consider BSL/SSPL before launch.
- Heavy reliance on C# 12/13 idioms limits brownfield adoption (many estates are C# 8–10); modernization nudges could be noisy.
- MCP server differentiation is temporary; moat must be rule quality plus data/analytics, not protocol plumbing.

## Go-To-Market Wedge
- Ship a PR review bot + GitHub Action that posts inline comments using the C# engine; value in one day and seeds analytics data.
- AI-team wedge: "AI lint before merge" GitHub App that only runs on AI-authored commits/branches, aligned to the `ai-agent` profile.
- Defer full web configurator; ship CLI `idiomatic-cs configure --generate` presets and capture usage/contact for leads.

## Monetization Fast-Paths (New)
- Usage-based hosted analysis/API + PR bot; price per KLoC analyzed/month with a free OSS tier.
- Enterprise add-ons: SSO, audit log, PII scrub, on-prem runner, SLAs — easier sell to security/compliance buyers.
- Paid rule-pack marketplace (compliance/industry packs) with revenue share.
- Premium auto-fix PRs: high-confidence fixes land as PRs; charge per accepted fix or per repo seat.

## Product Sharpening
- Bias early scoring toward correctness; keep modernization rules at suggestion level to reduce noise and build trust.
- Ship thin VS Code/Rider extensions early (shim over CLI) to drive daily active usage; stronger wedge than MCP for most teams.
- Add regression-gate mode: fail CI only on new violations vs baseline to enable brownfield adoption.
- Build public benchmark corpus/leaderboard for AI agents (submit code → idiomatic score) to position as the C# quality eval.

## Defensibility & Data
- Collect anonymized rule-fire stats and accepted-fix rates to tune severities and train a confidence model.
- Offer opt-in upload of before/after snippets; process features client-side to lower privacy friction while building an auto-fix suggester.

## Lean Execution Order
1) PR bot + GitHub Action using the C# engine (free OSS tier).
2) IDE extensions (VS Code/Rider) atop the same engine.
3) Hosted API/MCP for agent teams once trust is established.
4) Lightweight rule browser + preset export (no heavy configurator yet).
5) Analytics dashboard after sustained ingestion.

## Immediate Decisions
- Pick the license for the core now (BSL/SSPL vs permissive) to protect GTM.
- Define PR-bot MVP scope and success metrics (time-to-signal, false-positive rate, accepted-fix rate).
- Draft hosted pricing/tiers for analysis/API and enterprise add-ons.
