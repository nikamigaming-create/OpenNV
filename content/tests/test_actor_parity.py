import tempfile
import unittest
from pathlib import Path
import sys

from PIL import Image

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_parity import (  # noqa: E402
    angle_error,
    difference_metrics,
    image_metrics,
    normalize_form,
    shot_state_metrics,
)


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

    def test_retail_shot_state_metrics_fail_closed(self):
        bone_names = [f"bone-{index}" for index in range(50)]
        retail_bones = [
            {
                "name": name,
                "transform": {
                    "localRotation": [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0],
                    "localTranslation": [0.0, 0.0, 0.0],
                    "worldRotation": [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0],
                    "worldTranslation": [0.0, 0.0, 0.0],
                },
            }
            for name in bone_names
        ]
        godot_bones = [
            {
                "name": name,
                "localTranslation": [0.0, 0.0, 0.0],
                "localRotationQuaternion": [0.0, 0.0, 0.0, 1.0],
                "worldPosition": [0.0, 0.0, 0.0],
                "worldRotationQuaternion": [0.0, 0.0, 0.0, 1.0],
            }
            for name in bone_names
        ]
        retail = {
            "referenceTransform": {"position": [1.0, 2.0, 3.0], "rotation": [0.0, 0.0, 3.1]},
            "camera": {
                "position": [4.0, 5.0, 6.0],
                "aim": [1.0, 2.0, 7.0],
                "distance": 70.0,
                "projection": {"fovYDegrees": 46.6921257},
            },
            "pose": {
                "activeSequences": [
                    {
                        "file": r"Characters\_Male\Locomotion\mtidle.kf",
                        "lastScaled": 1.202,
                    }
                ],
                "bones": retail_bones,
            },
            "contextActors": [
                {
                    "referenceForm": "0x20",
                    "baseForm": "0x21",
                    "position": [8.0, 9.0, 10.0],
                    "rotation": [0.0, 0.0, 1.25],
                    "activeSequences": [{"file": "sit.kf", "weight": 1.0, "lastScaled": 0.5}],
                    "bones": retail_bones,
                }
            ],
        }
        godot = {
            "retailStateApplied": True,
            "cellOriginGameUnits": [0.0, 0.0, 0.0],
            "unitsToMeters": 1.0,
            "referencePositionGameUnits": [1.0, 2.0, 3.0],
            "referenceYawRadians": 3.1,
            "referenceGodotYawRadians": -3.1,
            "cameraPositionGameUnits": [4.0, 5.0, 6.0],
            "cameraAimGameUnits": [1.0, 2.0, 7.0],
            "distanceMeters": 70.0 * 0.0142875,
            "verticalFovDegrees": 46.6921257,
            "appliedAnimationPhaseSeconds": 1.202,
            "poseBones": godot_bones,
            "contextActors": [
                {
                    "referenceFormId": "00000020",
                    "baseFormId": "00000021",
                    "positionGameUnits": [8.0, 9.0, 10.0],
                    "godotYawRadians": -1.25,
                    "appliedAnimationPhaseSeconds": 0.5,
                    "poseBones": godot_bones,
                }
            ],
        }
        self.assertEqual(shot_state_metrics(retail, godot)["status"], "pass")
        godot["verticalFovDegrees"] = 75.0
        self.assertEqual(shot_state_metrics(retail, godot)["status"], "fail")
        self.assertAlmostEqual(angle_error(0.0, 2.0 * 3.141592653589793), 0.0)


if __name__ == "__main__":
    unittest.main()
