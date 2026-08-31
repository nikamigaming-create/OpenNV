from __future__ import annotations

import hashlib
import json
import math
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CONTRACTS = ROOT / "content" / "contracts"
CONTRACT = CONTRACTS / "fnv-racesex-preview-camera-v1.json"
SCHEMA = CONTRACTS / "fnv-racesex-preview-camera-v1.schema.json"


class FnvRaceSexPreviewCameraContractTest(unittest.TestCase):
    def setUp(self) -> None:
        self.contract = json.loads(CONTRACT.read_text(encoding="utf-8"))
        self.schema = json.loads(SCHEMA.read_text(encoding="utf-8"))

    def test_schema_and_contract_fail_closed_on_unproven_camera_values(self) -> None:
        self.assertEqual(
            "opennv-fnv-racesex-preview-camera/v1",
            self.contract["schema"],
        )
        self.assertEqual(
            self.schema["properties"]["schema"]["const"],
            self.contract["schema"],
        )
        self.assertEqual("blocked-static-evidence-incomplete", self.contract["status"])
        self.assertFalse(self.contract["parityReady"])
        self.assertFalse(self.contract["cameraContractReady"])
        for field in ("projection", "target", "distance", "frustum", "aspectBehavior"):
            value = self.contract["camera"][field]
            self.assertEqual("unresolved", value["status"])
            self.assertIsNone(value["value"])
            self.assertTrue(value["blocker"])
        serialized = json.dumps(self.contract["camera"], sort_keys=True).casefold()
        for unproven_key in (
            "fovdegrees",
            "orthographicsize",
            "nearplane",
            "farplane",
            "targetoffset",
            "cameradistance",
        ):
            self.assertNotIn(unproven_key, serialized)

    def test_owned_xml_contract_keeps_ui_geometry_separate_from_projection(self) -> None:
        source = self.contract["ownedUi"]
        self.assertEqual(
            "1c5e9daa5aa5eb9ae11044718874d0d27cb3665ec994487b2cc77a828805af98",
            source["memberSha256"],
        )
        self.assertEqual(64, len(source["memberSha256"]))
        int(source["memberSha256"], 16)
        face_grab = source["faceGrab"]
        self.assertEqual(
            (1, 150, 50, 680, 620, 100),
            tuple(face_grab[key] for key in ("id", "x", "y", "width", "height", "tileDepth")),
        )
        self.assertEqual({"numerator": 34, "denominator": 31}, face_grab["aspect"])
        self.assertTrue(
            math.isclose(
                face_grab["width"] / face_grab["height"],
                face_grab["aspect"]["numerator"] / face_grab["aspect"]["denominator"],
            )
        )
        traits = source["onMenuOpenTraits"]
        self.assertEqual(["user10", "user11", "user12"], traits["fullIn"])
        self.assertEqual(["user13", "user14", "user15"], traits["fullOut"])
        self.assertEqual("user16", traits["startingZoomPercent"])
        self.assertEqual("unresolved", traits["runtimeValuesStatus"])
        self.assertNotEqual("confirmed", self.contract["camera"]["aspectBehavior"]["status"])

    def test_public_contract_contains_no_private_address_or_executable_hash(self) -> None:
        public_text = CONTRACT.read_text(encoding="utf-8")
        self.assertNotIn("0x", public_text.casefold())
        self.assertNotIn(
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57",
            public_text.casefold(),
        )
        # Re-hashing the public artifact is deterministic and requires no owned data.
        self.assertEqual(
            hashlib.sha256(public_text.encode("utf-8")).hexdigest(),
            hashlib.sha256(CONTRACT.read_bytes()).hexdigest(),
        )


if __name__ == "__main__":
    unittest.main()
