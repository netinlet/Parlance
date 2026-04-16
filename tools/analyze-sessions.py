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

    session_dir = resolve_session_dir(args.project_dir, args.session_dir)

    if not session_dir.exists():
        print(f'Session directory not found: {session_dir}', file=sys.stderr)
        sys.exit(1)

    print(f'Session dir: {session_dir}')


if __name__ == '__main__':
    main()
