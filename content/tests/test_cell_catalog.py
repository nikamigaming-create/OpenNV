from __future__ import annotations

import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from cell_catalog import BaseObject, scan_cell_catalog  # noqa: E402
from cell_scene import (  # noqa: E402
    environment_texture_paths,
    godot_position,
    godot_rotation_quaternion,
    godot_yaw_radians,
    interaction_manifest,
    load_recipe,
    load_spatial_recipe,
    reference_selection_reason,
    vr_smoke_loadout_manifest,
)
from plugin_records import COMPRESSED_RECORD_FLAG, PluginFormatError, iter_plugin_records  # noqa: E402
from runtime_configuration import load_runtime_configuration  # noqa: E402


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
    light = record(
        "LIGH",
        0x302,
        subrecord("EDID", b"SyntheticLight\0")
        + subrecord("DATA", struct.pack("<iI4BIffIf", -1, 256, 100, 80, 40, 0, 0, 1.0, 90.0, 0, 0.0))
        + subrecord("FNAM", struct.pack("<f", 1.5)),
    )
    item = record(
        "MISC",
        0x303,
        subrecord("EDID", b"SyntheticPickup\0") + subrecord("MODL", b"clutter/test/pickup.nif\0"),
    )
    container = record(
        "CONT",
        0x304,
        subrecord("EDID", b"SyntheticContainer\0")
        + subrecord("MODL", b"clutter/test/container.nif\0")
        + subrecord("CNTO", struct.pack("<Ii", 0x303, 2)),
    )
    weapon = record(
        "WEAP",
        0x305,
        subrecord("EDID", b"SyntheticWeapon\0")
        + subrecord("MODL", b"weapons/test/weapon.nif\0")
        + subrecord("NAM0", struct.pack("<I", 0x306))
        + subrecord("DATA", struct.pack("<IIfHB", 100, 200, 2.0, 26, 6)),
    )
    ammo = record("AMMO", 0x306, subrecord("EDID", b"SyntheticAmmo\0"))
    xcll = bytes((10, 20, 30, 0, 40, 50, 60, 0, 70, 80, 90, 0)) + struct.pack(
        "<ffii3f", 64.0, 3750.0, 0, 250, 1.0, 6600.0, 1.25
    )
    cell = record(
        "CELL",
        0x100,
        subrecord("EDID", b"SyntheticRoom\0") + subrecord("DATA", b"\x01") + subrecord("XCLL", xcll),
    )
    floor_reference = record(
        "REFR",
        0x200,
        subrecord("NAME", struct.pack("<I", 0x300))
        + subrecord("XSCL", struct.pack("<f", 0.75))
        + subrecord("DATA", struct.pack("<6f", 10.0, 20.0, 30.0, 0.0, 0.0, 1.5)),
    )
    door_reference = record(
        "REFR",
        0x201,
        subrecord("NAME", struct.pack("<I", 0x301))
        + subrecord("XTEL", struct.pack("<I6f", 0x400, 1.0, 2.0, 3.0, 0.0, 0.0, 0.0))
        + subrecord("DATA", struct.pack("<6f", 40.0, 50.0, 60.0, 0.0, 0.0, 0.0)),
    )
    light_reference = record(
        "REFR",
        0x202,
        subrecord("NAME", struct.pack("<I", 0x302))
        + subrecord("DATA", struct.pack("<6f", 15.0, 25.0, 35.0, 0.0, 0.0, 0.0)),
    )
    item_reference = record(
        "REFR",
        0x203,
        subrecord("NAME", struct.pack("<I", 0x303))
        + subrecord("DATA", struct.pack("<6f", 18.0, 28.0, 38.0, 0.0, 0.0, 0.0)),
    )
    container_reference = record(
        "REFR",
        0x204,
        subrecord("NAME", struct.pack("<I", 0x304))
        + subrecord("DATA", struct.pack("<6f", 19.0, 29.0, 39.0, 0.0, 0.0, 0.0)),
    )
    weapon_reference = record(
        "REFR",
        0x205,
        subrecord("NAME", struct.pack("<I", 0x305))
        + subrecord("DATA", struct.pack("<6f", 20.0, 30.0, 40.0, 0.1, 0.2, 0.3)),
    )
    children = group(
        struct.pack("<I", 0x100),
        6,
        group(
            struct.pack("<I", 0x100),
            9,
            floor_reference
            + door_reference
            + light_reference
            + item_reference
            + container_reference
            + weapon_reference,
        ),
    )
    return (
        header
        + group(b"STAT", 0, static)
        + group(b"DOOR", 0, door)
        + group(b"LIGH", 0, light)
        + group(b"MISC", 0, item)
        + group(b"CONT", 0, container)
        + group(b"WEAP", 0, weapon)
        + group(b"AMMO", 0, ammo)
        + group(b"CELL", 0, cell + children)
    )


class CellCatalogTest(unittest.TestCase):
    def test_static_collection_base_resolves_for_placed_reference(self) -> None:
        header = record("TES4", 0, subrecord("HEDR", struct.pack("<fII", 1.34, 1, 0)))
        static_collection = record(
            "SCOL",
            0x500,
            subrecord("EDID", b"SyntheticCollection\0")
            + subrecord("MODL", b"SCOL/SyntheticCollection.NIF\0"),
        )
        cell = record(
            "CELL",
            0x501,
            subrecord("EDID", b"SyntheticExterior\0") + subrecord("DATA", b"\x00"),
        )
        reference = record(
            "REFR",
            0x502,
            subrecord("NAME", struct.pack("<I", 0x500))
            + subrecord("DATA", struct.pack("<6f", 10.0, 20.0, 30.0, 0.0, 0.0, 0.0)),
        )
        children = group(
            struct.pack("<I", 0x501),
            6,
            group(struct.pack("<I", 0x501), 9, reference),
        )
        plugin = (
            header
            + group(b"SCOL", 0, static_collection)
            + group(b"CELL", 0, cell + children)
        )

        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "synthetic-scol.esm"
            path.write_bytes(plugin)
            catalog = scan_cell_catalog(path)

        base = catalog.base_objects[0x500]
        self.assertEqual(base.record_type, "SCOL")
        self.assertEqual(base.model_path, "scol\\syntheticcollection.nif")
        self.assertEqual(catalog.references_for(0x501)[0].base_form_id, base.form_id)

    def test_environment_slots_require_the_retail_shader_flag(self):
        surface = {
            "textures": ["diffuse", "normal", "", "", "cube", "mask"],
            "material": {"shaderFlags1Enabled": []},
        }
        self.assertEqual(environment_texture_paths(surface), (None, None))
        surface["material"]["shaderFlags1Enabled"] = ["sf_environment_mapping"]
        self.assertEqual(environment_texture_paths(surface), ("cube", "mask"))
        surface["material"]["shaderFlags2Enabled"] = ["sf_2_envmap_light_fade"]
        self.assertEqual(environment_texture_paths(surface), (None, None))

    def test_cell_reference_graph_and_transforms(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "synthetic.esm"
            path.write_bytes(synthetic_plugin())
            catalog = scan_cell_catalog(path)

        cell = catalog.cells[0x100]
        self.assertTrue(cell.interior)
        self.assertEqual(cell.editor_id, "SyntheticRoom")
        self.assertIsNone(cell.worldspace_form_id)
        self.assertEqual(catalog.base_objects[0x300].model_path, "test\\floor.nif")
        references = catalog.references_for(cell.form_id)
        self.assertEqual(len(references), 6)
        self.assertEqual(references[0].transform.position, (10.0, 20.0, 30.0))
        self.assertEqual(references[0].transform.rotation_radians, (0.0, 0.0, 1.5))
        self.assertAlmostEqual(references[0].scale, 0.75)
        self.assertEqual(references[1].scale, 1.0)
        self.assertEqual(references[1].teleport_destination_form_id, 0x400)
        self.assertEqual(references[1].teleport_destination_transform.position, (1.0, 2.0, 3.0))
        self.assertEqual(catalog.base_objects[references[1].base_form_id].record_type, "DOOR")
        self.assertEqual(cell.lighting.ambient_rgb, (10, 20, 30))
        self.assertEqual(cell.lighting.fog_far, 3750.0)
        self.assertEqual(catalog.lights[0x302].radius, 256)
        self.assertEqual(catalog.lights[0x302].color_rgb, (100, 80, 40))
        self.assertEqual(catalog.lights[0x302].intensity, 1.5)
        self.assertEqual(catalog.base_objects[0x303].record_type, "MISC")
        self.assertEqual(catalog.containers[0x304].items[0].item_form_id, 0x303)
        self.assertEqual(catalog.containers[0x304].items[0].count, 2)
        self.assertEqual(catalog.weapons[0x305].damage, 26)
        self.assertEqual(catalog.weapons[0x305].clip_size, 6)
        self.assertEqual(catalog.weapons[0x305].ammo_form_id, 0x306)
        self.assertEqual(
            vr_smoke_loadout_manifest(
                {
                    "vrSmokeLoadout": {
                        "weaponFormId": "00000305",
                        "ammoFormId": "00000306",
                        "reserveMagazines": 1,
                    }
                },
                catalog,
            ),
            {
                "weaponFormId": "00000305",
                "weaponEditorId": "SyntheticWeapon",
                "modelPath": "weapons\\test\\weapon.nif",
                "ammoFormId": "00000306",
                "ammoEditorId": "SyntheticAmmo",
                "damage": 26,
                "clipSize": 6,
                "reserveRounds": 6,
                "source": "recipe-identity-plus-retail-records",
            },
        )
        container_interaction = interaction_manifest(
            next(reference for reference in references if reference.form_id == 0x204),
            catalog.base_objects[0x304],
            catalog,
        )
        self.assertEqual(
            container_interaction,
            {
                "type": "container",
                "items": [
                    {
                        "itemFormId": "00000303",
                        "itemEditorId": "SyntheticPickup",
                        "itemRecordType": "MISC",
                        "count": 2,
                        "resolved": True,
                    }
                ],
            },
        )
        weapon_interaction = interaction_manifest(
            next(reference for reference in references if reference.form_id == 0x205),
            catalog.base_objects[0x305],
            catalog,
        )
        self.assertEqual(weapon_interaction["weapon"]["damage"], 26)
        self.assertEqual(weapon_interaction["weapon"]["clipSize"], 6)
        self.assertEqual(weapon_interaction["weapon"]["ammoFormId"], "00000306")

    def test_truncated_group_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "truncated.esm"
            path.write_bytes(group(b"CELL", 0, b"")[:-1])
            with self.assertRaises(PluginFormatError):
                list(iter_plugin_records(path))

    def test_compressed_record_retains_explicit_bad_checksum_evidence(self) -> None:
        payload = subrecord("EDID", b"ChecksumEvidence\0")
        compressed = bytearray(record("STAT", 0x991, payload, COMPRESSED_RECORD_FLAG))
        compressed[-1] ^= 0x01
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "bad-checksum.esm"
            path.write_bytes(bytes(compressed))
            loaded = list(iter_plugin_records(path))
        self.assertEqual(len(loaded), 1)
        self.assertEqual(loaded[0].data, payload)
        self.assertFalse(loaded[0].compression_checksum_valid)

    def test_reference_position_conversion_applies_origin_once(self) -> None:
        self.assertEqual(godot_position((11.0, 22.0, 33.0), (1.0, 2.0, 3.0)), [10.0, 30.0, -20.0])
        yaw = godot_rotation_quaternion((0.0, 0.0, 1.5707963267948966))
        self.assertAlmostEqual(yaw[0], 0.0)
        self.assertAlmostEqual(yaw[1], -0.7071067811865475)
        self.assertAlmostEqual(yaw[2], 0.0)
        self.assertAlmostEqual(yaw[3], 0.7071067811865476)
        self.assertAlmostEqual(godot_yaw_radians(1.5), -1.5)
        pitch = godot_rotation_quaternion((1.5707963267948966, 0.0, 0.0))
        self.assertAlmostEqual(pitch[0], 0.7071067811865475)
        self.assertAlmostEqual(pitch[1], 0.0)
        self.assertAlmostEqual(pitch[2], 0.0)
        self.assertAlmostEqual(pitch[3], 0.7071067811865476)
        roll = godot_rotation_quaternion((0.0, 1.5707963267948966, 0.0))
        self.assertAlmostEqual(roll[0], 0.0)
        self.assertAlmostEqual(roll[1], 0.0)
        self.assertAlmostEqual(roll[2], -0.7071067811865475)
        self.assertAlmostEqual(roll[3], 0.7071067811865476)

    def test_recipe_accounts_for_editor_and_effect_exclusions(self) -> None:
        recipe = load_recipe("goodsprings-saloon-structure-v1")
        compiler = load_runtime_configuration().content_compiler
        self.assertEqual(
            reference_selection_reason(
                BaseObject(1, "FURN", "BarKeep", "furniture\\barkeep.nif"),
                recipe,
                compiler,
            ),
            "editor-only-base",
        )
        self.assertEqual(
            reference_selection_reason(
                BaseObject(2, "STAT", "Glow", "effects\\glow.nif"),
                recipe,
                compiler,
            ),
            "special-effect-shader-required",
        )
        self.assertEqual(
            reference_selection_reason(
                BaseObject(300, "STAT", "Table", "furniture\\table01.nif"),
                recipe,
                compiler,
            ),
            "selected",
        )
        self.assertEqual(
            reference_selection_reason(
                BaseObject(4, "TREE", "Shrub", "wastelandshrub01.spt"),
                recipe,
                compiler,
            ),
            "outside-recipe",
        )

        exterior_recipe = load_spatial_recipe("goodsprings-actor-review-background-v1")
        self.assertEqual(
            reference_selection_reason(
                BaseObject(5, "SCOL", "SCOLgsHouse02", "scol\\scolgshouse02.nif"),
                exterior_recipe,
                compiler,
            ),
            "selected",
        )


if __name__ == "__main__":
    unittest.main()
