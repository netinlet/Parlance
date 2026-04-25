#!/usr/bin/env python3
"""Validate curation set YAML files.

This is a lightweight "doctor" validator with no third-party dependencies.
"""

from __future__ import annotations

import sys
from pathlib import Path

import yaml


ROOT = Path(__file__).resolve().parent.parent
SETS_DIR = ROOT / "docs" / "curation" / "sets"
CATALOG_DIR = ROOT / "docs" / "research" / "analyzer-catalog"

ALLOWED_TOP = {"version", "set", "include", "exclude", "overrides"}
ALLOWED_SET = {"id", "title", "status", "description", "extends", "owner"}
ALLOWED_WHERE = {
    "package_in",
    "analysis_type_in",
    "default_severity_in",
    "applies_to_in",
    "id_prefix_in",
    "tags_any",
    "max_open_issues",
    "deprecated",
}
ALLOWED_STATUS = {"active", "proposed", "superseded", "archived"}
ALLOWED_SEVERITY = {"error", "warning", "suggestion", "silent", "none"}
ALLOWED_ANALYSIS_TYPE = {"bug", "code_smell", "vulnerability", "security_hotspot"}
ALLOWED_APPLIES_TO = {"all", "production", "tests"}


def load_catalog_rule_ids() -> set[str]:
    ids: set[str] = set()
    for path in sorted(CATALOG_DIR.glob("*.yaml")):
        entries = yaml.safe_load(path.read_text()) or []
        for entry in entries:
            rid = entry.get("id")
            if isinstance(rid, str):
                ids.add(rid)
    return ids


def err(errors: list[str], file: str, message: str) -> None:
    errors.append(f"{file}: {message}")


def validate_clause(
    file_name: str,
    clause: object,
    path: str,
    catalog_ids: set[str],
    errors: list[str],
) -> None:
    if not isinstance(clause, dict):
        err(errors, file_name, f"{path} must be an object")
        return

    allowed = {"rule_ids", "where"}
    unknown = set(clause.keys()) - allowed
    if unknown:
        err(errors, file_name, f"{path} has unknown fields: {sorted(unknown)}")

    if "rule_ids" not in clause and "where" not in clause:
        err(errors, file_name, f"{path} must have rule_ids or where")

    if "rule_ids" in clause:
        rule_ids = clause["rule_ids"]
        if not isinstance(rule_ids, list):
            err(errors, file_name, f"{path}.rule_ids must be a list")
        else:
            for i, rid in enumerate(rule_ids):
                if not isinstance(rid, str):
                    err(errors, file_name, f"{path}.rule_ids[{i}] must be string")
                elif rid not in catalog_ids:
                    err(errors, file_name, f"{path}.rule_ids[{i}] unknown rule id: {rid}")

    if "where" in clause:
        where = clause["where"]
        if not isinstance(where, dict):
            err(errors, file_name, f"{path}.where must be an object")
            return
        unknown_where = set(where.keys()) - ALLOWED_WHERE
        if unknown_where:
            err(errors, file_name, f"{path}.where has unknown keys: {sorted(unknown_where)}")

        if "analysis_type_in" in where:
            vals = where["analysis_type_in"]
            if not isinstance(vals, list):
                err(errors, file_name, f"{path}.where.analysis_type_in must be list")
            else:
                bad = [v for v in vals if v not in ALLOWED_ANALYSIS_TYPE]
                if bad:
                    err(errors, file_name, f"{path}.where.analysis_type_in invalid values: {bad}")

        if "default_severity_in" in where:
            vals = where["default_severity_in"]
            if not isinstance(vals, list):
                err(errors, file_name, f"{path}.where.default_severity_in must be list")
            else:
                bad = [v for v in vals if v not in ALLOWED_SEVERITY]
                if bad:
                    err(errors, file_name, f"{path}.where.default_severity_in invalid values: {bad}")

        if "applies_to_in" in where:
            vals = where["applies_to_in"]
            if not isinstance(vals, list):
                err(errors, file_name, f"{path}.where.applies_to_in must be list")
            else:
                bad = [v for v in vals if v not in ALLOWED_APPLIES_TO]
                if bad:
                    err(errors, file_name, f"{path}.where.applies_to_in invalid values: {bad}")

        if "max_open_issues" in where:
            if not isinstance(where["max_open_issues"], int) or where["max_open_issues"] < 0:
                err(errors, file_name, f"{path}.where.max_open_issues must be integer >= 0")

        if "deprecated" in where and not isinstance(where["deprecated"], bool):
            err(errors, file_name, f"{path}.where.deprecated must be boolean")


def validate_set_doc(file_path: Path, catalog_ids: set[str], errors: list[str]) -> dict | None:
    file_name = file_path.name
    try:
        doc = yaml.safe_load(file_path.read_text()) or {}
    except Exception as ex:
        err(errors, file_name, f"failed to parse yaml: {ex}")
        return None

    if not isinstance(doc, dict):
        err(errors, file_name, "root must be object")
        return None

    unknown_top = set(doc.keys()) - ALLOWED_TOP
    if unknown_top:
        err(errors, file_name, f"unknown top-level fields: {sorted(unknown_top)}")

    if doc.get("version") != 1:
        err(errors, file_name, "version must be 1")

    set_obj = doc.get("set")
    if not isinstance(set_obj, dict):
        err(errors, file_name, "set must be object")
        return doc
    unknown_set = set(set_obj.keys()) - ALLOWED_SET
    if unknown_set:
        err(errors, file_name, f"set has unknown fields: {sorted(unknown_set)}")

    required_set = {"id", "title", "status", "description", "extends", "owner"}
    missing_set = required_set - set(set_obj.keys())
    if missing_set:
        err(errors, file_name, f"set missing fields: {sorted(missing_set)}")
    else:
        if set_obj["status"] not in ALLOWED_STATUS:
            err(errors, file_name, f"set.status invalid: {set_obj['status']}")
        if not isinstance(set_obj["extends"], list):
            err(errors, file_name, "set.extends must be list")

    for key in ("include", "exclude"):
        if key not in doc:
            err(errors, file_name, f"missing {key}")
            continue
        clauses = doc[key]
        if not isinstance(clauses, list):
            err(errors, file_name, f"{key} must be list")
            continue
        for idx, clause in enumerate(clauses):
            validate_clause(file_name, clause, f"{key}[{idx}]", catalog_ids, errors)

    overrides = doc.get("overrides")
    if not isinstance(overrides, dict):
        err(errors, file_name, "overrides must be object")
    else:
        if set(overrides.keys()) - {"severity"}:
            err(errors, file_name, "overrides has unsupported keys")
        severity = overrides.get("severity")
        if not isinstance(severity, dict):
            err(errors, file_name, "overrides.severity must be object")
        else:
            for rid, sev in severity.items():
                if rid not in catalog_ids:
                    err(errors, file_name, f"overrides.severity unknown rule id: {rid}")
                if sev not in ALLOWED_SEVERITY:
                    err(errors, file_name, f"overrides.severity invalid severity for {rid}: {sev}")

    return doc


def validate_inheritance_cycles(set_docs: dict[str, dict], errors: list[str]) -> None:
    graph: dict[str, list[str]] = {}
    for sid, doc in set_docs.items():
        graph[sid] = list((doc.get("set") or {}).get("extends", []))

    seen: set[str] = set()
    stack: set[str] = set()

    def dfs(node: str) -> None:
        if node in stack:
            errors.append(f"<sets>: inheritance cycle detected at {node}")
            return
        if node in seen:
            return
        seen.add(node)
        stack.add(node)
        for parent in graph.get(node, []):
            if parent not in graph:
                errors.append(f"<sets>: {node} extends unknown set '{parent}'")
                continue
            dfs(parent)
        stack.remove(node)

    for sid in graph:
        dfs(sid)


def main() -> None:
    files = sorted(SETS_DIR.glob("*.yaml"))
    if not files:
        print("No curation set files found.")
        sys.exit(1)

    catalog_ids = load_catalog_rule_ids()
    errors: list[str] = []
    set_docs: dict[str, dict] = {}

    for f in files:
        doc = validate_set_doc(f, catalog_ids, errors)
        if not doc:
            continue
        sid = (doc.get("set") or {}).get("id")
        if isinstance(sid, str):
            if sid in set_docs:
                err(errors, f.name, f"duplicate set id: {sid}")
            else:
                set_docs[sid] = doc

    validate_inheritance_cycles(set_docs, errors)

    print(f"Checked {len(files)} set files against {len(catalog_ids)} catalog rules.")
    if errors:
        print("\nErrors:")
        for e in errors:
            print(f"  {e}")
        sys.exit(1)

    print("All curation sets valid.")


if __name__ == "__main__":
    main()
