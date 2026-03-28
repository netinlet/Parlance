#!/usr/bin/env python3
"""
Enriches open_issues for each rule by fetching all open issues from the
upstream repo and counting those that mention the rule ID in title or body.

Uses the gh CLI (must be authenticated). Fetches up to MAX_ISSUES per repo
to avoid runaway API usage on large repos.
"""

import json
import re
import subprocess
import yaml
from pathlib import Path

CATALOG_DIR = Path("docs/research/analyzer-catalog")

# Map package key → GitHub repo slug
REPO_MAP = {
    "ca":               "dotnet/roslyn-analyzers",
    "ide":              "dotnet/roslyn",
    "rcs":              "dotnet/roslynator",
    "sonar":            "SonarSource/sonar-dotnet",
    "stylecop":         "DotNetAnalyzers/StyleCopAnalyzers",
    "meziantou":        "meziantou/Meziantou.Analyzer",
    "scs":              "security-code-scan/security-code-scan",
    "asyncfixer":       "semihokur/AsyncFixer",
    "idisp":            "DotNetAnalyzers/IDisposableAnalyzers",
    "xunit":            "xunit/xunit.analyzers",
    "nunit":            "nunit/nunit.analyzers",
    "fluentassertions": "fluentassertions/fluentassertions.analyzers",
    "nsubstitute":      "nsubstitute/NSubstitute.Analyzers",
    "errorprone":       "SergeyTeplyakov/ErrorProne.NET",
    "sharpsource":      "Vannevelj/SharpSource",
    "disposablefixer":  "BADF00D/DisposableFixer",
    "vsthrd":           "microsoft/vs-threading",
    "csharpguidelines": "bkoelman/CSharpGuidelinesAnalyzer",
    "exhaustive":       "WalkerCodeRanger/ExhaustiveMatching",
    "refl":             "DotNetAnalyzers/ReflectionAnalyzers",
    "aspnetcore":       "DotNetAnalyzers/AspNetCoreAnalyzers",
    "hyperlinq":        "NetFabric/NetFabric.Hyperlinq.Analyzer",
}

# Fetch at most this many open issues per repo (100 per page, so n pages)
MAX_ISSUES = 500


def fetch_open_issues(repo: str) -> list[dict]:
    """Fetch all open issues from a repo via gh CLI (up to MAX_ISSUES)."""
    issues = []
    page = 1
    per_page = 100
    while len(issues) < MAX_ISSUES:
        url = f"repos/{repo}/issues?state=open&per_page={per_page}&page={page}"
        try:
            out = subprocess.check_output(
                ["gh", "api", url,
                 "--jq", "[.[] | {title: .title, body: (.body // \"\")}]"],
                text=True, stderr=subprocess.DEVNULL,
            )
            page_issues = json.loads(out)
        except (subprocess.CalledProcessError, json.JSONDecodeError):
            break
        if not page_issues:
            break
        issues.extend(page_issues)
        if len(page_issues) < per_page:
            break
        page += 1
    return issues


def build_issue_index(issues: list[dict]) -> dict[str, int]:
    """Count how many issues mention each rule ID."""
    counts: dict[str, int] = {}
    _id_re = re.compile(r'\b([A-Z][A-Za-z]*\d{3,5})\b')
    for issue in issues:
        text = (issue.get("title") or "") + " " + (issue.get("body") or "")
        for rule_id in set(_id_re.findall(text)):
            counts[rule_id] = counts.get(rule_id, 0) + 1
    return counts


def enrich(catalog_file: Path, issue_counts: dict[str, int]):
    entries = yaml.safe_load(catalog_file.read_text()) or []
    changed = 0
    for e in entries:
        count = issue_counts.get(e["id"], 0)
        if e.get("open_issues") != count:
            e["open_issues"] = count
            changed += 1
    with open(catalog_file, "w") as f:
        yaml.dump(entries, f, allow_unicode=True, sort_keys=False, default_flow_style=False)
    nonzero = sum(1 for e in entries if (e.get("open_issues") or 0) > 0)
    print(f"  {catalog_file.name}: {nonzero}/{len(entries)} rules have open issues ({changed} updated)")


def main():
    for package, repo in REPO_MAP.items():
        catalog_file = CATALOG_DIR / f"{package}.yaml"
        if not catalog_file.exists():
            continue
        print(f"Fetching open issues from {repo}...")
        issues = fetch_open_issues(repo)
        print(f"  {len(issues)} issues fetched")
        counts = build_issue_index(issues)
        enrich(catalog_file, counts)


if __name__ == "__main__":
    main()
