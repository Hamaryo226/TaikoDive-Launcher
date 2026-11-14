"""Structural CI policy checks; these do not execute GitHub Actions or the UI.

Run: python -m unittest discover -s tools/ci -p 'test_*.py' -v
Install the test-only parser with: python -m pip install -r tools/ci/requirements.txt
"""

import json
from pathlib import Path
import re
import unittest

import yaml

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github" / "workflows"


def workflow(name):
    # BaseLoader preserves Actions' `on` key (YAML 1.1 otherwise reads it as True).
    return yaml.load((WORKFLOWS / name).read_text(encoding="utf-8"), Loader=yaml.BaseLoader)


class WorkflowPolicyTests(unittest.TestCase):
    def test_only_production_mode_runs_on_pull_requests(self):
        comparison = workflow("compare-publish-sizes.yml")
        self.assertEqual(set(comparison["on"]), {"pull_request", "workflow_dispatch"})
        publish = comparison["jobs"]["publish"]
        self.assertEqual(publish["needs"], "test")
        # Constrain the expression rather than pretending to implement Actions' evaluator.
        matrix = publish["strategy"]["matrix"]["include"]
        self.assertIsInstance(matrix, str, "PR matrix must not unconditionally publish all modes")
        match = re.fullmatch(
            r"\$\{\{ github.event_name == 'workflow_dispatch' && fromJSON\('([^']+)'\) \|\| fromJSON\('([^']+)'\) \}\}",
            matrix.strip(),
        )
        if match is None:
            self.fail("Matrix must select manual modes only on workflow_dispatch")
        manual, pull_request = map(json.loads, match.groups())
        self.assertEqual([item["mode"] for item in manual], ["Baseline", "TrimmedSingleFile", "NativeAot"])
        self.assertEqual(pull_request, [{"mode": "TrimmedSingleFile", "experimental": False, "upload_output": False}])
        self.assertEqual([item["mode"] for item in manual if item["experimental"]], ["NativeAot"])
        self.assertEqual(publish["continue-on-error"], "${{ matrix.experimental }}")


    def test_size_measurement_and_binary_artifacts_are_manual_only(self):
        jobs = workflow("compare-publish-sizes.yml")["jobs"]
        steps = jobs["publish"]["steps"]
        named = {step.get("name"): step for step in steps}
        manual = "github.event_name == 'workflow_dispatch'"
        # Compression/metrics must be a separate guarded step, not part of PR publishing.
        size_steps = [step for step in steps if "Compress-Archive" in step.get("run", "")]
        self.assertEqual(len(size_steps), 1)
        self.assertEqual(size_steps[0].get("if"), manual)
        self.assertEqual(named["Upload metrics"].get("if"), manual)
        self.assertEqual(named["Upload experimental output"].get("if"), manual + " && matrix.upload_output")
        self.assertEqual(jobs["summarize"]["if"], "always() && " + manual)
        screenshot = named["Upload UI screenshots"]
        self.assertEqual(screenshot["if"], "always() && matrix.mode == 'TrimmedSingleFile'")
        self.assertEqual(screenshot["with"]["retention-days"], "7")
        self.assertEqual(screenshot["with"]["path"], "${{ runner.temp }}/launcher-ui/*.png")


    def test_main_publication_cannot_be_cancelled_mid_manifest_write(self):
        publish = workflow("publish-main-update.yml")
        self.assertEqual(publish["concurrency"], {
            "group": "launcher-main-update", "cancel-in-progress": "false"
        })


    def test_main_skips_only_documentation_pushes(self):
        triggers = workflow("publish-main-update.yml")["on"]
        self.assertEqual(set(triggers), {"push", "workflow_dispatch"})
        self.assertEqual(triggers["push"].get("branches"), ["main"])
        self.assertEqual(triggers["push"].get("paths-ignore"), ["docs/**", "**/*.md"])
        self.assertNotIn("paths", triggers["push"])


    def test_one_time_migration_is_retired(self):
        self.assertFalse((WORKFLOWS / "migrate-releases-to-r2.yml").exists(),
                         "Re-running the completed migration can roll R2 back to stale Releases")


    def test_ci_policy_checks_run_on_workflow_and_policy_changes(self):
        comparison = workflow("compare-publish-sizes.yml")
        paths = comparison["on"]["pull_request"]["paths"]
        self.assertIn(".github/workflows/**", paths)
        self.assertIn("tools/ci/**", paths)
        steps = comparison["jobs"]["test"]["steps"]
        runs = [step.get("run", "") for step in steps]
        self.assertIn("python -m pip install -r tools/ci/requirements.txt", runs)
        self.assertIn("python -m unittest discover -s tools/ci -p 'test_*.py' -v", runs)


    def test_production_build_and_smoke_gates_are_preserved(self):
        comparison = workflow("compare-publish-sizes.yml")
        jobs = comparison["jobs"]
        unit = next(step for step in jobs["test"]["steps"] if step.get("name") == "Run tests")
        self.assertEqual(unit["run"], r"dotnet test Tests\TaikoDiveLauncher.Tests.csproj -c Debug -p:Platform=x64")
        self.assertNotIn("if", jobs["test"])
        self.assertNotIn("if", unit)
        self.assertNotIn("continue-on-error", unit)
        publish = jobs["publish"]
        self.assertNotIn("if", publish)
        named = {step.get("name"): step for step in publish["steps"]}
        build = named["Publish ${{ matrix.mode }}"]
        self.assertNotIn("if", build)
        self.assertNotIn("continue-on-error", build)
        self.assertIn('elseif ("${{ matrix.mode }}" -eq "TrimmedSingleFile")', build["run"])
        self.assertIn(r'-p:PublishProfile=Properties\PublishProfiles\win-x64.pubxml', build["run"])
        self.assertIn("& dotnet publish @publishArgs", build["run"])
        self.assertIn("exit $LASTEXITCODE", build["run"])
        startup = named["Smoke-test startup"]
        self.assertNotIn("if", startup)
        self.assertNotIn("continue-on-error", startup)
        self.assertIn("if ($process.HasExited)", startup["run"])
        self.assertEqual(startup["env"]["TAIKODIVE_LAUNCHER_DISABLE_UPDATE_CHECK"], "1")
        ui = named["Verify launcher pages and capture screenshots"]
        self.assertEqual(ui["if"], "matrix.mode == 'TrimmedSingleFile'")
        self.assertNotIn("continue-on-error", ui)
        self.assertIn("./tools/Smoke-LauncherUi.ps1", ui["run"])

    def test_public_legacy_release_bridge_follows_successful_r2_publish(self):
        publication = workflow("publish-main-update.yml")
        self.assertEqual(publication["permissions"], {"contents": "write"})
        steps = publication["jobs"]["publish"]["steps"]
        names = [step.get("name") for step in steps]
        expected_order = ["Run tests", "Build single-file launcher", "Create update manifest",
                          "Publish main update channel to Cloudflare R2", "Publish legacy main update channel"]
        indices = [names.index(name) for name in expected_order]
        self.assertEqual(indices, sorted(indices))
        for index in indices:
            self.assertNotIn("if", steps[index])
            self.assertNotIn("continue-on-error", steps[index])
        r2 = steps[indices[-2]]["run"]
        self.assertIn(r".\tools\r2\Publish-TaikoDiveR2.ps1", r2)
        legacy = steps[indices[-1]]
        self.assertEqual(legacy["env"]["GH_TOKEN"], "${{ github.token }}")
        self.assertIn("$tag = 'launcher-main'", legacy["run"])
        self.assertIn('git push origin "refs/tags/$tag" --force', legacy["run"])
        self.assertIn("gh release upload $tag $executable --clobber", legacy["run"])
        self.assertIn("gh release upload $tag $manifest --clobber", legacy["run"])
        # Keep checkout credentials until the legacy git push is explicitly migrated.
        checkout = steps[names.index("Checkout main")]
        self.assertNotEqual(checkout["with"].get("persist-credentials"), "false")


if __name__ == "__main__":
    unittest.main()
