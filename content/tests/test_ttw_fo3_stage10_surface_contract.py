from __future__ import annotations

import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from ttw_fo3_stage10_surface_contract import derive_surface_contract  # noqa: E402


class TtwFo3Stage10SurfaceContractTest(unittest.TestCase):
    def test_derives_depth_and_app_culled_state_from_raw_rows(self) -> None:
        raw_path = Path("raw.jsonl").resolve()
        presentation_path = Path("presentation.json").resolve()
        camera_rotation = [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0]
        presentation = {
            "schema": "opennv.fo3-ttw-oracle-cg00-stage10-presentation/v1",
            "campaign": "Fallout3",
            "edition": "TTW",
            "stage": 10,
            "evidence": {"rawPath": str(raw_path), "rawSha256": "a" * 64},
            "camera": {
                "frame": 7,
                "worldTransform": {
                    "rotationRowMajor": camera_rotation,
                    "translationGameUnits": [0.0, 0.0, 0.0],
                    "scale": 1.0,
                },
                "frustum": [-1.0, 1.0, 1.0, -1.0, 5.0, 100.0, 0.0],
            },
            "participants": {
                role: {
                    "referenceFormKey": f"Fallout3.esm:00000{index}",
                    "runtimeFormId": f"{index:08x}",
                    "visible": True,
                    "appCulled": False,
                }
                for index, role in enumerate(("father", "doctor", "mother"), 1)
            },
        }
        events = [{
            "schema": "nikami-retail-oracle/v4",
            "event": "review-camera-observation",
            "frame": 7,
            "projectionExact": True,
            "cameraWorld": {
                "rotation": camera_rotation,
                "translation": [0.0, 0.0, 0.0],
                "scale": 1.0,
            },
            "frustum": [-1.0, 1.0, 1.0, -1.0, 5.0, 100.0, 0.0],
        }]
        for ref_form in range(1, 4):
            events.extend([
                {
                    "schema": "nikami-retail-oracle/v4",
                    "event": "actor-frame",
                    "frame": 7,
                    "refForm": ref_form,
                    "bones": [{
                        "name": "Surface",
                        "parentName": "Root",
                        "depth": 1,
                        "runtimeFlags": 1 if ref_form == 3 else 0,
                    }],
                },
                {
                    "schema": "nikami-retail-oracle/v4",
                    "event": "actor-geometry-status",
                    "frame": 7,
                    "refForm": ref_form,
                    "geometryCandidates": 1,
                    "emittedShapes": 1,
                    "pointerReadFailures": 0,
                    "dataReadFailures": 0,
                    "invalidDataLayouts": 0,
                    "vertexReadFailures": 0,
                    "traversalFault": False,
                },
                {
                    "schema": "nikami-retail-oracle/v4",
                    "event": "actor-geometry",
                    "frame": 7,
                    "refForm": ref_form,
                    "name": "Surface",
                    "parentName": "Root",
                    "runtimeType": "NiTriShape",
                    "shaderPropertyType": "BSShaderPPLightingProperty",
                    "skinInstanceType": None,
                    "depth": 1,
                    "complete": True,
                    "vertexCount": 2,
                    "fnv1a32": ref_form,
                    "transform": {
                        "worldRotation": camera_rotation,
                        "worldTranslation": [2.0, 0.0, 0.0],
                        "worldScale": 1.0,
                    },
                    "vertices": [[1.0, 0.0, 0.0], [4.0, 0.0, 0.0]],
                },
            ])
        result = derive_surface_contract(
            presentation,
            events,
            presentation_path=presentation_path,
            presentation_sha256="b" * 64,
            raw_path=raw_path,
            raw_sha256="a" * 64,
        )
        father = result["participants"]["father"]["surfaces"][0]
        mother = result["participants"]["mother"]["surfaces"][0]
        self.assertEqual([3.0, 6.0], father["sortedDepthsGameUnits"])
        self.assertEqual(1, father["verticesAtOrBehindNearPlane"])
        self.assertFalse(father["appCulled"])
        self.assertTrue(mother["appCulled"])


if __name__ == "__main__":
    unittest.main()
