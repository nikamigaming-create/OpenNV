from __future__ import annotations

import math
import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from gallery_retail_evidence import (  # noqa: E402
    _directional_lighting_reference,
    _select_presentation_reference,
)
from runtime_configuration import load_runtime_configuration  # noqa: E402


REFERENCE_FORM_ID = "00000011"
BASE_FORM_ID = "00000010"
REFERENCE_FORM = int(REFERENCE_FORM_ID, 16)
BASE_FORM = int(BASE_FORM_ID, 16)
IDENTITY_ROTATION = [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0]
IDENTITY_MATRIX = [
    1.0,
    0.0,
    0.0,
    0.0,
    0.0,
    1.0,
    0.0,
    0.0,
    0.0,
    0.0,
    1.0,
    0.0,
    0.0,
    0.0,
    0.0,
    1.0,
]
FINAL_EYE_PROJECTION = [
    0.5625,
    0.0,
    0.0,
    0.0,
    0.0,
    1.0,
    0.0,
    0.0,
    0.0,
    0.0,
    1000.0 / 999.9,
    1.0,
    0.0,
    0.0,
    -100.0 / 999.9,
    0.0,
]


def _selection_policy() -> dict[str, object]:
    return {
        "schema": "opennv-gallery-presentation-selection/v1",
        "candidateShotKinds": [
            "front-full-body",
            "idle-motion",
            "rear-full-body",
            "front-detail",
            "rear-detail",
        ],
        "semanticFocusFacingRules": [
            {
                "focusKind": "head",
                "allowedShotKinds": [
                    "front-full-body",
                    "front-detail",
                    "idle-motion",
                ],
                "minimumCameraDirectionDotFocusForward": 0.99,
                "maximumCameraDirectionDotFocusForward": 1.0,
            },
            {
                "focusKind": "screen",
                "allowedShotKinds": ["rear-full-body", "rear-detail"],
                "minimumCameraDirectionDotFocusForward": -1.0,
                "maximumCameraDirectionDotFocusForward": -0.99,
            },
            {
                "focusKind": "root",
                "allowedShotKinds": [
                    "front-full-body",
                    "front-detail",
                    "idle-motion",
                ],
                "minimumCameraDirectionDotFocusForward": 0.99,
                "maximumCameraDirectionDotFocusForward": 1.0,
            },
        ],
        "requiredSurfaceStatus": "visible-final-eye-semantic-focus-draw",
        "requireSemanticFocusSurface": True,
        "requireCameraOutsideActorWorldBound": True,
        "requireClearCameraCorridor": True,
        "cameraTranslationToleranceGameUnits": 0.1,
        "tieBreak": "candidate-order-then-lowest-source-frame",
    }


def _candidate(
    shot_kind: str,
    frame: int,
    focus_kind: str,
    facing_dot: float,
    *,
    outside_bound: bool = True,
    corridor_passed: bool = True,
) -> tuple[list[dict[str, object]], dict[str, object], dict[str, object]]:
    focus_forward = [1.0, 0.0]
    camera_direction = [facing_dot, 0.0]
    camera_translation = [0.0, 0.0, 10.0]
    camera_contract = {
        "schema": "test-oracle/v1",
        "event": "portrait-camera-source-frame",
        "frame": frame,
        "shotKind": shot_kind,
        "focusKind": focus_kind,
        "focusRuleOrdinal": 0,
        "headForwardXY": focus_forward,
        "cameraDirectionXY": camera_direction,
        "camera": camera_translation,
        "cameraCorridor": {
            "outsideWorldBound": outside_bound,
            "passed": corridor_passed,
        },
    }
    camera = {
        "schema": "test-oracle/v1",
        "event": "review-camera-observation",
        "frame": frame,
        "shotKind": shot_kind,
        "readable": True,
        "projectionExact": True,
        "cameraWorld": {
            "rotation": IDENTITY_ROTATION,
            "translation": camera_translation,
            "scale": 1.0,
        },
        "fovYRadians": 1.0,
        "frustum": [-1.0, 1.0, 1.0, -1.0, 0.1, 1000.0, 0.0],
        "viewport": [0.0, 0.0, 1280.0, 720.0],
        "viewMatrix": IDENTITY_MATRIX,
        "projectionMatrix": IDENTITY_MATRIX,
    }
    snapshot = {
        "schema": "test-oracle/v1",
        "event": "actor-visual-snapshot",
        "frame": frame,
        "refForm": REFERENCE_FORM,
        "baseForm": BASE_FORM,
        "rootWorld": {
            "rotation": IDENTITY_ROTATION,
            "translation": [0.0, 0.0, 0.0],
            "scale": 1.0,
        },
    }
    pose = {
        "schema": "test-oracle/v1",
        "event": "actor-pose-sample",
        "frame": frame,
        "refForm": REFERENCE_FORM,
        "baseForm": BASE_FORM,
        "weaponOut": False,
        "weaponForm": 0,
        "animationDataSequences": [
            {
                "file": "meshes/characters/_male/idle.kf",
                "state": 1,
                "cycle": 1,
                "weight": 1.0,
                "frequency": 1.0,
                "lastScaled": 0.5,
                "group": 0,
            }
        ],
    }
    surface = {
        "sourceFrame": frame,
        "shotKind": shot_kind,
        "status": "visible-final-eye-semantic-focus-draw",
        "semanticFocusSurface": True,
        "renderFrame": frame - 1,
        "backBufferWidth": 1280,
        "backBufferHeight": 720,
        "projection": FINAL_EYE_PROJECTION,
        "verticalFovRadians": math.pi / 2.0,
    }
    source_frame = {
        "path": f"C:/evidence/frame-{frame:06d}.bmp",
        "bytes": 1,
        "sha256": "a" * 64,
    }
    return [camera_contract, camera, snapshot, pose], surface, source_frame


def _select(
    candidates: list[
        tuple[list[dict[str, object]], dict[str, object], dict[str, object]]
    ],
) -> dict[str, object]:
    events = [event for candidate in candidates for event in candidate[0]]
    report = {
        "runtime": {
            "surfaceContract": {
                "sourceFrames": [candidate[1] for candidate in candidates]
            }
        }
    }
    source_frames = [candidate[2] for candidate in candidates]
    return _select_presentation_reference(
        events,
        report,
        source_frames,
        _selection_policy(),
        REFERENCE_FORM_ID,
        BASE_FORM_ID,
    )


def _directional_report(
    presentation: dict[str, object],
    *,
    disagree: bool = False,
) -> dict[str, object]:
    grass = load_runtime_configuration().content_compiler.retail_grass
    values = [0.0] * (grass.shader.vertex_constant_register_count * 4)

    def write(register_name: str, source: list[float]) -> None:
        first = grass.shader.registers[register_name] * 4
        values[first : first + len(source)] = source

    write("diffuseDirection", [0.6, 0.0, 0.8])
    write("diffuseColor", [1.0, 0.9, 0.7])
    write("ambientColor", [0.3, 0.4, 0.5])
    write("directionalScale", [1.5])
    source_frame = int(presentation["frame"])

    def record(ordinal: int, constants: list[float]) -> dict[str, object]:
        return {
            "ordinal": ordinal,
            "sourceFrame": source_frame,
            "renderFrame": source_frame - grass.draw.render_frame_lead,
            "vertexShader": {"fnv1a32": grass.shader.vertex_fnv1a32},
            "pixelShader": {"fnv1a32": grass.shader.pixel_fnv1a32},
            "vertexConstants": {
                "registerCount": grass.shader.vertex_constant_register_count,
                "values": constants,
            },
        }

    records = [record(1, values)]
    if disagree:
        changed = list(values)
        changed[grass.shader.registers["diffuseDirection"] * 4] = 0.7
        records.append(record(2, changed))
    return {
        "capture": {
            "retailGrass": {
                "schema": grass.capture.schema,
                "event": grass.capture.event,
                "renderFrameLead": grass.draw.render_frame_lead,
                "candidateFrames": [
                    {
                        "shotKind": presentation["shotKind"],
                        "sourceFrame": source_frame,
                        "matchingRecordCount": len(records),
                        "records": records,
                    }
                ],
            }
        }
    }


class GalleryRetailPresentationSelectionTest(unittest.TestCase):
    def test_humanoid_head_selects_front_full_body(self) -> None:
        selected = _select(
            [
                _candidate("rear-full-body", 200, "head", -1.0),
                _candidate("front-full-body", 100, "head", 1.0),
            ]
        )
        self.assertEqual(selected["shotKind"], "front-full-body")
        self.assertEqual(selected["frame"], 100)
        self.assertAlmostEqual(
            selected["camera"]["fovYRadians"],
            math.pi / 2.0,
        )
        self.assertEqual(
            selected["camera"]["projectionMatrix"],
            FINAL_EYE_PROJECTION,
        )
        self.assertEqual(
            selected["camera"]["renderProjection"]["source"],
            "retail-report-runtime-surface-contract-source-frame",
        )

    def test_yes_man_screen_rejects_inside_front_and_selects_rear(self) -> None:
        selected = _select(
            [
                _candidate(
                    "front-full-body",
                    100,
                    "screen",
                    1.0,
                    outside_bound=False,
                ),
                _candidate("rear-full-body", 200, "screen", -1.0),
            ]
        )
        self.assertEqual(selected["shotKind"], "rear-full-body")
        self.assertEqual(selected["selection"]["focusKind"], "screen")
        self.assertTrue(selected["selection"]["cameraOutsideActorWorldBound"])

    def test_candidate_tie_break_uses_lowest_source_frame(self) -> None:
        selected = _select(
            [
                _candidate("front-full-body", 110, "head", 1.0),
                _candidate("front-full-body", 100, "head", 1.0),
            ]
        )
        self.assertEqual(selected["frame"], 100)

    def test_float32_unit_dot_roundoff_is_clamped(self) -> None:
        selected = _select(
            [_candidate("front-full-body", 100, "head", 1.00000008)]
        )
        self.assertEqual(
            selected["selection"]["cameraDirectionDotFocusForward"], 1.0
        )
        self.assertGreater(
            selected["selection"]["rawCameraDirectionDotFocusForward"], 1.0
        )

    def test_idle_motion_is_front_full_body_fallback(self) -> None:
        selected = _select(
            [
                _candidate(
                    "front-full-body",
                    100,
                    "head",
                    1.0,
                    corridor_passed=False,
                ),
                _candidate("idle-motion", 200, "head", 1.0),
            ]
        )
        self.assertEqual(selected["shotKind"], "idle-motion")
        self.assertEqual(selected["frame"], 200)

    def test_blocked_camera_corridor_rejects_capture(self) -> None:
        with self.assertRaisesRegex(ValueError, "no presentation frame"):
            _select(
                [
                    _candidate(
                        "front-full-body",
                        100,
                        "head",
                        1.0,
                        corridor_passed=False,
                    )
                ]
            )

    def test_exterior_directional_light_is_bound_to_presentation_frame(self) -> None:
        presentation = _select([_candidate("front-full-body", 100, "head", 1.0)])
        reference = _directional_lighting_reference(
            _directional_report(presentation),
            presentation,
            "exterior",
            load_runtime_configuration(),
        )
        self.assertIsNotNone(reference)
        assert reference is not None
        self.assertEqual(reference["sourceFrame"], 100)
        self.assertEqual(reference["renderFrame"], 99)
        self.assertEqual(reference["diffuseDirectionGamebryo"], [0.6, 0.0, 0.8])
        self.assertEqual(reference["recordCount"], 1)

    def test_exterior_directional_light_rejects_disagreeing_draws(self) -> None:
        presentation = _select([_candidate("front-full-body", 100, "head", 1.0)])
        with self.assertRaisesRegex(ValueError, "changed within"):
            _directional_lighting_reference(
                _directional_report(presentation, disagree=True),
                presentation,
                "exterior",
                load_runtime_configuration(),
            )

    def test_interior_directional_light_is_not_invented(self) -> None:
        presentation = _select([_candidate("front-full-body", 100, "head", 1.0)])
        self.assertIsNone(
            _directional_lighting_reference(
                {},
                presentation,
                "interior",
                load_runtime_configuration(),
            )
        )


if __name__ == "__main__":
    unittest.main()
