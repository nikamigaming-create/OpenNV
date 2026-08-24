import math
import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_review_contract import (  # noqa: E402
    _appearance_contract,
    _d3d_perspective_frustum,
    _replace_d3d_projection_xy,
)
from prepare_creature_review import _retail_equipped_weapon_attachment  # noqa: E402


class ActorReviewContractTest(unittest.TestCase):
    @staticmethod
    def _appearance_events(role: str = "weapon", schema: str = "nikami-fnv-sidecar-appearance/v3"):
        frame = 70
        weapon_form = 0x010117F7
        model_path = "weapons/2handmelee/knifespear/knifespear.nif"
        return [
            {
                "event": "actor-pose-sample",
                "frame": frame,
                "weaponForm": weapon_form,
                "weaponOut": False,
            },
            {
                "event": "actor-visual-snapshot",
                "frame": frame,
                "appearance": {
                    "schema": schema,
                    "complete": True,
                    "truncated": False,
                    "equippedWeapon": {
                        "state": "equipped",
                        "renderState": "visible-source-bound",
                        "weaponOut": False,
                        "sourceFormId": "0x010117F7",
                        "modelPath": model_path,
                        "nodePresent": True,
                    },
                    "renderParts": [
                        {
                            "role": role,
                            "sourceFormId": "0x010117F7",
                            "modelPath": model_path,
                            "required": True,
                            "attached": True,
                            "drawable": True,
                            "visible": True,
                            "textureBindings": [],
                        }
                    ],
                },
            },
        ]

    def test_appearance_contract_accepts_pose_bound_equipped_weapon(self):
        result = _appearance_contract(self._appearance_events())

        self.assertEqual(result["frame"], 70)
        self.assertEqual(
            result["snapshot"]["equippedWeapon"]["sourceFormId"],
            "0x010117F7",
        )

    def test_appearance_contract_rejects_weapon_geometry_mislabeled_as_actor(self):
        with self.assertRaisesRegex(ValueError, "authoritative visible runtime attachment"):
            _appearance_contract(self._appearance_events(role="actor"))

    def test_appearance_contract_rejects_legacy_unbound_snapshot(self):
        with self.assertRaisesRegex(ValueError, "incomplete or truncated"):
            _appearance_contract(
                self._appearance_events(schema="nikami-fnv-sidecar-appearance/v1")
            )

    def test_appearance_contract_accepts_modeled_weapon_not_visible_at_frame(self):
        events = self._appearance_events()
        appearance = events[1]["appearance"]
        appearance["equippedWeapon"]["renderState"] = "not-visible-at-frame"
        appearance["equippedWeapon"]["nodePresent"] = False
        appearance["renderParts"][0]["role"] = "actor"

        result = _appearance_contract(events)

        self.assertEqual(
            result["snapshot"]["equippedWeapon"]["renderState"],
            "not-visible-at-frame",
        )

    def test_appearance_contract_accepts_model_less_embedded_weapon(self):
        events = self._appearance_events()
        appearance = events[1]["appearance"]
        weapon = appearance["equippedWeapon"]
        weapon["renderState"] = "not-visible-at-frame"
        weapon["modelPath"] = ""
        appearance["renderParts"][0]["role"] = "actor"

        _appearance_contract(events)

    def test_appearance_contract_rejects_drawn_weapon_not_visible_at_frame(self):
        events = self._appearance_events()
        appearance = events[1]["appearance"]
        appearance["equippedWeapon"]["renderState"] = "not-visible-at-frame"
        appearance["equippedWeapon"]["weaponOut"] = True
        events[0]["weaponOut"] = True
        appearance["renderParts"][0]["role"] = "actor"

        with self.assertRaisesRegex(ValueError, "nonvisible equipped weapon"):
            _appearance_contract(events)

    def test_appearance_contract_rejects_non_object_texture_binding(self):
        events = self._appearance_events()
        events[1]["appearance"]["renderParts"][0]["textureBindings"] = ["invalid"]

        with self.assertRaisesRegex(ValueError, "texture bindings"):
            _appearance_contract(events)

    def test_creature_compiler_retains_retail_weapon_source_identity(self):
        events = self._appearance_events()
        snapshot = events[1]["appearance"]
        snapshot["renderParts"][0]["sourceSlot"] = 5

        attachment = _retail_equipped_weapon_attachment(
            {"retail": {"appearance": {"snapshot": snapshot}}}
        )

        self.assertIsNotNone(attachment)
        self.assertEqual(attachment.role, "weapon")
        self.assertEqual(attachment.source_form_id, "0x010117F7")
        self.assertEqual(attachment.source_slot, 5)
        self.assertEqual(
            attachment.model_path,
            "weapons/2handmelee/knifespear/knifespear.nif",
        )

    def test_creature_compiler_omits_weapon_not_visible_at_frame(self):
        events = self._appearance_events()
        snapshot = events[1]["appearance"]
        snapshot["equippedWeapon"]["renderState"] = "not-visible-at-frame"
        snapshot["renderParts"][0]["role"] = "actor"

        attachment = _retail_equipped_weapon_attachment(
            {"retail": {"appearance": {"snapshot": snapshot}}}
        )

        self.assertIsNone(attachment)

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
