#!/usr/bin/env python3
"""
Converts manifest generator JSON output to catalog YAML files for CA, IDE, and RCS.
Fields requiring source analysis (fix_type, configurable, introduced, modified_at)
are left as null for subsequent enrichment passes.
"""

import json
import sys
import yaml
from pathlib import Path

PACKAGE_MAP = {
    "Microsoft.CodeAnalysis.NetAnalyzers": "ca",
    "Microsoft.CodeAnalysis.CSharp.NetAnalyzers": "ca",
    "Microsoft.CodeAnalysis.CSharp.CodeStyle": "ide",
    "Microsoft.CodeAnalysis.CodeStyle": "ide",
    "Roslynator.CSharp.Analyzers": "rcs",
    "Roslynator_Analyzers_Roslynator.Workspaces.Core": "rcs",
}

SOURCE_REPO_MAP = {
    "ca": "dotnet/roslyn-analyzers",
    "ide": "dotnet/roslyn",
    "rcs": "dotnet/roslynator",
}


def docs_url(package: str, rule_id: str) -> str:
    if package == "ca":
        return f"https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/{rule_id.lower()}"
    if package == "ide":
        return f"https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/{rule_id.lower()}"
    if package == "rcs":
        return f"https://github.com/dotnet/roslynator/blob/main/docs/analyzers/{rule_id}.md"
    return ""


def manifest_entry_to_catalog(rule: dict) -> dict | None:
    source = rule.get("source", "")
    package = PACKAGE_MAP.get(source)
    if not package:
        return None

    rule_id = rule["id"]
    return {
        "id": rule_id,
        "title": rule["title"],
        "package": package,
        "category": rule["category"],
        "analysis_type": None,       # must be set per rule — filled by review pass
        "sonar_type": None,
        "default_severity": rule["defaultSeverity"],
        "enabled_by_default": rule["defaultSeverity"] not in ("none", "silent"),
        "deprecated": False,
        "fix_type": None,            # filled by enrich-fix-type.py
        "configurable": None,        # filled by source analysis
        "requires_option": None,
        "scope": None,
        "applies_to": "all",
        "supersedes": None,
        "related": [],
        "tags": [],
        "security_standards": {},
        "remediation_minutes": None,
        "open_issues": None,         # filled by enrich-open-issues.py
        "source_repo": SOURCE_REPO_MAP[package],
        "docs_url": docs_url(package, rule_id),
        "introduced": None,          # filled by enrich-git-dates.py
        "modified_at": None,         # filled by enrich-git-dates.py
        "research_at": "2026-03",
        "notes": rule.get("description") or rule["title"],
    }


def main():
    if len(sys.argv) < 2:
        print("Usage: extract-catalog.py <manifest.json>")
        sys.exit(1)

    with open(sys.argv[1]) as f:
        manifest = json.load(f)

    buckets: dict[str, list] = {"ca": [], "ide": [], "rcs": []}
    seen: set[str] = set()
    for rule in manifest["rules"]:
        entry = manifest_entry_to_catalog(rule)
        if entry and entry["package"] in buckets and entry["id"] not in seen:
            buckets[entry["package"]].append(entry)
            seen.add(entry["id"])

    catalog_dir = Path(__file__).parent.parent / "docs" / "research" / "analyzer-catalog"
    for package, entries in buckets.items():
        out = catalog_dir / f"{package}.yaml"
        with open(out, "w") as f:
            yaml.dump(entries, f, allow_unicode=True, sort_keys=False, default_flow_style=False)
        print(f"  {out.name}: {len(entries)} rules written")


if __name__ == "__main__":
    main()
