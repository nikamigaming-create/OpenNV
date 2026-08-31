from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
OPENING = ROOT / "runtime" / "src" / "Campaigns" / "NewVegas" / "Opening"


class FnvFaceGenPreviewRuntimeTest(unittest.TestCase):
    def test_owned_ui_value_is_normalized_once_inside_preview_host(self) -> None:
        recipe = json.loads(
            (ROOT / "content" / "recipes" / "fnv-new-game-opening-v1.json")
            .read_text(encoding="utf-8")
        )
        preview = recipe["newGameFlow"]["characterRules"][
            "faceGenControlSpace"
        ]["runtimePreviewControl"]
        self.assertEqual(0.1, preview["morphWeightScale"])
        self.assertEqual(2.5, preview["acceptanceValue"] * preview["morphWeightScale"])

        host = (OPENING / "OpeningPlayerFaceGenPreviewHost.cs").read_text(
            encoding="utf-8"
        )
        apply_method = host[
            host.index("internal void Apply") : host.index(
                "internal Image CaptureRenderedImage"
            )
        ]
        self.assertIn("var morphWeight = uiValue * _morphWeightScale;", apply_method)
        self.assertIn("SetBlendShapeValue(binding.Index, morphWeight)", apply_method)
        self.assertNotIn("SetBlendShapeValue(binding.Index, uiValue)", apply_method)

        flow = (OPENING / "OpeningQuestRuntime.cs").read_text(encoding="utf-8")
        self.assertNotIn(
            "value * previewPolicy.MorphWeightScale);",
            flow,
        )
        self.assertNotIn(
            "_faceGeometryControlValues[control.SettingEntity] *\n"
            "                        previewPolicy.MorphWeightScale);",
            flow,
        )

    def test_vertex_delta_report_converts_actor_game_units_to_meters(self) -> None:
        host = (OPENING / "OpeningPlayerFaceGenPreviewHost.cs").read_text(
            encoding="utf-8"
        )
        metric = host[
            host.index("internal float MaximumAppliedVertexDeltaMeters") : host.index(
                "private static void VerifyHash"
            )
        ]
        self.assertIn("position.Length() * MathF.Abs(weight) * _unitsToMeters", metric)
        self.assertNotIn("position.Length() * MathF.Abs(weight));", metric)


if __name__ == "__main__":
    unittest.main()
