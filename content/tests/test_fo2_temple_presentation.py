from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo2_profile import inspect_fo2_profile  # noqa: E402
from prepare_fo2_temple_presentation import (  # noqa: E402
    CACHE_MANIFEST_NAME,
    prepare_fo2_temple_presentation,
)
from plugin_stack import file_sha256  # noqa: E402
from content.tests.test_fo2_first_slice import synthetic_dat2, synthetic_frm  # noqa: E402


class Fo2TemplePresentationTest(unittest.TestCase):
    def test_decodes_only_admitted_tile_and_object_frames_deterministically(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            install = root / "Fallout 2"
            install.mkdir()
            tile_frm = synthetic_frm()
            object_frm = synthetic_frm()
            palette = bytearray(768)
            palette[3:6] = bytes((8, 16, 24))
            (install / "master.dat").write_bytes(
                synthetic_dat2(
                    [
                        ("color.pal", bytes(palette), False),
                        ("art\\tiles\\tiles.lst", b"unused.frm\r\ntemple.frm\r\n", False),
                        ("art\\tiles\\temple.frm", tile_frm, True),
                        ("art\\scenery\\temple01.frm", object_frm, True),
                    ]
                )
            )
            (install / "critter.dat").write_bytes(
                synthetic_dat2([("art\\critters\\unused.frm", b"unused", False)])
            )
            (install / "patch000.dat").write_bytes(
                synthetic_dat2([("data\\maps.txt", b"[Map 126]\r\n", False)])
            )
            profile_path = root / "fallout2-profile.json"
            profile = inspect_fo2_profile(install, "synthetic")
            profile_path.write_text(json.dumps(profile), encoding="utf-8")
            object_sha = hashlib.sha256(object_frm).hexdigest()
            source_path = root / "temple-source.json"
            source = {
                "schema": "opennv-fo2-first-slice/v1",
                "status": "transported-source-manifest",
                "campaign": "Fallout2",
                "slice": "TempleOfTrials",
                "sourceProfile": {
                    "sourceProfileId": profile["sourceProfileId"],
                    "saveCompatibilityId": profile["saveCompatibilityId"],
                    "sha256": file_sha256(profile_path),
                },
                "overlayOrderHighToLow": ["patch000.dat", "critter.dat", "master.dat"],
                "map": {
                    "sha256": "0" * 64,
                    "layout": {
                        "elevations": [
                            {
                                "elevation": 0,
                                "rawEntries": [0x00010001] * 10000,
                            }
                        ]
                    },
                },
                "frms": [
                    {
                        "logicalPath": "art\\scenery\\temple01.frm",
                        "source": "fallout2-master-dat:art\\scenery\\temple01.frm",
                        "bytes": len(object_frm),
                        "sha256": object_sha,
                        "placements": [
                            {
                                "serial": 1,
                                "fid": "02000000",
                                "frame": 0,
                                "rotation": 0,
                                "elevation": 0,
                                "tile": 18493,
                            }
                        ],
                    }
                ],
                "promotion": {"transported": True},
                "runtimeCompatibility": {"ready": False},
                "retailOrDerivedAssetsPackaged": False,
                "generatedCaches": [],
            }
            source_path.write_text(json.dumps(source), encoding="utf-8")

            first = prepare_fo2_temple_presentation(
                profile_path,
                source_path,
                root / "cache-a",
            )
            second = prepare_fo2_temple_presentation(
                profile_path,
                source_path,
                root / "cache-b",
            )

            self.assertEqual(first["counts"]["tileIds"], 1)
            self.assertEqual(first["counts"]["objectFrmIdentities"], 1)
            self.assertEqual(first["counts"]["pngArtifacts"], 2)
            self.assertEqual(
                [row["pngSha256"] for row in first["artifacts"]],
                [row["pngSha256"] for row in second["artifacts"]],
            )
            self.assertFalse(first["runtimeCompatibility"]["ready"])
            self.assertFalse(first["cachePolicy"]["distributionAllowed"])
            self.assertTrue((root / "cache-a" / CACHE_MANIFEST_NAME).is_file())
            self.assertEqual(len(list((root / "cache-a" / "assets").rglob("*.png"))), 2)


if __name__ == "__main__":
    unittest.main()
