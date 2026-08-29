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
from prepare_fo2_character_start import (  # noqa: E402
    CACHE_MANIFEST_NAME,
    parse_fo2_premade_gcd,
    prepare_fo2_character_start,
)
from content.tests.test_fo2_first_slice import synthetic_dat2, synthetic_frm  # noqa: E402
from content.tests.test_fo2_player_presentation import synthetic_walk_frm  # noqa: E402


def gcd(name: str, sex: int, special: list[int], tags: list[int], traits: list[int]) -> bytes:
    values = [0] * 108
    values[1:8] = special
    values[34] = 25
    values[35] = sex
    values[101:104] = tags
    values[104] = -1
    values[105:107] = (traits + [-1, -1])[:2]
    data = bytearray(struct.pack(">108i", *values))
    encoded = name.encode("cp1252")
    data[372 : 372 + len(encoded)] = encoded
    data[372 + len(encoded)] = 0
    return bytes(data)


class Fo2CharacterStartTest(unittest.TestCase):
    def test_transports_three_premades_and_female_idle_without_source_payloads(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            install = root / "Fallout 2"
            install.mkdir()
            frm = synthetic_frm()
            walk_frm = synthetic_walk_frm()
            palette = bytearray(768)
            palette[3:6] = bytes((12, 24, 36))
            profiles = {
                "combat": gcd("Narg", 0, [8, 5, 9, 3, 4, 7, 4], [4, 0, 5], [6, 15]),
                "stealth": gcd("Mingan", 0, [5, 8, 4, 4, 5, 9, 5], [8, 9, 10], [14, 2]),
                "diplomat": gcd("Chitsa", 1, [4, 5, 4, 10, 7, 6, 4], [14, 15, 6], [3, 13]),
            }
            bio = b"Owned synthetic biography text. " * 4
            critter_lines = [f"unused{index:03d}" for index in range(61)] + [
                "hfprim,11,1"
            ]
            female_prototype = struct.pack(">III", 0x01000002, 101, 0x0100003D)
            master_members = [
                ("color.pal", bytes(palette), False),
                (
                    "proto\\critters\\critters.lst",
                    b"00000001.pro\r\n00000002.pro\r\n",
                    False,
                ),
                ("proto\\critters\\00000002.pro", female_prototype, False),
            ]
            for asset in ("pickchar", "combat", "stealth", "diplomat"):
                master_members.append((f"art\\intrface\\{asset}.frm", frm, True))
            for identity, data in profiles.items():
                master_members.extend(
                    [
                        (f"premade\\{identity}.gcd", data, False),
                        (f"premade\\{identity}.bio", bio, False),
                    ]
                )
            (install / "master.dat").write_bytes(synthetic_dat2(master_members))
            (install / "critter.dat").write_bytes(
                synthetic_dat2(
                    [
                        (
                            "art\\critters\\critters.lst",
                            ("\r\n".join(critter_lines) + "\r\n").encode("ascii"),
                            False,
                        ),
                        ("art\\critters\\hfprimaa.frm", frm, True),
                        ("art\\critters\\hfprimab.frm", walk_frm, True),
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
            recipe_path = root / "synthetic-character-start.json"
            premades = []
            for identity, name, role in (
                ("combat", "Narg", "combat"),
                ("stealth", "Mingan", "stealth"),
                ("diplomat", "Chitsa", "diplomat"),
            ):
                premades.append(
                    {
                        "id": identity,
                        "name": name,
                        "role": role,
                        "gcd": {
                            "logicalPath": f"premade\\{identity}.gcd",
                            "sha256": hashlib.sha256(profiles[identity]).hexdigest(),
                        },
                        "bio": {
                            "logicalPath": f"premade\\{identity}.bio",
                            "sha256": hashlib.sha256(bio).hexdigest(),
                        },
                        "panel": {
                            "logicalPath": f"art\\intrface\\{identity}.frm",
                            "sha256": hashlib.sha256(frm).hexdigest(),
                            "width": 1,
                            "height": 1,
                            "frame": 0,
                        },
                    }
                )
            recipe_path.write_text(
                json.dumps(
                    {
                        "schema": "opennv-fo2-character-start-recipe/v1",
                        "id": recipe_path.stem,
                        "campaign": "Fallout2",
                        "sourceProfileSchema": "opennv-fo2-owned-profile/v1",
                        "overlayOrderHighToLow": [
                            "patch000.dat",
                            "critter.dat",
                            "master.dat",
                        ],
                        "palette": {
                            "logicalPath": "color.pal",
                            "sha256": hashlib.sha256(bytes(palette)).hexdigest(),
                        },
                        "picker": {
                            "logicalPath": "art\\intrface\\pickchar.frm",
                            "sha256": hashlib.sha256(frm).hexdigest(),
                            "width": 1,
                            "height": 1,
                            "frame": 0,
                        },
                        "premades": premades,
                        "femalePresentation": {
                            "critterListLogicalPath": "art\\critters\\critters.lst",
                            "artIndex": 61,
                            "artListEntry": "hfprim,11,1",
                            "fid": "0100003d",
                            "prototypeListLogicalPath": "proto\\critters\\critters.lst",
                            "prototypeListIndex": 2,
                            "prototypeListEntry": "00000002.pro",
                            "prototypeLogicalPath": "proto\\critters\\00000002.pro",
                            "prototypePid": "01000002",
                            "logicalPath": "art\\critters\\hfprimaa.frm",
                            "frame": 0,
                            "directions": list(range(6)),
                            "walkAnimationCode": "AB",
                            "walkLogicalPath": "art\\critters\\hfprimab.frm",
                            "walkFrames": list(range(8)),
                            "walkFps": 10,
                        },
                        "presentation": {
                            "viewport": [640, 480],
                            "panel": [24, 20, 592, 260],
                            "layoutStatus": "synthetic",
                        },
                        "unsupported": ["custom editor"],
                    }
                ),
                encoding="utf-8",
            )

            first = prepare_fo2_character_start(
                profile_path,
                root / "cache-a",
                recipe_path,
            )
            second = prepare_fo2_character_start(
                profile_path,
                root / "cache-b",
                recipe_path,
            )

            self.assertEqual(
                [row["profile"]["name"] for row in first["characters"]],
                ["Narg", "Mingan", "Chitsa"],
            )
            self.assertEqual(first["characters"][2]["profile"]["sex"], "Female")
            self.assertEqual(first["characters"][2]["profile"]["allocatedSpecial"], [4, 5, 4, 10, 7, 6, 4])
            self.assertEqual(first["femalePresentation"]["fid"], "0100003d")
            self.assertEqual(len(first["femalePresentation"]["directions"]), 6)
            self.assertEqual(first["femalePresentation"]["prototype"]["pid"], "01000002")
            self.assertEqual(first["femalePresentation"]["prototype"]["fid"], "0100003d")
            self.assertEqual(first["femalePresentation"]["walkArt"]["fps"], 10)
            self.assertEqual(first["femalePresentation"]["walkArt"]["framesPerDirection"], 8)
            self.assertEqual(
                len(first["femalePresentation"]["walkArt"]["directions"]),
                48,
            )
            self.assertEqual(
                [row["pngSha256"] for row in first["femalePresentation"]["directions"]],
                [row["pngSha256"] for row in second["femalePresentation"]["directions"]],
            )
            self.assertTrue((root / "cache-a" / CACHE_MANIFEST_NAME).is_file())
            self.assertFalse(any((root / "cache-a").rglob("*.gcd")))
            self.assertFalse(any((root / "cache-a").rglob("*.bio")))
            self.assertFalse(first["cachePolicy"]["distributionAllowed"])
            with self.assertRaisesRegex(Exception, "432 bytes"):
                parse_fo2_premade_gcd(profiles["combat"][:-4])


if __name__ == "__main__":
    unittest.main()
