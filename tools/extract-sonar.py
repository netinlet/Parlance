#!/usr/bin/env python3
"""
Extracts SonarQube C# rule metadata from a cloned sonar-dotnet repo.
Reads flat rspec JSON files for all fields. Maps sonar_type to analysis_type.
"""

import json
import re
import yaml
import subprocess
from pathlib import Path

SONAR_REPO = Path("analyzers_research/sonar-dotnet")
RSPEC_DIR = SONAR_REPO / "analyzers" / "rspec" / "cs"
RULES_DIR = SONAR_REPO / "analyzers" / "src" / "SonarAnalyzer.CSharp" / "Rules"

SEVERITY_MAP = {
    "Blocker": "error",
    "Critical": "error",
    "Major": "warning",
    "Minor": "suggestion",
    "Info": "suggestion",
}

SONAR_TYPE_TO_ANALYSIS_TYPE = {
    "BUG": "bug",
    "CODE_SMELL": "code_smell",
    "VULNERABILITY": "vulnerability",
    "SECURITY_HOTSPOT": "security_hotspot",
}

SCOPE_MAP = {
    "All": "all",
    "Main": "production",
    "Tests": "tests",
}

QUICKFIX_TO_FIX_TYPE = {
    "covered": "auto",
    "targeted": "auto",
    "partial": "manual",
    "infeasible": "none",
    "unknown": "none",
}

def get_git_dates(rule_file: Path) -> tuple[str | None, str | None]:
    """Returns (introduced YYYY-MM, modified_at YYYY-MM)."""
    try:
        log = subprocess.check_output(
            ["git", "-C", str(SONAR_REPO), "log", "--follow", "--format=%ai",
             str(rule_file.relative_to(SONAR_REPO))],
            stderr=subprocess.DEVNULL, text=True
        ).strip().splitlines()
        if not log:
            return None, None
        to_ym = lambda s: s[:7]
        return to_ym(log[-1]), to_ym(log[0])
    except Exception:
        return None, None

def parse_remediation_minutes(cost_str: str | None) -> int | None:
    """Parse '5min' -> 5, '0min' -> 0, None -> None."""
    if not cost_str:
        return None
    m = re.match(r"(\d+)min", cost_str)
    return int(m.group(1)) if m else None

def main():
    if not RSPEC_DIR.exists():
        print(f"rspec directory not found: {RSPEC_DIR}")
        raise SystemExit(1)

    entries = []
    for json_file in sorted(RSPEC_DIR.glob("S*.json")):
        rule_id = json_file.stem  # "S1066"

        with open(json_file) as f:
            meta = json.load(f)

        sonar_type = meta.get("type", "CODE_SMELL")
        analysis_type = SONAR_TYPE_TO_ANALYSIS_TYPE.get(sonar_type, "code_smell")
        deprecated = meta.get("status", "ready") == "deprecated"

        severity_raw = meta.get("defaultSeverity", "Major")
        severity = SEVERITY_MAP.get(severity_raw, "suggestion")
        if sonar_type == "BUG" and severity == "suggestion":
            severity = "warning"

        applies_to = SCOPE_MAP.get(meta.get("scope", "All"), "all")
        fix_type = QUICKFIX_TO_FIX_TYPE.get(meta.get("quickfix", "unknown"), "none")

        security_standards = meta.get("securityStandards", {})
        tags = meta.get("tags", [])
        remediation_minutes = parse_remediation_minutes(
            meta.get("remediation", {}).get("constantCost")
        )

        enabled = not deprecated

        rule_files = list(RULES_DIR.glob(f"*{rule_id}*.cs"))
        rule_file = rule_files[0] if rule_files else None
        introduced, modified_at = get_git_dates(rule_file) if rule_file else (None, None)

        entries.append({
            "id": rule_id,
            "title": meta.get("title", ""),
            "package": "sonar",
            "category": sonar_type,
            "analysis_type": analysis_type,
            "sonar_type": sonar_type,
            "default_severity": severity,
            "enabled_by_default": enabled,
            "deprecated": deprecated,
            "fix_type": fix_type,
            "configurable": False,
            "requires_option": None,
            "scope": None,
            "applies_to": applies_to,
            "supersedes": None,
            "related": [],
            "tags": tags,
            "security_standards": security_standards,
            "remediation_minutes": remediation_minutes,
            "open_issues": None,
            "source_repo": "SonarSource/sonar-dotnet",
            "docs_url": f"https://rules.sonarsource.com/csharp/{rule_id}",
            "introduced": introduced,
            "modified_at": modified_at,
            "research_at": "2026-03",
            "notes": meta.get("title", ""),
        })

    catalog_dir = Path("docs/research/analyzer-catalog")
    out = catalog_dir / "sonar.yaml"
    with open(out, "w") as f:
        yaml.dump(entries, f, allow_unicode=True, sort_keys=False, default_flow_style=False)
    print(f"sonar.yaml: {len(entries)} rules written")

    # Quick breakdown
    from collections import Counter
    types = Counter(e["sonar_type"] for e in entries)
    fix_types = Counter(e["fix_type"] for e in entries)
    deprecated_count = sum(1 for e in entries if e["deprecated"])
    print(f"  types:      {dict(sorted(types.items()))}")
    print(f"  fix_type:   {dict(sorted(fix_types.items()))}")
    print(f"  deprecated: {deprecated_count}")

if __name__ == "__main__":
    main()
