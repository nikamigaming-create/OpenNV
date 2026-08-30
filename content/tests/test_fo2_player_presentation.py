from __future__ import annotations

import hashlib
import json
import struct
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


def synthetic_walk_frm() -> bytes:
    header = bytearray(0x3E)
    struct.pack_into(">IHHH", header, 0, 4, 10, 0, 8)
    struct.pack_into(">6h", header, 0x0A, 0, 0, 0, 0, 0, 0)
    struct.pack_into(">6h", header, 0x16, 0, 0, 0, 0, 0, 0)
    struct.pack_into(">6I", header, 0x22, 0, 0, 0, 0, 0, 0)
    frames = b"".join(
        struct.pack(">HHIhh", 1, 1, 1, frame, -frame) + bytes((frame + 1,))
        for frame in range(8)
    )
    struct.pack_into(">I", header, 0x3A, len(frames))
    return bytes(header) + frames


def recipe(path: Path) -> None:
    relief = json.loads(
        (TOOLS.parent / "recipes" / "fo2-arroyo-player-presentation-v1.json")
        .read_text(encoding="utf-8")
    )["player"]["relief3d"]
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
                    "prototypeListLogicalPath": "proto\\critters\\critters.lst",
                    "prototypeListIndex": 1,
                    "prototypeListEntry": "00000001.pro",
                    "prototypeLogicalPath": "proto\\critters\\00000001.pro",
                    "prototypePid": "01000001",
                    "idleFrmLogicalPath": "art\\critters\\hmwarraa.frm",
                    "frame": 0,
                    "directions": [0, 1, 2, 3, 4, 5],
                    "walkAnimationCode": "AB",
                    "walkFrmLogicalPath": "art\\critters\\hmwarrab.frm",
                    "walkFrames": list(range(8)),
                    "walkFps": 10,
                    "equippedWeapon": {
                        "role": "Spear-equipped Chosen One source animation",
                        "itemFid": "0000002a",
                        "itemPid": "00000007",
                        "weaponAnimationCode": 4,
                        "weaponArtSuffix": "g",
                        "idleAnimationCode": "GA",
                        "idleFrmLogicalPath": "art\\critters\\hmwarrga.frm",
                        "idleFrame": 0,
                        "walkAnimationCode": "GB",
                        "walkFrmLogicalPath": "art\\critters\\hmwarrgb.frm",
                        "walkFrames": list(range(8)),
                        "walkFps": 10,
                        "geometryDisposition": (
                            "owned-critter-frm-composites-player-and-spear-"
                            "no-separable-3d-weapon-transform"
                        ),
                    },
                    "relief3d": relief,
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
            opaque_frm = bytearray(synthetic_frm())
            opaque_frm[-1] = 1
            frm = bytes(opaque_frm)
            walk_frm = synthetic_walk_frm()
            prototype = struct.pack(">III", 0x01000001, 100, 0x0100003E)
            (install / "master.dat").write_bytes(
                synthetic_dat2(
                    [
                        ("color.pal", bytes(palette), False),
                        ("proto\\critters\\critters.lst", b"00000001.pro\r\n", False),
                        ("proto\\critters\\00000001.pro", prototype, False),
                    ]
                )
            )
            (install / "critter.dat").write_bytes(
                synthetic_dat2(
                    [
                        ("art\\critters\\critters.lst", critter_list, False),
                        ("art\\critters\\hmwarraa.frm", frm, True),
                        ("art\\critters\\hmwarrab.frm", walk_frm, True),
                        ("art\\critters\\hmwarrga.frm", frm, True),
                        ("art\\critters\\hmwarrgb.frm", walk_frm, True),
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
            self.assertEqual(first["prototype"]["pid"], "01000001")
            self.assertEqual(first["prototype"]["fid"], "0100003e")
            self.assertEqual(first["walkArt"]["logicalPath"], "art\\critters\\hmwarrab.frm")
            self.assertEqual(first["walkArt"]["framesPerDirection"], 8)
            self.assertEqual(first["walkArt"]["fps"], 10)
            self.assertTrue(first["walkArt"]["animationPlayback"])
            equipped = first["equippedWeaponArt"]
            self.assertEqual(equipped["itemFid"], "0000002a")
            self.assertEqual(equipped["itemPid"], "00000007")
            self.assertEqual(equipped["weaponAnimationCode"], 4)
            self.assertEqual(equipped["weaponArtSuffix"], "g")
            self.assertEqual(equipped["idle"]["animationCode"], "GA")
            self.assertEqual(
                equipped["idle"]["logicalPath"], "art\\critters\\hmwarrga.frm"
            )
            self.assertEqual(equipped["walk"]["animationCode"], "GB")
            self.assertEqual(
                equipped["walk"]["logicalPath"], "art\\critters\\hmwarrgb.frm"
            )
            self.assertEqual(
                [(row["rotation"], row["frame"]) for row in first["artifacts"]],
                [(direction, 0) for direction in range(6)]
                + [(direction, frame) for direction in range(6) for frame in range(8)]
                + [(direction, 0) for direction in range(6)]
                + [(direction, frame) for direction in range(6) for frame in range(8)],
            )
            self.assertEqual(
                [row["pngSha256"] for row in first["artifacts"]],
                [row["pngSha256"] for row in second["artifacts"]],
            )
            for artifact in first["artifacts"]:
                relief = artifact["relief3d"]
                self.assertEqual(
                    relief["sourceOpaquePixels"], relief["solidOpaquePixels"]
                )
                self.assertTrue(relief["islandCount"])
                self.assertEqual(
                    relief["solidOpaquePixels"],
                    sum(island["opaquePixels"] for island in relief["islands"]),
                )
            self.assertFalse(first["cachePolicy"]["distributionAllowed"])
            self.assertTrue(first["cachePolicy"]["containsDerivedOwnedPixels"])
            self.assertTrue((root / "cache-a" / CACHE_MANIFEST_NAME).is_file())
            expected_pngs = {
                row[key]
                for row in first["artifacts"]
                for key in ("png",)
            } | {
                row["relief3d"][key]
                for row in first["artifacts"]
                for key in ("normalPng", "solidMaskPng", "depthPng")
            }
            actual_pngs = {
                path.relative_to(root / "cache-a").as_posix()
                for path in (root / "cache-a" / "assets").rglob("*.png")
            }
            self.assertEqual(actual_pngs, expected_pngs)

            changed_lines = critter_lines.copy()
            changed_lines[62] = "not-hmwarr"
            changed_list = ("\r\n".join(changed_lines) + "\r\n").encode("ascii")
            (install / "critter.dat").write_bytes(
                synthetic_dat2(
                    [
                        ("art\\critters\\critters.lst", changed_list, False),
                        ("art\\critters\\hmwarraa.frm", frm, True),
                        ("art\\critters\\hmwarrab.frm", walk_frm, True),
                        ("art\\critters\\hmwarrga.frm", frm, True),
                        ("art\\critters\\hmwarrgb.frm", walk_frm, True),
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
