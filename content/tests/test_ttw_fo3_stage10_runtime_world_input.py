from __future__ import annotations

import copy
import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from plugin_stack import file_sha256  # noqa: E402
from ttw_fo3_stage10_resource_closure import (  # noqa: E402
    LIVE_ONLY_FIELDS,
    SCHEMA as RESOURCE_CLOSURE_SCHEMA,
    STATUS as RESOURCE_CLOSURE_STATUS,
    _canonical_sha256,
)
from ttw_fo3_stage10_runtime_world_input import (  # noqa: E402
    LIVE_OBSERVATION_BLOCKER,
    RUNTIME_ARTIFACT_BLOCKER,
    SCHEMA,
    STATUS,
    project_ttw_fo3_stage10_runtime_world_input,
)
from ttw_fo3_stage10_world_materialization import (  # noqa: E402
    EXPECTED_BASE_EDITOR_IDS,
    EXPECTED_CELL_FORM_KEY,
    EXPECTED_SEQUENCE_NAMES,
    PROJECTION_SCHEMA,
    PROJECTION_STATUS,
    ROLE_ORDER,
)


DIGEST = "a" * 64
PLUGIN_STACK_ID = "b" * 64
SAVE_COMPATIBILITY_ID = f"ttw:{PLUGIN_STACK_ID}"
FORM_IDS = {
    "player": "00000007",
    "father": "060290a6",
    "doctor": "060290a3",
    "mother": "0605eddf",
}
FORM_KEYS = {
    "player": "FalloutNV.esm:000007",
    "father": "Fallout3.esm:0290a6",
    "doctor": "Fallout3.esm:0290a3",
    "mother": "Fallout3.esm:05eddf",
}
REFERENCE_KEYS = {
    "father": "Fallout3.esm:0290a7",
    "doctor": "Fallout3.esm:0290a5",
    "mother": "Fallout3.esm:05ede0",
}


def record(
    form_key: str,
    runtime_form_id: str,
    record_type: str,
    editor_id: str | None,
) -> dict[str, object]:
    return {
        "formKey": form_key,
        "runtimeFormId": runtime_form_id,
        "winner": {
            "plugin": "TaleOfTwoWastelands.esm",
            "loadOrderIndex": 1,
            "sourceRootIndex": 1,
            "pluginSha256": DIGEST,
            "recordSha256": DIGEST,
            "flags": 0,
        },
        "overriddenVersions": [],
        "recordType": record_type,
        "editorId": editor_id,
        "stableLocalFormId": runtime_form_id[-8:],
    }


def member(path: str) -> dict[str, object]:
    return {
        "logicalPath": path,
        "bytes": len(path),
        "sha256": DIGEST,
        "winner": {
            "kind": "bsa",
            "archive": "Synthetic.bsa",
            "archiveOrderIndex": 0,
            "sourceRootIndex": 1,
            "archiveSha256": DIGEST,
            "memberBytes": len(path),
            "memberSha256": DIGEST,
        },
        "overriddenVersions": [],
    }


def actor(role: str) -> dict[str, object]:
    editor = "Player" if role == "player" else EXPECTED_BASE_EDITOR_IDS[role]
    base = record(FORM_KEYS[role], FORM_IDS[role], "NPC_", editor)
    return {
        "role": role,
        "base": base,
        "female": role in {"doctor", "mother"},
        "race": record("FalloutNV.esm:000019", "00000019", "RACE", "Caucasian"),
        "hair": {
            "record": record("FalloutNV.esm:000111", "00000111", "HAIR", "Hair")
        },
        "eyes": {
            "record": record("FalloutNV.esm:000222", "00000222", "EYES", "Eyes")
        },
        "headParts": [],
        "skeleton": {"member": member("meshes\\characters\\_male\\skeleton.nif")},
        "raceModels": [],
        "outfit": [],
        "faceGen": {"modelCompanions": []},
    }


def model(path: str, collision: dict[str, object]) -> dict[str, object]:
    return {
        "member": member(path),
        "kind": "owned-nif-resource-graph",
        "materials": [],
        "collision": collision,
        "decoder": {"status": "synthetic"},
        "runtimeDecoderContractAdmitted": True,
    }


def fixture(
    root: Path,
) -> tuple[
    dict[str, object],
    dict[str, object],
    list[dict[str, object]],
    Path,
    Path,
]:
    static_base = record("Fallout3.esm:000100", "06000100", "STAT", "VaultWall")
    phantom_base = record("Fallout3.esm:000101", "06000101", "ACTI", "Trigger")
    static_ref = record("Fallout3.esm:000200", "06000200", "REFR", None)
    phantom_ref = record("Fallout3.esm:000201", "06000201", "REFR", None)
    inline_ref = record("Fallout3.esm:000202", "06000202", "REFR", None)
    actor_refs = {
        role: record(key, "06" + key[-6:], "ACHR", None)
        for role, key in REFERENCE_KEYS.items()
    }
    static_model = model(
        "meshes\\architecture\\vault\\wall.nif",
        {
            "source": "embedded-in-model-member",
            "blockTypes": ["bhkRigidBody"],
            "blockCount": 1,
        },
    )
    phantom_model = model(
        "meshes\\triggers\\trigplayerwall01.nif",
        {
            "semantics": "retain-non-blocking-overlap-trigger",
            "filter": {"layer": 12, "layerName": "FOL_TRIGGER", "flags": 0, "group": 0},
            "broadPhase": {"type": 2, "typeName": "BROAD_PHASE_PHANTOM"},
            "shape": {"type": "bhkBoxShape", "halfExtents": [1.0, 2.0, 3.0]},
        },
    )
    phantom_model["presentation"] = {
        "disposition": "exclude-editor-marker-only-surface"
    }
    actors = {role: actor(role) for role in ROLE_ORDER}
    actor_sections: dict[str, list[dict[str, object]]] = {}
    section_identities = []
    for index, role in enumerate(ROLE_ORDER, start=1):
        package = record(
            f"Fallout3.esm:10{index:04x}",
            f"0610{index:04x}",
            "PACK",
            f"{role}Package",
        )
        idle = record(
            f"Fallout3.esm:11{index:04x}",
            f"0611{index:04x}",
            "IDLE",
            f"{role}Idle",
        )
        animation = member(f"meshes\\characters\\_male\\idleanims\\{role}.kf")
        section_identities.extend((package, idle))
        actor_sections[role] = [
            {
                "section": 1,
                "packageFormId": package["stableLocalFormId"],
                "idleFormId": idle["stableLocalFormId"],
                "animationLogicalPath": animation["logicalPath"],
                "packageSourceIdentity": package,
                "idleSourceIdentity": idle,
                "animationMemberIdentity": animation,
            }
        ]
    projection = {
        "schema": PROJECTION_SCHEMA,
        "status": PROJECTION_STATUS,
        "campaign": "Fallout3",
        "edition": "TTW",
        "ownedPayloadsEmitted": False,
        "archiveMembersIndexed": True,
        "runtimeReady": False,
        "earlyBirthSequence": {
            "actorPackageSections": actor_sections,
            "sceneParticipants": [
                {
                    "role": role,
                    "reference": {"sourceIdentity": actor_refs[role]},
                }
                for role in REFERENCE_KEYS
            ],
        },
    }
    projection_path = root / "projection.json"
    projection_path.write_text(json.dumps(projection), encoding="utf-8")
    references = [
        {
            "reference": static_ref,
            "baseFormKey": static_base["formKey"],
            "baseDisposition": "effective-plugin-record",
            "authoredTransformAuthority": False,
        },
        {
            "reference": phantom_ref,
            "baseFormKey": phantom_base["formKey"],
            "baseDisposition": "effective-plugin-record",
            "authoredTransformAuthority": False,
        },
        {
            "reference": inline_ref,
            "baseFormKey": "FalloutNV.esm:000020",
            "baseDisposition": "inline-reference-primitive-no-plugin-base-record",
            "authoredTransformAuthority": False,
        },
        *[
            {
                "reference": actor_refs[role],
                "baseFormKey": FORM_KEYS[role],
                "baseDisposition": "effective-plugin-record",
                "authoredTransformAuthority": False,
            }
            for role in REFERENCE_KEYS
        ],
    ]
    expanded_records = [
        record(EXPECTED_CELL_FORM_KEY, "06028138", "CELL", "Vault101d"),
        static_base,
        phantom_base,
        static_ref,
        phantom_ref,
        inline_ref,
        *actor_refs.values(),
        *[actors[role]["base"] for role in ROLE_ORDER],
        *section_identities,
    ]
    expanded_members = [
        static_model["member"],
        phantom_model["member"],
        *[
            actor_sections[role][0]["animationMemberIdentity"] for role in ROLE_ORDER
        ],
    ]
    closure = {
        "schema": RESOURCE_CLOSURE_SCHEMA,
        "status": RESOURCE_CLOSURE_STATUS,
        "campaign": "Fallout3",
        "edition": "TTW",
        "stage": {"questEditorId": "CG00", "stage": 10},
        "identity": {
            "projection": {
                "path": str(projection_path),
                "sha256": file_sha256(projection_path),
            },
            "sourceProfile": {"file": "profile.json", "sha256": DIGEST},
            "sourceNamespace": {"file": "namespace.json", "sha256": DIGEST},
            "pluginStackId": PLUGIN_STACK_ID,
            "saveCompatibilityId": SAVE_COMPATIBILITY_ID,
            "expandedRecordClosureSha256": _canonical_sha256(expanded_records),
            "expandedMemberClosureSha256": _canonical_sha256(expanded_members),
        },
        "cell": {
            "identity": expanded_records[0],
            "references": references,
            "baseObjects": [
                {"record": static_base, "model": static_model},
                {"record": phantom_base, "model": phantom_model},
            ],
            "inlinePrimitiveReferences": [references[2]],
            "authoredReferenceTransformsPublished": False,
        },
        "camera1st": {
            "targetNode": "Camera1st",
            "skeleton": member("meshes\\characters\\_1stperson\\skeleton.nif"),
            "section1Animation": actor_sections["player"][0]["animationMemberIdentity"],
            "runtimeNodeMaterialized": False,
        },
        "actors": actors,
        "expandedClosure": {
            "recordCount": len(expanded_records),
            "memberCount": len(expanded_members),
            "records": expanded_records,
            "members": expanded_members,
        },
        "resourceClosureReady": True,
        "identityOnlyIntrospectionModels": [],
        "runtimeMaterializationBlockers": [
            "runtime-cell-actor-and-camera-nodes-not-emitted-by-identity-closure"
        ],
        "liveOnlyFields": list(LIVE_ONLY_FIELDS),
        "ownedPayloadsEmitted": False,
        "authoredTransformsAcceptedAsLive": False,
        "standaloneArtifactsAccepted": False,
        "runtimeNodesMaterialized": False,
        "runtimeReady": False,
    }
    closure_path = root / "closure.json"
    closure_path.write_text(json.dumps(closure), encoding="utf-8")
    placements = [
        {
            "formKey": row["reference"]["formKey"],
            "recordType": row["reference"]["recordType"],
            "transform": {
                "authority": "effective-reference-DATA-and-XSCL-authored-not-live",
                "positionGameUnits": [1.0, 2.0, 3.0],
                "rotationRadians": [0.0, 0.0, 0.0],
                "scale": 1.0,
                "dataSha256": DIGEST,
                "xsclSha256": None,
            },
            **(
                {
                    "inlinePrimitive": {
                        "dimensionsGameUnits": [4.0, 5.0, 6.0],
                        "colorRgba": [0.0, 0.0, 0.0, 0.25],
                        "primitiveType": 3,
                        "xprmSha256": DIGEST,
                        "multiboundDimensionsGameUnits": None,
                        "occlusionPlane": None,
                        "physicsCollisionAuthority": False,
                    }
                }
                if row is references[2]
                else {}
            ),
        }
        for row in references
    ]
    return closure, projection, placements, closure_path, projection_path


class TtwFo3Stage10RuntimeWorldInputTest(unittest.TestCase):
    def test_emits_source_nodes_and_retains_exact_live_gate(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (
                closure,
                projection,
                placements,
                closure_path,
                projection_path,
            ) = fixture(root)
            result = project_ttw_fo3_stage10_runtime_world_input(
                closure,
                projection,
                placements,
                closure_path=closure_path,
                closure_sha256=file_sha256(closure_path),
                projection_path=projection_path,
            )

        self.assertEqual(result["schema"], SCHEMA)
        self.assertEqual(result["status"], STATUS)
        self.assertEqual(len(result["nodes"]["cellShell"]), 1)
        self.assertEqual(result["nodes"]["collisionNodeCount"], 1)
        self.assertEqual(len(result["nodes"]["phantoms"]), 1)
        self.assertEqual(len(result["nodes"]["inlineVolumes"]), 1)
        self.assertEqual(list(result["nodes"]["actors"]), list(ROLE_ORDER))
        self.assertIsNone(result["nodes"]["actors"]["player"]["reference"])
        self.assertTrue(
            all(
                row["renderedRootTransform"] is None
                and row["visible"] is None
                and row["controllerPhase"] is None
                for row in result["nodes"]["actors"].values()
            )
        )
        self.assertEqual(
            result["nodes"]["phantoms"][0]["collision"]["semantics"],
            "retain-non-blocking-overlap-trigger",
        )
        self.assertEqual(
            result["liveObservationGate"]["unresolvedFields"],
            list(LIVE_ONLY_FIELDS),
        )
        self.assertEqual(
            result["runtimeBlockers"],
            [RUNTIME_ARTIFACT_BLOCKER, LIVE_OBSERVATION_BLOCKER],
        )
        self.assertTrue(result["runtimeWorldInputReady"])
        self.assertTrue(result["runtimeNodeDescriptorsEmitted"])
        self.assertFalse(result["runtimeArtifactsMaterialized"])
        self.assertFalse(result["runtimeReady"])

    def test_rejects_missing_live_only_field(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (
                closure,
                projection,
                placements,
                closure_path,
                projection_path,
            ) = fixture(root)
            closure["liveOnlyFields"] = list(LIVE_ONLY_FIELDS[:-1])
            closure_path.write_text(json.dumps(closure), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "resource closure gate differs"):
                project_ttw_fo3_stage10_runtime_world_input(
                    closure,
                    projection,
                    placements,
                    closure_path=closure_path,
                    closure_sha256=file_sha256(closure_path),
                    projection_path=projection_path,
                )


if __name__ == "__main__":
    unittest.main()
