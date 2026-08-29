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
from prepare_fo2_player_presentation import (  # noqa: E402
    CACHE_MANIFEST_NAME,
    prepare_fo2_player_presentation,
)
from content.tests.test_fo2_first_slice import synthetic_dat2, synthetic_frm  # noqa: E402


def recipe(path: Path) -> None:
    path.write_text(
        json.dumps(
            {
                "schema": "opennv-fo2-player-presentation-recipe/v1",
                "id": path.stem,
                "campaign": "Fallout2",
                "sourceProfileSchema": "opennv-fo2-owned-profile/v1",
                "overlayOrderHighToLow": [
                    "patch000.dat",
                    "critter.dat",
                    "master.dat",
                ],
                "player": {
                    "role": "Chosen One male tribal source presentation",
                    "critterListLogicalPath": "art\\critters\\critters.lst",
                    "artIndex": 62,
                    "artListEntry": "hmwarr,11,1",
                    "objectType": 1,
                    "fid": "0100003e",
                    "idleFrmLogicalPath": "art\\critters\\hmwarraa.frm",
                    "frame": 0,
                    "directions": [0, 1, 2, 3, 4, 5],
                },
                "unsupported": ["gameplay"],
            }
        ),
        encoding="utf-8",
    )


class Fo2PlayerPresentationTest(unittest.TestCase):
    def test_decodes_exact_hmwarr_idle_directions_and_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            install = root / "Fallout 2"
            install.mkdir()
            palette = bytearray(768)
            palette[3:6] = bytes((12, 24, 36))
            critter_lines = [f"unused{index:03d}" for index in range(62)] + [
                "hmwarr,11,1"
            ]
            critter_list = ("\r\n".join(critter_lines) + "\r\n").encode("ascii")
            frm = synthetic_frm()
            (install / "master.dat").write_bytes(
                synthetic_dat2([("color.pal", bytes(palette), False)])
            )
            (install / "critter.dat").write_bytes(
                synthetic_dat2(
                    [
                        ("art\\critters\\critters.lst", critter_list, False),
                        ("art\\critters\\hmwarraa.frm", frm, True),
                    ]
                )
            )
            (install / "patch000.dat").write_bytes(
                synthetic_dat2([("data\\maps.txt", b"[Map 3]\r\n", False)])
            )
            profile_path = root / "fallout2-profile.json"
            profile_path.write_text(
                json.dumps(inspect_fo2_profile(install, "synthetic")),
                encoding="utf-8",
            )
            recipe_path = root / "synthetic-player.json"
            recipe(recipe_path)

            first = prepare_fo2_player_presentation(
                profile_path,
                root / "cache-a",
                recipe_path,
            )
            second = prepare_fo2_player_presentation(
                profile_path,
                root / "cache-b",
                recipe_path,
            )

            self.assertEqual(first["idleArt"]["fid"], "0100003e")
            self.assertEqual(first["idleArt"]["logicalPath"], "art\\critters\\hmwarraa.frm")
            self.assertEqual(first["idleArt"]["sha256"], hashlib.sha256(frm).hexdigest())
            self.assertEqual(first["idleArt"]["admittedDirections"], list(range(6)))
            self.assertFalse(first["idleArt"]["animationPlayback"])
            self.assertEqual(
                [(row["rotation"], row["frame"]) for row in first["artifacts"]],
                [(direction, 0) for direction in range(6)],
            )
            self.assertEqual(
                [row["pngSha256"] for row in first["artifacts"]],
                [row["pngSha256"] for row in second["artifacts"]],
            )
            self.assertFalse(first["cachePolicy"]["distributionAllowed"])
            self.assertTrue(first["cachePolicy"]["containsDerivedOwnedPixels"])
            self.assertTrue((root / "cache-a" / CACHE_MANIFEST_NAME).is_file())
            self.assertEqual(len(list((root / "cache-a" / "assets").rglob("*.png"))), 6)

            changed_lines = critter_lines.copy()
            changed_lines[62] = "not-hmwarr"
            changed_list = ("\r\n".join(changed_lines) + "\r\n").encode("ascii")
            (install / "critter.dat").write_bytes(
                synthetic_dat2(
                    [
                        ("art\\critters\\critters.lst", changed_list, False),
                        ("art\\critters\\hmwarraa.frm", frm, True),
                    ]
                )
            )
            profile_path.write_text(
                json.dumps(inspect_fo2_profile(install, "synthetic-changed")),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(Exception, "critters.lst entry drifted"):
                prepare_fo2_player_presentation(
                    profile_path,
                    root / "rejected-list-entry",
                    recipe_path,
                )


if __name__ == "__main__":
    unittest.main()
