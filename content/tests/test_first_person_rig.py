from __future__ import annotations

import hashlib
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import first_person_rig


class FirstPersonRigTest(unittest.TestCase):
    def test_two_owned_hands_share_pose_contract_and_keep_distinct_grip_bones(self) -> None:
        recipe = {
            "id": "test-cell",
            "firstPersonRig": {
                "skeletonPath": "characters\\_1stperson\\skeleton.nif",
                "poseAnimationPath": "characters\\_1stperson\\1hpaim.kf",
                "skeletonRootBone": "Bip01",
                "rigidAttachmentBone": "HeadAnims",
                "bipedHeadBone": "Bip01 Head",
                "cameraBone": "Camera1st",
                "weaponBone": "Weapon",
                "left": {
                    "modelPath": "characters\\_male\\lefthand1st.nif",
                    "gripBone": "Bip01 L Hand",
                },
                "right": {
                    "modelPath": "characters\\_male\\righthand1st.nif",
                    "gripBone": "Bip01 R Hand",
                },
            },
        }
        members = {
            "characters\\_1stperson\\skeleton.nif": SimpleNamespace(
                logical_path="meshes\\characters\\_1stperson\\skeleton.nif",
                data=b"skeleton",
                sha256="skeleton-sha",
            ),
            "characters\\_1stperson\\1hpaim.kf": SimpleNamespace(
                logical_path="meshes\\characters\\_1stperson\\1hpaim.kf",
                data=b"animation",
                sha256="animation-sha",
            ),
            "characters\\_male\\lefthand1st.nif": SimpleNamespace(
                logical_path="meshes\\characters\\_male\\lefthand1st.nif",
                data=b"left",
                sha256="left-sha",
            ),
            "characters\\_male\\righthand1st.nif": SimpleNamespace(
                logical_path="meshes\\characters\\_male\\righthand1st.nif",
                data=b"right",
                sha256="right-sha",
            ),
        }
        exported_inputs = []

        def export(actor_input, _textures, gltf_path, sidecar_path, _compiler):
            exported_inputs.append(actor_input)
            gltf_path.parent.mkdir(parents=True, exist_ok=True)
            gltf_path.write_bytes(actor_input.components[0].model_payload)
            sidecar_path.write_text("{}", encoding="utf-8")
            return {
                "outputs": {
                    "gltf": {
                        "sha256": hashlib.sha256(gltf_path.read_bytes()).hexdigest(),
                    },
                },
            }

        with tempfile.TemporaryDirectory() as temporary:
            with (
                patch.object(first_person_rig, "BsaArchive", side_effect=lambda path: path),
                patch.object(first_person_rig, "_member", side_effect=lambda _archive, path: members[path]),
                patch.object(first_person_rig, "export_actor_gltf", side_effect=export),
            ):
                result = first_person_rig.prepare_first_person_rig(
                    Path("meshes.bsa"),
                    [Path("textures.bsa")],
                    Path(temporary),
                    recipe,
                    SimpleNamespace(),
                )

        self.assertEqual(result["schema"], first_person_rig.FIRST_PERSON_RIG_SCHEMA)
        self.assertEqual(result["provider"], "retail-first-person-skinned-hands")
        self.assertEqual(result["cameraBone"], "Camera1st")
        self.assertEqual(result["weaponBone"], "Weapon")
        self.assertEqual(result["hands"]["left"]["gripBone"], "Bip01 L Hand")
        self.assertEqual(result["hands"]["right"]["gripBone"], "Bip01 R Hand")
        self.assertEqual(len(exported_inputs), 2)
        self.assertEqual(
            {actor_input.rigid_attachment_node for actor_input in exported_inputs},
            {"HeadAnims"},
        )
        self.assertEqual(
            {actor_input.components[0].role for actor_input in exported_inputs},
            {"left-hand", "right-hand"},
        )


if __name__ == "__main__":
    unittest.main()
