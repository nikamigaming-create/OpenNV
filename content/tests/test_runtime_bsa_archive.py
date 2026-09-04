from __future__ import annotations

import hashlib
import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

from content.tests.test_bsa_archive import synthetic_bsa


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
PROBE_PROJECT = (
    REPOSITORY_ROOT
    / "runtime"
    / "tools"
    / "OpenNV.ResourceProbe"
    / "OpenNV.ResourceProbe.csproj"
)


class RuntimeBsaArchiveTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        subprocess.run(
            ["dotnet", "build", str(PROBE_PROJECT), "-c", "Release", "--nologo"],
            cwd=REPOSITORY_ROOT,
            check=True,
            capture_output=True,
            text=True,
        )
        executable = "OpenNV.ResourceProbe.exe" if os.name == "nt" else "OpenNV.ResourceProbe"
        cls.probe = PROBE_PROJECT.parent / "bin" / "Release" / "net8.0" / executable

    def test_runtime_reader_matches_compressed_and_plain_members(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "synthetic.bsa"
            archive.write_bytes(synthetic_bsa())
            self._assert_member(
                archive,
                "Textures/Test/First.dds",
                b"compressed-owned-bytes",
            )
            self._assert_member(
                archive,
                "meshes\\test\\second.nif",
                b"plain-owned-bytes",
            )
            for logical_path in (
                "Textures/Test/First.dds",
                "meshes\\test\\second.nif",
            ):
                result = subprocess.run(
                    [
                        str(self.probe),
                        "--compare-directory-readers",
                        str(archive),
                        logical_path,
                    ],
                    check=False,
                    capture_output=True,
                    text=True,
                )
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn("OPENNV_BSA_DIRECTORY_EQUIVALENCE_OK", result.stdout)

    def test_runtime_source_uses_highest_priority_loose_override(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            low_root = root / "low"
            high_root = root / "high"
            logical = Path("textures") / "test" / "winner.dds"
            for source, payload in (
                (data_root, b"retail"),
                (low_root, b"low-mod"),
                (high_root, b"high-mod"),
            ):
                destination = source / logical
                destination.parent.mkdir(parents=True)
                destination.write_bytes(payload)
            manifest = root / "mod-stack.json"
            manifest.write_text(
                json.dumps(
                    {
                        "schema": "opennv-mod-stack/v2",
                        "status": "registered-read-only-source-stack",
                        "edition": "fallout-new-vegas",
                        "game": "fallout-new-vegas",
                        "engineBuild": "1.4.0.525",
                        "contentVersion": "1.4.0.525",
                        "supportedCampaigns": ["fallout-new-vegas"],
                        "semanticExtensions": {
                            "mode": "clean-room",
                            "required": [],
                            "cleanRoomCapabilities": [],
                        },
                        "sourceOrder": "low-to-high-last-wins",
                        "roots": [
                            {"id": "owned-data", "priority": 0, "root": str(data_root)},
                            {"id": "low", "priority": 1, "root": str(low_root)},
                            {"id": "high", "priority": 2, "root": str(high_root)},
                        ],
                        "looseFiles": [
                            self._loose_row(0, "low", low_root / logical, logical),
                            self._loose_row(1, "high", high_root / logical, logical),
                        ],
                        "plugins": [],
                        "archives": [],
                        "stackId": "b" * 64,
                        "saveCompatibilityId": "fallout-new-vegas:" + "b" * 64,
                    }
                ),
                encoding="utf-8",
            )
            manifest_sha256 = hashlib.sha256(manifest.read_bytes()).hexdigest()
            expected = hashlib.sha256(b"high-mod").hexdigest()
            result = subprocess.run(
                [
                    str(self.probe),
                    "--resolve-stack",
                    str(manifest),
                    str(logical),
                    "unused.bsa",
                    expected,
                    manifest_sha256,
                    "b" * 64,
                    "fallout-new-vegas",
                ],
                check=False,
                capture_output=True,
                text=True,
            )

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("OPENNV_RESOURCE_OK", result.stdout)
        self.assertIn(str(high_root), result.stdout)

    def test_runtime_reader_rejects_path_escape(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "synthetic.bsa"
            archive.write_bytes(synthetic_bsa())
            result = subprocess.run(
                [str(self.probe), str(archive), "..\\secret.dds"],
                check=False,
                capture_output=True,
                text=True,
            )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("escapes", result.stderr)

    def test_runtime_source_rejects_preferred_archive_that_is_not_active(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            inactive = data_root / "Inactive.bsa"
            inactive.write_bytes(synthetic_bsa())
            manifest = root / "mod-stack.json"
            manifest.write_text(
                json.dumps(
                    {
                        "schema": "opennv-mod-stack/v2",
                        "status": "registered-read-only-source-stack",
                        "edition": "fallout-new-vegas",
                        "game": "fallout-new-vegas",
                        "engineBuild": "1.4.0.525",
                        "contentVersion": "1.4.0.525",
                        "supportedCampaigns": ["fallout-new-vegas"],
                        "semanticExtensions": {
                            "mode": "clean-room",
                            "required": [],
                            "cleanRoomCapabilities": [],
                        },
                        "sourceOrder": "low-to-high-last-wins",
                        "roots": [
                            {"id": "owned-data", "priority": 0, "root": str(data_root)},
                        ],
                        "looseFiles": [],
                        "plugins": [],
                        "archives": [],
                        "stackId": "c" * 64,
                        "saveCompatibilityId": "fallout-new-vegas:" + "c" * 64,
                    }
                ),
                encoding="utf-8",
            )
            manifest_sha256 = hashlib.sha256(manifest.read_bytes()).hexdigest()
            result = subprocess.run(
                [
                    str(self.probe),
                    "--resolve-stack",
                    str(manifest),
                    "textures/test/first.dds",
                    inactive.name,
                    hashlib.sha256(b"compressed-owned-bytes").hexdigest(),
                    manifest_sha256,
                    "c" * 64,
                    "fallout-new-vegas",
                ],
                check=False,
                capture_output=True,
                text=True,
            )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("not active in the bound mod stack", result.stderr)

    def test_runtime_source_revalidates_bound_archive_ini_provenance(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            resource = data_root / "textures" / "test" / "winner.dds"
            resource.parent.mkdir(parents=True)
            resource.write_bytes(b"owned-loose")
            ini = root / "Fallout_default.ini"
            ini.write_text("[Archive]\nSArchiveList=Base.bsa\n", encoding="utf-8")
            ini_stat = ini.stat()
            ini_bytes = ini.read_bytes()
            manifest = root / "mod-stack.json"
            manifest.write_text(
                json.dumps(
                    {
                        "schema": "opennv-mod-stack/v2",
                        "status": "registered-read-only-source-stack",
                        "edition": "fallout-new-vegas",
                        "game": "fallout-new-vegas",
                        "engineBuild": "1.4.0.525",
                        "contentVersion": "1.4.0.525",
                        "supportedCampaigns": ["fallout-new-vegas"],
                        "semanticExtensions": {
                            "mode": "clean-room",
                            "required": [],
                            "cleanRoomCapabilities": [],
                        },
                        "sourceOrder": "low-to-high-last-wins",
                        "roots": [
                            {"id": "owned-data", "priority": 0, "root": str(data_root)},
                        ],
                        "looseFiles": [
                            self._loose_row(
                                0,
                                "owned-data",
                                resource,
                                Path("textures") / "test" / "winner.dds",
                            )
                        ],
                        "plugins": [],
                        "archives": [],
                        "archiveOrderSource": {
                            "kind": "fallout-default-ini",
                            "files": [
                                {
                                    "path": str(ini),
                                    "bytes": len(ini_bytes),
                                    "mtimeMs": int(ini_stat.st_mtime * 1000),
                                    "sha256": hashlib.sha256(ini_bytes).hexdigest(),
                                }
                            ],
                            "entries": [{"key": "SArchiveList", "file": "Base.bsa"}],
                        },
                        "stackId": "d" * 64,
                        "saveCompatibilityId": "fallout-new-vegas:" + "d" * 64,
                    }
                ),
                encoding="utf-8",
            )
            manifest_sha256 = hashlib.sha256(manifest.read_bytes()).hexdigest()
            ini.write_text("[Archive]\nSArchiveList=Changed.bsa\n", encoding="utf-8")
            result = subprocess.run(
                [
                    str(self.probe),
                    "--resolve-stack",
                    str(manifest),
                    "textures/test/winner.dds",
                    "unused.bsa",
                    hashlib.sha256(b"owned-loose").hexdigest(),
                    manifest_sha256,
                    "d" * 64,
                    "fallout-new-vegas",
                ],
                check=False,
                capture_output=True,
                text=True,
            )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Archive-order provenance", result.stderr)

    def _assert_member(self, archive: Path, logical_path: str, expected: bytes) -> None:
        sha256 = hashlib.sha256(expected).hexdigest()
        result = subprocess.run(
            [str(self.probe), str(archive), logical_path, sha256],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(f"bytes={len(expected)}", result.stdout)
        self.assertIn(f"sha256={sha256}", result.stdout)

    @staticmethod
    def _loose_row(index: int, root_id: str, source: Path, logical: Path) -> dict[str, object]:
        metadata = source.stat()
        return {
            "index": index,
            "rootId": root_id,
            "path": logical.as_posix(),
            "bytes": metadata.st_size,
            "mtimeMs": metadata.st_mtime_ns // 1_000_000,
        }


if __name__ == "__main__":
    unittest.main()
