#!/usr/bin/env python3
"""Offline checks for the actual metadata script and rolling release selection."""
import importlib.util
import os
from pathlib import Path
import subprocess
import tempfile
import sys
sys.dont_write_bytecode = True
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('retention', ROOT / 'scripts/release-retention.py')
retention = importlib.util.module_from_spec(spec)
spec.loader.exec_module(retention)


class ReleasePolicyTests(unittest.TestCase):
    def metadata(self, ref, event='push', attempt='1', success=True):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / 'output'
            env = dict(os.environ, GITHUB_REF=ref, GITHUB_EVENT_NAME=event,
                       GITHUB_RUN_ID='12345', GITHUB_RUN_ATTEMPT=attempt,
                       GITHUB_OUTPUT=str(output))
            result = subprocess.run(['bash', 'scripts/release-metadata.sh'], cwd=ROOT,
                                    env=env, text=True, capture_output=True)
            self.assertEqual(result.returncode == 0, success, result.stderr)
            return dict(line.split('=', 1) for line in output.read_text().splitlines()) if output.exists() else {}

    def test_feature_channel_is_shared_and_unique(self):
        for branch in ('feature/new-ux', 'feature/nested/some-work', 'feature/$(echo-untrusted)'):
            data = self.metadata('refs/heads/' + branch)
            self.assertEqual(data['channel'], 'feature')
            self.assertEqual(data['prerelease'], 'true')
            self.assertRegex(data['tag'], r'^feature-\d{8}-\d{6}-12345-1$')
            self.assertIn('-feature.', data['modversion'])
            self.assertNotIn(branch, data['title'])
        rerun = self.metadata('refs/heads/feature/new-ux', attempt='2')
        self.assertTrue(rerun['tag'].endswith('-12345-2'))

    def test_tip_and_stable(self):
        tip = self.metadata('refs/heads/main')
        self.assertEqual(tip['channel'], 'tip')
        stable = self.metadata('refs/heads/release/1.2.3')
        self.assertEqual(stable['tag'], 'v1.2.3')
        self.assertEqual(stable['prerelease'], 'false')
        self.metadata('refs/heads/release/nested/invalid', success=False)

    def test_only_branch_push_or_dispatch_can_publish(self):
        for ref, event in [('refs/pull/42/merge', 'pull_request'),
                           ('refs/heads/feature/test', 'pull_request'),
                           ('refs/tags/main', 'workflow_dispatch'),
                           ('refs/heads/fix/test', 'push'), ('refs/heads/chore/test', 'push')]:
            self.assertEqual(self.metadata(ref, event), {'publish': 'false'})
        self.assertEqual(self.metadata('refs/heads/feature/test', 'workflow_dispatch')['publish'], 'true')

    def test_retention_all_pages_and_protected_releases(self):
        def release(number, tag=None, **flags):
            return dict(id=number, tag_name=tag or f'feature-{number}',
                        published_at='2026-09-05T00:00:00Z', prerelease=True, draft=False) | flags
        pages = [[release(n) for n in range(1, 151)], [release(n) for n in range(151, 251)]]
        pages[1] += [release(999, 'tip-999'), release(998, 'v1.2.3', prerelease=False),
                     release(997, 'feature-draft', draft=True), release(996, 'feature-stable', prerelease=False)]
        stale = retention.stale_tags(pages, 'feature', 5)
        self.assertEqual(len(stale), 245)
        self.assertEqual(stale[0], 'feature-245')
        self.assertNotIn('tip-999', stale)
        self.assertEqual(retention.stale_tags(pages, 'tip', 5), [])
        self.assertEqual(retention.stale_tags(pages, 'feature', 300), [])
        with self.assertRaises(ValueError): retention.stale_tags(pages, 'feature', 0)

    def test_retention_uses_publication_time(self):
        releases = [dict(id=n, tag_name=f'feature-{n}', prerelease=True, draft=False,
                         created_at=f'2026-09-0{n}T00:00:00Z',
                         published_at=f'2026-09-0{7-n}T00:00:00Z')
                    for n in range(1, 7)]
        self.assertEqual(retention.stale_tags([releases], 'feature', 5), ['feature-6'])


if __name__ == '__main__':
    unittest.main()
