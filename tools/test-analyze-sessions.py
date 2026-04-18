#!/usr/bin/env python3
"""Regression tests for analyze-sessions.py."""

import importlib.util
import unittest
from pathlib import Path


MODULE_PATH = Path(__file__).with_name('analyze-sessions.py')
SPEC = importlib.util.spec_from_file_location('analyze_sessions', MODULE_PATH)
analyze_sessions = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(analyze_sessions)


class AnalyzeSessionsTests(unittest.TestCase):
    def test_grep_type_cs_fallback_reports_search_context(self):
        call = {
            'name': 'Grep',
            'input': {
                'pattern': 'WorkspaceSessionHolder',
                'type': 'cs',
            },
        }

        ok, note = analyze_sessions.is_native_fallback(call)
        description = analyze_sessions.describe_fallback(call, note)

        self.assertTrue(ok)
        self.assertIn('WorkspaceSessionHolder', description)
        self.assertIn('type=cs', description)
        self.assertNotEqual('  Grep   → ', description)


if __name__ == '__main__':
    unittest.main()
