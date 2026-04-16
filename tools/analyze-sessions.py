#!/usr/bin/env python3
"""Analyze Claude Code session JSONL files for Parlance tool adoption."""

import argparse
import json
import os
import re
import sys
from collections import defaultdict
from datetime import date, datetime, timedelta
from pathlib import Path


def encode_path_for_claude(project_dir: str) -> str:
    """Encode a project path the way Claude Code does for session dir naming.

    Claude replaces '/' with '-' in the absolute path.
    e.g. /mnt/wsl/foo/bar -> -mnt-wsl-foo-bar
    """
    return str(Path(project_dir).resolve()).replace('/', '-')


def resolve_session_dir(project_dir: str | None, session_dir: str | None) -> Path:
    """Resolve the Claude Code session directory for a project."""
    if session_dir:
        return Path(session_dir).expanduser()

    cwd = project_dir or os.getcwd()
    encoded = encode_path_for_claude(cwd)
    return Path.home() / '.claude' / 'projects' / encoded


def resolve_date_range(days: int, since: str | None, until: str | None) -> tuple[date, date]:
    """Resolve start/end dates. --since/--until take precedence over --days."""
    end = date.fromisoformat(until) if until else date.today()
    if since:
        start = date.fromisoformat(since)
    else:
        start = end - timedelta(days=days - 1)
    return start, end


def load_sessions(session_dir: Path, start: date, end: date) -> list[dict]:
    """Load all JSONL session files within the date range.

    Returns list of session dicts with keys:
      session_id, date, branch, records (list of raw JSONL records)
    """
    sessions = []
    for jsonl_file in sorted(session_dir.glob('*.jsonl')):
        file_date = date.fromtimestamp(jsonl_file.stat().st_mtime)
        if not (start <= file_date <= end):
            continue

        records = []
        try:
            with open(jsonl_file, encoding='utf-8', errors='ignore') as f:
                for line in f:
                    line = line.strip()
                    if line:
                        try:
                            records.append(json.loads(line))
                        except json.JSONDecodeError:
                            pass
        except OSError:
            continue

        if not records:
            continue

        # Extract session metadata from first record that has it
        session_id = jsonl_file.stem
        branch = 'unknown'
        session_date = file_date
        for rec in records:
            if 'gitBranch' in rec:
                branch = rec['gitBranch']
            if 'timestamp' in rec:
                try:
                    session_date = datetime.fromisoformat(
                        rec['timestamp'].replace('Z', '+00:00')
                    ).date()
                except ValueError:
                    pass
            if branch != 'unknown':
                break

        sessions.append({
            'session_id': session_id,
            'date': session_date,
            'branch': branch,
            'records': records,
        })

    return sorted(sessions, key=lambda s: s['date'], reverse=True)


def main():
    parser = argparse.ArgumentParser(
        description='Analyze Claude Code sessions for Parlance tool adoption'
    )
    parser.add_argument('--days', type=int, default=7,
                        help='Look back N days (default: 7)')
    parser.add_argument('--since', help='Start date YYYY-MM-DD')
    parser.add_argument('--until', help='End date YYYY-MM-DD (default: today)')
    parser.add_argument('--project-dir', help='Project root (default: cwd)')
    parser.add_argument('--session-dir', help='Direct override of session dir')
    args = parser.parse_args()

    if args.days <= 0:
        print('Error: --days must be >= 1', file=sys.stderr)
        sys.exit(1)

    session_dir = resolve_session_dir(args.project_dir, args.session_dir)
    if not session_dir.exists():
        print(f'Session directory not found: {session_dir}', file=sys.stderr)
        sys.exit(1)

    start, end = resolve_date_range(args.days, args.since, args.until)
    sessions = load_sessions(session_dir, start, end)
    print(f'Found {len(sessions)} sessions from {start} to {end}')


if __name__ == '__main__':
    main()
