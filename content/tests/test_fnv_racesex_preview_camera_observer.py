from __future__ import annotations

import json
import shutil
import subprocess
import unittest
from pathlib import Path


REPOSITORY = Path(__file__).resolve().parents[2]
SCRIPT = REPOSITORY / "scripts" / "Invoke-FNVRetailRaceSexCameraObservation.ps1"
PRIVATE_LAYOUT = Path(
    r"D:\Dev\Tools\Ghidrust\workspace\evidence\falloutnv_1_4_0_525"
    r"\camera\racesex-preview-live-layout.json"
)


class FnvRaceSexPreviewCameraObserverTests(unittest.TestCase):
    def test_script_is_explicit_pid_observe_only_and_has_no_retail_control_path(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")
        for forbidden in (
            "[switch]$Launch",
            "Start-Process",
            "process_launch",
            "process_write",
            "process_break",
            "process_continue",
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
        self.assertIn(
            "'TargetProcessId is required; this observer never launches or selects FalloutNV.exe.'",
            source,
        )
        self.assertEqual(1, source.count("-Name 'process_attach'"))

    def test_validate_only_precedes_process_lookup_and_observer_start(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")
        validate_index = source.index("if ($ValidateOnly)")
        self.assertLess(validate_index, source.index("$targetProcess = Get-Process"))
        self.assertLess(validate_index, source.index("$startInfo = [Diagnostics.ProcessStartInfo]"))

    def test_public_contract_is_written_only_after_unique_stable_join_checks(self) -> None:
        source = SCRIPT.read_text(encoding="utf-8")
        object_join_index = source.index("$unique = @($RequiredObjects")
        snapshot_join_index = source.rindex("Assert-LiveSnapshot $snapshot $layout")
        stability_index = source.rindex("Assert-StableSnapshots $snapshots")
        write_index = source.rindex("[IO.File]::WriteAllText(")
        self.assertLess(object_join_index, snapshot_join_index)
        self.assertLess(snapshot_join_index, stability_index)
        self.assertLess(stability_index, write_index)
        for required_probe in (
            "faceGrabId",
            "faceGrabRect",
            "projectionMatrix",
            "fovDegrees",
            "targetWorld",
            "distance",
            "near",
            "far",
            "aspect",
            "fullIn",
            "fullOut",
            "startingZoomPercent",
        ):
            self.assertIn(f"'{required_probe}'", source)

    def test_private_layout_stays_fail_closed_without_reviewed_live_offsets(self) -> None:
        if not PRIVATE_LAYOUT.exists():
            self.skipTest("Private clean-room evidence workspace is absent")
        layout = json.loads(PRIVATE_LAYOUT.read_text(encoding="utf-8"))
        self.assertEqual(
            "nikami-private-fnv-racesex-preview-live-layout/v1",
            layout["schema"],
        )
        self.assertEqual("incomplete-static-route-only", layout["status"])
        self.assertEqual({}, layout["objects"])
        self.assertEqual({}, layout["probes"])
        self.assertEqual({}, layout["constraints"])
        self.assertTrue(layout["blocker"])

    @unittest.skipUnless(shutil.which("pwsh"), "PowerShell is required")
    def test_validate_only_refuses_incomplete_layout_without_attaching(self) -> None:
        if not PRIVATE_LAYOUT.exists():
            self.skipTest("Private clean-room evidence workspace is absent")
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
            "opennv-fnv-racesex-preview-camera-observer-validation/v1",
            report["schema"],
        )
        self.assertEqual("blocked-private-layout-incomplete", report["status"])
        self.assertFalse(report["private_layout"]["ready"])
        self.assertFalse(report["process_attached"])
        self.assertFalse(report["public_contract_emitted"])
        self.assertEqual("observe", report["observer"]["required_mode"])


if __name__ == "__main__":
    unittest.main()
