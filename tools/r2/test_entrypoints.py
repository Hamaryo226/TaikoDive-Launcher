import json
import os
import shutil
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).parent


class EntrypointTests(unittest.TestCase):
    def test_python_cli_help_lists_explicit_apply(self):
        result = subprocess.run([sys.executable, str(ROOT / 'r2_manager.py'), '--help'],
                                text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('--apply', result.stdout)
        self.assertIn('--channel', result.stdout)

    @unittest.skipUnless(shutil.which('pwsh'), 'PowerShell 7 is required for wrapper tests')
    def test_powershell_wrappers_delegate_expected_modes(self):
        for script, parameters, apply in [
                ('Maintain-TaikoDiveR2.ps1', '-Channel assets', False),
                ('Maintain-TaikoDiveR2.ps1', '-Channel game -Apply', True),
                ('Publish-TaikoDiveR2.ps1', '-Channel launcher -PayloadPath payload.exe -ManifestPath manifest.json', True)]:
            with self.subTest(script=script, parameters=parameters):
                path = ROOT / script
                self.assertTrue(path.exists(), f'{script} wrapper missing')
                command = ("function python { $global:LASTEXITCODE=0; ConvertTo-Json -InputObject @($args) -Compress }; "
                           "function aws { throw 'Direct AWS invocation forbidden in wrapper test' }; "
                           f"& '{path}' {parameters}")
                env = {k: v for k, v in os.environ.items() if not k.startswith(('R2_', 'AWS_'))}
                result = subprocess.run(['pwsh', '-NoProfile', '-Command', command],
                                        capture_output=True, text=True, env=env)
                self.assertEqual(result.returncode, 0, result.stderr)
                args = json.loads(result.stdout)
                self.assertTrue(args[0].endswith('r2_manager.py'))
                self.assertEqual('--apply' in args, apply)
                self.assertIn('--channel', args)


if __name__ == '__main__':
    unittest.main()
