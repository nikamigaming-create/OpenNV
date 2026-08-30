from __future__ import annotations

import json
import shutil
import subprocess
import unittest
from pathlib import Path


REPOSITORY = Path(__file__).resolve().parents[2]
SCRIPT = REPOSITORY / "scripts" / "Invoke-TTWFo3RetailStage10Observation.ps1"
RECIPE = REPOSITORY / "content" / "recipes" / "ttw-fo3-retail-stage10-observer-v1.json"


class TtwFo3RetailObserverContractTests(unittest.TestCase):
    def test_recipe_binds_exact_target_observer_and_stage10_closure(self) -> None:
        recipe = json.loads(RECIPE.read_text(encoding="utf-8"))
        self.assertEqual(
            recipe["schema"],
            "opennv-ttw-fo3-retail-stage10-observer/v1",
        )
        self.assertEqual(recipe["target"]["executable"], "FalloutNV.exe")
        self.assertEqual(recipe["target"]["version"], "1.4.0.525")
        self.assertEqual(
            recipe["target"]["sha256"],
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
        )
        self.assertEqual(recipe["observer"]["mode"], "observe")
        self.assertEqual(recipe["observer"]["toolSurface"], 8)
        self.assertEqual(recipe["observation"]["questEditorId"], "CG00")
        self.assertEqual(recipe["observation"]["stage"], 10)
        self.assertEqual(
            list(recipe["participants"]),
            ["player", "father", "doctor", "mother"],
        )
        for role, participant in recipe["participants"].items():
            self.assertEqual(participant["packageSection"], 1, role)
            self.assertTrue(participant["packageFormKey"].startswith("FalloutNV.esm:"))
            self.assertTrue(participant["idleFormKey"].startswith("FalloutNV.esm:"))
            self.assertTrue(participant["sequenceName"].endswith("Section01"))

    def test_script_has_observe_only_surface_and_no_retail_launch_path(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")
        for forbidden in (
            "[switch]$Launch",
            "Start-Process",
            "process_write",
            "process_breakpoint",
            "process_resume",
            "process_step",
            "SendInput",
            "PostMessage",
            "SetForegroundWindow",
            "Get-Process -Name",
        ):
            self.assertNotIn(forbidden, source)
        self.assertIn("-Name 'process_attach'", source)
        self.assertIn("mode = 'observe'", source)
        self.assertIn("-Name 'process_detach'", source)
        self.assertIn("launched_by_observer = $false", source)
        self.assertIn(
            "'TargetProcessId is required; this observer never launches or selects FalloutNV.exe.'",
            source,
        )

        validate_index = source.index("if ($ValidateOnly)")
        process_lookup_index = source.index("$targetProcess = Get-Process")
        observer_start_index = source.index("$startInfo = [Diagnostics.ProcessStartInfo]")
        self.assertLess(validate_index, process_lookup_index)
        self.assertLess(validate_index, observer_start_index)

    @unittest.skipUnless(shutil.which("pwsh"), "PowerShell is required")
    def test_validate_only_hash_binds_owned_ttw_inputs_without_process(self) -> None:
        expected_inputs = (
            Path(r"D:\TTW\Installed"),
            Path.home()
            / "AppData"
            / "Local"
            / "OpenNV"
            / "profiles"
            / "ttw-profile.json",
        )
        if not all(path.exists() for path in expected_inputs):
            self.skipTest("Owned TTW corpus is not installed on this host")
        completed = subprocess.run(
            [
                shutil.which("pwsh") or "pwsh",
                "-NoLogo",
                "-NoProfile",
                "-File",
                str(SCRIPT),
                "-ValidateOnly",
            ],
            cwd=REPOSITORY,
            check=True,
            capture_output=True,
            text=True,
        )
        report = json.loads(completed.stdout)
        self.assertEqual(
            report["schema"],
            "opennv.ttw-fo3-retail-cg00-stage10-observer-validation/v1",
        )
        self.assertEqual(report["ttw_identity"]["source_root"], r"D:\TTW\Installed")
        self.assertEqual(report["participant_count"], 4)
        self.assertFalse(report["process_attached"])
        self.assertFalse(report["production_contract_emitted"])
        self.assertEqual(report["observer"]["required_mode"], "observe")


if __name__ == "__main__":
    unittest.main()
