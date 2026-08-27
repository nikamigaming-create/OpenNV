from __future__ import annotations

import struct
import hashlib
import sys
import tempfile
import unittest
import zlib
from dataclasses import replace
from pathlib import Path
from types import SimpleNamespace

from PIL import Image

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from landscape_catalog import (  # noqa: E402
    CONFIGURED_MISSING_BASE_SOURCE,
    LAND_VERTEX_COUNT,
    resolved_layer_texture_form_id,
    scan_landscape_catalog,
)
from landscape_gltf import (  # noqa: E402
    LANDSCAPE_CONTRACT_SOURCE,
    LANDSCAPE_LAYER_WEIGHT_OPERATION,
    LANDSCAPE_LIGHTING_MODEL,
    LANDSCAPE_MATERIAL_SCHEMA,
    LANDSCAPE_NORMAL_DECODE,
    LANDSCAPE_WEIGHT_INTERPOLATION,
    LANDSCAPE_WEIGHT_STORAGE,
    bake_landscape_diffuse,
    landscape_geometry,
    landscape_materials,
    landscape_quadrant_geometry,
    landscape_quadrant_vertex_weights,
    landscape_weight_map_payload,
)
from landscape_stack import resolve_owned_landscape  # noqa: E402
from plugin_records import (  # noqa: E402
    COMPRESSED_RECORD_FLAG,
    iter_plugin_records,
    iter_subrecords,
)
from plugin_stack import file_sha256  # noqa: E402
from runtime_configuration import load_runtime_configuration  # noqa: E402


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, data: bytes, flags: int = 0) -> bytes:
    stored = struct.pack("<I", len(data)) + zlib.compress(data) if flags & COMPRESSED_RECORD_FLAG else data
    return struct.pack("<4s4I2H", signature.encode("ascii"), len(stored), flags, form_id, 0, 0, 0) + stored


def group(label: bytes, group_type: int, contents: bytes) -> bytes:
    return struct.pack("<4sI4siHHI", b"GRUP", 24 + len(contents), label, group_type, 0, 0, 0) + contents


def header(*masters: str) -> bytes:
    data = subrecord("HEDR", struct.pack("<fII", 1.34, 0, 0))
    for master in masters:
        data += subrecord("MAST", master.encode("ascii") + b"\0")
        data += subrecord("DATA", bytes(8))
    return record("TES4", 0, data)


def plugin(include_btxt: bool = True, include_geometry: bool = True) -> bytes:
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
    default_texture_set = record(
        "TXST",
        0x11,
        subrecord("EDID", b"SyntheticDefaultTextureSet\0")
        + subrecord("TX00", b"landscape/default.dds\0")
        + subrecord("TX01", b"landscape/default_n.dds\0"),
    )
    default_landscape_texture = record(
        "LTEX",
        0xA0D,
        subrecord("EDID", b"dirt01\0") + subrecord("TNAM", struct.pack("<I", 0x11)),
    )
    height_data = struct.pack("<f", 100.0) + bytes(LAND_VERTEX_COUNT) + bytes(3)
    normals = bytes((0, 0, 127)) * LAND_VERTEX_COUNT
    colors = bytes((128, 64, 32)) * LAND_VERTEX_COUNT
    layers = (
        b"".join(
            subrecord("BTXT", struct.pack("<IBBH", 0x20, quadrant, 0x66, 0xFFFF))
            for quadrant in range(4)
        )
        if include_btxt
        else b""
    )
    layers += subrecord("ATXT", struct.pack("<IBBH", 0x20, 0, 0, 0))
    layers += subrecord("VTXT", struct.pack("<HHf", 18, 0xCAFE, 0.75))
    land = record(
        "LAND",
        0x30,
        subrecord("DATA", struct.pack("<I", 0x1F if include_geometry else 0x1E))
        + (subrecord("VNML", normals) if include_geometry else b"")
        + (subrecord("VHGT", height_data) if include_geometry else b"")
        + subrecord("VCLR", colors)
        + layers,
        COMPRESSED_RECORD_FLAG,
    )
    cell_children = group(struct.pack("<I", 0x40), 6, group(struct.pack("<I", 0x40), 9, land))
    world_children = group(struct.pack("<I", 0x50), 1, cell_children)
    return (
        header()
        + group(b"TXST", 0, texture_set + default_texture_set)
        + group(b"LTEX", 0, landscape_texture + default_landscape_texture)
        + world_children
    )


def override_plugin() -> bytes:
    texture_set = record(
        "TXST",
        0x10,
        subrecord("EDID", b"SyntheticTextureSetOverride\0")
        + subrecord("TX00", b"landscape/override.dds\0")
        + subrecord("TX01", b"landscape/override_n.dds\0"),
    )
    return header("Synthetic.esm") + group(b"TXST", 0, texture_set)


class LandscapeCatalogTest(unittest.TestCase):
    def test_data_flagged_land_without_vertex_payload_is_explicitly_non_geometric(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "synthetic.esm"
            path.write_bytes(plugin(include_geometry=False))
            catalog = scan_landscape_catalog(path, {0x40})
        self.assertIsNone(catalog.optional_landscape_for_cell(0x40))
        non_geometric = catalog.non_geometric_landscapes[0x40]
        self.assertEqual(non_geometric.form_id, 0x30)
        self.assertEqual(non_geometric.flags, 0x1E)
        self.assertEqual(len(non_geometric.base_texture_form_ids), 4)
        self.assertEqual(len(non_geometric.alpha_layers), 1)

    def test_missing_btxt_uses_profile_owned_default_with_explicit_provenance(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "synthetic.esm"
            path.write_bytes(plugin(include_btxt=False))
            catalog = scan_landscape_catalog(path, {0x40})
        landscape = catalog.landscape_for_cell(0x40)
        self.assertEqual([layer.quadrant for layer in landscape.base_layers], [0, 1, 2, 3])
        self.assertTrue(
            all(
                layer.source == CONFIGURED_MISSING_BASE_SOURCE
                for layer in landscape.base_layers
            )
        )
        self.assertEqual(catalog.diffuse_path(0xA0D), "landscape\\default.dds")

    def test_owned_stack_resolves_stable_land_ltex_and_winning_txst(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            base_path = root / "Synthetic.esm"
            override_path = root / "Dlc.esm"
            base_path.write_bytes(plugin())
            override_path.write_bytes(override_plugin())
            landscape_record = next(
                iter_plugin_records(base_path, frozenset({"LAND"}))
            )
            signature_counts: dict[str, int] = {}
            for row in iter_subrecords(landscape_record):
                signature_counts[row.signature] = signature_counts.get(row.signature, 0) + 1
            manifest = {
                "inputs": [
                    {
                        "file": base_path.name,
                        "loadOrderIndex": 0,
                        "masters": [],
                        "sha256": file_sha256(base_path),
                        "bytes": base_path.stat().st_size,
                    },
                    {
                        "file": override_path.name,
                        "loadOrderIndex": 1,
                        "masters": [base_path.name],
                        "sha256": file_sha256(override_path),
                        "bytes": override_path.stat().st_size,
                    },
                ]
            }
            cell = {
                "formKey": "Synthetic.esm:000040",
                "runtimeFormId": "00000040",
                "interior": False,
                "coordinates": [2, -1],
                "worldspace": {
                    "key": "Synthetic.esm:000050",
                    "runtimeFormId": "00000050",
                },
            }
            child = {
                "formKey": "Synthetic.esm:000030",
                "runtimeFormId": "00000030",
                "recordType": "LAND",
                "childKind": "landscape",
                "cell": {
                    "key": cell["formKey"],
                    "runtimeFormId": cell["runtimeFormId"],
                },
                "sourcePlugin": base_path.name,
                "sourceLocalFormId": "00000030",
                "recordFlags": "00040000",
                "recordDataSha256": hashlib.sha256(landscape_record.data).hexdigest(),
                "compressionChecksumValid": True,
                "subrecordSignatureCounts": dict(sorted(signature_counts.items())),
            }

            resolved = resolve_owned_landscape(root, manifest, cell, child)

        contract = resolved.textures.texture_contract(0x20)
        self.assertEqual(resolved.identity.form_key, child["formKey"])
        self.assertEqual(contract["ltexFormKey"], "Synthetic.esm:000020")
        self.assertEqual(contract["txstFormKey"], "Synthetic.esm:000010")
        self.assertEqual(contract["txstSource"]["sourcePlugin"], "Dlc.esm")
        self.assertEqual(contract["ltexEditorId"], "SyntheticLandscape")
        self.assertEqual(contract["txstEditorId"], "SyntheticTextureSetOverride")
        self.assertEqual(contract["diffusePath"], "landscape\\override.dds")

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

        zero_layer = replace(landscape.alpha_layers[0], texture_form_id=0)
        zero_landscape = replace(landscape, alpha_layers=(zero_layer,))
        self.assertEqual(
            resolved_layer_texture_form_id(zero_landscape, zero_layer),
            landscape.base_layers[zero_layer.quadrant].texture_form_id,
        )

        compiler = load_runtime_configuration().content_compiler
        positions, normals, uvs, colors, triangles = landscape_geometry(
            landscape,
            (2, -1),
            (8192.0, -4096.0, 800.0),
            compiler.exterior_cell_size_game_units,
        )
        self.assertEqual(positions[0], (0.0, 0.0, -0.0))
        self.assertEqual(positions[-1], (4096.0, 0.0, -4096.0))
        self.assertEqual(normals[0], (0.0, 1.0, -0.0))
        self.assertEqual(uvs[0], (0.0, 0.0))
        self.assertEqual(uvs[-1], (1.0, 1.0))
        self.assertEqual(len(colors), LAND_VERTEX_COUNT)
        self.assertEqual(len(triangles), 32 * 32 * 2)

        baked = bake_landscape_diffuse(
            landscape,
            lambda _form_id: Image.new("RGB", (4, 4), (200, 100, 50)),
            compiler,
        )
        expected_side = compiler.landscape_quadrant_pixels * 2
        self.assertEqual(baked.size, (expected_side, expected_side))
        self.assertEqual(baked.getpixel((100, 900)), (200, 100, 50))

    def test_layered_quadrants_keep_shared_borders_and_float32_weights(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "synthetic.esm"
            path.write_bytes(plugin())
            catalog = scan_landscape_catalog(path, {0x40})
        landscape = catalog.landscape_for_cell(0x40)
        compiler = load_runtime_configuration().content_compiler
        positions, normals, uvs, colors, triangles = landscape_quadrant_geometry(
            landscape,
            (2, -1),
            (8192.0, -4096.0, 800.0),
            0,
            compiler.exterior_cell_size_game_units,
        )
        self.assertEqual(len(positions), 17 * 17)
        self.assertEqual(len(normals), len(positions))
        self.assertEqual(len(uvs), len(positions))
        self.assertEqual(len(colors), len(positions))
        self.assertEqual(len(triangles), 16 * 16 * 2)
        self.assertEqual(positions[0], (0.0, 0.0, -0.0))
        self.assertEqual(positions[-1], (2048.0, 0.0, -2048.0))
        self.assertEqual(uvs[0], (0.0, 0.0))
        self.assertEqual(uvs[-1], (1.0, 1.0))

        first = replace(landscape.alpha_layers[0], quadrant=0, layer_index=0)
        second = replace(first, layer_index=1, opacities=())
        weights = landscape_quadrant_vertex_weights([first, second])
        self.assertEqual(weights[18], (0.25, 0.75, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0))
        payload = landscape_weight_map_payload([first, second], 0)
        self.assertEqual(len(payload), 17 * 17 * 4 * 4)
        pixel_offset = (1 * 17 + 1) * 4 * 4
        self.assertEqual(
            struct.unpack_from("<4f", payload, pixel_offset),
            (0.25, 0.75, 0.0, 0.0),
        )

        retail_overlap = [
            0.0,
            0.004235319793224335,
            0.044715024530887604,
            0.0,
            0.9992889761924744,
        ]
        retail_layers = [
            replace(
                first,
                layer_index=index,
                opacities=(
                    ()
                    if opacity == 0.0
                    else (SimpleNamespace(vertex_index=71, opacity=opacity),)
                ),
            )
            for index, opacity in enumerate(retail_overlap)
        ]
        retail_weights = landscape_quadrant_vertex_weights(retail_layers)[71]
        self.assertEqual(
            struct.pack("<8f", *retail_weights).hex(),
            "00000000000000007265843b64b92e3d00000000a00b743f0000000000000000",
        )

        artifact = SimpleNamespace(asset_id="diffuse")
        materials = landscape_materials(
            landscape,
            "asset",
            {0x20: artifact},
            {},
            {(0, 0): {"id": "weights"}},
            load_runtime_configuration().content_compiler,
        )
        self.assertEqual(len(materials), 4)
        self.assertEqual(materials[0]["name"], "LAND_asset_Q0")
        contract = materials[0]["landscapeContract"]
        self.assertEqual(contract["schema"], LANDSCAPE_MATERIAL_SCHEMA)
        self.assertEqual(contract["model"], LANDSCAPE_LIGHTING_MODEL)
        self.assertEqual(contract["diffuseDomain"], "encoded")
        self.assertEqual(contract["normalDecode"], LANDSCAPE_NORMAL_DECODE)
        self.assertEqual(
            contract["layerWeightOperation"],
            LANDSCAPE_LAYER_WEIGHT_OPERATION,
        )
        self.assertEqual(contract["weightInterpolation"], LANDSCAPE_WEIGHT_INTERPOLATION)
        self.assertEqual(contract["weightStorage"], LANDSCAPE_WEIGHT_STORAGE)
        self.assertEqual(contract["retailWeightSemantics"], ["TEXCOORD1", "TEXCOORD2"])
        self.assertEqual(contract["retailWeightType"], "float4")
        self.assertEqual(contract["source"], LANDSCAPE_CONTRACT_SOURCE)
        self.assertFalse(materials[0]["diffuseSampleSrgb"])
        self.assertEqual(contract["weightMapTextureIds"], ["weights"])
        self.assertEqual(contract["baseWeightMapIndex"], 0)
        self.assertEqual(contract["baseWeightChannel"], 0)
        self.assertEqual(contract["layers"][0]["weightMapIndex"], 0)
        self.assertEqual(contract["layers"][0]["weightChannel"], 1)
        self.assertEqual(
            contract["samplersUsed"],
            2 + 2 + 1,
        )


if __name__ == "__main__":
    unittest.main()
