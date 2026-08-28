from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from compose_fo1_vault13_concept import compose  # noqa: E402


def write_json(path: Path, document: object) -> None:
    path.write_text(json.dumps(document, sort_keys=True) + "\n", encoding="utf-8")


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_bounded_gltf(path: Path, minimum: list[float], maximum: list[float]) -> None:
    write_json(
        path,
        {
            "meshes": [{"primitives": [{"attributes": {"POSITION": 0}}]}],
            "accessors": [{"min": minimum, "max": maximum}],
        },
    )


class Fo1ConceptCompositionTest(unittest.TestCase):
    def test_bounded_composition_resolves_the_nearest_portal_mount(self) -> None:
        with tempfile.TemporaryDirectory() as raw_directory:
            root = Path(raw_directory)
            model = root / "door.gltf"
            write_bounded_gltf(model, [-1.0, 0.0, -0.1], [1.0, 4.0, 0.1])
            mount_model = root / "mount.gltf"
            write_bounded_gltf(mount_model, [-5.0, 0.0, -10.0], [5.0, 8.0, 10.0])
            compiler = {"name": "test exporter", "sha256": "ab" * 32}
            sidecar = root / "door.opennv.json"
            write_json(
                sidecar,
                {
                    "compiler": compiler,
                    "source": {"sha256": "cd" * 32},
                    "coverage": {"surfaces": 1},
                },
            )
            materials = root / "door.materials.json"
            write_json(
                materials,
                {
                    "schema": "opennv-static-material-manifest/v1",
                    "textures": [],
                    "asset": {"materials": [{"name": "Door"}]},
                },
            )
            proof = root / "door-proof.json"
            write_json(
                proof,
                {
                    "schema": "opennv-fo1-door-presentation-proof/v1",
                    "recipe": {"id": "door-map"},
                    "sourceObjectContract": {"door": {"serial": 129}},
                    "target": {
                        "baseFormId": "00000001",
                        "editorId": "Door",
                        "logicalPath": "meshes\\door.nif",
                        "sourceNifSha256": "cd" * 32,
                    },
                    "outputs": {
                        "model": str(model),
                        "sidecar": str(sidecar),
                        "materialManifest": str(materials),
                        "modelSha256": sha256(model),
                        "materialManifestSha256": sha256(materials),
                    },
                },
            )
            donor = root / "donor.json"
            door_reference = {
                "formId": "00000010",
                "baseFormId": "00000011",
                "baseEditorId": "InvisibleDoor",
                "assetId": "old-door",
                "positionGodotUnits": [10.0, 0.0, 35.0],
                "positionGameUnits": [10.0, 20.0, 30.0],
                "rotationGodotQuaternion": [0.0, 0.0, 0.0, 1.0],
                "yawGodotRadians": 0.0,
                "yawRadians": 0.0,
            }
            mount_reference = {
                **door_reference,
                "formId": "00000012",
                "baseFormId": "00000013",
                "baseEditorId": "CaveMount",
                "assetId": "mount-asset",
                "positionGodotUnits": [10.0, 2.0, 20.0],
            }
            write_json(
                donor,
                {
                    "schema": "opennv-cell-scene/v11",
                    "recipe": "donor",
                    "compiler": compiler,
                    "cell": {"formId": "00000020", "editorId": "DonorCell"},
                    "coordinates": {
                        "unitsToMeters": 0.01,
                        "originGameUnits": [100.0, 200.0, 300.0],
                    },
                    "lighting": {"lights": []},
                    "assets": [
                        {
                            "id": "mount-asset",
                            "logicalPath": "meshes\\mount.nif",
                            "model": str(mount_model),
                            "materials": [],
                        }
                    ],
                    "textures": [],
                    "references": [
                        door_reference,
                        mount_reference,
                        {**door_reference, "formId": "near", "positionGodotUnits": [12.0, 0.0, 35.0]},
                        {**door_reference, "formId": "far", "positionGodotUnits": [50.0, 0.0, 35.0]},
                    ],
                    "coverage": {"authoredLights": 0},
                },
            )
            recipe = root / "recipe.json"
            write_json(
                recipe,
                {
                    "schema": "opennv-fo1-concept-composition/v1",
                    "id": "concept",
                    "donor": {
                        "sceneSchema": "opennv-cell-scene/v11",
                        "recipe": "donor",
                        "cellFormId": "00000020",
                        "cellEditorId": "DonorCell",
                        "replaceReferenceFormId": "00000010",
                        "replaceReferenceBaseFormId": "00000011",
                        "replaceReferenceBaseEditorId": "InvisibleDoor",
                        "mountReferenceFormId": "00000012",
                        "mountReferenceBaseFormId": "00000013",
                        "mountReferenceBaseEditorId": "CaveMount",
                        "mountLogicalPath": "meshes\\mount.nif",
                    },
                    "doorProof": {
                        "schema": "opennv-fo1-door-presentation-proof/v1",
                        "recipeId": "door-map",
                        "sourceDoorSerial": 129,
                        "targetBaseFormId": "00000001",
                        "targetEditorId": "Door",
                    },
                    "selection": {"radiusGodotUnits": 20.0},
                    "placement": {
                        "claim": "concept",
                    },
                    "camera": {
                        "homeSizeMeters": 12.0,
                        "outwardYawOffsetDegrees": -25.0,
                        "pitchDegrees": -48.0,
                    },
                    "lighting": {
                        "doorAccent": {
                            "positionOffsetMountLocalUnits": [0.0, 1.0, 2.0],
                            "radiusMeters": 4.0,
                            "color": [1.0, 0.5, 0.25],
                            "intensity": 2.0,
                        }
                    },
                    "hud": {"objective": "OBJECTIVE  Inspect"},
                    "unsupported": ["parity"],
                },
            )

            output = root / "output"
            manifest = compose(recipe, donor, proof, output)
            scene = json.loads((output / "cell-scene.json").read_text(encoding="utf-8"))
            mapped = next(row for row in scene["references"] if row["formId"] == "00000010")
            self.assertEqual(len(scene["references"]), 3)
            self.assertEqual(mapped["positionGodotUnits"], [10.0, 2.0, 30.0])
            self.assertEqual(mapped["positionGameUnits"], [110.0, 170.0, 302.0])
            self.assertEqual(mapped["presentationMapping"]["originalPositionGodotUnits"], [10.0, 0.0, 35.0])
            self.assertEqual(mapped["presentationMapping"]["selectedMouth"]["name"], "maximum-z")
            self.assertEqual(
                mapped["presentationMapping"]["resolvedDoorCenterGodotUnits"],
                [10.0, 4.0, 30.0],
            )
            self.assertEqual(scene["concept"]["hudObjective"], "OBJECTIVE  Inspect")
            self.assertEqual(scene["concept"]["camera"]["focusGodotUnits"], [10.0, 4.0, 30.0])
            self.assertEqual(scene["lighting"]["lights"][0]["positionGodotUnits"], [10.0, 5.0, 32.0])
            self.assertEqual(scene["coverage"]["authoredLights"], 1)
            self.assertEqual(manifest["output"]["references"], 3)

            with self.assertRaises(ValueError):
                compose(recipe, donor, proof, output)


if __name__ == "__main__":
    unittest.main()
