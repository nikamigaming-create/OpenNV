import hashlib
import math
import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_review_contract import (  # noqa: E402
    _appearance_contract,
    _d3d_perspective_frustum,
    _environment_contract,
    _fnv1a32,
    _replace_d3d_projection_xy,
    _validated_skin_palette,
)
from prepare_creature_review import _retail_equipped_weapon_attachment  # noqa: E402


class ActorReviewContractTest(unittest.TestCase):
    @staticmethod
    def _environment_evidence(
        directory: Path,
    ) -> tuple[list[dict[str, object]], dict[str, dict[str, object]]]:
        slot = {
            "previousForm": 0,
            "hidden": True,
            "age": 0.0,
            "flags": 1,
            "lastStrength": 0.0,
            "transitionTime": 0.0,
        }
        registers = [[0.0, 0.0, 0.0, 0.0] for _ in range(24)]
        registers[1] = [1.4, 0.0, 0.0, 0.0]
        registers[19] = [1.1, 0.2, 1.1, 1.3]
        registers[20] = [0.992831886, 0.660198152, 0.027684167, 0.392156869]
        registers[22] = [0.0, 0.0, 0.0, 0.0]
        inputs = []
        artifacts: dict[str, dict[str, object]] = {}
        for ordinal, (stage, width, height) in enumerate(((0, 1, 1), (1, 2, 1))):
            payload = bytes(width * height * 8)
            path = (directory / f"stage-{stage}.bin").resolve()
            path.write_bytes(payload)
            fnv1a32 = _fnv1a32(payload)
            sha256 = hashlib.sha256(payload).hexdigest()
            inputs.append(
                {
                    "ordinal": ordinal,
                    "stage": stage,
                    "getTextureResult": 0,
                    "resourceType": 3,
                    "levelCount": 1,
                    "levelDescriptionResult": 0,
                    "description": {
                        "format": 113,
                        "type": 1,
                        "usage": 1,
                        "pool": 0,
                        "multiSampleType": 0,
                        "multiSampleQuality": 0,
                        "width": width,
                        "height": height,
                    },
                    "getSurfaceResult": 0,
                    "createSystemSurfaceResult": 0,
                    "directTransferResult": 0,
                    "resolvedTransferResult": -1,
                    "copyResult": 0,
                    "lockResult": 0,
                    "allocationResult": 0,
                    "unlockResult": 0,
                    "srgbTexture": {"getResult": 0, "enabled": 0},
                    "rowBytes": width * 8,
                    "rowCount": height,
                    "canonicalBytes": len(payload),
                    "fnv1a32": fnv1a32,
                    "layoutResolved": True,
                    "withinConfiguredBound": True,
                    "captured": True,
                    "artifact": {
                        "written": True,
                        "path": str(path),
                        "bytes": len(payload),
                        "fnv1a32": fnv1a32,
                    },
                }
            )
            artifacts[str(path).casefold()] = {
                "kind": "retail-image-space-shader-input",
                "path": str(path),
                "bytes": len(payload),
                "sha256": sha256,
            }
        events = [
            {
                "event": "render-environment",
                "frame": 46,
                "currentWeatherForm": 0x001237D7,
                "defaultWeatherForm": 0x00158303,
                "gameHour": 12.1527586,
                "weatherPercent": 1.0,
                "skyMode": 1,
                "baseImageSpace": {"form": 0x0008809D, "traits": [1.0] * 33},
                "weatherImageSpace": {
                    "currentFadeIn": {**slot, "form": 0x000CEE18, "percent": 0.0254597664},
                    "currentFadeOut": {**slot, "form": 0x000CEE18, "percent": 0.974540234},
                    "transitionFadeIn": {**slot, "form": 0, "percent": 0.0},
                    "transitionFadeOut": {**slot, "form": 0, "percent": 0.0},
                },
                "sunAmbient": [0.0, 0.0, 0.0, 1.0],
                "sunDirectional": [1.0, 1.0, 1.0, 1.0],
                "sunFog": [1.0, 1.0, 1.0, 1.0],
            },
            {
                "event": "image-space-shader-constants",
                "frame": 1,
                "byteCount": 748,
                "fnv1a32": 0x0A008802,
                "path": "hdr-cinematic",
                "getConstantsResult": 0,
                "inputCaptureEnabled": True,
                "expectedShaderByteCount": 748,
                "expectedShaderFnv1a32": 0x0A008802,
                "sourceFrame": 2,
                "renderFrame": 1,
                "renderFrameLead": 1,
                "srgbWrite": {"getResult": 0, "enabled": 0},
                "registers": registers,
                "inputTextures": inputs,
            },
        ]
        return events, artifacts

    def test_environment_contract_retains_weather_slots_and_shader_registers(self):
        with tempfile.TemporaryDirectory() as temporary:
            events, artifacts = self._environment_evidence(Path(temporary))
            result = _environment_contract(events, artifacts)

            self.assertEqual(
                result["weatherImageSpace"]["currentFadeIn"]["form"],
                0x000CEE18,
            )
            self.assertAlmostEqual(
                result["weatherImageSpace"]["currentFadeOut"]["percent"],
                0.974540234,
            )
            self.assertEqual(result["imageSpaceShader"]["fnv1a32"], 0x0A008802)
            self.assertEqual(result["imageSpaceShader"]["registers"][19][3], 1.3)
            self.assertEqual(
                [row["stage"] for row in result["imageSpaceShader"]["inputTextures"]],
                [0, 1],
            )
            self.assertEqual(
                result["imageSpaceShader"]["inputTextures"][1]["description"]["width"],
                2,
            )

    def test_environment_contract_rejects_missing_weather_slot(self):
        with tempfile.TemporaryDirectory() as temporary:
            events, artifacts = self._environment_evidence(Path(temporary))
            del events[0]["weatherImageSpace"]["transitionFadeOut"]

            with self.assertRaisesRegex(ValueError, "four-slot"):
                _environment_contract(events, artifacts)

    def test_environment_contract_rejects_changed_shader_input(self):
        with tempfile.TemporaryDirectory() as temporary:
            events, artifacts = self._environment_evidence(Path(temporary))
            Path(events[1]["inputTextures"][0]["artifact"]["path"]).write_bytes(b"changed!")

            with self.assertRaisesRegex(ValueError, "content changed"):
                _environment_contract(events, artifacts)

    @staticmethod
    def _appearance_events(role: str = "weapon", schema: str = "nikami-fnv-sidecar-appearance/v4"):
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
                "nodes": [
                    {
                        "name": "KnifeSpear:0",
                        "nodePath": "root/weapon/0",
                    }
                ],
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
                            "skinned": False,
                            "geometryName": "KnifeSpear:0",
                            "visualNodePath": "root/weapon/0",
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

    def test_skin_contract_reclassifies_previous_render_frame_cache(self):
        def instance(name: str, frame_id: int) -> dict[str, object]:
            matrices = [1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0]
            return {
                "nodePath": f"root/{name}",
                "geometryName": name,
                "skinInstanceType": "BSDismemberSkinInstance",
                "rootParentName": "Scene Root",
                "frameId": frame_id,
                "status": "captured",
                "matrixCount": 1,
                "registersPerMatrix": 3,
                "componentsPerRegister": 4,
                "allocatedBytes": 48,
                "matrixBytes": 48,
                "matricesReadable": True,
                "matricesFinite": True,
                "bonesReadable": True,
                "matrices": matrices,
                "fnv1a32": _fnv1a32(struct.pack("<12f", *matrices)),
                "bones": [{"index": 0, "name": "Bip01 Pelvis"}],
            }

        event = {
            "skinPaletteCapture": {
                "visitedNodes": 2,
                "geometryCandidates": 2,
                "skinInstances": 2,
                "capturedPalettes": 2,
                "notRenderCached": 0,
                "invalidPalettes": 0,
                "traversalTruncated": False,
            },
            "skinPalettes": [instance("Current", 11), instance("Stale", 10)],
        }

        result = _validated_skin_palette(event, 70)

        self.assertEqual(result["summary"]["capturedPalettes"], 1)
        self.assertEqual(result["summary"]["notRenderCached"], 1)
        self.assertEqual(result["summary"]["currentRenderFrameId"], 11)
        self.assertEqual(result["summary"]["staleRenderCachesReclassified"], 1)
        self.assertEqual(result["instances"][1]["status"], "not-render-cached")
        self.assertEqual(
            result["instances"][1]["cacheClassification"],
            "stale-not-bound-to-current-render-frame",
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
