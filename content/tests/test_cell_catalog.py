from __future__ import annotations

import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from cell_catalog import scan_cell_catalog  # noqa: E402
from cell_scene import godot_position  # noqa: E402
from plugin_records import COMPRESSED_RECORD_FLAG, PluginFormatError, iter_plugin_records  # noqa: E402


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, data: bytes, flags: int = 0) -> bytes:
    stored = data
    if flags & COMPRESSED_RECORD_FLAG:
        stored = struct.pack("<I", len(data)) + zlib.compress(data)
    return struct.pack("<4s4I2H", signature.encode("ascii"), len(stored), flags, form_id, 0, 0, 0) + stored


def group(label: bytes, group_type: int, contents: bytes) -> bytes:
    size = 24 + len(contents)
    return struct.pack("<4sI4siHHI", b"GRUP", size, label, group_type, 0, 0, 0) + contents


def synthetic_plugin() -> bytes:
    header = record("TES4", 0, subrecord("HEDR", struct.pack("<fII", 1.34, 4, 0)))
    static = record(
        "STAT",
        0x300,
        subrecord("EDID", b"SyntheticFloor\0") + subrecord("MODL", b"meshes/test/floor.nif\0"),
        COMPRESSED_RECORD_FLAG,
    )
    door = record(
        "DOOR",
        0x301,
        subrecord("EDID", b"SyntheticDoor\0") + subrecord("MODL", b"meshes/test/door.nif\0"),
    )
    cell = record("CELL", 0x100, subrecord("EDID", b"SyntheticRoom\0") + subrecord("DATA", b"\x01"))
    floor_reference = record(
        "REFR",
        0x200,
        subrecord("NAME", struct.pack("<I", 0x300))
        + subrecord("DATA", struct.pack("<6f", 10.0, 20.0, 30.0, 0.0, 0.0, 1.5)),
    )
    door_reference = record(
        "REFR",
        0x201,
        subrecord("NAME", struct.pack("<I", 0x301))
        + subrecord("XTEL", struct.pack("<I6f", 0x400, 1.0, 2.0, 3.0, 0.0, 0.0, 0.0))
        + subrecord("DATA", struct.pack("<6f", 40.0, 50.0, 60.0, 0.0, 0.0, 0.0)),
    )
    children = group(struct.pack("<I", 0x100), 6, group(struct.pack("<I", 0x100), 9, floor_reference + door_reference))
    return header + group(b"STAT", 0, static) + group(b"DOOR", 0, door) + group(b"CELL", 0, cell + children)


class CellCatalogTest(unittest.TestCase):
    def test_cell_reference_graph_and_transforms(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "synthetic.esm"
            path.write_bytes(synthetic_plugin())
            catalog = scan_cell_catalog(path)

        cell = catalog.cells[0x100]
        self.assertTrue(cell.interior)
        self.assertEqual(cell.editor_id, "SyntheticRoom")
        self.assertEqual(catalog.base_objects[0x300].model_path, "meshes\\test\\floor.nif")
        references = catalog.references_for(cell.form_id)
        self.assertEqual(len(references), 2)
        self.assertEqual(references[0].transform.position, (10.0, 20.0, 30.0))
        self.assertEqual(references[0].transform.rotation_radians, (0.0, 0.0, 1.5))
        self.assertEqual(references[1].teleport_destination_form_id, 0x400)
        self.assertEqual(references[1].teleport_destination_transform.position, (1.0, 2.0, 3.0))
        self.assertEqual(catalog.base_objects[references[1].base_form_id].record_type, "DOOR")

    def test_truncated_group_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "truncated.esm"
            path.write_bytes(group(b"CELL", 0, b"")[:-1])
            with self.assertRaises(PluginFormatError):
                list(iter_plugin_records(path))

    def test_reference_position_conversion_applies_origin_once(self) -> None:
        self.assertEqual(godot_position((11.0, 22.0, 33.0), (1.0, 2.0, 3.0)), [10.0, 30.0, -20.0])


if __name__ == "__main__":
    unittest.main()
