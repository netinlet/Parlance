#!/usr/bin/env python3
"""
Determines fix_type for rules that don't already have it set,
by grepping cloned source repos for CodeFixProvider classes referencing each rule ID.
"""

import re
import subprocess
import yaml
from pathlib import Path

REPO_DIRS = {
    "ca":               Path("analyzers_research/roslyn-analyzers"),
    "ide":              Path("analyzers_research/roslyn"),
    "rcs":              Path("analyzers_research/roslynator"),
    "stylecop":         Path("analyzers_research/StyleCopAnalyzers"),
    "meziantou":        Path("analyzers_research/Meziantou.Analyzer"),
    "scs":              None,  # no code fixes in SCS
    "asyncfixer":       Path("analyzers_research/AsyncFixer"),
    "idisp":            Path("analyzers_research/IDisposableAnalyzers"),
    "xunit":            Path("analyzers_research/xunit.analyzers"),
    "nunit":            Path("analyzers_research/nunit.analyzers"),
    "fluentassertions": Path("analyzers_research/fluentassertions.analyzers"),
    "nsubstitute":      Path("analyzers_research/NSubstitute.Analyzers"),
    "errorprone":       Path("analyzers_research/ErrorProne.NET"),
    "sharpsource":      Path("analyzers_research/SharpSource"),
    "disposablefixer":  Path("analyzers_research/DisposableFixer"),
    "vsthrd":           Path("analyzers_research/vs-threading"),
    "csharpguidelines": Path("analyzers_research/CSharpGuidelinesAnalyzer"),
    "exhaustive":       Path("analyzers_research/ExhaustiveMatching"),
    "refl":             Path("analyzers_research/ReflectionAnalyzers"),
    "aspnetcore":       Path("analyzers_research/AspNetCoreAnalyzers"),
    "hyperlinq":        Path("analyzers_research/NetFabric.Hyperlinq.Analyzer"),
}

# sonar fix_type is already set from rspec quickfix field — skip enrichment
SKIP_PACKAGES = {"sonar"}

_ID_PATTERN = re.compile(r'"([A-Z][A-Za-z]*\d{3,5})"')


def rule_ids_with_fixes(repo_dir: Path) -> set[str]:
    """Find all rule IDs referenced inside CodeFixProvider source files."""
    try:
        result = subprocess.check_output(
            ["grep", "-r", "--include=*.cs", "-l", "CodeFixProvider", str(repo_dir)],
            text=True, stderr=subprocess.DEVNULL,
        )
        fix_files = result.strip().splitlines()
    except subprocess.CalledProcessError:
        return set()

    ids: set[str] = set()
    for f in fix_files:
        try:
            content = Path(f).read_text(errors="ignore")
            ids.update(_ID_PATTERN.findall(content))
        except Exception:
            pass
    return ids


def enrich(catalog_file: Path, fixable_ids: set[str] | None):
    entries = yaml.safe_load(catalog_file.read_text()) or []
    changed = 0
    for e in entries:
        if e.get("fix_type") is not None:
            continue
        if fixable_ids is None:
            e["fix_type"] = "none"
        else:
            e["fix_type"] = "auto" if e["id"] in fixable_ids else "none"
        changed += 1
    with open(catalog_file, "w") as f:
        yaml.dump(entries, f, allow_unicode=True, sort_keys=False, default_flow_style=False)
    auto = sum(1 for e in entries if e.get("fix_type") == "auto")
    print(f"  {catalog_file.name}: {auto}/{len(entries)} auto-fixable ({changed} updated)")


def main():
    catalog_dir = Path("docs/research/analyzer-catalog")
    for package, repo_dir in REPO_DIRS.items():
        if package in SKIP_PACKAGES:
            continue
        catalog_file = catalog_dir / f"{package}.yaml"
        if not catalog_file.exists():
            continue
        if repo_dir is None or not repo_dir.exists():
            # No code fixes or repo not cloned — mark all as none
            enrich(catalog_file, None)
            continue
        print(f"Scanning {repo_dir.name}...")
        fixable = rule_ids_with_fixes(repo_dir)
        enrich(catalog_file, fixable)


if __name__ == "__main__":
    main()
