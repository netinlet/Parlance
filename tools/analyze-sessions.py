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


CS_EXTENSIONS = re.compile(
    r'\.(cs|csproj|sln|slnx|props|targets)$', re.IGNORECASE
)
CS_GLOB_PATTERN = re.compile(
    r'(/\*\*?)?/[^/]*\.(cs|csproj|sln|slnx|props|targets)$'
    r'|\.(cs|csproj|sln|slnx|props|targets)$',
    re.IGNORECASE
)


def extract_tool_calls(records: list[dict]) -> list[dict]:
    """Extract all tool_use blocks from assistant messages.

    Returns list of dicts with keys: name, input, timestamp
    """
    calls = []
    for rec in records:
        if rec.get('type') != 'assistant':
            continue
        content = rec.get('message', {}).get('content', [])
        if not isinstance(content, list):
            continue
        ts = rec.get('timestamp', '')
        for block in content:
            if isinstance(block, dict) and block.get('type') == 'tool_use':
                calls.append({
                    'name': block.get('name', ''),
                    'input': block.get('input', {}),
                    'timestamp': ts,
                })
    return calls


def is_parlance_call(call: dict) -> bool:
    """Check if a tool call is a Parlance MCP tool."""
    return call.get('name', '').startswith('mcp__parlance__')


def is_native_fallback(call: dict) -> tuple[bool, str]:
    """Check if a tool call is a native fallback on a C# file.

    Returns (is_fallback, reason_note).
    """
    name = call['name']
    inp = call['input']

    if name == 'Read':
        fp = inp.get('file_path', '')
        if CS_EXTENSIONS.search(fp):
            return True, ''
        return False, ''

    if name == 'Grep':
        path = inp.get('path', '')
        glob = inp.get('glob', '')
        type_ = inp.get('type', '')
        if CS_EXTENSIONS.search(path):
            return True, ''
        if CS_GLOB_PATTERN.search(glob):
            return True, ''
        if type_ == 'cs':
            return True, ''
        if '/src/' in path and not glob and not type_:
            return True, '(no glob, /src/ path)'
        return False, ''

    if name == 'Glob':
        pattern = inp.get('pattern', '')
        if CS_GLOB_PATTERN.search(pattern):
            return True, ''
        return False, ''

    return False, ''


def describe_fallback(call: dict, note: str) -> str:
    """Format a violation line for the report."""
    name = call['name']
    inp = call['input']
    if name == 'Read':
        target = inp.get('file_path', '')
    elif name == 'Grep':
        target = inp.get('path', '') or inp.get('glob', '')
    elif name == 'Glob':
        target = inp.get('pattern', '')
    else:
        target = str(inp)
    suffix = f'  {note}' if note else ''
    return f'  {name:<6} → {target}{suffix}'


def analyze_session(session: dict) -> dict:
    """Analyze a single session, returning counts and violation lines."""
    calls = extract_tool_calls(session['records'])
    parlance_count = 0
    violations = []

    for call in calls:
        if is_parlance_call(call):
            parlance_count += 1
        else:
            ok, note = is_native_fallback(call)
            if ok:
                violations.append(describe_fallback(call, note))

    return {
        'session_id': session['session_id'],
        'date': session['date'],
        'branch': session['branch'],
        'parlance': parlance_count,
        'fallbacks': len(violations),
        'violations': violations,
        'first_timestamp': next(
            (c['timestamp'] for c in calls if c['timestamp']), ''
        ),
    }


def format_report(results: list[dict], start: date, end: date) -> str:
    days = (end - start).days + 1
    total_parlance = sum(r['parlance'] for r in results)
    total_fallbacks = sum(r['fallbacks'] for r in results)

    lines = []
    lines.append(f'=== Session Analysis: {start} → {end} ({days} days) ===')
    lines.append(
        f'Sessions: {len(results)}  |  '
        f'Parlance calls: {total_parlance}  |  '
        f'Native fallbacks: {total_fallbacks}'
    )
    lines.append('')

    # Summary table
    lines.append('SESSION SUMMARY')
    lines.append(f'{"Date":<12}{"Session":<10}{"Branch":<24}{"Parlance":>9}{"Fallbacks":>11}')
    lines.append(f'{"-"*12}{"-"*10}{"-"*24}{"-"*9}{"-"*11}')
    for r in results:
        lines.append(
            f'{str(r["date"]):<12}'
            f'{r["session_id"][:8]:<10}'
            f'{r["branch"][:23]:<24}'
            f'{r["parlance"]:>9}'
            f'{r["fallbacks"]:>11}'
        )

    # Violations detail
    violations_exist = any(r['violations'] for r in results)
    if violations_exist:
        lines.append('')
        lines.append('VIOLATIONS DETAIL')
        for r in results:
            if not r['violations']:
                continue
            ts = r['first_timestamp'][11:16] if r['first_timestamp'] else '?'
            lines.append(
                f'[{r["date"]} {ts} | {r["session_id"][:8]} | {r["branch"]}]'
            )
            lines.extend(r['violations'])

    return '\n'.join(lines)


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
    parser.add_argument('--days', type=int, default=7)
    parser.add_argument('--since')
    parser.add_argument('--until')
    parser.add_argument('--project-dir')
    parser.add_argument('--session-dir')
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

    if not sessions:
        print(f'No sessions found from {start} to {end}.')
        return

    results = [analyze_session(s) for s in sessions]
    print(format_report(results, start, end))


if __name__ == '__main__':
    main()
