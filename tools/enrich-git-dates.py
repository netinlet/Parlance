#!/usr/bin/env python3
"""
Enriches introduced and modified_at for each rule by inspecting git history
in the cloned source repos.

- modified_at: date of the most recent commit touching the file that defines
  the rule (format: YYYY-MM).
- introduced: date of the first commit that added the rule (format: YYYY-MM).
  Requires full git history. With shallow clones only the latest commit is
  available, so introduced is left null unless the repo has sufficient depth.

Run from the repo root. Repos must be cloned under analyzers_research/.
"""

import re
import subprocess
import yaml
from pathlib import Path

CATALOG_DIR = Path("docs/research/analyzer-catalog")

REPO_DIRS = {
    "ca":               Path("analyzers_research/roslyn-analyzers"),
    "ide":              Path("analyzers_research/roslyn"),
    "rcs":              Path("analyzers_research/roslynator"),
    "sonar":            Path("analyzers_research/sonar-dotnet"),
    "stylecop":         Path("analyzers_research/StyleCopAnalyzers"),
    "meziantou":        Path("analyzers_research/Meziantou.Analyzer"),
    "scs":              Path("analyzers_research/security-code-scan"),
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


def git_log_dates(repo: Path, file_path: str) -> tuple[str | None, str | None]:
    """
    Returns (first_date, last_date) in YYYY-MM format for a file.
    With shallow clones, first_date == last_date (single commit).
    Returns (None, None) if the file has no git history.
    """
    try:
        out = subprocess.check_output(
            ["git", "-C", str(repo), "log", "--follow", "--format=%aI", "--", file_path],
            text=True, stderr=subprocess.DEVNULL,
        ).strip()
    except subprocess.CalledProcessError:
        return None, None

    dates = [line[:7] for line in out.splitlines() if line.strip()]  # YYYY-MM
    if not dates:
        return None, None
    return dates[-1], dates[0]  # oldest first, newest last


def find_defining_files(repo: Path, rule_id: str) -> list[str]:
    """
    Find source files in the repo that mention the rule ID as a string literal
    or constant. Returns paths relative to the repo root.
    """
    try:
        result = subprocess.check_output(
            ["grep", "-rl", "--include=*.cs", "--include=*.yaml", "--include=*.yml",
             rule_id, str(repo)],
            text=True, stderr=subprocess.DEVNULL,
        )
        abs_paths = result.strip().splitlines()
        # Return paths relative to repo root
        return [str(Path(p).relative_to(repo)) for p in abs_paths]
    except subprocess.CalledProcessError:
        return []


def best_defining_file(files: list[str], rule_id: str) -> str | None:
    """
    Heuristically pick the most relevant file for a rule ID.
    Prefers files whose name contains the rule ID or 'DiagnosticDescriptor',
    and avoids test files.
    """
    if not files:
        return None

    rid_lower = rule_id.lower()

    def score(path: str) -> int:
        p = path.lower()
        s = 0
        if "test" in p or "spec" in p:
            s -= 100
        if rid_lower in p:
            s += 50
        if "diagnostic" in p or "analyzer" in p or "rule" in p:
            s += 10
        if p.endswith(".cs"):
            s += 5
        return s

    return max(files, key=score)


def enrich(catalog_file: Path, repo: Path):
    entries = yaml.safe_load(catalog_file.read_text()) or []
    changed = 0
    skipped = 0

    for e in entries:
        if e.get("introduced") is not None and e.get("modified_at") is not None:
            continue  # already set

        rule_id = e["id"]
        files = find_defining_files(repo, rule_id)
        target = best_defining_file(files, rule_id)
        if target is None:
            skipped += 1
            continue

        first, last = git_log_dates(repo, target)
        if last is not None and e.get("modified_at") is None:
            e["modified_at"] = last
            changed += 1
        if first is not None and e.get("introduced") is None:
            # Only set introduced if we have more than one commit (not a shallow clone)
            # Check by seeing if first != last (i.e., history goes beyond the tip)
            if first != last:
                e["introduced"] = first
                changed += 1

    with open(catalog_file, "w") as f:
        yaml.dump(entries, f, allow_unicode=True, sort_keys=False, default_flow_style=False)

    has_modified = sum(1 for e in entries if e.get("modified_at") is not None)
    has_introduced = sum(1 for e in entries if e.get("introduced") is not None)
    print(f"  {catalog_file.name}: modified_at={has_modified}/{len(entries)}, "
          f"introduced={has_introduced}/{len(entries)}, "
          f"changed={changed}, skipped={skipped}")


def main():
    for package, repo in REPO_DIRS.items():
        catalog_file = CATALOG_DIR / f"{package}.yaml"
        if not catalog_file.exists():
            print(f"  {package}: catalog file not found, skipping")
            continue
        if not repo.exists():
            print(f"  {package}: repo {repo} not cloned, skipping")
            continue
        print(f"Processing {package} ({repo.name})...")
        enrich(catalog_file, repo)


if __name__ == "__main__":
    main()
