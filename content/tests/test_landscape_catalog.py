from __future__ import annotations

import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

from PIL import Image

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from landscape_catalog import LAND_VERTEX_COUNT, scan_landscape_catalog  # noqa: E402
from landscape_gltf import bake_landscape_diffuse, landscape_geometry  # noqa: E402
from plugin_records import COMPRESSED_RECORD_FLAG  # noqa: E402


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, data: bytes, flags: int = 0) -> bytes:
    stored = struct.pack("<I", len(data)) + zlib.compress(data) if flags & COMPRESSED_RECORD_FLAG else data
    return struct.pack("<4s4I2H", signature.encode("ascii"), len(stored), flags, form_id, 0, 0, 0) + stored


def group(label: bytes, group_type: int, contents: bytes) -> bytes:
    return struct.pack("<4sI4siHHI", b"GRUP", 24 + len(contents), label, group_type, 0, 0, 0) + contents


def plugin() -> bytes:
    texture_set = record(
        "TXST",
        0x10,
        subrecord("EDID", b"SyntheticTextureSet\0")
        + subrecord("TX00", b"landscape/synthetic.dds\0")
        + subrecord("TX01", b"landscape/synthetic_n.dds\0"),
    )
    landscape_texture = record(
        "LTEX",
        0x20,
        subrecord("EDID", b"SyntheticLandscape\0") + subrecord("TNAM", struct.pack("<I", 0x10)),
    )
    height_data = struct.pack("<f", 100.0) + bytes(LAND_VERTEX_COUNT) + bytes(3)
    normals = bytes((0, 0, 127)) * LAND_VERTEX_COUNT
    colors = bytes((128, 64, 32)) * LAND_VERTEX_COUNT
    layers = b"".join(subrecord("BTXT", struct.pack("<IBBH", 0x20, quadrant, 0x66, 0xFFFF)) for quadrant in range(4))
    layers += subrecord("ATXT", struct.pack("<IBBH", 0x20, 0, 0, 0))
    layers += subrecord("VTXT", struct.pack("<HHf", 18, 0xCAFE, 0.75))
    land = record(
        "LAND",
        0x30,
        subrecord("DATA", struct.pack("<I", 0x1F))
        + subrecord("VNML", normals)
        + subrecord("VHGT", height_data)
        + subrecord("VCLR", colors)
        + layers,
        COMPRESSED_RECORD_FLAG,
    )
    cell_children = group(struct.pack("<I", 0x40), 6, group(struct.pack("<I", 0x40), 9, land))
    world_children = group(struct.pack("<I", 0x50), 1, cell_children)
    return group(b"TXST", 0, texture_set) + group(b"LTEX", 0, landscape_texture) + world_children


class LandscapeCatalogTest(unittest.TestCase):
    def test_landscape_geometry_layers_and_world_ownership(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "synthetic.esm"
            path.write_bytes(plugin())
            catalog = scan_landscape_catalog(path, {0x40})
        landscape = catalog.landscape_for_cell(0x40)
        self.assertEqual(landscape.worldspace_form_id, 0x50)
        self.assertEqual(landscape.flags, 0x1F)
        self.assertTrue(landscape.compression_checksum_valid)
        self.assertEqual(len(landscape.heights), LAND_VERTEX_COUNT)
        self.assertEqual(landscape.heights[0], 800.0)
        self.assertEqual(landscape.normals[0], (0.0, 0.0, 1.0))
        self.assertAlmostEqual(landscape.colors[0][0], 128 / 255.0)
        self.assertEqual([layer.quadrant for layer in landscape.base_layers], [0, 1, 2, 3])
        self.assertEqual(landscape.alpha_layers[0].opacities[0].vertex_index, 18)
        self.assertEqual(landscape.alpha_layers[0].opacities[0].unknown, 0xCAFE)
        self.assertEqual(catalog.diffuse_path(0x20), "landscape\\synthetic.dds")

        positions, normals, uvs, colors, triangles = landscape_geometry(
            landscape, (2, -1), (8192.0, -4096.0, 800.0)
        )
        self.assertEqual(positions[0], (0.0, 0.0, -0.0))
        self.assertEqual(positions[-1], (4096.0, 0.0, -4096.0))
        self.assertEqual(normals[0], (0.0, 1.0, -0.0))
        self.assertEqual(uvs[0], (0.0, 1.0))
        self.assertEqual(uvs[-1], (1.0, 0.0))
        self.assertEqual(len(colors), LAND_VERTEX_COUNT)
        self.assertEqual(len(triangles), 32 * 32 * 2)

        baked = bake_landscape_diffuse(
            landscape,
            lambda _form_id: Image.new("RGB", (4, 4), (200, 100, 50)),
        )
        self.assertEqual(baked.size, (1024, 1024))
        self.assertEqual(baked.getpixel((100, 900)), (200, 100, 50))


if __name__ == "__main__":
    unittest.main()
