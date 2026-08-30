from __future__ import annotations

import sys
import unittest
from pathlib import Path

from PIL import Image


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from fo2_arroyo_molded_surface import (  # noqa: E402
    BYTE_CHANNEL_MAXIMUM,
    NORMAL_CHANNEL_NEUTRAL,
    _derive_periodic_normal_map,
)


class Fo2ArroyoMoldedSurfaceTest(unittest.TestCase):
    def test_periodic_normal_is_deterministic_and_source_luminance_bound(self) -> None:
        source = Image.new("RGB", (4, 3))
        for y in range(source.height):
            for x, value in enumerate((0, 64, 128, 64)):
                source.putpixel((x, y), (value, value, value))

        first = _derive_periodic_normal_map(
            source,
            blur_radius=0.0,
            sample_radius=1,
            strength=2.0,
        )
        second = _derive_periodic_normal_map(
            source,
            blur_radius=0.0,
            sample_radius=1,
            strength=2.0,
        )

        self.assertEqual(first.tobytes(), second.tobytes())
        self.assertEqual(
            first.getpixel((0, 1)),
            (NORMAL_CHANNEL_NEUTRAL, NORMAL_CHANNEL_NEUTRAL, BYTE_CHANNEL_MAXIMUM),
        )
        self.assertLess(first.getpixel((1, 1))[0], NORMAL_CHANNEL_NEUTRAL)
        self.assertGreater(first.getpixel((3, 1))[0], NORMAL_CHANNEL_NEUTRAL)
        self.assertEqual(first.getpixel((1, 1))[1], NORMAL_CHANNEL_NEUTRAL)
        self.assertLess(first.getpixel((1, 1))[2], BYTE_CHANNEL_MAXIMUM)


if __name__ == "__main__":
    unittest.main()
