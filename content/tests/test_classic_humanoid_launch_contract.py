from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "scripts"
PREFLIGHT = SCRIPTS / "Assert-ClassicHumanoidDonorPreviewSet.ps1"
RESOLVER = SCRIPTS / "Resolve-ClassicHumanoidDonorPreviewSet.ps1"

FO1_LAUNCH_OR_PROOFS = (
    SCRIPTS / "Test-GodotRuntime.ps1",
    SCRIPTS / "Test-Fo1XrSimulatorControls.ps1",
    SCRIPTS / "Capture-Fo1XrSimulatorPreview.ps1",
)
FO1_STATIC_PROOF = SCRIPTS / "Test-OpenNVFallout1PlayerPresentationStatic.ps1"
FO2_LAUNCH_OR_PROOFS = (
    SCRIPTS / "Start-OpenNVFallout2Arroyo.ps1",
    SCRIPTS / "Test-OpenNVFallout2ArroyoPlayer.ps1",
    SCRIPTS / "Test-OpenNVFallout2CharacterStart.ps1",
    SCRIPTS / "Test-OpenNVFallout2CustomCharacters.ps1",
    SCRIPTS / "Test-OpenNVFallout2OpeningHandoff.ps1",
)


class ClassicHumanoidLaunchContractTest(unittest.TestCase):
    @staticmethod
    def _write_preview_set(root: Path, include_female: bool) -> Path:
        root.mkdir()
        roles = ("body", "left-hand", "right-hand")
        sexes = ("male", "female") if include_female else ("male",)
        body_sources: dict[str, list[dict[str, object]]] = {}
        previews: list[dict[str, object]] = []
        for sex in sexes:
            model = root / f"{sex}.gltf"
            sidecar = root / f"{sex}.opennv.json"
            model.write_bytes(b"{}")
            sidecar.write_text(
                json.dumps(
                    {
                        "schema": "opennv-actor-gltf/v4",
                        "status": "skinned-animated",
                        "skeleton": {"rigidAttachmentNode": "Weapon"},
                        "surfaces": [{"role": role} for role in roles],
                    }
                ),
                encoding="utf-8",
            )
            body_sources[sex] = [
                {
                    "role": role,
                    "modelSha256": "a" * 64,
                    "diffuseSha256": "b" * 64,
                    "normalSha256": "c" * 64,
                    "retainedSurfaceCount": 1,
                }
                for role in roles
            ]
            previews.append(
                {
                    "sex": sex,
                    "outputs": {
                        "gltf": str(model),
                        "sidecar": str(sidecar),
                        "gltfSha256": hashlib.sha256(model.read_bytes()).hexdigest(),
                        "sidecarSha256": hashlib.sha256(sidecar.read_bytes()).hexdigest(),
                    },
                }
            )
        preview_set = root / "preview-set.json"
        preview_set.write_text(
            json.dumps(
                {
                    "schema": "opennv-owned-player-facegen-preview-set/v3",
                    "status": "compiled-default-male-and-female-full-body-live-previews-with-ctl-egm-targets-all-native-geometry-controls-runtime-bound",
                    "fullBody": True,
                    "bodyComponentRoles": list(roles),
                    "presentationOutfitFormId": "00000007",
                    "playerFormId": "00000014",
                    "bodyComponentSourcesBySex": body_sources,
                    "previews": previews,
                }
            ),
            encoding="utf-8",
        )
        return preview_set

    @staticmethod
    def _write_install_manifest(preview_set: Path) -> Path:
        preview_hash = hashlib.sha256(preview_set.read_bytes()).hexdigest()
        handoff_root = preview_set.parent
        opening = handoff_root / "opening-manifest.json"
        opening.write_text(
            json.dumps(
                {
                    "outputs": {
                        "playerFaceGenPreviewSet": {
                            "path": str(preview_set),
                            "sha256": preview_hash,
                        },
                    },
                }
            ),
            encoding="utf-8",
        )
        install = handoff_root / "install-manifest.json"
        install.write_text(
            json.dumps(
                {
                    "schema": "opennv-legal-asset-cache/v1",
                    "status": "prepared-legal-assets",
                    "outputs": {
                        "openingManifest": str(opening),
                        "openingManifestSha256": hashlib.sha256(
                            opening.read_bytes()
                        ).hexdigest(),
                        "openingPlayerFaceGenPreviewSet": str(preview_set),
                        "openingPlayerFaceGenPreviewSetSha256": preview_hash,
                    },
                }
            ),
            encoding="utf-8",
        )
        return install

    @unittest.skipUnless(shutil.which("pwsh"), "PowerShell is required for script contract execution")
    def test_preflight_accepts_only_complete_hashed_two_sex_input(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            complete = self._write_preview_set(root / "complete", include_female=True)
            incomplete = self._write_preview_set(root / "incomplete", include_female=False)
            complete_install = self._write_install_manifest(complete)
            incomplete_install = self._write_install_manifest(incomplete)

            resolved_complete = subprocess.run(
                [
                    "pwsh",
                    "-NoProfile",
                    "-File",
                    str(RESOLVER),
                    "-InstallManifest",
                    str(complete_install),
                ],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )
            resolved_incomplete = subprocess.run(
                [
                    "pwsh",
                    "-NoProfile",
                    "-File",
                    str(RESOLVER),
                    "-InstallManifest",
                    str(incomplete_install),
                ],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )
            self.assertEqual(resolved_complete.returncode, 0, resolved_complete.stderr)
            self.assertTrue(
                os.path.samefile(resolved_complete.stdout.strip(), complete),
                resolved_complete.stdout,
            )
            self.assertEqual(resolved_incomplete.returncode, 0, resolved_incomplete.stderr)

            accepted = subprocess.run(
                [
                    "pwsh",
                    "-NoProfile",
                    "-File",
                    str(PREFLIGHT),
                    "-PreviewSet",
                    resolved_complete.stdout.strip(),
                ],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )
            rejected = subprocess.run(
                [
                    "pwsh",
                    "-NoProfile",
                    "-File",
                    str(PREFLIGHT),
                    "-PreviewSet",
                    resolved_incomplete.stdout.strip(),
                ],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )
            self.assertEqual(accepted.returncode, 0, accepted.stderr)
            self.assertIn("OPENNV_CLASSIC_HUMANOID_DONOR_PRECHECK_PASS", accepted.stdout)
            self.assertNotEqual(rejected.returncode, 0)
            self.assertIn("sex variants are incomplete", rejected.stderr)

    @unittest.skipUnless(shutil.which("pwsh"), "PowerShell is required for script contract execution")
    def test_install_manifest_resolver_rejects_a_stale_nested_preview_hash(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            preview_set = self._write_preview_set(root / "complete", include_female=True)
            install = self._write_install_manifest(preview_set)
            document = json.loads(install.read_text(encoding="utf-8"))
            document["outputs"]["openingPlayerFaceGenPreviewSetSha256"] = "0" * 64
            install.write_text(json.dumps(document), encoding="utf-8")

            resolved = subprocess.run(
                [
                    "pwsh",
                    "-NoProfile",
                    "-File",
                    str(RESOLVER),
                    "-InstallManifest",
                    str(install),
                ],
                cwd=ROOT,
                text=True,
                capture_output=True,
                check=False,
            )
            self.assertNotEqual(resolved.returncode, 0)
            self.assertIn("does not match the opening manifest", resolved.stderr)

    def test_preflight_validates_the_full_hash_bound_body_outfit_and_socket_join(self) -> None:
        source = PREFLIGHT.read_text(encoding="utf-8")

        self.assertIn("opennv-owned-player-facegen-preview-set/v3", source)
        self.assertIn("opennv-actor-gltf/v4", source)
        self.assertIn("$RequiredSexes = @('male', 'female')", source)
        self.assertIn("$RequiredBodyRoles = @('body', 'left-hand', 'right-hand')", source)
        self.assertIn("presentationOutfitFormId", source)
        self.assertIn("rigidAttachmentNode", source)
        self.assertIn("Assert-HashBoundFile $modelPath", source)
        self.assertIn("Assert-HashBoundFile $sidecarPath", source)
        self.assertIn("Classic humanoid donor sex variants are incomplete or duplicated.", source)

    def test_player_entry_points_preflight_and_pass_only_the_shared_option(self) -> None:
        for path in FO1_LAUNCH_OR_PROOFS + FO2_LAUNCH_OR_PROOFS:
            with self.subTest(script=path.name):
                source = path.read_text(encoding="utf-8")
                self.assertIn("ClassicHumanoidInstallManifest", source)
                self.assertIn("Resolve-ClassicHumanoidDonorPreviewSet.ps1", source)
                self.assertIn("$classicHumanoidDonorPreviewSet", source)
                self.assertIn("Assert-ClassicHumanoidDonorPreviewSet.ps1", source)
                self.assertIn("classic-humanoid-donor-preview-set", source)
                self.assertNotIn("fo2-humanoid-donor-scene", source)
                self.assertNotIn("HumanoidDonorScene", source)

        static_proof = FO1_STATIC_PROOF.read_text(encoding="utf-8")
        self.assertIn("ClassicHumanoidInstallManifest", static_proof)
        self.assertIn("Resolve-ClassicHumanoidDonorPreviewSet.ps1", static_proof)
        self.assertIn("Assert-ClassicHumanoidDonorPreviewSet.ps1", static_proof)

    def test_resolver_requires_an_explicit_hash_bound_install_handoff(self) -> None:
        source = RESOLVER.read_text(encoding="utf-8")

        self.assertIn("[string]$InstallManifest", source)
        self.assertIn("openingPlayerFaceGenPreviewSet", source)
        self.assertIn("openingPlayerFaceGenPreviewSetSha256", source)
        self.assertIn("openingManifestSha256", source)
        self.assertIn("Assert-HashBoundFile $previewPath", source)
        self.assertNotIn("Get-ChildItem", source)

    def test_fo1_runtime_gate_rejects_missing_donor_before_invoking_godot(self) -> None:
        source = (SCRIPTS / "Test-GodotRuntime.ps1").read_text(encoding="utf-8")

        resolver = source.index("& $classicHumanoidResolver")
        preflight = source.index("& $classicHumanoidPreflight")
        startup = source.index("$startupOutput = & $Godot")
        self.assertLess(resolver, preflight)
        self.assertLess(preflight, startup)
        self.assertIn("no substitute player body is admitted", source)
        self.assertIn("--fo1-hex-scene $fo1Scene @fo1DonorArguments", source)


if __name__ == "__main__":
    unittest.main()
