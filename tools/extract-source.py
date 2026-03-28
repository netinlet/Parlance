#!/usr/bin/env python3
"""
Extracts rule metadata from cloned source repos for the 18 non-manifest packages.
Produces one YAML catalog file per package under docs/research/analyzer-catalog/.

Run from the repo root:
    python3 tools/extract-source.py [package ...]
    python3 tools/extract-source.py           # all packages
    python3 tools/extract-source.py xunit nunit idisp
"""

import re
import sys
import yaml
import xml.etree.ElementTree as ET
from pathlib import Path
from collections import defaultdict

RESEARCH_AT = "2026-03"
CATALOG_DIR = Path("docs/research/analyzer-catalog")
REPOS = Path("analyzers_research")

SEVERITY_MAP = {
    "Error": "error",
    "Warning": "warning",
    "Info": "suggestion",
    "Hidden": "silent",
}


# ── helpers ─────────────────────────────────────────────────────────────────

def parse_resx(path: Path) -> dict[str, str]:
    """Load a .resx file; return {name: value}."""
    result: dict[str, str] = {}
    try:
        tree = ET.parse(path)
        for data in tree.findall("data"):
            name = data.get("name", "")
            val_el = data.find("value")
            if val_el is not None and val_el.text:
                result[name] = val_el.text.strip()
    except Exception:
        pass
    return result


def load_const_map(path: Path, pattern: re.Pattern) -> dict[str, str]:
    """Extract {const_name: string_value} from a C# constants file."""
    result: dict[str, str] = {}
    try:
        text = path.read_text(errors="ignore")
        for m in pattern.finditer(text):
            result[m.group(1)] = m.group(2)
    except Exception:
        pass
    return result


def parse_severity(raw: str) -> str:
    for key, val in SEVERITY_MAP.items():
        if key in raw:
            return val
    return "suggestion"


def parse_enabled(raw: str) -> bool:
    return "false" not in raw.lower()


def catalog_entry(
    package: str,
    rule_id: str,
    title: str,
    category: str,
    severity: str,
    enabled: bool,
    source_repo: str,
    docs_url: str,
    notes: str = "",
) -> dict:
    return {
        "id": rule_id,
        "title": title,
        "package": package,
        "category": category,
        "analysis_type": None,
        "sonar_type": None,
        "default_severity": severity,
        "enabled_by_default": enabled,
        "deprecated": False,
        "fix_type": None,
        "configurable": None,
        "requires_option": None,
        "scope": None,
        "applies_to": "all",
        "supersedes": None,
        "related": [],
        "tags": [],
        "security_standards": {},
        "remediation_minutes": None,
        "open_issues": None,
        "source_repo": source_repo,
        "docs_url": docs_url,
        "introduced": None,
        "modified_at": None,
        "research_at": RESEARCH_AT,
        "notes": notes or title,
    }


# ── Named-param Descriptors.cs extractor ───────────────────────────────────

_NAMED_DESCRIPTOR_RE = re.compile(
    r'id:\s*"([^"]+)"'
    r'.*?'
    r'title:\s*"([^"]+)"'
    r'.*?'
    r'(?:category:\s*(\w+(?:\.\w+)*|"[^"]+"))?'
    r'.*?'
    r'defaultSeverity:\s*DiagnosticSeverity\.(\w+)'
    r'.*?'
    r'isEnabledByDefault:\s*(true|false)',
    re.DOTALL,
)

_CAT_STRING_RE = re.compile(r'"([^"]+)"')


def extract_named_param_descriptors(text: str) -> list[tuple]:
    """Returns list of (id, title, category, severity, enabled)."""
    results = []
    for m in _NAMED_DESCRIPTOR_RE.finditer(text):
        rule_id = m.group(1)
        title = m.group(2)
        cat_raw = m.group(3) or ""
        cat_m = _CAT_STRING_RE.match(cat_raw)
        category = cat_m.group(1) if cat_m else cat_raw.split(".")[-1]
        severity = SEVERITY_MAP.get(m.group(4), "suggestion")
        enabled = m.group(5) == "true"
        results.append((rule_id, title, category, severity, enabled))
    return results


# ── Packages ────────────────────────────────────────────────────────────────

def extract_idisp() -> list[dict]:
    """IDisposableAnalyzers — central Descriptors.cs with named params."""
    repo = REPOS / "IDisposableAnalyzers"
    text = (repo / "IDisposableAnalyzers" / "Descriptors.cs").read_text(errors="ignore")
    entries = []
    for rule_id, title, category, severity, enabled in extract_named_param_descriptors(text):
        entries.append(catalog_entry(
            "idisp", rule_id, title, category, severity, enabled,
            "DotNetAnalyzers/IDisposableAnalyzers",
            f"https://github.com/DotNetAnalyzers/IDisposableAnalyzers/blob/master/documentation/{rule_id}.md",
        ))
    return entries


def extract_aspnetcore() -> list[dict]:
    """AspNetCoreAnalyzers — central Descriptors.cs with named params."""
    repo = REPOS / "AspNetCoreAnalyzers"
    text = (repo / "AspNetCoreAnalyzers" / "Descriptors.cs").read_text(errors="ignore")
    entries = []
    for rule_id, title, category, severity, enabled in extract_named_param_descriptors(text):
        entries.append(catalog_entry(
            "aspnetcore", rule_id, title, category, severity, enabled,
            "DotNetAnalyzers/AspNetCoreAnalyzers",
            f"https://github.com/DotNetAnalyzers/AspNetCoreAnalyzers/blob/master/documentation/{rule_id}.md",
        ))
    return entries


def extract_refl() -> list[dict]:
    """ReflectionAnalyzers — central Descriptors.cs with named params."""
    repo = REPOS / "ReflectionAnalyzers"
    text = (repo / "ReflectionAnalyzers" / "Descriptors.cs").read_text(errors="ignore")
    entries = []
    for rule_id, title, category, severity, enabled in extract_named_param_descriptors(text):
        entries.append(catalog_entry(
            "refl", rule_id, title, category, severity, enabled,
            "DotNetAnalyzers/ReflectionAnalyzers",
            f"https://github.com/DotNetAnalyzers/ReflectionAnalyzers/blob/master/documentation/{rule_id}.md",
        ))
    return entries


def extract_errorprone() -> list[dict]:
    """ErrorProne.NET — DiagnosticDescriptors.cs; ID is nameof(EPC11) pattern."""
    repo = REPOS / "ErrorProne.NET"
    desc_file = repo / "src" / "ErrorProne.NET.CoreAnalyzers" / "DiagnosticDescriptors.cs"
    text = desc_file.read_text(errors="ignore")

    # Pattern: static readonly DiagnosticDescriptor EPC11 = new DiagnosticDescriptor(
    #              nameof(EPC11), title: "...", ..., DiagnosticSeverity.Warning, ...
    pat = re.compile(
        r'DiagnosticDescriptor\s+(\w+)\s*=\s*new\s+DiagnosticDescriptor\s*\(\s*'
        r'nameof\(\w+\)'    # first positional arg = nameof
        r'.*?'
        r'title:\s*"([^"]+)"'
        r'.*?'
        r'(?:category:\s*(\w+))?'
        r'.*?'
        r'DiagnosticSeverity\.(\w+)'
        r'.*?'
        r'isEnabledByDefault:\s*(true|false)',
        re.DOTALL,
    )
    # Also handle positional (no `title:` label) — second string arg
    pat2 = re.compile(
        r'DiagnosticDescriptor\s+(\w+)\s*=\s*new\s+DiagnosticDescriptor\s*\(\s*'
        r'nameof\(\w+\)\s*,\s*'
        r'title:\s*"([^"]+)"',
        re.DOTALL,
    )

    entries: dict[str, dict] = {}
    for m in pat.finditer(text):
        var_name = m.group(1)
        rule_id = var_name  # nameof resolves to the variable name which IS the rule ID
        title = m.group(2)
        category = m.group(3) or "ErrorProne"
        severity = SEVERITY_MAP.get(m.group(4), "suggestion")
        enabled = m.group(5) == "true"
        if rule_id not in entries:
            entries[rule_id] = catalog_entry(
                "errorprone", rule_id, title, category, severity, enabled,
                "SergeyTeplyakov/ErrorProne.NET",
                f"https://github.com/SergeyTeplyakov/ErrorProne.NET/blob/master/docs/{rule_id}.md",
            )

    # Also check for ERP prefix rules (different file)
    erp_file = repo / "src" / "ErrorProne.NET.CoreAnalyzers" / "ExceptionAnalyzers" / "ExceptionHandlingHelpers.cs"
    erp_files = list((repo / "src").rglob("DiagnosticDescriptors*.cs"))
    for ef in erp_files:
        ef_text = ef.read_text(errors="ignore")
        for m in pat.finditer(ef_text):
            var_name = m.group(1)
            rule_id = var_name
            if rule_id not in entries and (rule_id.startswith("EPC") or rule_id.startswith("ERP")):
                title = m.group(2)
                category = m.group(3) or "ErrorProne"
                severity = SEVERITY_MAP.get(m.group(4), "suggestion")
                enabled = m.group(5) == "true"
                entries[rule_id] = catalog_entry(
                    "errorprone", rule_id, title, category, severity, enabled,
                    "SergeyTeplyakov/ErrorProne.NET",
                    f"https://github.com/SergeyTeplyakov/ErrorProne.NET/blob/master/docs/{rule_id}.md",
                )

    return list(entries.values())


def extract_xunit() -> list[dict]:
    """xunit.analyzers — Utility/Descriptors.xUnit*.cs with Diagnostic(id, title, ...) helper."""
    repo = REPOS / "xunit.analyzers"
    desc_dir = repo / "src" / "xunit.analyzers" / "Utility"

    # Pattern: Diagnostic("xUnit2000", "Title", Category, Severity, "message")
    pat = re.compile(
        r'Diagnostic\s*\(\s*"(xUnit\d+)"\s*,\s*"([^"]+)"\s*,\s*(\w+)\s*,\s*(\w+)',
        re.DOTALL,
    )
    # Category enum values
    cat_map = {
        "Assertions": "Assertions",
        "Usage": "Usage",
        "Extensibility": "Extensibility",
    }

    entries: dict[str, dict] = {}
    for cs_file in sorted(desc_dir.glob("Descriptors.*.cs")):
        text = cs_file.read_text(errors="ignore")
        for m in pat.finditer(text):
            rule_id = m.group(1)
            title = m.group(2)
            category = cat_map.get(m.group(3), m.group(3))
            severity = SEVERITY_MAP.get(m.group(4), "suggestion")
            if rule_id not in entries:
                entries[rule_id] = catalog_entry(
                    "xunit", rule_id, title, category, severity, True,
                    "xunit/xunit.analyzers",
                    f"https://xunit.net/xunit.analyzers/rules/{rule_id}",
                )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_nunit() -> list[dict]:
    """nunit.analyzers — per-file DiagnosticDescriptorCreator.Create with named params.
    IDs from AnalyzerIdentifiers.cs; titles from per-analyzer *Constants.cs files."""
    repo = REPOS / "nunit.analyzers"
    src_dir = repo / "src" / "nunit.analyzers"

    # Load identifier map: const_name → "NUnit1001"
    ident_map = load_const_map(
        src_dir / "Constants" / "AnalyzerIdentifiers.cs",
        re.compile(r'const\s+string\s+(\w+)\s*=\s*"([^"]+)"'),
    )
    # Load all string constants from *Constants.cs files
    all_consts: dict[str, str] = {}
    for cf in src_dir.rglob("*Constants.cs"):
        all_consts.update(load_const_map(cf, re.compile(r'const\s+string\s+(\w+)\s*=\s*"([^"]+)"')))

    # Per-file: find each DiagnosticDescriptorCreator.Create block and extract fields independently
    pat_create = re.compile(r'DiagnosticDescriptorCreator\.Create\s*\(')
    pat_id = re.compile(r'id:\s*AnalyzerIdentifiers\.(\w+)')
    pat_title = re.compile(r'title:\s*(?:"([^"]+)"|(?:\w+)\.(\w+))')
    pat_sev = re.compile(r'defaultSeverity:\s*DiagnosticSeverity\.(\w+)')
    pat_enabled = re.compile(r'isEnabledByDefault:\s*(true|false)')
    pat_cat = re.compile(r'category:\s*(?:Categories\.(\w+)|"([^"]+)")')

    entries: dict[str, dict] = {}
    for cs_file in sorted(src_dir.rglob("*.cs")):
        if "Test" in str(cs_file) or "Constants" in cs_file.name:
            continue
        text = cs_file.read_text(errors="ignore")
        # Find each Create( block by scanning forward from each match
        for start_m in pat_create.finditer(text):
            start = start_m.start()
            # Grab up to 1000 chars — enough for a descriptor call
            chunk = text[start:start + 1000]
            id_m = pat_id.search(chunk)
            if not id_m:
                continue
            rule_id = ident_map.get(id_m.group(1), id_m.group(1))
            if not rule_id or not rule_id.startswith("NUnit"):
                continue
            title_m = pat_title.search(chunk)
            title = (title_m.group(1) if title_m and title_m.group(1)
                     else all_consts.get(title_m.group(2) if title_m else "", rule_id) if title_m
                     else rule_id)
            sev_m = pat_sev.search(chunk)
            severity = SEVERITY_MAP.get(sev_m.group(1), "warning") if sev_m else "warning"
            en_m = pat_enabled.search(chunk)
            enabled = en_m.group(1) == "true" if en_m else True
            cat_m = pat_cat.search(chunk)
            category = (cat_m.group(1) or cat_m.group(2) or "NUnit") if cat_m else "NUnit"
            if rule_id not in entries:
                entries[rule_id] = catalog_entry(
                    "nunit", rule_id, title, category, severity, enabled,
                    "nunit/nunit.analyzers",
                    f"https://github.com/nunit/nunit.analyzers/blob/master/documentation/analyzers/{rule_id}.md",
                )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_fluentassertions() -> list[dict]:
    """FluentAssertions.Analyzers — Tips/*.cs; IDs inline or in DiagnosticId const."""
    repo = REPOS / "fluentassertions.analyzers"
    src_dir = repo / "src" / "FluentAssertions.Analyzers"

    entries: dict[str, dict] = {}

    # Pattern 1: inline ID new("FAA0002", title: "...", ..., DiagnosticSeverity.X, isEnabledByDefault: bool)
    pat1 = re.compile(
        r'new\s*(?:DiagnosticDescriptor)?\s*\(\s*"(FA[A-Z]?\d+)"\s*,'
        r'.*?title:\s*"([^"]+)"'
        r'.*?DiagnosticSeverity\.(\w+)'
        r'.*?isEnabledByDefault:\s*(true|false)',
        re.DOTALL,
    )
    # Pattern 2: per-file const DiagnosticId + const Title + new DiagnosticDescriptor(DiagnosticId, Title, ...)
    pat_id = re.compile(r'const\s+string\s+DiagnosticId\s*=\s*"(FA[A-Z]?\d+)"')
    pat_title = re.compile(r'const\s+string\s+Title\s*=\s*"([^"]+)"')
    pat_sev = re.compile(r'DiagnosticSeverity\.(\w+)')

    for cs_file in sorted(src_dir.rglob("*.cs")):
        text = cs_file.read_text(errors="ignore")
        for m in pat1.finditer(text):
            rule_id = m.group(1)
            if rule_id not in entries:
                entries[rule_id] = catalog_entry(
                    "fluentassertions", rule_id, m.group(2), "FluentAssertions",
                    SEVERITY_MAP.get(m.group(3), "suggestion"), m.group(4) == "true",
                    "fluentassertions/fluentassertions.analyzers",
                    f"https://github.com/fluentassertions/fluentassertions.analyzers/blob/master/docs/tips.md#{rule_id.lower()}",
                )
        # Pattern 2 — per-file DiagnosticId const
        id_m = pat_id.search(text)
        if id_m and id_m.group(1) not in entries:
            rule_id = id_m.group(1)
            title_m = pat_title.search(text)
            title = title_m.group(1) if title_m else rule_id
            sev_m = pat_sev.search(text)
            severity = SEVERITY_MAP.get(sev_m.group(1), "suggestion") if sev_m else "suggestion"
            entries[rule_id] = catalog_entry(
                "fluentassertions", rule_id, title, "FluentAssertions", severity, True,
                "fluentassertions/fluentassertions.analyzers",
                f"https://github.com/fluentassertions/fluentassertions.analyzers/blob/master/docs/tips.md#{rule_id.lower()}",
            )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_nsubstitute() -> list[dict]:
    """NSubstitute.Analyzers — DiagnosticDescriptors.cs with constant IDs + resource titles."""
    repo = REPOS / "NSubstitute.Analyzers"
    # Load identifier map: const_name → "NS1000"
    ident_file = repo / "src" / "NSubstitute.Analyzers.Shared" / "DiagnosticIdentifiers.cs"
    ident_map = load_const_map(
        ident_file,
        re.compile(r'const\s+string\s+(\w+)\s*=\s*"([^"]+)"'),
    )

    desc_file = repo / "src" / "NSubstitute.Analyzers.Shared" / "DiagnosticDescriptors.cs"
    text = desc_file.read_text(errors="ignore")

    # Pattern: CreateDiagnosticDescriptor(name: nameof(X), id: DiagnosticIdentifiers.Y, category: "...", defaultSeverity: ..., isEnabledByDefault: ...)
    pat = re.compile(
        r'CreateDiagnosticDescriptor\s*\('
        r'.*?'
        r'id:\s*DiagnosticIdentifiers\.(\w+)'
        r'.*?'
        r'category:\s*(\w+(?:\.\w+)*(?:\.GetDisplayName\(\))?)'
        r'.*?'
        r'defaultSeverity:\s*DiagnosticSeverity\.(\w+)'
        r'.*?'
        r'isEnabledByDefault:\s*(true|false)',
        re.DOTALL,
    )
    # property name pattern from nameof
    name_pat = re.compile(
        r'public\s+static\s+DiagnosticDescriptor\s+(\w+)\s*\{',
    )

    entries: dict[str, dict] = {}
    # match property name + descriptor together
    property_name_map = {}
    for m in name_pat.finditer(text):
        property_name_map[m.start()] = m.group(1)

    for m in pat.finditer(text):
        const_name = m.group(1)
        rule_id = ident_map.get(const_name, const_name)
        if not rule_id or not rule_id.startswith("NS"):
            continue
        category = m.group(2).split(".")[-1].replace("GetDisplayName()", "").strip()
        severity = SEVERITY_MAP.get(m.group(3), "suggestion")
        enabled = m.group(4) == "true"
        # Title: use property name as fallback (it's descriptive)
        prop_name = const_name  # best we can do without resource resolution
        title = " ".join(re.sub(r'([A-Z])', r' \1', prop_name).split())
        if rule_id not in entries:
            entries[rule_id] = catalog_entry(
                "nsubstitute", rule_id, title, category, severity, enabled,
                "nsubstitute/NSubstitute.Analyzers",
                f"https://github.com/nsubstitute/NSubstitute.Analyzers/blob/master/documentation/rules/{rule_id}.md",
            )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_scs() -> list[dict]:
    """SecurityCodeScan — Config/Messages.yml has all rules with titles and CWE."""
    import yaml as _yaml  # local to avoid name collision at module level
    repo = REPOS / "security-code-scan"
    messages_file = repo / "SecurityCodeScan" / "Config" / "Messages.yml"
    with open(messages_file) as f:
        messages = _yaml.safe_load(f)

    # Also get list of actual enabled rules from C# files
    rule_files = list((repo / "SecurityCodeScan" / "Analyzers").rglob("*.cs"))

    entries = []
    for rule_id, meta in messages.items():
        if not str(rule_id).startswith("SCS"):
            continue
        title_raw = meta.get("title", str(rule_id))
        # Titles with taint placeholders — trim to first sentence
        title = re.split(r"\s+where\s+", title_raw, maxsplit=1)[0].strip()
        if len(title) > 120:
            title = title[:120].rstrip() + "…"
        description = meta.get("description", "")
        cwe = meta.get("cwe")
        security_standards = {}
        if cwe:
            security_standards["CWE"] = [f"CWE-{cwe}"]

        entries.append({
            **catalog_entry(
                "scs", str(rule_id), title, "Security", "warning", True,
                "security-code-scan/security-code-scan",
                f"https://security-code-scan.github.io/#{rule_id}",
                notes=description or title,
            ),
            "security_standards": security_standards,
            "analysis_type": "vulnerability",  # all SCS rules are security
        })

    return sorted(entries, key=lambda e: e["id"])


def extract_asyncfixer() -> list[dict]:
    """AsyncFixer — DiagnosticIds.cs + per-file Title constants."""
    repo = REPOS / "AsyncFixer"
    # Load const map
    ids_file = repo / "AsyncFixer" / "DiagnosticIds.cs"
    id_map = load_const_map(
        ids_file,
        re.compile(r'const\s+string\s+(\w+)\s*=\s*"([^"]+)"'),
    )
    # Reverse: "AsyncFixer01" → const name for lookup
    reverse_map = {v: k for k, v in id_map.items()}

    # Per-file pattern: new DiagnosticDescriptor(DiagnosticIds.UnnecessaryAsync, Title, ...)
    # Title is a const: private const string Title = "Unnecessary async/await usage";
    pat_desc = re.compile(
        r'new\s+DiagnosticDescriptor\s*\(\s*DiagnosticIds\.(\w+)',
        re.DOTALL,
    )
    pat_title = re.compile(r'(?:private|public)\s+(?:static\s+)?const\s+string\s+Title\s*=\s*"([^"]+)"')
    pat_severity = re.compile(r'DiagnosticSeverity\.(\w+)')
    pat_enabled = re.compile(r'isEnabledByDefault:\s*(true|false)')

    entries: dict[str, dict] = {}
    src_dir = repo / "AsyncFixer"
    for cs_file in sorted(src_dir.rglob("*Analyzer.cs")):
        if "Test" in str(cs_file):
            continue
        text = cs_file.read_text(errors="ignore")
        dm = pat_desc.search(text)
        if not dm:
            continue
        const_name = dm.group(1)
        rule_id = id_map.get(const_name, const_name)
        title_m = pat_title.search(text)
        title = title_m.group(1) if title_m else rule_id
        sev_m = pat_severity.search(text)
        severity = SEVERITY_MAP.get(sev_m.group(1), "warning") if sev_m else "warning"
        en_m = pat_enabled.search(text)
        enabled = en_m.group(1) == "true" if en_m else True
        if rule_id not in entries:
            entries[rule_id] = catalog_entry(
                "asyncfixer", rule_id, title, "Async", severity, enabled,
                "semihokur/AsyncFixer",
                f"https://github.com/semihokur/AsyncFixer#{rule_id.lower()}",
            )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_stylecop() -> list[dict]:
    """StyleCop.Analyzers — per-file with DiagnosticId const + resx titles."""
    repo = REPOS / "StyleCopAnalyzers"
    rules_root = repo / "StyleCop.Analyzers" / "StyleCop.Analyzers"

    # Load all resx files into merged dict
    all_resources: dict[str, str] = {}
    for resx in rules_root.rglob("*.resx"):
        if "." in resx.stem and resx.stem.split(".")[-1] in ("pl-PL", "de-DE", "zh-TW", "zh-CN"):
            continue
        all_resources.update(parse_resx(resx))

    pat_id = re.compile(r'public\s+const\s+string\s+DiagnosticId\s*=\s*"([^"]+)"')
    pat_severity = re.compile(r'DiagnosticSeverity\.(\w+)')
    pat_enabled = re.compile(r'AnalyzerConstants\.(EnabledByDefault|DisabledByDefault)')
    # Category from AnalyzerCategory.XxxRules reference
    pat_category = re.compile(r'AnalyzerCategory\.(\w+)')

    entries: dict[str, dict] = {}
    for cs_file in sorted(rules_root.rglob("*.cs")):
        if "Test" in str(cs_file) or "Resources" in cs_file.name or "Helper" in cs_file.name:
            continue
        text = cs_file.read_text(errors="ignore")
        id_m = pat_id.search(text)
        if not id_m:
            continue
        rule_id = id_m.group(1)
        if not rule_id.startswith("SA") and not rule_id.startswith("SX"):
            continue

        # Title from resx
        title_key = f"{rule_id}Title"
        title = all_resources.get(title_key, rule_id)

        # Category from AnalyzerCategory.XxxRules
        cat_m = pat_category.search(text)
        category = cat_m.group(1) if cat_m else "StyleCop"

        sev_matches = pat_severity.findall(text)
        # Filter to just the DiagnosticDescriptor ctor arguments
        severity = SEVERITY_MAP.get(sev_matches[0], "warning") if sev_matches else "warning"

        en_m = pat_enabled.search(text)
        enabled = en_m.group(1) == "EnabledByDefault" if en_m else True

        if rule_id not in entries:
            entries[rule_id] = catalog_entry(
                "stylecop", rule_id, title, category, severity, enabled,
                "DotNetAnalyzers/StyleCopAnalyzers",
                f"https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/{rule_id}.md",
            )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_meziantou() -> list[dict]:
    """Meziantou.Analyzer — RuleIdentifiers.cs + per-file DiagnosticDescriptor."""
    repo = REPOS / "Meziantou.Analyzer"
    id_map = load_const_map(
        repo / "src" / "Meziantou.Analyzer" / "RuleIdentifiers.cs",
        re.compile(r'const\s+string\s+(\w+)\s*=\s*"([^"]+)"'),
    )

    # Pattern: new( RuleIdentifiers.X, title: "...", ..., RuleCategories.Y, DiagnosticSeverity.Z, ...
    # or: new( RuleIdentifiers.X, title: "...", ..., "category", DiagnosticSeverity.Z, ...
    pat = re.compile(
        r'new\s*\(\s*RuleIdentifiers\.(\w+)\s*,'
        r'.*?'
        r'title:\s*"([^"]+)"'
        r'.*?'
        r'(?:RuleCategories\.(\w+)|"([^"]+)")'
        r'.*?'
        r'DiagnosticSeverity\.(\w+)'
        r'.*?'
        r'isEnabledByDefault:\s*(true|false)',
        re.DOTALL,
    )

    entries: dict[str, dict] = {}
    rules_dir = repo / "src" / "Meziantou.Analyzer" / "Rules"
    for cs_file in sorted(rules_dir.rglob("*.cs")):
        text = cs_file.read_text(errors="ignore")
        for m in pat.finditer(text):
            const_name = m.group(1)
            rule_id = id_map.get(const_name, const_name)
            if not rule_id or not rule_id.startswith("MA"):
                continue
            title = m.group(2)
            category = m.group(3) or m.group(4) or "Meziantou"
            severity = SEVERITY_MAP.get(m.group(5), "suggestion")
            enabled = m.group(6) == "true"
            if rule_id not in entries:
                entries[rule_id] = catalog_entry(
                    "meziantou", rule_id, title, category, severity, enabled,
                    "meziantou/Meziantou.Analyzer",
                    f"https://github.com/meziantou/Meziantou.Analyzer/blob/main/docs/Rules/{rule_id}.md",
                )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_sharpsource() -> list[dict]:
    """SharpSource — DiagnosticId.cs constants + per-file Rule definitions."""
    repo = REPOS / "SharpSource"
    id_map = load_const_map(
        repo / "SharpSource" / "SharpSource" / "Utilities" / "DiagnosticId.cs",
        re.compile(r'const\s+string\s+(\w+)\s*=\s*"([^"]+)"'),
    )

    # Pattern in each file:
    # public static DiagnosticDescriptor Rule => new(
    #     DiagnosticId.XxxYyy,
    #     "Title",
    #     "Message",
    #     Categories.Xxx,
    #     DiagnosticSeverity.Warning,
    #     true, ...
    pat = re.compile(
        r'new\s*\(\s*DiagnosticId\.(\w+)\s*,'
        r'\s*"([^"]+)"\s*,'   # title (second arg)
        r'.*?'
        r'(?:Categories\.(\w+)|"([^"]+)")'
        r'.*?'
        r'DiagnosticSeverity\.(\w+)'
        r'.*?'
        r'(true|false)',
        re.DOTALL,
    )

    entries: dict[str, dict] = {}
    diag_dir = repo / "SharpSource" / "SharpSource" / "Diagnostics"
    for cs_file in sorted(diag_dir.rglob("*.cs")):
        if "Test" in str(cs_file):
            continue
        text = cs_file.read_text(errors="ignore")
        for m in pat.finditer(text):
            const_name = m.group(1)
            rule_id = id_map.get(const_name, const_name)
            if not rule_id or not rule_id.startswith("SS"):
                continue
            title = m.group(2)
            category = m.group(3) or m.group(4) or "SharpSource"
            severity = SEVERITY_MAP.get(m.group(5), "warning")
            enabled = m.group(6) == "true"
            if rule_id not in entries:
                entries[rule_id] = catalog_entry(
                    "sharpsource", rule_id, title, category, severity, enabled,
                    "Vannevelj/SharpSource",
                    f"https://github.com/Vannevelj/SharpSource/blob/master/docs/{rule_id}.md",
                )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_disposablefixer() -> list[dict]:
    """DisposableFixer — Constants.cs has Id nested class + DiagnosticDescriptor blocks."""
    repo = REPOS / "DisposableFixer"
    src_df = repo / "src" / "DisposableFixer"
    const_file = src_df / "Constants.cs"
    const_text = const_file.read_text(errors="ignore")

    # Load resx titles
    resources = parse_resx(src_df / "Resources.resx")

    # Build id map: const_name → "DF0100" from all const string assignments in the file
    id_map: dict[str, str] = {}
    for m in re.finditer(r'const\s+string\s+(\w+)\s*=\s*"(DF\d+)"', const_text):
        id_map[m.group(1)] = m.group(2)

    # Each DiagnosticDescriptor block has:
    #   id: Id.SomeConst  → look up in id_map
    #   nameof(Resources.XxxTitle) → look up in resources
    pat_block = re.compile(r'new\s+DiagnosticDescriptor\s*\((.{20,2000}?)(?=new\s+DiagnosticDescriptor|\Z)', re.DOTALL)
    pat_id_ref = re.compile(r'id:\s*Id(?:\.\w+)+\.(\w+)')
    pat_id_ref2 = re.compile(r'id:\s*Id\.(\w+)')
    pat_title_key = re.compile(r'nameof\s*\(\s*Resources\.(\w+Title)\s*\)')
    pat_severity = re.compile(r'DiagnosticSeverity\.(\w+)')

    entries: dict[str, dict] = {}
    for m in pat_block.finditer(const_text):
        block = m.group(1)
        # Try deepest nested const first, then top-level
        id_ref_m = pat_id_ref.search(block) or pat_id_ref2.search(block)
        if not id_ref_m:
            continue
        const_name = id_ref_m.group(1)
        rule_id = id_map.get(const_name)
        if not rule_id:
            continue
        title_m = pat_title_key.search(block)
        title = resources.get(title_m.group(1), rule_id) if title_m else rule_id
        sev_m = pat_severity.search(block)
        severity = SEVERITY_MAP.get(sev_m.group(1), "warning") if sev_m else "warning"
        if rule_id not in entries:
            entries[rule_id] = catalog_entry(
                "disposablefixer", rule_id, title, "Disposable", severity, True,
                "BADF00D/DisposableFixer",
                f"https://github.com/BADF00D/DisposableFixer/blob/master/src/DisposableFixer/doc/{rule_id}.md",
            )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_vsthrd() -> list[dict]:
    """Microsoft.VisualStudio.Threading.Analyzers — per-file Id const + Strings.resx titles."""
    repo = REPOS / "vs-threading"
    # Load resx resources (both core and CSharp analyzers)
    resources: dict[str, str] = {}
    for resx in (repo / "src").rglob("Strings.resx"):
        resources.update(parse_resx(resx))

    # Per-file: public const string Id = "VSTHRD001";
    # Title in resx as VSTHRD001_Title
    pat_id = re.compile(r'(?:public|internal)\s+const\s+string\s+Id\s*=\s*"([^"]+)"')
    pat_severity = re.compile(r'DiagnosticSeverity\.(\w+)')
    pat_enabled = re.compile(r'isEnabledByDefault:\s*(true|false)')

    entries: dict[str, dict] = {}
    for cs_file in sorted((repo / "src").rglob("*.cs")):
        if "Test" in str(cs_file) or "CodeFix" in cs_file.name:
            continue
        text = cs_file.read_text(errors="ignore")
        id_m = pat_id.search(text)
        if not id_m:
            continue
        rule_id = id_m.group(1)
        if not rule_id.startswith("VSTHRD"):
            continue

        # Title from resx: VSTHRD001_Title or VSTHRD001Title
        title = (resources.get(f"{rule_id}_Title")
                 or resources.get(f"{rule_id}Title")
                 or rule_id)

        sev_matches = pat_severity.findall(text)
        severity = SEVERITY_MAP.get(sev_matches[0], "warning") if sev_matches else "warning"
        en_m = pat_enabled.search(text)
        enabled = en_m.group(1) == "true" if en_m else True

        if rule_id not in entries:
            entries[rule_id] = catalog_entry(
                "vsthrd", rule_id, title, "Threading", severity, enabled,
                "microsoft/vs-threading",
                f"https://github.com/microsoft/vs-threading/blob/main/doc/analyzers/{rule_id}.md",
            )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_csharpguidelines() -> list[dict]:
    """CSharpGuidelinesAnalyzer — per-file DiagnosticId = "AV" + "NNNN" + inline Title."""
    repo = REPOS / "CSharpGuidelinesAnalyzer"

    # DiagnosticId = AnalyzerCategory.RulePrefix + "2230" — prefix is "AV"
    pat_id = re.compile(
        r'const\s+string\s+DiagnosticId\s*=\s*(?:AnalyzerCategory\.RulePrefix\s*\+\s*"(\d+)"|"(AV\d+)")'
    )
    pat_title = re.compile(r'const\s+string\s+Title\s*=\s*"([^"]+)"')
    pat_category = re.compile(r'Category\.DisplayName')  # Category is class-based
    pat_cat_class = re.compile(r'private\s+static\s+readonly\s+(?:AnalyzerCategory|Category)\s+Category\s*=\s*\w+\.(\w+)')
    pat_severity = re.compile(r'DiagnosticSeverity\.(\w+)')
    pat_enabled = re.compile(r'new\s*\(DiagnosticId.*?(?:DiagnosticSeverity\.\w+)\s*,\s*(true|false)')

    entries: dict[str, dict] = {}
    rules_dir = repo / "src" / "CSharpGuidelinesAnalyzer" / "CSharpGuidelinesAnalyzer" / "Rules"
    for cs_file in sorted(rules_dir.rglob("*.cs")):
        if "Test" in str(cs_file):
            continue
        text = cs_file.read_text(errors="ignore")
        id_m = pat_id.search(text)
        if not id_m:
            continue
        suffix = id_m.group(1)
        inline_id = id_m.group(2)
        rule_id = f"AV{suffix}" if suffix else (inline_id or "")
        if not rule_id.startswith("AV"):
            continue

        title_m = pat_title.search(text)
        title = title_m.group(1) if title_m else rule_id

        cat_m = pat_cat_class.search(text)
        category = cat_m.group(1) if cat_m else cs_file.parent.name

        sev_matches = pat_severity.findall(text)
        severity = SEVERITY_MAP.get(sev_matches[0], "warning") if sev_matches else "warning"

        en_m = pat_enabled.search(text)
        enabled = en_m.group(1) == "true" if en_m else True

        if rule_id not in entries:
            entries[rule_id] = catalog_entry(
                "csharpguidelines", rule_id, title, category, severity, enabled,
                "bkoelman/CSharpGuidelinesAnalyzer",
                f"https://github.com/bkoelman/CSharpGuidelinesAnalyzer/blob/master/doc/reference/{rule_id.lower()}.md",
            )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_exhaustive() -> list[dict]:
    """ExhaustiveMatching — Diagnostics.cs with resx titles."""
    repo = REPOS / "ExhaustiveMatching"
    resx = parse_resx(repo / "ExhaustiveMatching.Analyzer" / "Resources.resx")

    # Pattern: new DiagnosticDescriptor("EM0001", EM0001Title, ..., Category, DiagnosticSeverity.Error, ...
    pat = re.compile(
        r'new\s+DiagnosticDescriptor\s*\(\s*"(EM\d+)"\s*,'
        r'.*?'
        r'DiagnosticSeverity\.(\w+)',
        re.DOTALL,
    )

    text = (repo / "ExhaustiveMatching.Analyzer" / "Diagnostics.cs").read_text(errors="ignore")

    entries: dict[str, dict] = {}
    for m in pat.finditer(text):
        rule_id = m.group(1)
        title_key = f"{rule_id}Title"
        title = resx.get(title_key, rule_id)
        severity = SEVERITY_MAP.get(m.group(2), "error")
        if rule_id not in entries:
            entries[rule_id] = catalog_entry(
                "exhaustive", rule_id, title, "ExhaustiveMatching", severity, True,
                "WalkerCodeRanger/ExhaustiveMatching",
                f"https://github.com/WalkerCodeRanger/ExhaustiveMatching/blob/main/docs/{rule_id}.md",
            )

    return sorted(entries.values(), key=lambda e: e["id"])


def extract_hyperlinq() -> list[dict]:
    """NetFabric.Hyperlinq.Analyzer — DiagnosticIds.cs + per-file inline Title."""
    repo = REPOS / "NetFabric.Hyperlinq.Analyzer"
    id_map = load_const_map(
        repo / "NetFabric.Hyperlinq.Analyzer" / "DiagnosticIds.cs",
        re.compile(r'const\s+string\s+(\w+)\s*=\s*"([^"]+)"'),
    )

    # Per-file: DiagnosticId from DiagnosticIds.XId, Title inline
    pat = re.compile(
        r'const\s+string\s+DiagnosticId\s*=\s*DiagnosticIds\.(\w+)'
        r'.*?'
        r'(?:LocalizableString|LocalizableString\?|static\s+readonly)?.*?Title\s*=\s*(?:new\s+LocalizableResourceString[^;]*;|"([^"]+)")',
        re.DOTALL,
    )
    pat_title_plain = re.compile(
        r'(?:LocalizableString|string)\s+Title\s*=\s*"([^"]+)"'
    )
    pat_severity = re.compile(r'DiagnosticSeverity\.(\w+)')

    entries: dict[str, dict] = {}
    analyzer_dir = repo / "NetFabric.Hyperlinq.Analyzer" / "Analyzers"
    for cs_file in sorted(analyzer_dir.rglob("*.cs")):
        text = cs_file.read_text(errors="ignore")
        # Look for DiagnosticId = DiagnosticIds.XId pattern
        id_ref_m = re.search(r'const\s+string\s+DiagnosticId\s*=\s*DiagnosticIds\.(\w+)', text)
        if not id_ref_m:
            continue
        const_name = id_ref_m.group(1)
        rule_id = id_map.get(const_name, const_name)
        if not rule_id.startswith("HLQ"):
            continue

        title_m = pat_title_plain.search(text)
        title = title_m.group(1) if title_m else rule_id

        sev_matches = pat_severity.findall(text)
        severity = SEVERITY_MAP.get(sev_matches[0], "warning") if sev_matches else "warning"

        if rule_id not in entries:
            entries[rule_id] = catalog_entry(
                "hyperlinq", rule_id, title, "Performance", severity, True,
                "NetFabric/NetFabric.Hyperlinq.Analyzer",
                f"https://github.com/NetFabric/NetFabric.Hyperlinq.Analyzer/tree/master/docs/reference/{rule_id}.md",
            )

    return sorted(entries.values(), key=lambda e: e["id"])


# ── dispatch ─────────────────────────────────────────────────────────────────

EXTRACTORS = {
    "stylecop": extract_stylecop,
    "meziantou": extract_meziantou,
    "scs": extract_scs,
    "asyncfixer": extract_asyncfixer,
    "idisp": extract_idisp,
    "xunit": extract_xunit,
    "nunit": extract_nunit,
    "fluentassertions": extract_fluentassertions,
    "nsubstitute": extract_nsubstitute,
    "errorprone": extract_errorprone,
    "sharpsource": extract_sharpsource,
    "disposablefixer": extract_disposablefixer,
    "vsthrd": extract_vsthrd,
    "csharpguidelines": extract_csharpguidelines,
    "exhaustive": extract_exhaustive,
    "refl": extract_refl,
    "aspnetcore": extract_aspnetcore,
    "hyperlinq": extract_hyperlinq,
}


def main():
    packages = sys.argv[1:] if len(sys.argv) > 1 else list(EXTRACTORS.keys())
    unknown = [p for p in packages if p not in EXTRACTORS]
    if unknown:
        print(f"Unknown packages: {unknown}")
        print(f"Available: {sorted(EXTRACTORS)}")
        sys.exit(1)

    CATALOG_DIR.mkdir(parents=True, exist_ok=True)

    for package in packages:
        try:
            entries = EXTRACTORS[package]()
            out = CATALOG_DIR / f"{package}.yaml"
            with open(out, "w") as f:
                yaml.dump(entries, f, allow_unicode=True, sort_keys=False, default_flow_style=False)
            print(f"  {package}.yaml: {len(entries)} rules written")
        except Exception as e:
            print(f"  {package}: ERROR — {e}")
            import traceback
            traceback.print_exc()


if __name__ == "__main__":
    main()
