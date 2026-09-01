from __future__ import annotations

import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo1_map_objects import (  # noqa: E402
    Fo1ResourceResolver,
    critter_fid_fields,
    placed_critter_art_state,
)
from fo1_profile import Fo1ProfileError  # noqa: E402
from prepare_fo1_campaign_presentation import (  # noqa: E402
    build_connected_wall_topology,
    hex_neighbor_across_edge,
    parse_message_catalog,
    resolve_child,
    source_sprite_logical_path,
    validate_viewer_config,
)
from fo1_campaign_transport import build_campaign_transport  # noqa: E402,F401


MAP_FORMAT = {
    "supportedCritterIdleAnimation": 0,
    "supportedCritterIdleWeapon": 0,
    "supportedCritterPackedRotation": 0,
}


class Fo1CampaignPresentationTest(unittest.TestCase):
    def test_transport_supports_one_explicit_map_without_directory_scan(self) -> None:
        import inspect

        source = inspect.getsource(build_campaign_transport)
        self.assertIn("map_file: str | None", source)
        self.assertIn("selected MAP is absent", source)
        self.assertIn("map_paths = [selected]", source)
    def test_message_catalog_preserves_source_text(self) -> None:
        catalog = parse_message_catalog(
            "# comment\r\n{100}{}{Giant Rat}\r\n{101}{ignored}{Vault {Dweller}}\r\n"
        )
        self.assertEqual(catalog, {100: "Giant Rat", 101: "Vault {Dweller}"})

    def test_message_catalog_fails_closed_on_unsupported_rows(self) -> None:
        with self.assertRaises(Fo1ProfileError):
            parse_message_catalog("not a Fallout message row")
        with self.assertRaises(Fo1ProfileError):
            parse_message_catalog("{100}{}{first}\n{100}{}{second}")

    def test_regular_object_uses_its_source_art_directory(self) -> None:
        obj = {
            "artFilename": "door.frm",
            "prototype": {"object_type": 2},
            "fid": "02000001",
            "rotation": 0,
            "frame": 0,
        }
        self.assertEqual(
            source_sprite_logical_path(obj, MAP_FORMAT),
            "art\\scenery\\door.frm",
        )

    def test_source_critter_fid_selects_exact_weapon_and_rejects_non_idle(self) -> None:
        idle = {
            "artFilename": "hmjmps,0",
            "prototype": {"object_type": 1},
            "fid": "01000001",
            "rotation": 0,
            "frame": 0,
        }
        weaponed = {**idle, "fid": "01007001"}
        rocket = {**idle, "fid": "0100a001"}
        animated = {**idle, "fid": "01070001"}
        self.assertEqual(
            source_sprite_logical_path(idle, MAP_FORMAT),
            "art\\critters\\hmjmpsaa.frm",
        )
        self.assertEqual(
            source_sprite_logical_path(weaponed, MAP_FORMAT),
            "art\\critters\\hmjmpsja.frm",
        )
        self.assertEqual(
            source_sprite_logical_path(rocket, MAP_FORMAT),
            "art\\critters\\hmjmpsma.frm",
        )
        self.assertIsNone(source_sprite_logical_path(animated, MAP_FORMAT))
        self.assertIsNone(
            source_sprite_logical_path({**idle, "fid": "11000001"}, MAP_FORMAT)
        )
        self.assertEqual(
            critter_fid_fields(int("0100700a", 16)),
            {"animation": 0, "weapon": 7, "packedRotation": 0},
        )

    def test_single_frame_death_selects_direction_file_terminal_frame(self) -> None:
        state = placed_critter_art_state(
            "hmjmps,0",
            int("0137000b", 16),
            4,
        )
        self.assertEqual(state.logical_path, "art\\critters\\hmjmpsrh.frm")
        self.assertEqual(state.source_rotation, 4)
        self.assertEqual(state.frame_selection, "terminal")
        with self.assertRaises(Fo1ProfileError):
            placed_critter_art_state("hmjmps,0", int("012f000b", 16), 4)

    def test_called_shot_picture_uses_critter_list_alias_for_owned_rows(self) -> None:
        resolver = object.__new__(Fo1ResourceResolver)
        art_by_index = {
            11: "hmjmps,11",
            28: "naghul,11",
            48: "nmpeas,11",
        }
        resolver.art_filename = lambda fid: art_by_index[fid & 0x0FFF]

        def read(logical_path: str):
            if logical_path != "art\\critters\\hmjmpsna.frm":
                raise FileNotFoundError(logical_path)
            return object()

        resolver.read = read
        for fid, rotation in (
            ("013f001c", 4),
            ("013f0030", 1),
            ("013f0030", 0),
        ):
            state = resolver.placed_critter_art_state(int(fid, 16), rotation)
            self.assertEqual(state.logical_path, "art\\critters\\hmjmpsna.frm")
            self.assertEqual(state.source_rotation, rotation)
            self.assertEqual(state.frame_selection, "stored")
            self.assertEqual(state.alias_art_index, 11)

    def test_runtime_binds_source_critter_fid_and_frm_timing(self) -> None:
        runtime_root = Path(__file__).resolve().parents[2] / "runtime" / "src"
        contract = (
            runtime_root
            / "Campaigns/Fallout1/Fo1CampaignPresentationContract.cs"
        ).read_text(encoding="utf-8")
        viewer = (
            runtime_root
            / "Campaigns/Fallout1/Fo1CampaignPresentationViewer.cs"
        ).read_text(encoding="utf-8")
        self.assertIn('TryGetProperty("critterFidState"', contract)
        for field in (
            'GetProperty("directionOffset")',
            'GetProperty("framesPerSecond")',
            'GetProperty("actionFrame")',
            'RequiredString(source, "frameSelection")',
        ):
            self.assertIn(field, contract)
        for metadata in (
            '"source_fid_animation"',
            '"source_fid_weapon"',
            '"source_frm_fps"',
            '"source_frm_action_frame"',
            '"source_frm_frame_selection"',
        ):
            self.assertIn(metadata, viewer)

    def test_campaign_child_paths_cannot_escape(self) -> None:
        root = Path.cwd().resolve()
        self.assertEqual(resolve_child(root, "maps/example.json"), root / "maps/example.json")
        with self.assertRaises(Fo1ProfileError):
            resolve_child(root, "../escape.json")

    def test_viewer_config_requires_explicit_capture_thresholds(self) -> None:
        viewer = {
            "defaultMapId": "v13ent",
            "scene": {
                "sourceSpriteOrientation": "camera-facing-source-reference",
                "sourceReferenceOrbitEnabled": True,
                "sourceReferenceVisibleByDefault": False,
                "sourceColorMultiplier": [1.0, 1.0, 1.0, 1.0],
                "tonemapExposure": 1.0,
                "fogDensity": 0.01,
                "fogAerialPerspective": 0.5,
            },
            "wallGeometry": {
                "mode": "source-wall-hex-union-v1",
                "sourceObjectType": 3,
                "collisionMode": "blocking-wall-hex-union-v1",
                "cellRadiusScale": 1.0,
                "heightMeters": 3.0,
                "groundSinkMeters": 0.05,
                "roughness": 1.0,
                "metallic": 0.0,
                "sourceAlphaThreshold": 0.2,
                "unresolvedSourceAlbedo": [0.2, 0.2, 0.2, 1.0],
                "sideColorMultiplier": [0.8, 0.8, 0.8, 1.0],
                "topColorMultiplier": [0.6, 0.6, 0.6, 1.0],
            },
            "statusPanel": {
                "leftPixels": 0.0,
                "topPixels": 0.0,
                "rightPixels": 1.0,
                "bottomPixels": 1.0,
                "textLeftPixels": 0.0,
                "textTopPixels": 0.0,
                "textRightPixels": 1.0,
                "textBottomPixels": 1.0,
                "panelColor": [0.0, 0.0, 0.0, 1.0],
                "fontColor": [0.0, 1.0, 0.0, 1.0],
                "fontSizePixels": 17,
            },
            "capture": {
                "warmupFrames": 1,
                "settleFrames": 1,
                "expectedWidthPixels": 1280,
                "expectedHeightPixels": 720,
                "darkPixelLuminance": 0.03,
                "minimumMeanLuminance": 0.02,
                "minimumLuminanceDeviation": 0.02,
                "maximumDarkFraction": 0.9,
            },
        }
        self.assertIs(validate_viewer_config(viewer), viewer)
        viewer["capture"]["maximumDarkFraction"] = 2.0
        with self.assertRaises(Fo1ProfileError):
            validate_viewer_config(viewer)

    def test_wall_topology_joins_adjacent_source_cells_in_linear_union(self) -> None:
        first = 20100
        second = hex_neighbor_across_edge(first, 0)
        third = hex_neighbor_across_edge(second, 1)

        def wall(serial: int, tile: int, blocking: bool = True) -> dict:
            return {
                "serial": serial,
                "tile": tile,
                "rotation": serial % 6,
                "flags": "00000000" if blocking else "00000010",
                "artFilename": "block.frm" if serial == 2 else "ca001.frm",
                "prototype": {"object_type": 3},
            }

        floor_ids = [2] * 10000
        topology = build_connected_wall_topology(
            [wall(1, first), wall(2, second), wall(3, third, False)],
            floor_ids,
            default_tile_id=1,
            no_block_flag=16,
        )
        self.assertEqual([row["tile"] for row in topology["cells"]], sorted([first, second, third]))
        self.assertEqual(topology["coverage"]["occupiedHexes"], 3)
        self.assertEqual(topology["coverage"]["connectedComponents"], 1)
        self.assertEqual(topology["coverage"]["boundaryEdges"], 14)
        self.assertEqual(topology["coverage"]["blockingHexes"], 2)
        self.assertEqual(topology["coverage"]["nonBlockingHexes"], 1)

    def test_wall_topology_keeps_isolated_source_truth_explicit(self) -> None:
        rows = [
            {
                "serial": serial,
                "tile": tile,
                "rotation": 0,
                "flags": "00000000",
                "artFilename": "va1000.frm",
                "prototype": {"object_type": 3},
            }
            for serial, tile in ((1, 20100), (2, 21100), (3, -1))
        ]
        topology = build_connected_wall_topology(rows, [1] * 10000, 1, 16)
        self.assertEqual(topology["sourceWallObjects"], 3)
        self.assertEqual(topology["offGridSourceWallObjects"], 1)
        self.assertEqual(topology["coverage"]["occupiedHexes"], 2)
        self.assertEqual(topology["coverage"]["connectedComponents"], 2)
        self.assertEqual(topology["coverage"]["isolatedHexes"], 2)


if __name__ == "__main__":
    unittest.main()
