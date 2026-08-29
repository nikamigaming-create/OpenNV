from __future__ import annotations

import copy
import hashlib
import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo2_profile import inspect_fo2_profile  # noqa: E402
from prepare_fo2_temple_presentation import (  # noqa: E402
    CACHE_MANIFEST_NAME,
    FLOOR_ALPHA_FILL,
    FLOOR_PROJECTION_MODE,
    FLOOR_UNPROJECTED_TEXTURE_SIZE,
    _derive_map_presentation_graph,
    prepare_fo2_map_presentation,
    prepare_fo2_temple_presentation,
)
from plugin_stack import file_sha256  # noqa: E402
from fo1_map_objects import Fo1ResourceResolver  # noqa: E402
from content.tests.test_fo2_first_slice import (  # noqa: E402
    synthetic_dat2,
    synthetic_frm,
    synthetic_map,
)


def synthetic_caves_map() -> bytes:
    data = bytearray(synthetic_map())
    data[4:20] = b"ARCAVES.MAP\0\0\0\0\0"
    struct.pack_into(">i", data, 0x34, 3)
    return bytes(data)


def synthetic_floor_frm() -> bytes:
    width = 80
    height = 36
    pixels = bytearray(width * height)
    for y in range(height):
        for x in range(width):
            normalized_x = abs((x - (width - 1) / 2.0) / (width / 2.0))
            normalized_y = abs((y - (height - 1) / 2.0) / (height / 2.0))
            if normalized_x + normalized_y <= 1.0:
                pixels[y * width + x] = 1
    header = bytearray(0x3E)
    struct.pack_into(">IHHH", header, 0, 4, 10, 0, 1)
    struct.pack_into(">6h", header, 0x0A, 0, 0, 0, 0, 0, 0)
    struct.pack_into(">6h", header, 0x16, 0, 0, 0, 0, 0, 0)
    struct.pack_into(">6I", header, 0x22, 0, 0, 0, 0, 0, 0)
    frame = struct.pack(">HHIhh", width, height, len(pixels), 0, 0) + pixels
    struct.pack_into(">I", header, 0x3A, len(frame))
    return bytes(header) + frame


class Fo2TemplePresentationTest(unittest.TestCase):
    def test_decodes_only_admitted_tile_and_object_frames_deterministically(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            install = root / "Fallout 2"
            install.mkdir()
            tile_frm = synthetic_floor_frm()
            object_frm = synthetic_frm()
            temple_map = synthetic_map()
            caves_map = synthetic_caves_map()
            prototype = bytearray(0x24)
            struct.pack_into(">III", prototype, 0, 0x02000001, 100, 0x02000000)
            struct.pack_into(">i", prototype, 0x20, 5)
            palette = bytearray(768)
            palette[3:6] = bytes((8, 16, 24))
            (install / "master.dat").write_bytes(
                synthetic_dat2(
                    [
                        ("color.pal", bytes(palette), False),
                        ("art\\tiles\\tiles.lst", b"unused.frm\r\ntemple.frm\r\n", False),
                        ("art\\tiles\\temple.frm", tile_frm, True),
                        ("proto\\scenery\\scenery.lst", b"test.pro\r\n", False),
                        ("proto\\scenery\\test.pro", bytes(prototype), False),
                        ("art\\scenery\\scenery.lst", b"test.frm\r\n", False),
                        ("art\\scenery\\test.frm", object_frm, True),
                        ("maps\\artemple.map", temple_map, True),
                        ("maps\\arcaves.map", caves_map, True),
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
            resolver = Fo1ResourceResolver(
                None,
                install / "patch000.dat",
                [install / "critter.dat", install / "master.dat"],
            )
            with resolver.access_scope():
                temple_graph, temple_placements = _derive_map_presentation_graph(
                    temple_map,
                    resolver,
                )
                caves_graph, caves_placements = _derive_map_presentation_graph(
                    caves_map,
                    resolver,
                )
            object_sha = hashlib.sha256(object_frm).hexdigest()
            source_path = root / "temple-source.json"
            source = {
                "schema": "opennv-fo2-first-slice/v2",
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
                    "logicalPath": "maps\\artemple.map",
                    "source": "fallout2-master-dat:maps\\artemple.map",
                    "bytes": len(temple_map),
                    "sha256": hashlib.sha256(temple_map).hexdigest(),
                    **temple_graph,
                },
                "frms": [
                    {
                        "logicalPath": "art\\scenery\\test.frm",
                        "source": "fallout2-master-dat:art\\scenery\\test.frm",
                        "bytes": len(object_frm),
                        "sha256": object_sha,
                        "placements": temple_placements["art\\scenery\\test.frm"],
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
            tile_artifact = next(
                row for row in first["artifacts"] if row["kind"] == "tiles"
            )
            self.assertEqual(
                tile_artifact["floorProjection"],
                {
                    "mode": FLOOR_PROJECTION_MODE,
                    "sourceWidth": 80,
                    "sourceHeight": 36,
                    "outputSizePixels": FLOOR_UNPROJECTED_TEXTURE_SIZE,
                    "alphaFill": FLOOR_ALPHA_FILL,
                },
            )
            self.assertEqual(
                [tile_artifact["width"], tile_artifact["height"]],
                [FLOOR_UNPROJECTED_TEXTURE_SIZE, FLOOR_UNPROJECTED_TEXTURE_SIZE],
            )
            tile_png = Image.open(root / "cache-a" / tile_artifact["png"]).convert("RGBA")
            self.assertEqual(tile_png.getextrema()[3], (255, 255))
            self.assertEqual(
                [row["pngSha256"] for row in first["artifacts"]],
                [row["pngSha256"] for row in second["artifacts"]],
            )
            self.assertFalse(first["runtimeCompatibility"]["ready"])
            self.assertFalse(first["cachePolicy"]["distributionAllowed"])
            self.assertTrue((root / "cache-a" / CACHE_MANIFEST_NAME).is_file())
            self.assertEqual(len(list((root / "cache-a" / "assets").rglob("*.png"))), 2)

            source["map"]["layout"]["elevations"][0]["rawEntries"][0] = 0x00010000
            source_path.write_text(json.dumps(source), encoding="utf-8")
            with self.assertRaisesRegex(Exception, "graph differs from owned bytes"):
                prepare_fo2_temple_presentation(
                    profile_path,
                    source_path,
                    root / "rejected-mutated-layout",
                )
            source["map"].update(copy.deepcopy(temple_graph))

            source["map"]["objects"]["elevations"][0]["objects"][0]["flags"] = "00000001"
            source_path.write_text(json.dumps(source), encoding="utf-8")
            with self.assertRaisesRegex(Exception, "graph differs from owned bytes"):
                prepare_fo2_temple_presentation(
                    profile_path,
                    source_path,
                    root / "rejected-mutated-object",
                )
            source["map"].update(copy.deepcopy(temple_graph))

            source["schema"] = "opennv-fo2-owned-map-slice/v1"
            source["status"] = "transported-owned-map-source-and-presentation-graph"
            source["slice"] = "ArroyoCaves"
            source_path.write_text(json.dumps(source), encoding="utf-8")
            with self.assertRaisesRegex(Exception, "does not match Map 3"):
                prepare_fo2_map_presentation(
                    profile_path,
                    source_path,
                    root / "rejected-mislabeled-cache",
                    source_schema=source["schema"],
                    source_status=source["status"],
                    source_slice=source["slice"],
                    cache_schema="opennv-fo2-arroyo-caves-presentation-cache/v1",
                    cache_manifest_name="fo2-arroyo-caves-presentation-cache.json",
                    map_index=3,
                    map_name="ARCAVES.MAP",
                    map_logical_path="maps\\arcaves.map",
                    map_label="Arroyo Caves",
                )

            source["map"]["logicalPath"] = "maps\\arcaves.map"
            source["map"]["source"] = "fallout2-master-dat:maps\\arcaves.map"
            source["map"]["bytes"] = len(caves_map)
            source["map"]["sha256"] = hashlib.sha256(caves_map).hexdigest()
            source["map"].update(copy.deepcopy(caves_graph))
            source["frms"][0]["placements"] = caves_placements["art\\scenery\\test.frm"]
            source_path.write_text(json.dumps(source), encoding="utf-8")
            caves = prepare_fo2_map_presentation(
                profile_path,
                source_path,
                root / "cache-caves",
                source_schema=source["schema"],
                source_status=source["status"],
                source_slice=source["slice"],
                cache_schema="opennv-fo2-arroyo-caves-presentation-cache/v1",
                cache_manifest_name="fo2-arroyo-caves-presentation-cache.json",
                map_index=3,
                map_name="ARCAVES.MAP",
                map_logical_path="maps\\arcaves.map",
                map_label="Arroyo Caves",
            )

            self.assertEqual(caves["slice"], "ArroyoCaves")
            self.assertEqual(
                caves["schema"], "opennv-fo2-arroyo-caves-presentation-cache/v1"
            )
            self.assertIn("Map 3", caves["admission"]["tiles"])
            self.assertFalse(caves["runtimeCompatibility"]["ready"])


if __name__ == "__main__":
    unittest.main()
