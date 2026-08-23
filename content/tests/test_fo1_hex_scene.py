from __future__ import annotations

import math
import sys
import unittest
from pathlib import Path

from PIL import Image


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_fo1_hex_scene import (  # noqa: E402
    floor_index_for_hex,
    floor_patch_center,
    hex_center,
    unproject_floor,
)


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
        self.assertEqual(floor_indices, {510})
        expected = [
            sum(
                hex_center((10 + offset_y) * 200 + 20 + offset_x)[axis]
                for offset_y in range(2)
                for offset_x in range(2)
            )
            / 4.0
            for axis in range(3)
        ]
        self.assertEqual(floor_patch_center(510), expected)

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


if __name__ == "__main__":
    unittest.main()
