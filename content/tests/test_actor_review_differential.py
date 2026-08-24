import unittest
from pathlib import Path
import sys


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_review_differential import (  # noqa: E402
    motion_durations,
    rendering_passes,
    structural_passes,
)
from runtime_configuration import load_runtime_configuration  # noqa: E402


class ActorReviewDifferentialTest(unittest.TestCase):
    def test_motion_timing_comes_from_retail_source_frames(self):
        durations = motion_durations([165, 175, 185], 60.0)
        self.assertEqual(len(durations), 3)
        self.assertTrue(all(abs(value - (1.0 / 6.0)) < 1e-12 for value in durations))
        with self.assertRaises(ValueError):
            motion_durations([175, 165], 60.0)
        with self.assertRaises(ValueError):
            motion_durations([165], 60.0)

    def test_rendering_and_structure_fail_closed(self):
        configuration = load_runtime_configuration()
        retail = {"meanLuminance": 0.5}
        godot = {"meanLuminance": 0.5}
        difference = {
            "meanAbsoluteError": 0.0,
            "changedPixelFraction": 0.0,
        }
        self.assertTrue(rendering_passes(retail, godot, difference, configuration))
        difference["changedPixelFraction"] = 1.0
        self.assertFalse(rendering_passes(retail, godot, difference, configuration))

        sample = {
            "projectionExact": True,
            "posePassed": True,
            "skinPalette": {"passed": True},
            "finalSceneColorSurface": {},
            "cullingObservation": {},
        }
        self.assertTrue(structural_passes(sample))
        sample["posePassed"] = False
        self.assertFalse(structural_passes(sample))


if __name__ == "__main__":
    unittest.main()
