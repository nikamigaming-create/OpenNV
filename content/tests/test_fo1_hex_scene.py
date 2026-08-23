from __future__ import annotations

import math
import struct
import sys
import unittest
from pathlib import Path

from PIL import Image


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_fo1_hex_scene import (  # noqa: E402
    classic_floor_screen,
    classic_hex_screen,
    floor_index_for_hex,
    floor_patch_center,
    hex_center,
    parse_critter_pro,
    unproject_floor,
)
from render_fo1_source_map import paste_clipped  # noqa: E402


class Fo1HexSceneTest(unittest.TestCase):
    def test_one_meter_odd_row_hex_topology_and_floor_mapping(self) -> None:
        tile = 10 * 200 + 20
        neighbors = [
            9 * 200 + 19,
            9 * 200 + 20,
            10 * 200 + 19,
            10 * 200 + 21,
            11 * 200 + 19,
            11 * 200 + 20,
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
        self.assertGreater(sum(pixel[3] > 0 for pixel in result.get_flattened_data()), 3500)

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

    def test_source_review_compositor_clips_negative_art_positions(self) -> None:
        canvas = Image.new("RGBA", (3, 3), (0, 0, 0, 0))
        source = Image.new("RGBA", (3, 3), (255, 0, 0, 255))
        paste_clipped(canvas, source, (-1, -1))
        self.assertEqual(sum(pixel[3] > 0 for pixel in canvas.get_flattened_data()), 4)


if __name__ == "__main__":
    unittest.main()
