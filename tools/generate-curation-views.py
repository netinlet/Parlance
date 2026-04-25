#!/usr/bin/env python3
"""Generate human-queryable curation views from catalog + set references."""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

import yaml


ROOT = Path(__file__).resolve().parent.parent
CATALOG_DIR = ROOT / "docs" / "research" / "analyzer-catalog"
SETS_DIR = ROOT / "docs" / "curation" / "sets"
VIEWS_DIR = ROOT / "docs" / "curation" / "views"


@dataclass
class SetResult:
    rules: set[str]
    severity_overrides: dict[str, str]


def load_catalog() -> dict[str, dict]:
    rules: dict[str, dict] = {}
    for path in sorted(CATALOG_DIR.glob("*.yaml")):
        entries = yaml.safe_load(path.read_text()) or []
        for entry in entries:
            rules[entry["id"]] = entry
    return rules


def load_set_docs() -> dict[str, dict]:
    docs = {}
    for path in sorted(SETS_DIR.glob("*.yaml")):
        doc = yaml.safe_load(path.read_text()) or {}
        set_id = doc["set"]["id"]
        docs[set_id] = doc
    return docs


def _matches_where(rule: dict, where: dict) -> bool:
    def in_set(key: str, values: set[str]) -> bool:
        return rule.get(key) in values

    if "package_in" in where and not in_set("package", set(where["package_in"])):
        return False
    if "analysis_type_in" in where and not in_set("analysis_type", set(where["analysis_type_in"])):
        return False
    if "default_severity_in" in where and not in_set("default_severity", set(where["default_severity_in"])):
        return False
    if "applies_to_in" in where and not in_set("applies_to", set(where["applies_to_in"])):
        return False
    if "deprecated" in where and rule.get("deprecated") != where["deprecated"]:
        return False
    if "id_prefix_in" in where:
        prefixes = tuple(where["id_prefix_in"])
        if not str(rule.get("id", "")).startswith(prefixes):
            return False
    if "tags_any" in where:
        tags = set(rule.get("tags") or [])
        if tags.isdisjoint(set(where["tags_any"])):
            return False
    if "max_open_issues" in where:
        open_issues = rule.get("open_issues")
        if open_issues is None or open_issues > where["max_open_issues"]:
            return False
    return True


def _resolve_clause_rule_ids(clause: dict, catalog: dict[str, dict]) -> set[str]:
    out: set[str] = set()
    if "rule_ids" in clause:
        out.update(clause["rule_ids"] or [])
    if "where" in clause:
        where = clause["where"] or {}
        for rid, rule in catalog.items():
            if _matches_where(rule, where):
                out.add(rid)
    return out


def resolve_set(
    set_id: str,
    set_docs: dict[str, dict],
    catalog: dict[str, dict],
    visiting: set[str] | None = None,
    cache: dict[str, SetResult] | None = None,
) -> SetResult:
    if visiting is None:
        visiting = set()
    if cache is None:
        cache = {}
    if set_id in cache:
        return cache[set_id]
    if set_id in visiting:
        raise ValueError(f"Cycle detected in set inheritance: {set_id}")

    doc = set_docs[set_id]
    visiting.add(set_id)

    rules: set[str] = set()
    overrides: dict[str, str] = {}

    for parent in doc["set"].get("extends", []):
        parent_res = resolve_set(parent, set_docs, catalog, visiting, cache)
        rules |= parent_res.rules
        overrides.update(parent_res.severity_overrides)

    for clause in doc.get("include", []):
        rules |= _resolve_clause_rule_ids(clause, catalog)

    for clause in doc.get("exclude", []):
        rules -= _resolve_clause_rule_ids(clause, catalog)

    for rid, sev in (doc.get("overrides", {}).get("severity", {}) or {}).items():
        overrides[rid] = sev

    visiting.remove(set_id)
    result = SetResult(rules=rules, severity_overrides=overrides)
    cache[set_id] = result
    return result


def write_outputs(catalog: dict[str, dict], set_docs: dict[str, dict], resolved: dict[str, SetResult]) -> None:
    VIEWS_DIR.mkdir(parents=True, exist_ok=True)
    by_set_dir = VIEWS_DIR / "by-set"
    by_set_dir.mkdir(parents=True, exist_ok=True)

    set_ids = sorted(resolved.keys())
    all_rule_ids = sorted({rid for r in resolved.values() for rid in r.rules})

    matrix_md = VIEWS_DIR / "rule-set-matrix.md"
    matrix_json = VIEWS_DIR / "rule-set-matrix.json"

    header = ["Rule", "Package", "Default Severity", *set_ids]
    lines = [
        "# Rule-to-Set Matrix",
        "",
        "Generated from catalog + set references. No rule metadata duplication.",
        "",
        "| " + " | ".join(header) + " |",
        "| " + " | ".join(["---"] * len(header)) + " |",
    ]
    data = {"sets": set_ids, "rules": []}

    for rid in all_rule_ids:
        rule = catalog.get(rid, {})
        row = [rid, str(rule.get("package", "")), str(rule.get("default_severity", ""))]
        memberships = {}
        for sid in set_ids:
            in_set = rid in resolved[sid].rules
            memberships[sid] = in_set
            row.append("x" if in_set else "")
        lines.append("| " + " | ".join(row) + " |")
        data["rules"].append(
            {
                "id": rid,
                "package": rule.get("package"),
                "default_severity": rule.get("default_severity"),
                "membership": memberships,
            }
        )

    matrix_md.write_text("\n".join(lines) + "\n")
    matrix_json.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n")

    for sid in set_ids:
        set_doc = set_docs[sid]
        result = resolved[sid]
        out = by_set_dir / f"{sid}.md"
        rows = [
            f"# Set: {sid}",
            "",
            f"- Title: {set_doc['set'].get('title', sid)}",
            f"- Status: {set_doc['set'].get('status', 'unknown')}",
            f"- Rules: {len(result.rules)}",
            "",
            "| Rule | Package | Effective Severity |",
            "| --- | --- | --- |",
        ]
        for rid in sorted(result.rules):
            rule = catalog.get(rid, {})
            eff = result.severity_overrides.get(rid, rule.get("default_severity"))
            rows.append(f"| {rid} | {rule.get('package', '')} | {eff} |")
        out.write_text("\n".join(rows) + "\n")


def main() -> None:
    catalog = load_catalog()
    set_docs = load_set_docs()
    cache: dict[str, SetResult] = {}
    resolved = {sid: resolve_set(sid, set_docs, catalog, cache=cache) for sid in set_docs.keys()}
    write_outputs(catalog, set_docs, resolved)
    print(f"Generated views for {len(resolved)} sets and {sum(len(v.rules) for v in resolved.values())} memberships.")


if __name__ == "__main__":
    main()
