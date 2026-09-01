from __future__ import annotations

import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from cell_catalog import (  # noqa: E402
    CELL_LIGHTING_TEMPLATE_AMBIENT_COLOR,
    BaseObject,
    PlacedReference,
    Transform,
    scan_cell_catalog,
)
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
        subrecord("EDID", b"SyntheticPickup\0")
        + subrecord("FULL", b"Synthetic Pickup\0")
        + subrecord("MODL", b"clutter/test/pickup.nif\0")
        + subrecord("DATA", struct.pack("<if", 25, 0.5)),
    )
    container = record(
        "CONT",
        0x304,
        subrecord("EDID", b"SyntheticContainer\0")
        + subrecord("FULL", b"Synthetic Container\0")
        + subrecord("MODL", b"clutter/test/container.nif\0")
        + subrecord("CNTO", struct.pack("<Ii", 0x303, 2)),
    )
    weapon = record(
        "WEAP",
        0x305,
        subrecord("EDID", b"SyntheticWeapon\0")
        + subrecord("FULL", b"Synthetic Weapon\0")
        + subrecord("MODL", b"weapons/test/weapon.nif\0")
        + subrecord("NAM0", struct.pack("<I", 0x306))
        + subrecord("DATA", struct.pack("<IIfHB", 100, 200, 2.0, 26, 6)),
    )
    ammo = record(
        "AMMO",
        0x306,
        subrecord("EDID", b"SyntheticAmmo\0")
        + subrecord("FULL", b"Synthetic Ammo\0"),
    )
    opening_quest = record("QUST", 0x400, subrecord("EDID", b"SyntheticOpening\0"))
    tutorial_quest = record("QUST", 0x401, subrecord("EDID", b"SyntheticTutorial\0"))
    activator_script = record(
        "SCPT",
        0x307,
        subrecord("EDID", b"SyntheticDelayedActivatorSCRIPT\0")
        + subrecord(
            "SCTX",
            b"scn SyntheticDelayedActivatorSCRIPT\n"
            b"short grabbed\nshort released\nfloat timer\nshort runTimer\n"
            b"begin gamemode\n"
            b" if runTimer == 1\n if timer > 0\n set timer to timer - GetSecondsPassed\n"
            b" else\n if grabbed == 1\n set grabbed to 2\n"
            b" SetStage SyntheticTutorial 22\n SetObjectiveCompleted SyntheticOpening 20 1\n"
            b" elseif released == 1\n set released to 2\n SetStage SyntheticTutorial 24\n endif\n"
            b" endif\n endif\nend\n"
            b"begin OnGrab\n if (grabbed == 0 && GetObjectiveDisplayed SyntheticOpening 20)\n"
            b" set grabbed to 1\n set runTimer to 1\n set timer to 1\n endif\nend\n"
            b"begin OnRelease\n if (released == 0 && GetObjectiveDisplayed SyntheticOpening 20)\n"
            b" set released to 1\n set runTimer to 1\n set timer to 1\n endif\nend\0",
        ),
    )
    activator = record(
        "ACTI",
        0x308,
        subrecord("EDID", b"SyntheticDelayedActivator\0")
        + subrecord("MODL", b"clutter/test/activator.nif\0")
        + subrecord("SCRI", struct.pack("<I", 0x307)),
    )
    xcll = bytes((10, 20, 30, 0, 40, 50, 60, 0, 70, 80, 90, 0)) + struct.pack(
        "<ffii3f", 64.0, 3750.0, 0, 250, 1.0, 6600.0, 1.25
    )
    lighting_template = record(
        "LGTM",
        0x101,
        subrecord("EDID", b"SyntheticLightingTemplate\0")
        + subrecord(
            "DATA",
            bytes((90, 80, 70, 0, 60, 50, 40, 0, 30, 20, 10, 0))
            + struct.pack("<ffii3f", 128.0, 4000.0, 45, 180, 0.5, 7000.0, 1.5),
        ),
    )
    cell = record(
        "CELL",
        0x100,
        subrecord("EDID", b"SyntheticRoom\0")
        + subrecord("DATA", b"\x01")
        + subrecord("XCLL", xcll)
        + subrecord("LTMP", struct.pack("<I", 0x101))
        + subrecord(
            "LNAM",
            struct.pack("<I", CELL_LIGHTING_TEMPLATE_AMBIENT_COLOR),
        ),
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
        + subrecord("XESP", struct.pack("<IB3x", 0x200, 1))
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
    activator_reference = record(
        "REFR",
        0x208,
        subrecord("NAME", struct.pack("<I", 0x308))
        + subrecord("DATA", struct.pack("<6f", 21.0, 31.0, 41.0, 0.0, 0.0, 0.0)),
    )
    navmesh = record(
        "NAVM",
        0x206,
        subrecord("NVER", struct.pack("<I", 11))
        + subrecord("DATA", struct.pack("<6I", 0x100, 4, 2, 0, 0, 0))
        + subrecord(
            "NVVX",
            b"".join(
                struct.pack("<3f", *value)
                for value in (
                    (0.0, 0.0, 30.0),
                    (10.0, 0.0, 30.0),
                    (10.0, 10.0, 30.0),
                    (0.0, 10.0, 30.0),
                )
            ),
        )
        + subrecord(
            "NVTR",
            struct.pack("<3H3hI", 0, 1, 2, 0, -1, 1, 8)
            + struct.pack("<3H3hI", 0, 2, 3, 0, -1, -1, 8),
        )
        + subrecord("NVEX", struct.pack("<IIH", 0, 0x207, 4))
        + subrecord("NVDP", struct.pack("<II", 0x201, 1))
        + subrecord(
            "NVGD",
            struct.pack(
                "<I8f3H",
                1,
                10.0,
                10.0,
                0.0,
                0.0,
                30.0,
                10.0,
                10.0,
                30.0,
                2,
                0,
                1,
            ),
        ),
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
            + weapon_reference
            + activator_reference
            + navmesh,
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
        + group(b"QUST", 0, opening_quest + tutorial_quest)
        + group(b"SCPT", 0, activator_script)
        + group(b"ACTI", 0, activator)
        + group(b"LGTM", 0, lighting_template)
        + group(b"CELL", 0, cell + children)
    )


class CellCatalogTest(unittest.TestCase):
    def test_item_economics_use_exact_supported_record_layouts(self) -> None:
        header = record("TES4", 0, subrecord("HEDR", struct.pack("<fII", 1.34, 5, 0)))
        rows = (
            ("MISC", 0x510, struct.pack("<if", 10, 1.25), 10, 1.25, "fnv-misc-data-8-v1"),
            ("KEYM", 0x511, struct.pack("<if", 0, 0.0), 0, 0.0, "fnv-keym-data-8-v1"),
            ("IMOD", 0x512, struct.pack("<if", 175, 0.5), 175, 0.5, "fnv-imod-data-8-v1"),
            ("ARMO", 0x513, struct.pack("<iif", 250, 400, 12.0), 250, 12.0, "fnv-armo-data-12-v1"),
            (
                "WEAP",
                0x514,
                struct.pack("<iifHB", 500, 300, 5.5, 18, 10),
                500,
                5.5,
                "fnv-weap-data-15-v1",
            ),
        )
        plugin = header + b"".join(
            group(
                signature.encode("ascii"),
                0,
                record(
                    signature,
                    form_id,
                    subrecord("EDID", f"Synthetic{signature}\0".encode("ascii"))
                    + subrecord("DATA", data),
                ),
            )
            for signature, form_id, data, _, _, _ in rows
        )
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "supported-item-layouts.esm"
            path.write_bytes(plugin)
            catalog = scan_cell_catalog(path)

        for _, form_id, _, value, weight, layout in rows:
            with self.subTest(form_id=f"{form_id:08x}"):
                item = catalog.items[form_id]
                self.assertEqual(item.value, value)
                self.assertEqual(item.weight, weight)
                self.assertEqual(item.source_subrecord, "DATA")
                self.assertEqual(item.source_layout, layout)

    def test_supported_item_economics_fail_closed_on_malformed_data(self) -> None:
        layouts = {"MISC": 8, "KEYM": 8, "IMOD": 8, "ARMO": 12, "WEAP": 15}
        for ordinal, (signature, expected_bytes) in enumerate(layouts.items()):
            with self.subTest(signature=signature):
                header = record(
                    "TES4",
                    0,
                    subrecord("HEDR", struct.pack("<fII", 1.34, 1, 0)),
                )
                item = record(
                    signature,
                    0x520 + ordinal,
                    subrecord("EDID", f"Malformed{signature}\0".encode("ascii"))
                    + subrecord("DATA", bytes(expected_bytes - 1)),
                )
                with tempfile.TemporaryDirectory() as raw_directory:
                    path = Path(raw_directory) / f"malformed-{signature.lower()}.esm"
                    path.write_bytes(header + group(signature.encode("ascii"), 0, item))
                    with self.assertRaisesRegex(
                        ValueError,
                        rf"{signature} DATA must be {expected_bytes} bytes",
                    ):
                        scan_cell_catalog(path)

    def test_item_economics_reject_invalid_values_and_mark_unsupported_layouts(self) -> None:
        for suffix, data in (
            ("negative-value", struct.pack("<if", -1, 1.0)),
            ("negative-weight", struct.pack("<if", 1, -1.0)),
            ("non-finite-weight", struct.pack("<if", 1, float("nan"))),
        ):
            with self.subTest(suffix=suffix):
                header = record(
                    "TES4",
                    0,
                    subrecord("HEDR", struct.pack("<fII", 1.34, 1, 0)),
                )
                item = record(
                    "MISC",
                    0x530,
                    subrecord("EDID", b"InvalidMisc\0") + subrecord("DATA", data),
                )
                with tempfile.TemporaryDirectory() as raw_directory:
                    path = Path(raw_directory) / f"{suffix}.esm"
                    path.write_bytes(header + group(b"MISC", 0, item))
                    with self.assertRaisesRegex(ValueError, "invalid item economics"):
                        scan_cell_catalog(path)

        header = record("TES4", 0, subrecord("HEDR", struct.pack("<fII", 1.34, 1, 0)))
        unsupported = record(
            "ALCH",
            0x531,
            subrecord("EDID", b"UnsupportedAlchemy\0")
            + subrecord("FULL", b"Unsupported Alchemy\0")
            + subrecord("DATA", b"\x00"),
        )
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "unsupported-alch.esm"
            path.write_bytes(header + group(b"ALCH", 0, unsupported))
            catalog = scan_cell_catalog(path)
        base = catalog.base_objects[0x531]
        interaction = interaction_manifest(
            PlacedReference(
                0x532,
                0x100,
                base.form_id,
                0,
                Transform((0.0, 0.0, 0.0), (0.0, 0.0, 0.0)),
                1.0,
                None,
                None,
            ),
            base,
            catalog,
        )
        self.assertNotIn(base.form_id, catalog.items)
        self.assertNotIn("itemValue", interaction)
        self.assertNotIn("itemWeight", interaction)
        self.assertEqual(
            interaction["itemDefinition"]["source"],
            {
                "recordFormId": "00000531",
                "recordType": "ALCH",
                "economicsStatus": "unsupported-record-layout",
            },
        )

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
        self.assertEqual(len(references), 7)
        self.assertEqual(references[0].transform.position, (10.0, 20.0, 30.0))
        self.assertEqual(references[0].transform.rotation_radians, (0.0, 0.0, 1.5))
        self.assertAlmostEqual(references[0].scale, 0.75)
        self.assertEqual(references[1].scale, 1.0)
        self.assertEqual(references[1].teleport_destination_form_id, 0x400)
        self.assertEqual(references[1].teleport_destination_transform.position, (1.0, 2.0, 3.0))
        self.assertEqual(references[3].enable_parent_form_id, 0x200)
        self.assertTrue(references[3].enable_parent_opposite)
        self.assertEqual(catalog.base_objects[references[1].base_form_id].record_type, "DOOR")
        self.assertEqual(cell.authored_lighting.ambient_rgb, (10, 20, 30))
        self.assertEqual(cell.lighting.ambient_rgb, (90, 80, 70))
        self.assertEqual(cell.lighting.directional_rgb, (40, 50, 60))
        self.assertEqual(cell.lighting.fog_far, 3750.0)
        self.assertEqual(cell.lighting_template_form_id, 0x101)
        self.assertEqual(
            cell.lighting_template_flags,
            CELL_LIGHTING_TEMPLATE_AMBIENT_COLOR,
        )
        self.assertEqual(
            catalog.lighting_templates[0x101].editor_id,
            "SyntheticLightingTemplate",
        )
        self.assertEqual(catalog.lights[0x302].radius, 256)
        self.assertEqual(catalog.lights[0x302].color_rgb, (100, 80, 40))
        self.assertEqual(catalog.lights[0x302].intensity, 1.5)
        self.assertEqual(catalog.base_objects[0x303].record_type, "MISC")
        self.assertEqual(catalog.items[0x303].value, 25)
        self.assertEqual(catalog.items[0x303].weight, 0.5)
        self.assertEqual(catalog.items[0x303].source_layout, "fnv-misc-data-8-v1")
        self.assertEqual(catalog.containers[0x304].items[0].item_form_id, 0x303)
        self.assertEqual(catalog.containers[0x304].items[0].count, 2)
        self.assertEqual(catalog.weapons[0x305].damage, 26)
        self.assertEqual(catalog.weapons[0x305].clip_size, 6)
        self.assertEqual(catalog.weapons[0x305].ammo_form_id, 0x306)
        navmesh = catalog.navmeshes_for(cell.form_id)[0]
        self.assertEqual(navmesh.form_id, 0x206)
        self.assertEqual(navmesh.version, 11)
        self.assertEqual(navmesh.vertices[2], (10.0, 10.0, 30.0))
        self.assertEqual(navmesh.triangles[0].adjacent_triangles, (0, -1, 1))
        self.assertEqual(navmesh.external_connections[0].navmesh_form_id, 0x207)
        self.assertEqual(navmesh.door_portals[0].door_reference_form_id, 0x201)
        self.assertEqual(navmesh.spatial_grid.triangle_segments, ((0, 1),))
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
                "weaponDisplayName": "Synthetic Weapon",
                "modelPath": "weapons\\test\\weapon.nif",
                "ammoFormId": "00000306",
                "ammoEditorId": "SyntheticAmmo",
                "ammoDisplayName": "Synthetic Ammo",
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
                "displayName": "Synthetic Container",
                "items": [
                    {
                        "itemFormId": "00000303",
                        "itemEditorId": "SyntheticPickup",
                        "itemDisplayName": "Synthetic Pickup",
                        "itemRecordType": "MISC",
                        "count": 2,
                        "resolved": True,
                        "itemDefinition": {
                            "schema": "opennv-owned-item-definition/v1",
                            "formId": "00000303",
                            "editorId": "SyntheticPickup",
                            "displayName": "Synthetic Pickup",
                            "recordType": "MISC",
                            "source": {
                                "recordFormId": "00000303",
                                "recordType": "MISC",
                                "economicsStatus": "source-bound",
                                "subrecord": "DATA",
                                "layout": "fnv-misc-data-8-v1",
                            },
                        },
                        "itemValue": 25,
                        "itemWeight": 0.5,
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
        self.assertEqual(weapon_interaction["itemValue"], 100)
        self.assertEqual(weapon_interaction["itemWeight"], 2.0)
        self.assertEqual(
            weapon_interaction["itemDefinition"]["source"]["layout"],
            "fnv-weap-data-15-v1",
        )
        activator = catalog.base_objects[0x308]
        self.assertEqual(activator.attached_script_form_id, 0x307)
        self.assertEqual(catalog.scripts[0x307].editor_id, "SyntheticDelayedActivatorSCRIPT")
        self.assertEqual(catalog.quests[0x400].editor_id, "SyntheticOpening")
        activator_interaction = interaction_manifest(
            next(reference for reference in references if reference.form_id == 0x208),
            activator,
            catalog,
        )
        self.assertEqual(activator_interaction["type"], "scripted-activator")
        self.assertEqual(activator_interaction["script"], {
            "formId": "00000307",
            "editorId": "SyntheticDelayedActivatorSCRIPT",
        })
        self.assertEqual(activator_interaction["support"], "delayed-objective-events")
        self.assertEqual(
            activator_interaction["events"],
            [
                {
                    "event": "grab",
                    "guard": {
                        "questFormId": "00000400",
                        "questEditorId": "SyntheticOpening",
                        "objectiveIndex": 20,
                        "state": "displayed",
                    },
                    "delaySeconds": 1.0,
                    "commands": [
                        {
                            "kind": "setStage",
                            "questFormId": "00000401",
                            "questEditorId": "SyntheticTutorial",
                            "stage": 22,
                        },
                        {
                            "kind": "objective",
                            "questFormId": "00000400",
                            "questEditorId": "SyntheticOpening",
                            "index": 20,
                            "state": "completed",
                            "enabled": True,
                        },
                    ],
                },
                {
                    "event": "release",
                    "guard": {
                        "questFormId": "00000400",
                        "questEditorId": "SyntheticOpening",
                        "objectiveIndex": 20,
                        "state": "displayed",
                    },
                    "delaySeconds": 1.0,
                    "commands": [
                        {
                            "kind": "setStage",
                            "questFormId": "00000401",
                            "questEditorId": "SyntheticTutorial",
                            "stage": 24,
                        },
                    ],
                },
            ],
        )

    def test_localized_full_is_not_decoded_as_a_zstring(self) -> None:
        plugin = record(
            "TES4",
            0,
            subrecord("HEDR", struct.pack("<fII", 1.34, 1, 0)),
            0x00000080,
        ) + group(
            b"MISC",
            0,
            record(
                "MISC",
                0x500,
                subrecord("EDID", b"LocalizedItem\0")
                + subrecord("FULL", struct.pack("<I", 0x1234)),
            ),
        )
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "localized.esm"
            path.write_bytes(plugin)
            with self.assertRaisesRegex(ValueError, "owned STRINGS table"):
                scan_cell_catalog(path)

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

        exterior_recipe = load_spatial_recipe("goodsprings-doc-exterior-active-set-v1")
        self.assertEqual(exterior_recipe["editorId"], "Goodsprings")
        self.assertEqual(exterior_recipe["cellFormId"], "000daebb")
        self.assertEqual(exterior_recipe["entryDoorReferenceFormId"], "00103e69")
        self.assertEqual(exterior_recipe["reciprocalDoorReferenceFormId"], "00103e61")
        self.assertEqual(
            exterior_recipe["streaming"],
            {
                "mode": "retail-ini",
                "loadedGridDiameter": 5,
                "source": {
                    "file": "Fallout_default.ini",
                    "sha256": "a701c3a96af26f83ba6399b4a579af59fa075868949519f4dec45bf47bf7f95d",
                },
                "section": "General",
                "key": "uGridsToLoad",
            },
        )
        self.assertEqual(
            reference_selection_reason(
                BaseObject(5, "SCOL", "SCOLgsHouse02", "scol\\scolgshouse02.nif"),
                exterior_recipe,
                compiler,
            ),
            "selected",
        )

        route_recipe = load_recipe("goodsprings-doc-mitchell-house-v1")
        self.assertEqual(route_recipe["schema"], "opennv-cell-recipe/v2")
        self.assertEqual(
            route_recipe["linkedCellRecipes"],
            [
                {
                    "recipe": "goodsprings-doc-exterior-active-set-v1",
                    "fromDoorReferenceFormId": "00103e61",
                },
                {
                    "recipe": "goodsprings-saloon-structure-v1",
                    "fromDoorReferenceFormId": "0010636f",
                },
            ],
        )
        self.assertEqual(
            [
                *route_recipe["actorRecipes"],
                *load_spatial_recipe("goodsprings-doc-exterior-active-set-v1")[
                    "actorRecipes"
                ],
                *load_recipe("goodsprings-saloon-structure-v1")["actorRecipes"],
            ],
            [
                "goodsprings-doc-mitchell-actor-v1",
                "goodsprings-easy-pete-actor-v1",
                "goodsprings-trudy-actor-v1",
                "goodsprings-settler-04-actor-v1",
                "goodsprings-sunny-smiles-actor-v1",
                "goodsprings-cheyenne-actor-v1",
                "goodsprings-vcg02-gecko-1-actor-v1",
                "goodsprings-vcg02-gecko-2-actor-v1",
            ],
        )

    def test_fo1_vault13_donor_recipe_matches_current_cell_contract(self) -> None:
        recipe = load_recipe("fo1-vault13-cave-donor-smoke-v1")

        self.assertEqual(recipe["id"], "fo1-vault13-cave-donor-smoke-v1")
        self.assertIs(recipe["exportStrict"], False)
        self.assertEqual(recipe["textureAliases"], {})


if __name__ == "__main__":
    unittest.main()
