from __future__ import annotations

import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_humanoid_review import (  # noqa: E402
    _retail_face_geometry_name,
    _retail_visible_attachments,
    _runtime_form_key,
)


class PrepareHumanoidReviewTest(unittest.TestCase):
    @staticmethod
    def _part(
        *,
        role: str,
        source_form_id: str,
        source_slot: int,
        model_path: str,
        bindings: list[tuple[str, str]],
    ) -> dict[str, object]:
        return {
            "role": role,
            "sourceFormId": source_form_id,
            "sourceSlot": source_slot,
            "modelPath": model_path,
            "required": True,
            "attached": True,
            "drawable": True,
            "visible": True,
            "textureBindings": [
                {"semantic": semantic, "path": path}
                for semantic, path in bindings
            ],
        }

    def test_groups_runtime_attachments_and_modifies_only_data_proven_skin(self) -> None:
        outfit = "armor/leatherarmor/f/outfitf.nif"
        glove = "armor/leatherarmor/f/glovel.nif"
        parts = [
            self._part(
                role="equipment",
                source_form_id="0x00020423",
                source_slot=2,
                model_path=outfit,
                bindings=[("baseColor", "textures/armor/leatherarmor/outfitf.dds")],
            ),
            self._part(
                role="equipment",
                source_form_id="0x00020423",
                source_slot=2,
                model_path=outfit,
                bindings=[
                    ("baseColor", "textures/characters/childfemale/upperbodyfemale.dds"),
                    ("faceGenDetail", "textures/characters/female/upperbodyfemale_sk.dds"),
                ],
            ),
            self._part(
                role="equipment",
                source_form_id="0x00029387",
                source_slot=3,
                model_path=glove,
                bindings=[("baseColor", "textures/characters/female/handfemale.dds")],
            ),
        ]
        contract = {"retail": {"appearance": {"snapshot": {"renderParts": parts}}}}

        attachments = _retail_visible_attachments(
            contract,
            (
                "characters/female/upperbodyfemale.dds",
                "characters/female/handfemale.dds",
            ),
        )

        self.assertEqual(len(attachments), 2)
        self.assertEqual(
            attachments[0].base_color_paths,
            (
                "textures\\armor\\leatherarmor\\outfitf.dds",
                "textures\\characters\\childfemale\\upperbodyfemale.dds",
            ),
        )
        self.assertEqual(
            attachments[0].skin_diffuse_paths,
            ("textures\\characters\\childfemale\\upperbodyfemale.dds",),
        )
        self.assertEqual(
            attachments[1].skin_diffuse_paths,
            ("textures\\characters\\female\\handfemale.dds",),
        )

    def test_runtime_form_key_uses_the_observed_load_order_namespace(self) -> None:
        plugins = [{"file": "FalloutNV.esm"}, {"file": "DeadMoney.esm"}]

        key = _runtime_form_key("0x01001234", plugins)

        self.assertIsNotNone(key)
        self.assertEqual(key.text, "DeadMoney.esm:001234")
        self.assertIsNone(_runtime_form_key("0x02001234", plugins))

    def test_face_geometry_name_comes_from_every_retail_skin_sample(self) -> None:
        instance = {
            "geometryName": "FaceGenFace",
            "rootParentName": "BSFaceGenNiNodeSkinned",
            "status": "captured",
        }
        contract = {
            "retail": {
                "shots": [
                    {
                        "samples": [
                            {"frame": frame, "skinPalette": {"instances": [instance]}}
                            for frame in (70, 95)
                        ]
                    }
                ]
            }
        }

        self.assertEqual(_retail_face_geometry_name(contract), "FaceGenFace")


if __name__ == "__main__":
    unittest.main()
