from __future__ import annotations

import hashlib
import struct
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from facegen_controls import (  # noqa: E402
    FACEGEN_CTL_SIGNATURE,
    decode_facegen_control_space,
)
from bsa_archive import ExtractedMember  # noqa: E402
from opening_catalog import (  # noqa: E402
    FACEGEN_CONTROL_SPACE_STATUS,
    FACEGEN_SLIDER_EVIDENCE_CLASSIFICATION,
    FACEGEN_SLIDER_EVIDENCE_ENGINE_BUILD,
    FACEGEN_SLIDER_EVIDENCE_EXECUTABLE_SHA256_PARTS,
    FACEGEN_SLIDER_HIGH_GLOBAL_ADDRESS,
    FACEGEN_SLIDER_INCREMENT_DEFAULT_THRESHOLD,
    FACEGEN_SLIDER_INCREMENT_TRAIT,
    FACEGEN_SLIDER_JUMP,
    FACEGEN_SLIDER_LOW_GLOBAL_ADDRESS,
    FACEGEN_SLIDER_MORPH_WEIGHT_SCALE,
    FACEGEN_SLIDER_ORDINARY_INCREMENT,
    FACEGEN_SLIDER_SOURCE_MAXIMUM,
    FACEGEN_SLIDER_SOURCE_MINIMUM,
    FACEGEN_SLIDER_UI_MAXIMUM,
    FACEGEN_SLIDER_UI_MINIMUM,
    FACEGEN_SLIDER_UI_SCALE,
    _compile_facegen_control_space,
)


GEOMETRY_BASIS_VERSION = 101
TEXTURE_BASIS_VERSION = 202
SYMMETRIC_GEOMETRY_BASIS_COUNT = 2
ASYMMETRIC_GEOMETRY_BASIS_COUNT = 1
SYMMETRIC_TEXTURE_BASIS_COUNT = 2
ASYMMETRIC_TEXTURE_BASIS_COUNT = 0
OPAQUE_TAIL = b"demographic-tail"
SYNTHETIC_ARCHIVE_SHA256 = "a" * 64
SYNTHETIC_EXECUTABLE_SHA256 = "b" * 64


def _controls(rows: list[tuple[tuple[float, ...], str]]) -> bytes:
    payload = bytearray(struct.pack("<I", len(rows)))
    for axis, label in rows:
        payload.extend(struct.pack(f"<{len(axis)}f", *axis))
        encoded = label.encode("utf-8")
        payload.extend(struct.pack("<I", len(encoded)))
        payload.extend(encoded)
    return bytes(payload)


def synthetic_ctl() -> bytes:
    return b"".join(
        (
            struct.pack(
                "<8s6I",
                FACEGEN_CTL_SIGNATURE,
                GEOMETRY_BASIS_VERSION,
                TEXTURE_BASIS_VERSION,
                SYMMETRIC_GEOMETRY_BASIS_COUNT,
                ASYMMETRIC_GEOMETRY_BASIS_COUNT,
                SYMMETRIC_TEXTURE_BASIS_COUNT,
                ASYMMETRIC_TEXTURE_BASIS_COUNT,
            ),
            _controls([((1.0, 0.0), "Brow"), ((0.0, -1.0), "Nose")]),
            _controls([((0.5,), "Twist")]),
            _controls([((0.0, 1.0), "Tint")]),
            _controls([]),
            OPAQUE_TAIL,
        )
    )


def synthetic_policy(payload: bytes) -> dict[str, object]:
    return {
        "memberLogicalPath": "facegen\\si.ctl",
        "memberSha256": hashlib.sha256(payload).hexdigest(),
        "expectedFormat": {
            "formatSignature": FACEGEN_CTL_SIGNATURE.decode("ascii"),
            "geometryBasisVersion": GEOMETRY_BASIS_VERSION,
            "textureBasisVersion": TEXTURE_BASIS_VERSION,
            "basisCounts": {
                "symmetricGeometry": SYMMETRIC_GEOMETRY_BASIS_COUNT,
                "asymmetricGeometry": ASYMMETRIC_GEOMETRY_BASIS_COUNT,
                "symmetricTexture": SYMMETRIC_TEXTURE_BASIS_COUNT,
                "asymmetricTexture": ASYMMETRIC_TEXTURE_BASIS_COUNT,
            },
            "linearControlCounts": {
                "symmetricGeometry": 2,
                "asymmetricGeometry": 1,
                "symmetricTexture": 1,
                "asymmetricTexture": 0,
            },
        },
        "nativeGeometryExposure": {
            "classification": "synthetic-static-contract",
            "engineBuild": "synthetic-build",
            "sourceExecutableSha256": SYNTHETIC_EXECUTABLE_SHA256,
            "settingEntityTemplate": "sShape{oneBasedIndex:02d}",
            "controlIndices": [1],
        },
        "runtimePreviewControl": {
            "controlIndex": 1,
            "minimum": FACEGEN_SLIDER_UI_MINIMUM,
            "maximum": FACEGEN_SLIDER_UI_MAXIMUM,
            "step": FACEGEN_SLIDER_ORDINARY_INCREMENT,
            "jump": FACEGEN_SLIDER_JUMP,
            "morphWeightScale": FACEGEN_SLIDER_MORPH_WEIGHT_SCALE,
            "resetValue": 0.0,
            "acceptanceValue": FACEGEN_SLIDER_JUMP,
            "sliderSemanticsEvidence": {
                "classification": FACEGEN_SLIDER_EVIDENCE_CLASSIFICATION,
                "engineBuild": FACEGEN_SLIDER_EVIDENCE_ENGINE_BUILD,
                "sourceExecutableSha256Parts": list(
                    FACEGEN_SLIDER_EVIDENCE_EXECUTABLE_SHA256_PARTS
                ),
                "sourceMinimum": FACEGEN_SLIDER_SOURCE_MINIMUM,
                "sourceMaximum": FACEGEN_SLIDER_SOURCE_MAXIMUM,
                "uiScale": FACEGEN_SLIDER_UI_SCALE,
                "uiMinimum": FACEGEN_SLIDER_UI_MINIMUM,
                "uiMaximum": FACEGEN_SLIDER_UI_MAXIMUM,
                "ordinaryIncrement": FACEGEN_SLIDER_ORDINARY_INCREMENT,
                "jump": FACEGEN_SLIDER_JUMP,
                "morphWeightScale": FACEGEN_SLIDER_MORPH_WEIGHT_SCALE,
                "lowGlobalAddress": FACEGEN_SLIDER_LOW_GLOBAL_ADDRESS,
                "highGlobalAddress": FACEGEN_SLIDER_HIGH_GLOBAL_ADDRESS,
                "incrementTrait": FACEGEN_SLIDER_INCREMENT_TRAIT,
                "incrementDefaultThreshold": (
                    FACEGEN_SLIDER_INCREMENT_DEFAULT_THRESHOLD
                ),
            },
            "presentation": {
                "viewportWidthFraction": 0.5,
                "viewportHeightFraction": 0.35,
                "verticalFovHalfAngleFactor": 0.5,
                "depthExtentFraction": 0.5,
                "fullInVerticalOffsetGameUnits": -4.699999809265137,
                "fullInDistanceGameUnits": 55.0,
                "fullInYawRadians": -0.8399999737739563,
                "fullOutVerticalOffsetGameUnits": -11.399999618530273,
                "fullOutDistanceGameUnits": 135.0,
                "fullOutYawRadians": -0.800000011920929,
                "startingZoomFraction": 0.699999988079071,
            },
            "semantics": (
                "sibling-gamebryo-racesexmenu-ui-units-with-ctl-egm-weight-scale"
            ),
        },
    }


class SyntheticBsaArchive:
    payload = b""

    def __init__(self, _path: Path):
        pass

    def extract(self, logical_path: str) -> ExtractedMember:
        return ExtractedMember(
            logical_path,
            self.payload,
            False,
            7,
            len(self.payload),
        )


class FaceGenControlsTest(unittest.TestCase):
    def test_decodes_linear_axes_and_preserves_opaque_tail_identity(self):
        result = decode_facegen_control_space(synthetic_ctl())

        self.assertEqual(result.geometry_basis_version, GEOMETRY_BASIS_VERSION)
        self.assertEqual(result.texture_basis_version, TEXTURE_BASIS_VERSION)
        self.assertEqual(result.symmetric_geometry[1].label, "Nose")
        self.assertEqual(result.symmetric_geometry[1].axis, (0.0, -1.0))
        self.assertEqual(result.asymmetric_geometry[0].axis, (0.5,))
        self.assertEqual(result.asymmetric_texture, ())
        self.assertEqual(result.opaque_tail_bytes, len(OPAQUE_TAIL))
        self.assertEqual(
            result.opaque_tail_sha256,
            hashlib.sha256(OPAQUE_TAIL).hexdigest(),
        )

    def test_rejects_truncated_axis(self):
        payload = synthetic_ctl()
        with self.assertRaisesRegex(ValueError, "exceeds the owned payload"):
            decode_facegen_control_space(payload[:40])

    def test_rejects_non_finite_axis(self):
        payload = bytearray(synthetic_ctl())
        struct.pack_into("<f", payload, struct.calcsize("<8s7I"), float("nan"))
        with self.assertRaisesRegex(ValueError, "non-finite"):
            decode_facegen_control_space(bytes(payload))

    def test_compiler_binds_owned_member_and_native_exposed_subset(self):
        payload = synthetic_ctl()
        SyntheticBsaArchive.payload = payload
        with tempfile.TemporaryDirectory() as temporary:
            archive_path = Path(temporary) / "Synthetic Misc.bsa"
            archive_path.write_bytes(b"archive")
            with (
                patch("opening_catalog.BsaArchive", SyntheticBsaArchive),
                patch(
                    "opening_catalog.file_sha256",
                    return_value=SYNTHETIC_ARCHIVE_SHA256,
                ),
            ):
                result = _compile_facegen_control_space(
                    archive_path,
                    synthetic_policy(payload),
                )

        self.assertEqual(result["source"]["sha256"], hashlib.sha256(payload).hexdigest())
        self.assertEqual(
            result["nativeGeometryExposure"]["controls"],
            [
                {
                    "controlIndex": 1,
                    "settingEntity": "sShape02",
                    "sourceLabel": "Nose",
                    "axisSha256": hashlib.sha256(
                        struct.pack("<2f", 0.0, -1.0)
                    ).hexdigest(),
                }
            ],
        )
        self.assertEqual(
            result["nativeGeometryExposure"]["unexposedControlIndices"],
            [0],
        )
        self.assertEqual(result["runtimePreviewControl"]["settingEntity"], "sShape02")
        self.assertEqual(
            result["runtimePreviewControl"]["acceptanceValue"],
            FACEGEN_SLIDER_JUMP,
        )
        self.assertEqual(
            result["runtimePreviewControl"]["morphWeightScale"],
            FACEGEN_SLIDER_MORPH_WEIGHT_SCALE,
        )
        self.assertEqual(
            result["runtimePreviewControl"]["presentation"]["viewportHeightFraction"],
            0.35,
        )
        self.assertEqual(
            result["status"],
            FACEGEN_CONTROL_SPACE_STATUS,
        )
        self.assertEqual(
            result["runtimeDisposition"],
            "control-axes-and-default-preview-egm-targets-compiled-all-native-"
            "geometry-controls-runtime-bound-sibling-gamebryo-slider-semantics-"
            "corroborated",
        )


if __name__ == "__main__":
    unittest.main()
