from __future__ import annotations

import struct
import sys
import unittest
import hashlib
from pathlib import Path
from types import SimpleNamespace


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from plugin_records import Record  # noqa: E402
from bsa_archive import BsaArchive  # noqa: E402
from nif_trigger_phantom import decode_trigger_phantom_nif  # noqa: E402
from ttw_fo3_stage10_resource_closure import (  # noqa: E402
    LIVE_ONLY_FIELDS,
    _deduplicated_members,
    _identity_only_decoder_models,
    _nif_material_paths,
    _race_tables,
)


FORM_ID_BYTES = 4
OWNED_TTW_ROOT = Path(r"D:\TTW\Installed")
TRIGGER_ARCHIVE = "Fallout - Meshes.bsa"
TRIGGER_MEMBER = r"meshes\triggers\trigplayerwall01.nif"
TRIGGER_SHA256 = "c167f5899f80f94f21d80b9e62b002162e317f5172a6ea0c532109d6ffe598ef"


def subrecord(signature: str, payload: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(payload)) + payload


def text(value: str) -> bytes:
    return value.encode("ascii") + b"\0"


def member(logical_path: str, digest: str) -> dict[str, object]:
    return {
        "logicalPath": logical_path,
        "bytes": len(logical_path),
        "sha256": digest,
        "winner": {"kind": "bsa"},
        "overriddenVersions": [],
    }


class TtwFo3Stage10ResourceClosureTest(unittest.TestCase):
    @unittest.skipUnless(
        (OWNED_TTW_ROOT / TRIGGER_ARCHIVE).is_file(),
        "owned TTW Fallout - Meshes.bsa is unavailable",
    )
    def test_owned_player_wall_is_editor_only_phantom_trigger(self) -> None:
        payload = BsaArchive(OWNED_TTW_ROOT / TRIGGER_ARCHIVE).extract(
            TRIGGER_MEMBER
        ).data
        self.assertEqual(hashlib.sha256(payload).hexdigest(), TRIGGER_SHA256)

        decoded = decode_trigger_phantom_nif(payload)

        self.assertEqual(decoded.source_sha256, TRIGGER_SHA256)
        self.assertEqual(
            decoded.presentation,
            {
                "disposition": "exclude-editor-marker-only-surface",
                "rootBlock": 0,
                "rootName": "TrigPlayerWall01",
                "presentableSurfaceCount": 0,
                "editorMarkerSurfaceCount": 1,
                "editorMarkerNode": {"block": 7, "name": "EditorMarker"},
                "editorMarkerGeometry": {
                    "block": 9,
                    "name": "EditorMarker:0",
                    "type": "NiTriStrips",
                    "dataBlock": 13,
                },
            },
        )
        collision = decoded.collision
        self.assertEqual(collision["semantics"], "retain-non-blocking-overlap-trigger")
        self.assertEqual(
            collision["filter"],
            {"layer": 12, "layerName": "FOL_TRIGGER", "flags": 0, "group": 0},
        )
        self.assertEqual(
            collision["broadPhase"],
            {"type": 2, "typeName": "BROAD_PHASE_PHANTOM"},
        )
        self.assertEqual(
            collision["shape"]["halfExtents"],
            [29.661808013916016, 1.6406385898590088, 20.087324142456055],
        )
        self.assertEqual(
            collision["shape"]["affineMatrixColumnMajor"],
            [1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 6.233996868133545],
        )
        self.assertTrue(decoded.evidence()["runtimeMaterializationAdmission"])

    def test_race_table_selects_the_requested_sex_without_crossing_groups(self) -> None:
        payload = b"".join(
            (
                subrecord("NAM0", b""),
                subrecord("MNAM", b""),
                subrecord("INDX", struct.pack("<I", 0)),
                subrecord("MODL", text("Characters\\Head\\HeadMale.nif")),
                subrecord("ICON", text("Characters\\Male\\Head.dds")),
                subrecord("FNAM", b""),
                subrecord("INDX", struct.pack("<I", 0)),
                subrecord("MODL", text("Characters\\Head\\HeadFemale.nif")),
                subrecord("ICON", text("Characters\\Female\\Head.dds")),
                subrecord("NAM1", b""),
                subrecord("MNAM", b""),
                subrecord("INDX", struct.pack("<I", 0)),
                subrecord("MODL", text("Characters\\_Male\\UpperBody.nif")),
                subrecord("FNAM", b""),
                subrecord("INDX", struct.pack("<I", 0)),
                subrecord("MODL", text("Characters\\_Male\\FemaleUpperBody.nif")),
            )
        )
        version = SimpleNamespace(record=Record("RACE", 1, 0, payload, ()))

        male = _race_tables(version, False)
        female = _race_tables(version, True)

        self.assertEqual(male["headModels"], ["characters\\head\\headmale.nif"])
        self.assertEqual(
            female["headModels"],
            ["characters\\head\\headfemale.nif"],
        )
        self.assertEqual(
            male["bodyModels"],
            ["characters\\_male\\upperbody.nif"],
        )
        self.assertEqual(
            female["bodyModels"],
            ["characters\\_male\\femaleupperbody.nif"],
        )

    def test_nif_material_paths_include_shader_set_and_legacy_descriptors(self) -> None:
        shader = SimpleNamespace(
            texture_set=SimpleNamespace(
                textures=[
                    b"textures\\architecture\\vault\\wall.dds",
                    b"textures\\architecture\\vault\\wall_n.dds",
                ]
            )
        )
        descriptor = SimpleNamespace(
            texture_set=None,
            base_texture=SimpleNamespace(
                source=SimpleNamespace(
                    file_name=b"textures\\architecture\\vault\\trim.dds"
                )
            ),
            normal_texture=None,
            bump_map=None,
            glow_texture=None,
        )
        document = SimpleNamespace(
            blocks=[
                SimpleNamespace(properties=[shader]),
                SimpleNamespace(properties=[descriptor]),
            ]
        )

        self.assertEqual(
            _nif_material_paths(document),
            (
                "textures\\architecture\\vault\\trim.dds",
                "textures\\architecture\\vault\\wall.dds",
                "textures\\architecture\\vault\\wall_n.dds",
            ),
        )

    def test_member_deduplication_rejects_conflicting_effective_winners(self) -> None:
        digest_a = "a" * 64
        digest_b = "b" * 64
        first = member("meshes\\vault.nif", digest_a)
        second = member("MESHES\\VAULT.NIF", digest_b)

        with self.assertRaisesRegex(ValueError, "member identity conflicts"):
            _deduplicated_members({"first": first, "second": second})

    def test_identity_only_decoder_models_are_unique_and_live_fields_are_explicit(self) -> None:
        source_member = member("meshes\\triggers\\wall.nif", "c" * 64)
        model = {
            "kind": "owned-nif-resource-graph",
            "member": source_member,
            "runtimeDecoderContractAdmitted": False,
            "decoder": {"status": "owned-nif-identity-only-pyffi-introspection"},
        }

        rows = _identity_only_decoder_models([model, model])

        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["member"], source_member)
        self.assertEqual(
            LIVE_ONLY_FIELDS,
            (
                "player-reference-runtime-identity",
                "player-camera-world-transform",
                "player-camera-projection-frustum-and-fov",
                "player-camera-controller-phase",
                "father-rendered-root-transform-visibility-and-controller-phase",
                "doctor-rendered-root-transform-visibility-and-controller-phase",
                "mother-rendered-root-transform-visibility-and-controller-phase",
            ),
        )


if __name__ == "__main__":
    unittest.main()
