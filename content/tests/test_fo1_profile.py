from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from cell_catalog import BaseObject, Cell, CellCatalog, PlacedReference, Transform  # noqa: E402
from fo1_profile import (  # noqa: E402
    MAP_HEADER_SIZE,
    Fo1ProfileError,
    map_layout_manifest,
    parse_map_header,
    parse_map_layout,
    resolve_donor_cells,
    resolve_owned_path,
)


def synthetic_map_header(version: int = 20) -> bytes:
    data = bytearray(MAP_HEADER_SIZE)
    struct.pack_into(">i", data, 0x00, version)
    data[0x04:0x14] = b"V13ENT.MAP\0" + bytes(5)
    struct.pack_into(">10i", data, 0x14, 20090, 0, 0, 0, 430, 12, 1, 8, 35, 0)
    return bytes(data)


def synthetic_map() -> bytes:
    data = bytearray(synthetic_map_header())
    data.extend(struct.pack(">8i", 1, 2, 3, 4, 5, 6, 7, 8))
    entries = [(1 << 16) | 1] * 10000
    entries[123] = (45 << 16) | 70
    data.extend(struct.pack(">10000I", *entries))
    data.extend(b"scripts-and-objects")
    return bytes(data)


class Fo1ProfileTest(unittest.TestCase):
    def test_map_header_is_big_endian_and_exact(self) -> None:
        header = parse_map_header(synthetic_map_header())
        self.assertEqual(header.version, 20)
        self.assertEqual(header.name, "V13ENT.MAP")
        self.assertEqual(header.enteringTile, 20090)
        self.assertEqual(header.enteringElevation, 0)
        self.assertEqual(header.enteringRotation, 0)
        self.assertEqual(header.scriptIndex, 430)
        self.assertEqual(header.flags, 12)
        self.assertEqual(header.mapIndex, 35)

    def test_map_header_fails_closed(self) -> None:
        with self.assertRaises(Fo1ProfileError):
            parse_map_header(bytes(MAP_HEADER_SIZE - 1))
        with self.assertRaises(Fo1ProfileError):
            parse_map_header(synthetic_map_header(21))
        data = bytearray(synthetic_map_header())
        struct.pack_into(">i", data, 0x14, 40000)
        with self.assertRaises(Fo1ProfileError):
            parse_map_header(bytes(data))

    def test_unassigned_template_map_index_is_valid(self) -> None:
        data = bytearray(synthetic_map_header())
        struct.pack_into(">i", data, 0x34, -1)
        self.assertEqual(parse_map_header(bytes(data)).mapIndex, -1)

    def test_map_layout_transports_variables_and_present_elevation(self) -> None:
        layout = parse_map_layout(synthetic_map())
        self.assertEqual(layout.global_variables, (1, 2, 3, 4, 5, 6, 7, 8))
        self.assertEqual(layout.local_variables, ())
        self.assertEqual(len(layout.elevations), 1)
        self.assertEqual(layout.elevations[0].entries[123], (45 << 16) | 70)
        manifest = map_layout_manifest(layout)
        self.assertEqual(manifest["presentElevations"], [0])
        self.assertEqual(manifest["elevations"][0]["uniqueFloorIds"], 2)
        self.assertEqual(manifest["elevations"][0]["nonDefaultFloorCount"], 1)
        self.assertEqual(manifest["elevations"][0]["uniqueRoofIds"], 2)
        self.assertEqual(manifest["elevations"][0]["nonDefaultRoofCount"], 1)
        self.assertEqual(manifest["scriptsOffset"], MAP_HEADER_SIZE + 32 + 40000)

    def test_map_layout_rejects_truncated_tiles(self) -> None:
        with self.assertRaises(Fo1ProfileError):
            parse_map_layout(synthetic_map_header() + struct.pack(">8i", *([0] * 8)))

    def test_owned_path_cannot_escape_root(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            root = Path(raw_directory)
            (root / "safe.map").write_bytes(b"map")
            self.assertEqual(resolve_owned_path(root, "safe.map"), (root / "safe.map").resolve())
            with self.assertRaises(Fo1ProfileError):
                resolve_owned_path(root, "../escape.map")
            with self.assertRaises(Fo1ProfileError):
                resolve_owned_path(root, "C:/escape.map")

    def test_donor_catalog_resolves_identity_and_counts(self) -> None:
        cell = Cell(0x100, "SyntheticDonor", 1, None, None)
        cave = BaseObject(0x200, "STAT", "SyntheticCave", "dungeons\\caves\\room.nif")
        excluded = BaseObject(0x201, "STAT", "Excluded", "architecture\\urban\\wall.nif")
        catalog = CellCatalog(
            {cell.form_id: cell},
            {cave.form_id: cave, excluded.form_id: excluded},
            {},
            {},
            {},
            [
                PlacedReference(0x300, 0x100, 0x200, 0, Transform((0, 0, 0), (0, 0, 0)), None, None),
                PlacedReference(0x301, 0x100, 0x200, 0, Transform((1, 0, 0), (0, 0, 0)), None, None),
                PlacedReference(0x302, 0x100, 0x201, 0, Transform((2, 0, 0), (0, 0, 0)), None, None),
            ],
        )
        recipe = {
            "assetSource": {
                "donorCells": [
                    {
                        "role": "cave-kit",
                        "cellEditorId": "SyntheticDonor",
                        "cellFormId": "00000100",
                        "modelPrefixes": ["dungeons\\caves\\"],
                        "expectedPlacementCount": 2,
                        "expectedUniqueBaseCount": 1,
                    }
                ]
            }
        }
        resolved = resolve_donor_cells(recipe, catalog)
        self.assertEqual(resolved[0]["placementCount"], 2)
        self.assertEqual(resolved[0]["uniqueBaseCount"], 1)
        self.assertEqual(resolved[0]["bases"][0]["formId"], "00000200")

        recipe["assetSource"]["donorCells"][0]["expectedPlacementCount"] = 3
        with self.assertRaises(Fo1ProfileError):
            resolve_donor_cells(recipe, catalog)


if __name__ == "__main__":
    unittest.main()
