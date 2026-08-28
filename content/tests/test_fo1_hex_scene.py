from __future__ import annotations

import hashlib
import json
import math
import struct
import sys
import tempfile
import unittest
from pathlib import Path

from PIL import Image


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_fo1_hex_scene import (  # noqa: E402
    build_owned_cave_composition,
    classic_floor_screen,
    classic_hex_screen,
    floor_index_for_hex,
    floor_patch_center,
    hex_center,
    load_runtime_profile_recipe,
    parse_item_pro,
    parse_pid_header,
    parse_starting_inventory,
    parse_critter_pro,
    parse_ai_section,
    unproject_floor,
)
from render_fo1_source_map import paste_clipped  # noqa: E402


class Fo1HexSceneTest(unittest.TestCase):
    def test_runtime_profile_is_external_hash_pinned_and_provenance_labelled(self) -> None:
        recipes = Path(__file__).resolve().parents[1] / "recipes"
        scene_recipe_path = recipes / "fo1-v13ent-hex-slice-v1.json"
        scene_recipe = json.loads(scene_recipe_path.read_text(encoding="utf-8"))
        profile = load_runtime_profile_recipe(
            scene_recipe_path,
            scene_recipe["runtimeProfile"],
        )
        self.assertEqual(profile["schema"], "opennv-fo1-runtime-profile-recipe/v1")
        self.assertEqual(profile["id"], "fo1-classic-3d-runtime-v1")
        self.assertEqual(
            set(profile["authority"]),
            {"fallout1", "falloutNewVegas", "openNvAdaptation", "proofOnly"},
        )
        self.assertEqual(profile["gameplayAdaptation"]["ratMovementLimitHexes"], 3)
        self.assertEqual(profile["camera"]["firstPerson"]["eyeHeightMeters"], 1.66)
        self.assertEqual(profile["showcase"]["fixedFramesPerSecond"], 30)

    def test_runtime_profile_hash_and_path_escape_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            scene_recipe_path = root / "scene.json"
            scene_recipe_path.write_text("{}", encoding="utf-8")
            profile_path = root / "profile.json"
            profile_path.write_text(
                json.dumps(
                    {
                        "schema": "opennv-fo1-runtime-profile-recipe/v1",
                        "id": "fixture",
                        "authority": {},
                        "scenePresentation": {},
                        "camera": {},
                        "gameplayAdaptation": {},
                        "cutaway": {},
                        "showcase": {},
                    }
                ),
                encoding="utf-8",
            )
            digest = hashlib.sha256(profile_path.read_bytes()).hexdigest()
            with self.assertRaisesRegex(Exception, "hash drift"):
                load_runtime_profile_recipe(
                    scene_recipe_path,
                    {"path": "profile.json", "sha256": "0" * 64},
                )
            outside = root.parent / "outside-runtime-profile.json"
            try:
                outside.write_text(profile_path.read_text(encoding="utf-8"), encoding="utf-8")
                with self.assertRaisesRegex(Exception, "escapes"):
                    load_runtime_profile_recipe(
                        scene_recipe_path,
                        {"path": "../outside-runtime-profile.json", "sha256": digest},
                    )
            finally:
                outside.unlink(missing_ok=True)

    def test_one_meter_retail_column_parity_topology_and_floor_mapping(self) -> None:
        tile = 10 * 200 + 20
        neighbors = [
            11 * 200 + 19,
            11 * 200 + 20,
            11 * 200 + 21,
            10 * 200 + 19,
            10 * 200 + 21,
            9 * 200 + 20,
        ]
        center = hex_center(tile)
        for neighbor in neighbors:
            target = hex_center(neighbor)
            distance = math.sqrt((target[0] - center[0]) ** 2 + (target[2] - center[2]) ** 2)
            self.assertAlmostEqual(distance, 1.0)

        floor_indices = {
            floor_index_for_hex((10 + offset_y) * 200 + 20 + offset_x)
            for offset_y in range(2)
            for offset_x in range(2)
        }
        self.assertEqual(floor_indices, {589})
        expected = [
            sum(
                hex_center((10 + offset_y) * 200 + 20 + offset_x)[axis]
                for offset_y in range(2)
                for offset_x in range(2)
            )
            / 4.0
            for axis in range(3)
        ]
        self.assertEqual(floor_patch_center(589), expected)
        floor_screen = classic_floor_screen(589)
        hex_screen = classic_hex_screen(10 * 200 + 20)
        self.assertEqual(hex_screen, [floor_screen[0] + 64, floor_screen[1] + 11])

    def test_isometric_floor_diamond_unprojects_to_a_square_texture(self) -> None:
        source = Image.new("RGBA", (80, 36), (0, 0, 0, 0))
        for y in range(source.height):
            for x in range(source.width):
                if abs(x - 39.5) / 39.5 + abs(y - 17.5) / 17.5 <= 1.0:
                    source.putpixel((x, y), (x * 3 % 256, y * 7 % 256, 80, 255))
        result = unproject_floor(source, 64)
        self.assertEqual(result.size, (64, 64))
        self.assertGreater(result.getpixel((32, 32))[3], 240)
        self.assertEqual({pixel[3] for pixel in result.get_flattened_data()}, {255})

    def test_invalid_hex_and_floor_indices_fail_closed(self) -> None:
        with self.assertRaises(ValueError):
            hex_center(-1)
        with self.assertRaises(ValueError):
            floor_patch_center(10000)

    def test_critter_pro_stats_combine_base_and_bonus_arrays(self) -> None:
        payload = bytearray(0x1A0)
        struct.pack_into(">3i", payload, 0x20, -1, 12, 5)
        base = [0] * 35
        bonus = [0] * 35
        base[0:7] = [1, 2, 3, 4, 5, 6, 7]
        base[7:16] = [6, 5, 4, 0, 3, 100, 12, 1, 2]
        bonus[7] = 2
        bonus[8] = 1
        struct.pack_into(">35i", payload, 0x30, *base)
        struct.pack_into(">35i", payload, 0xBC, *bonus)
        result = parse_critter_pro(bytes(payload))
        self.assertEqual(result["aiPacket"], 12)
        self.assertEqual(result["team"], 5)
        self.assertEqual(result["hitPoints"], 8)
        self.assertEqual(result["actionPoints"], 6)
        self.assertEqual(result["armorClass"], 4)
        self.assertEqual(result["meleeDamage"], 3)
        self.assertEqual(result["sequence"], 12)

    def test_weapon_and_ammunition_pro_contracts_use_exact_big_endian_layouts(self) -> None:
        weapon = bytearray(122)
        struct.pack_into(">I", weapon, 0, 8)
        struct.pack_into(">i", weapon, 0x20, 3)
        values = [5, 5, 12, 0, 25, 0, -1, 3, 5, 0, 2, -1, 1, 8, 29, 12]
        struct.pack_into(">16i", weapon, 0x39, *values)
        weapon[0x79] = 65
        parsed_weapon = parse_item_pro(bytes(weapon))
        self.assertEqual(parsed_weapon["pid"], "00000008")
        self.assertEqual(parsed_weapon["subtypeName"], "weapon")
        self.assertEqual(parsed_weapon["minimumDamage"], 5)
        self.assertEqual(parsed_weapon["maximumDamage"], 12)
        self.assertEqual(parsed_weapon["maximumRangePrimary"], 25)
        self.assertEqual(parsed_weapon["actionPointCostPrimary"], 5)
        self.assertEqual(parsed_weapon["ammunitionPid"], 29)
        self.assertEqual(parsed_weapon["ammunitionCapacity"], 12)
        self.assertEqual(parsed_weapon["soundCode"], 65)

        ammunition = bytearray(81)
        struct.pack_into(">I", ammunition, 0, 29)
        struct.pack_into(">i", ammunition, 0x20, 4)
        struct.pack_into(">6i", ammunition, 0x39, 8, 24, 0, 25, 2, 1)
        parsed_ammunition = parse_item_pro(bytes(ammunition))
        self.assertEqual(parsed_ammunition["subtypeName"], "ammo")
        self.assertEqual(parsed_ammunition["roundsPerObject"], 24)
        self.assertEqual(parsed_ammunition["damageMultiplier"], 2)

        with self.assertRaisesRegex(Exception, "weapon PRO size"):
            parse_item_pro(bytes(weapon[:-1]))

    def test_v13_starting_inventory_is_decoded_from_script_and_pid_header(self) -> None:
        header = """
            #define PID_KNIFE (4)
            #define PID_10MM_PISTOL (8)
            #define PID_10MM_JHP (29)
            #define PID_STIMPAK (40)
        """
        source = """
            procedure base_inventory begin
              call give_item(dude_obj, {PID_KNIFE: 1, PID_10MM_PISTOL: 1, PID_10MM_JHP: 2});
            end
            procedure TagInven begin
              if is_skill_tagged(SKILL_FIRST_AID) then begin
                Item := create_object(PID_STIMPAK, 0, 0);
                add_mult_objs_to_inven(dude_obj, Item, 2);
              end
              if is_skill_tagged(SKILL_SMALL_GUNS) then begin
                Item := create_object(PID_10MM_JHP, 0, 0);
                add_obj_to_inven(dude_obj, Item);
              end
            end
        """
        pids = parse_pid_header(header)
        result = parse_starting_inventory(
            source,
            pids,
            {"SKILL_FIRST_AID": "First Aid", "SKILL_SMALL_GUNS": "Small Guns"},
        )
        self.assertEqual(
            [(row["symbol"], row["pid"], row["objects"]) for row in result["base"]],
            [("PID_KNIFE", 4, 1), ("PID_10MM_PISTOL", 8, 1), ("PID_10MM_JHP", 29, 2)],
        )
        self.assertEqual(result["tagBonuses"][0]["skill"], "First Aid")
        self.assertEqual(result["tagBonuses"][0]["items"][0]["objects"], 2)
        self.assertEqual(result["tagBonuses"][1]["items"][0]["objects"], 1)

    def test_rat_ai_section_is_parsed_without_inventing_defaults(self) -> None:
        result = parse_ai_section(
            "[Other]\nmax_dist=99\n[Rats]\naggression=80\nmax_dist=6\npacket_num=12\n",
            "Rats",
        )
        self.assertEqual(result["aggression"], "80")
        self.assertEqual(result["max_dist"], "6")
        self.assertEqual(result["packet_num"], "12")

    def test_source_review_compositor_clips_negative_art_positions(self) -> None:
        canvas = Image.new("RGBA", (3, 3), (0, 0, 0, 0))
        source = Image.new("RGBA", (3, 3), (255, 0, 0, 255))
        paste_clipped(canvas, source, (-1, -1))
        self.assertEqual(sum(pixel[3] > 0 for pixel in canvas.get_flattened_data()), 4)

    def test_owned_cave_composition_is_source_bound_and_deterministic(self) -> None:
        roles = (
            "wall",
            "corner",
            "room",
            "large-rock",
            "small-rock",
            "stalagmite",
            "vault-transition",
            "vault-frame",
            "vault-airlock",
            "vault-hall",
            "vault-hall-cap",
            "entrance-corpse",
        )
        manifest = {
            "caveKit": {
                "assets": [{"id": f"asset-{role}", "role": role} for role in roles],
                "textures": [
                    {
                        "requestedPath": r"textures\dungeons\caves\cavesmoothfloor01.dds"
                    },
                    {
                        "requestedPath": r"textures\dungeons\caves\cavesmoothfloor01_n.dds"
                    },
                    {
                        "requestedPath": r"textures\dungeons\caves\caverockwall03.dds"
                    },
                    {
                        "requestedPath": r"textures\dungeons\caves\caverockwall03_n.dds"
                    },
                ],
            },
            "composition": {
                "schema": "opennv-fo1-owned-cave-composition-recipe/v1",
                "floor": {
                    "schema": "opennv-fo1-owned-continuous-floor/v1",
                    "diffusePath": r"textures\dungeons\caves\cavesmoothfloor01.dds",
                    "normalPath": r"textures\dungeons\caves\cavesmoothfloor01_n.dds",
                    "heightMeters": -0.018,
                    "textureRepeatMeters": 3.2,
                    "albedoColor": [0.78, 0.74, 0.66, 1.0],
                    "roughness": 0.94,
                    "normalScale": 0.62,
                },
                "grounding": {
                    "schema": "opennv-fo1-owned-cave-grounding/v1",
                    "maximumRuntimeErrorMeters": 0.002,
                    "roles": {
                        "large-rock": {
                            "seatDepthHeightFraction": 0.06,
                            "minimumSeatDepthMeters": 0.06,
                            "maximumSeatDepthMeters": 0.10,
                        },
                        "small-rock": {
                            "seatDepthHeightFraction": 0.15,
                            "minimumSeatDepthMeters": 0.10,
                            "maximumSeatDepthMeters": 0.14,
                        },
                        "stalagmite": {
                            "seatDepthHeightFraction": 0.015,
                            "minimumSeatDepthMeters": 0.03,
                            "maximumSeatDepthMeters": 0.05,
                        },
                    },
                },
                "vaultPortal": {
                    "schema": "opennv-fo1-owned-vault-portal/v1",
                    "diffusePath": r"textures\dungeons\caves\caverockwall03.dds",
                    "normalPath": r"textures\dungeons\caves\caverockwall03_n.dds",
                    "behindDoorMeters": 0.55,
                    "frontReliefMeters": 0.35,
                    "depthMeters": 2.1,
                    "innerRadiusMeters": 1.75,
                    "outerHalfWidthMeters": 9.5,
                    "outerTopHeightMeters": 7.6,
                    "outerBottomHeightMeters": -0.12,
                    "radialNoiseMeters": 0.28,
                    "segments": 32,
                    "textureRepeatMeters": 2.7,
                    "albedoColor": [0.58, 0.55, 0.49, 1.0],
                    "roughness": 0.97,
                    "normalScale": 0.78,
                },
                "envelope": {
                    "schema": "opennv-fo1-owned-cave-envelope/v1",
                    "diffusePath": r"textures\dungeons\caves\caverockwall03.dds",
                    "normalPath": r"textures\dungeons\caves\caverockwall03_n.dds",
                    "backBehindDoorMeters": 1.1,
                    "forwardMeters": 49.0,
                    "halfWidthMeters": 25.0,
                    "ceilingHeightMeters": 7.35,
                    "ceilingReliefMeters": 0.55,
                    "doorwayHalfWidthMeters": 2.1,
                    "doorwayHeightMeters": 3.2,
                    "subdivisionsAcross": 32,
                    "subdivisionsForward": 52,
                    "textureRepeatMeters": 3.6,
                    "albedoColor": [0.13, 0.125, 0.11, 1.0],
                    "roughness": 0.96,
                    "normalScale": 0.75,
                },
                "wallArtPattern": r"^ca[0-9]+\.frm$",
                "rockArtPattern": r"^rock[0-9]+\.frm$",
                "stalagmiteArtPattern": r"^cstalag[0-9]+\.frm$",
                "wallBinMeters": 4.75,
                "wallNeighborhoodMeters": 6.25,
                "cornerAnisotropyThreshold": 0.42,
                "wallRibbonWidthScale": 1.42,
                "wallRibbonHeightMeters": 2.2,
                "wallRibbonDepthMeters": 1.35,
                "doorFrameBehindMeters": 0.75,
                "vaultAirlockBehindMeters": 2.0,
                "vaultHallBehindMeters": 6.0,
                "vaultHallCapBehindMeters": 10.0,
                "landingRoomGrid": {
                    "across": 5,
                    "depth": 7,
                    "spacingMeters": 7.3152,
                    "forwardOffsetMeters": -3.65,
                },
                "vaultFrameScale": [1.0, 1.0, 1.0],
                "entranceCorpse": {
                    "serial": 250,
                    "pid": "000000d3",
                    "tile": 101 * 200 + 99,
                    "artFilename": "v13bones.frm",
                },
                "roleScale": {role: [1.0, 1.0, 1.0] for role in roles},
            },
        }
        current_composition = json.loads(
            (
                Path(__file__).resolve().parents[1]
                / "recipes"
                / "fo1-v13ent-3d-presentation-v1.json"
            ).read_text(encoding="utf-8")
        )["composition"]
        current_composition["entranceCorpse"] = {
            "serial": 250,
            "pid": "000000d3",
            "tile": 101 * 200 + 99,
            "artFilename": "v13bones.frm",
        }
        manifest["composition"] = current_composition
        manifest["caveKit"]["textures"].extend(
            [
                {"requestedPath": r"textures\dungeons\vault\vwall01.dds"},
                {"requestedPath": r"textures\dungeons\vault\vwall01_n.dds"},
            ]
        )
        obstacles = [
            {
                "serial": 1,
                "tile": 100 * 200 + 100,
                "rotation": 0,
                "radiusMeters": 0.4,
                "artFilename": "ca001.frm",
            },
            {
                "serial": 2,
                "tile": 100 * 200 + 101,
                "rotation": 0,
                "radiusMeters": 0.4,
                "artFilename": "ca002.frm",
            },
            {
                "serial": 3,
                "tile": 102 * 200 + 101,
                "rotation": 2,
                "radiusMeters": 0.6,
                "artFilename": "rock01.frm",
            },
            {
                "serial": 4,
                "tile": 103 * 200 + 101,
                "rotation": 3,
                "radiusMeters": 0.3,
                "artFilename": "rock04.frm",
            },
            {
                "serial": 5,
                "tile": 104 * 200 + 101,
                "rotation": 1,
                "radiusMeters": 0.3,
                "artFilename": "cstalag1.frm",
            },
            {
                "serial": 6,
                "tile": 105 * 200 + 101,
                "rotation": 0,
                "radiusMeters": 0.3,
                "artFilename": "block.frm",
            },
        ]
        door = {"serial": 129, "tile": 98 * 200 + 100}
        entry = {"tile": 102 * 200 + 100}
        sprites = [
            {
                "serial": 250,
                "pid": "000000d3",
                "tile": 101 * 200 + 99,
                "rotation": 0,
                "artFilename": "v13bones.frm",
            }
        ]
        generation = {
            "rockSerialYawMultiplierDegrees": 47,
            "corridorClosurePaddingMeters": 2.5,
            "corpseYawOffsetDegrees": -18.0,
            "corpsePitchDegrees": 90.0,
        }
        wall_sprites = [
            {
                "serial": serial,
                "tile": obstacles[serial - 1]["tile"],
                "rotation": 0,
                "pixelOffset": [0, 0],
                "artifactId": "fixture-wall",
            }
            for serial in (1, 2)
        ]
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            output_root = root / "output"
            fixture = Image.new("RGBA", (18, 28), (0, 0, 0, 0))
            for y in range(3, 28):
                for x in range(2, 16):
                    fixture.putpixel((x, y), (96 + x, 82 + y, 68, 255))
            fixture_bytes = fixture.tobytes()
            artifact = {
                "id": "fixture-wall",
                "png": str((output_root / "sprites" / "fixture-wall.png").resolve()),
                "pngSha256": hashlib.sha256(fixture_bytes).hexdigest(),
                "sourceSha256": "1" * 64,
                "width": fixture.width,
                "height": fixture.height,
                "frameOffset": [0, 0],
            }

            def run(staging_name: str) -> dict[str, object]:
                staging = root / staging_name
                image_path = staging / "sprites" / "fixture-wall.png"
                image_path.parent.mkdir(parents=True)
                fixture.save(image_path, format="PNG", optimize=False)
                return build_owned_cave_composition(
                    obstacles,
                    sprites + wall_sprites,
                    {"fixture-wall": artifact},
                    door,
                    entry,
                    manifest,
                    generation,
                    staging,
                    output_root,
                    43.11464576045433,
                )

            first = run("staging-first")
            second = run("staging-second")
        self.assertEqual(first, second)
        coverage = first["coverage"]
        self.assertEqual(coverage["sourceWallObjects"], 2)
        self.assertEqual(coverage["sourceRockObjects"], 2)
        self.assertEqual(coverage["sourceStalagmiteObjects"], 1)
        self.assertEqual(coverage["groundedInstances"], 3)
        self.assertEqual(
            coverage["groundingRoles"],
            {"large-rock": 1, "small-rock": 1, "stalagmite": 1},
        )
        self.assertEqual(
            first["recipe"]["grounding"]["roles"]["small-rock"],
            current_composition["grounding"]["roles"]["small-rock"],
        )
        self.assertEqual(
            coverage["wallRibbonSegments"], len(first["frmRelief"]["placements"])
        )
        self.assertGreater(coverage["wallRibbonSegments"], 0)
        self.assertEqual(first["envelope"]["source"]["doorTile"], door["tile"])
        self.assertEqual(first["envelope"]["source"]["entryTile"], entry["tile"])
        self.assertEqual(
            first["envelope"]["schema"],
            "opennv-fo1-owned-cave-topology-envelope/v1",
        )
        self.assertIn("no rectangular room grid or sky box", first["envelope"]["source"]["mapping"])
        self.assertEqual(coverage["roles"]["terrain-envelope"], 1)
        self.assertEqual(coverage["roles"]["vault-portal"], 1)
        self.assertEqual(first["vaultPortal"]["source"]["doorTile"], door["tile"])
        self.assertEqual(first["vaultPortal"]["source"]["entryTile"], entry["tile"])
        self.assertEqual(
            first["vaultPortal"]["floorHeightMeters"],
            manifest["composition"]["floor"]["heightMeters"],
        )
        self.assertNotIn(6, {
            serial
            for placement in first["placements"]
            for serial in placement["source"]["serials"]
        })
        frame = next(
            row for row in first["placements"] if row["assetRole"] == "vault-frame"
        )
        self.assertEqual(frame["source"]["serials"], [door["serial"]])
        self.assertLess(frame["positionMeters"][2], hex_center(door["tile"])[2])
        corpse = next(
            row for row in first["placements"] if row["assetRole"] == "entrance-corpse"
        )
        self.assertEqual(corpse["positionMeters"], hex_center(sprites[0]["tile"]))
        self.assertEqual(corpse["source"]["serials"], [250])
        self.assertEqual(first["connectedWallVolume"]["coverage"]["sourcePlacements"], 2)
        self.assertGreaterEqual(first["connectedWallVolume"]["coverage"]["profileMeshes"], 1)


if __name__ == "__main__":
    unittest.main()
