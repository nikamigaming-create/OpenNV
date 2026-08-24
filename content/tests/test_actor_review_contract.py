import math
import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_review_contract import (  # noqa: E402
    _d3d_perspective_frustum,
    _replace_d3d_projection_xy,
)


class ActorReviewContractTest(unittest.TestCase):
    def test_captured_d3d9_projection_resolves_final_scene_frustum(self):
        projection = [
            0.9774190187454224, 0.0, 0.0, 0.0,
            0.0, 1.7376338243484497, 0.0, 0.0,
            0.0, 0.0, 1.0000141859054565, 1.0,
            0.0, 0.0, -5.000070571899414, 0.0,
        ]

        frustum, fov_y = _d3d_perspective_frustum(projection, "captured dog")

        self.assertAlmostEqual(frustum[0], -1.0231026, places=6)
        self.assertAlmostEqual(frustum[1], 1.0231026, places=6)
        self.assertAlmostEqual(frustum[2], 0.57549524, places=6)
        self.assertAlmostEqual(frustum[3], -0.57549524, places=6)
        self.assertAlmostEqual(frustum[4], 5.0, places=4)
        self.assertAlmostEqual(math.degrees(fov_y), 59.84044, places=4)

    def test_final_projection_replaces_only_combined_xy_rows(self):
        combined = [float(value) for value in range(1, 17)]
        culling = [
            2.0, 0.0, 0.0, 0.0,
            0.0, 4.0, 0.0, 0.0,
            0.0, 0.0, 1.25, 1.0,
            0.0, 0.0, -5.0, 0.0,
        ]
        surface = list(culling)
        surface[0] = 1.0
        surface[5] = 2.0

        result = _replace_d3d_projection_xy(
            combined,
            culling,
            surface,
            "synthetic final surface",
        )

        self.assertEqual(result[:4], [0.5, 1.0, 1.5, 2.0])
        self.assertEqual(result[4:8], [2.5, 3.0, 3.5, 4.0])
        self.assertEqual(result[8:], combined[8:])


if __name__ == "__main__":
    unittest.main()
