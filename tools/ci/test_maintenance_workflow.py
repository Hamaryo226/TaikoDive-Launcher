"""Policy checks for daily R2 maintenance; no cloud access."""
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]


class MaintenanceWorkflowTests(unittest.TestCase):
    def test_daily_maintenance_is_serialized_and_safe_by_default(self):
        path = ROOT / '.github/workflows/maintain-r2.yml'
        self.assertTrue(path.exists(), 'Daily R2 maintenance must be configured')
        text = path.read_text(encoding='utf-8')
        for required in (
            'schedule:', 'cron:', 'workflow_dispatch:', 'default: false',
            'group: launcher-main-update', 'cancel-in-progress: false',
            'timeout-minutes: 15', 'contents: read', 'persist-credentials: false',
            'Maintain-TaikoDiveR2.ps1', '-Channel launcher',
            "github.event_name == 'schedule'", 'inputs.apply',
        ):
            self.assertIn(required, text)
        self.assertNotIn('contents: write', text)


class R2TestGateTests(unittest.TestCase):
    def test_r2_tests_run_in_main_and_pr_checks(self):
        command = "python -m unittest discover -s tools/r2 -p 'test_*.py' -v"
        for name in ['publish-main-update.yml', 'compare-publish-sizes.yml']:
            text = (ROOT / '.github/workflows' / name).read_text(encoding='utf-8')
            self.assertIn(command, text)
        text = (ROOT / '.github/workflows/compare-publish-sizes.yml').read_text(encoding='utf-8')
        self.assertIn('"tools/r2/**"', text)


if __name__ == '__main__':
    unittest.main()
