#!/usr/bin/env python3
"""Validates analyzer catalog YAML files against the schema."""

import sys
import yaml
from pathlib import Path

REQUIRED_FIELDS = [
    "id", "title", "package", "category", "analysis_type",
    "sonar_type", "default_severity", "enabled_by_default", "deprecated",
    "fix_type", "configurable", "requires_option",
    "scope", "applies_to", "supersedes", "related",
    "tags", "security_standards", "remediation_minutes",
    "open_issues", "source_repo", "docs_url", "introduced",
    "modified_at", "research_at", "notes",
]

ENUMS = {
    "package": {
        "ca", "ide", "rcs", "sonar", "stylecop", "meziantou", "scs",
        "asyncfixer", "idisp", "xunit", "nunit", "fluentassertions",
        "nsubstitute", "errorprone", "sharpsource", "disposablefixer",
        "vsthrd", "csharpguidelines", "exhaustive", "refl", "aspnetcore", "hyperlinq",
    },
    "analysis_type": {"bug", "code_smell", "vulnerability", "security_hotspot"},
    "default_severity": {"error", "warning", "suggestion", "silent", "none"},
    "fix_type": {"auto", "manual", "none"},
    "scope": {"expression", "member", "type", "file", "project"},
    "applies_to": {"production", "tests", "all"},
}

# Fields that are allowed to be None/null
NULLABLE_FIELDS = {
    "sonar_type", "fix_type", "configurable", "requires_option",
    "scope", "supersedes", "remediation_minutes", "open_issues",
    "introduced", "modified_at", "analysis_type",
}


def validate_file(path: Path) -> list[str]:
    errors = []
    with open(path) as f:
        entries = yaml.safe_load(f)
    if not entries:
        return errors
    for i, entry in enumerate(entries):
        loc = f"{path.name}[{i}] {entry.get('id', '?')}"
        for field in REQUIRED_FIELDS:
            if field not in entry:
                errors.append(f"{loc}: missing field '{field}'")
        for field, valid in ENUMS.items():
            val = entry.get(field)
            if val is not None and val not in valid:
                errors.append(f"{loc}: '{field}' value {val!r} not in {sorted(valid)}")
        if not isinstance(entry.get("related", []), list):
            errors.append(f"{loc}: 'related' must be a list")
        if not isinstance(entry.get("tags", []), list):
            errors.append(f"{loc}: 'tags' must be a list")
        if not isinstance(entry.get("security_standards", {}), dict):
            errors.append(f"{loc}: 'security_standards' must be a dict")
    return errors


def main():
    catalog_dir = Path(__file__).parent.parent / "docs" / "research" / "analyzer-catalog"
    files = sorted(catalog_dir.glob("*.yaml"))
    if not files:
        print("No catalog files found.")
        sys.exit(1)
    all_errors = []
    total = 0
    for f in files:
        errors = validate_file(f)
        all_errors.extend(errors)
        count = len(yaml.safe_load(f.read_text()) or [])
        total += count
        status = "OK" if not errors else f"{len(errors)} error(s)"
        print(f"  {f.name}: {count} rules — {status}")
    print(f"\nTotal: {total} rules across {len(files)} files")
    if all_errors:
        print("\nErrors:")
        for e in all_errors:
            print(f"  {e}")
        sys.exit(1)
    else:
        print("All catalog files valid.")


if __name__ == "__main__":
    main()
