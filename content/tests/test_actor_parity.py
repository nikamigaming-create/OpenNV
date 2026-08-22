import tempfile
import unittest
from pathlib import Path
import sys

from PIL import Image

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_parity import difference_metrics, image_metrics, normalize_form  # noqa: E402


class ActorParityTest(unittest.TestCase):
    def test_form_identity_and_pixel_metrics_are_deterministic(self):
        self.assertEqual(normalize_form("0x104c6d"), "00104c6d")
        self.assertEqual(normalize_form("00104C6D"), "00104c6d")
        with tempfile.TemporaryDirectory() as directory:
            first = Path(directory) / "first.png"
            second = Path(directory) / "second.png"
            Image.new("RGB", (4, 3), (10, 20, 30)).save(first)
            Image.new("RGB", (4, 3), (10, 20, 30)).save(second)
            self.assertEqual(image_metrics(first)["width"], 4)
            self.assertEqual(difference_metrics(first, second)["meanAbsoluteError"], 0.0)


if __name__ == "__main__":
    unittest.main()
