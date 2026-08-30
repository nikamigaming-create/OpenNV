from __future__ import annotations

import copy
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from plugin_stack import file_sha256  # noqa: E402
from ttw_fo3_stage10_world_materialization import (  # noqa: E402
    BLOCKERS,
    MATERIALIZATION_SCHEMA,
    MATERIALIZATION_STATUS,
    project_ttw_fo3_stage10_world_inputs,
)


RECORD_COUNT = 76
MEMBER_COUNT = 57
SECTION = 1
HEX_LENGTH = 64
ROLE_BASES = {
    "player": "Player",
    "father": "CG00Dad",
    "doctor": "CG00DoctorLi",
    "mother": "CG00Mom",
}
ROLE_REFERENCES = {
    "father": ("CG00DadREF", "0290a7", "03a17b"),
    "doctor": ("CG00DoctorLiREF", "0290a5", "0290a4"),
    "mother": ("CG00MomREF", "05ede0", "06a810"),
}


def canonical_sha256(value: object) -> str:
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()


def source_identity(
    record_type: str,
    local_form_id: str,
    editor_id: str,
    *,
    origin: str = "Fallout3.esm",
) -> dict[str, object]:
    stable_local = local_form_id.zfill(8)
    digest = hashlib.sha256(
        f"{record_type}:{origin}:{stable_local}:{editor_id}".encode("utf-8")
    ).hexdigest()
    return {
        "formKey": f"{origin}:{stable_local[2:]}",
        "runtimeFormId": f"06{stable_local[2:]}" if origin == "Fallout3.esm" else stable_local,
        "winner": {
            "plugin": origin,
            "loadOrderIndex": 6 if origin == "Fallout3.esm" else 0,
            "sourceRootIndex": 1,
            "pluginSha256": hashlib.sha256(origin.encode("utf-8")).hexdigest(),
            "recordSha256": digest,
        },
        "overriddenVersions": [],
        "recordType": record_type,
        "editorId": editor_id,
        "stableLocalFormId": stable_local,
    }


def member_identity(logical_path: str) -> dict[str, object]:
    digest = hashlib.sha256(logical_path.encode("utf-8")).hexdigest()
    byte_count = len(logical_path.encode("utf-8")) + HEX_LENGTH
    return {
        "logicalPath": logical_path,
        "bytes": byte_count,
        "sha256": digest,
        "winner": {
            "kind": "bsa",
            "archive": "Fallout - Meshes.bsa",
            "memberBytes": byte_count,
            "memberSha256": digest,
        },
        "overriddenVersions": [],
    }


def synthetic_projection(root: Path) -> tuple[dict[str, object], Path, str]:
    source_profile_path = root / "ttw-profile.json"
    source_namespace_path = root / "ttw-effective-source.json"
    source_profile_path.write_text('{"profile":"ttw"}\n', encoding="utf-8")
    source_namespace_path.write_text('{"namespace":"ttw"}\n', encoding="utf-8")
    profile_sha256 = file_sha256(source_profile_path)
    namespace_sha256 = file_sha256(source_namespace_path)
    plugin_stack_id = hashlib.sha256(b"synthetic-ttw-stack").hexdigest()
    save_compatibility_id = f"ttw:{plugin_stack_id}"
    source_profile = {
        "file": str(source_profile_path.resolve()),
        "sha256": profile_sha256,
        "pluginStackId": plugin_stack_id,
        "saveCompatibilityId": save_compatibility_id,
    }
    source_namespace = {
        "file": str(source_namespace_path.resolve()),
        "sha256": namespace_sha256,
    }

    records = [
        source_identity("CELL", "00028138", "Vault101d"),
        source_identity("NPC_", "00000007", "Player", origin="FalloutNV.esm"),
        source_identity("NPC_", "000290a6", "CG00Dad"),
        source_identity("NPC_", "000290a3", "CG00DoctorLi"),
        source_identity("NPC_", "0005eddf", "CG00Mom"),
    ]
    participants = []
    for role, (editor_id, reference_id, marker_id) in ROLE_REFERENCES.items():
        reference = source_identity("ACHR", f"00{reference_id}", editor_id)
        marker = source_identity(
            "REFR",
            f"00{marker_id}",
            f"{ROLE_BASES[role]}StartMarker",
        )
        records.extend((reference, marker))
        participants.append(
            {
                "role": role,
                "reference": {"sourceIdentity": copy.deepcopy(reference)},
                "startMarker": {"sourceIdentity": copy.deepcopy(marker)},
            }
        )

    members = []
    actor_sections: dict[str, list[dict[str, object]]] = {}
    for role_index, role in enumerate(ROLE_BASES, start=1):
        package = source_identity(
            "PACK",
            f"0010{role_index:04x}",
            f"CG00{role.title()}Section01Package",
        )
        idle = source_identity(
            "IDLE",
            f"0011{role_index:04x}",
            f"CG00{role.title()}Section01Idle",
        )
        animation = member_identity(
            f"meshes\\characters\\_male\\idleanims\\cg00{role}section01.kf"
        )
        records.extend((package, idle))
        members.append(animation)
        actor_sections[role] = [
            {
                "section": SECTION,
                "packageFormId": package["stableLocalFormId"],
                "idleFormId": idle["stableLocalFormId"],
                "animationLogicalPath": animation["logicalPath"],
                "packageSourceIdentity": copy.deepcopy(package),
                "idleSourceIdentity": copy.deepcopy(idle),
                "animationMemberIdentity": copy.deepcopy(animation),
            }
        ]
    camera_skeleton = member_identity(
        "meshes\\characters\\_1stperson\\skeleton.nif"
    )
    members.append(camera_skeleton)
    while len(records) < RECORD_COUNT:
        row_index = len(records)
        records.append(
            source_identity(
                "INFO",
                f"0020{row_index:04x}",
                f"SyntheticInfo{row_index}",
            )
        )
    while len(members) < MEMBER_COUNT:
        members.append(member_identity(f"sound\\synthetic-{len(members)}.wav"))

    record_closure = {"recordCount": len(records), "records": records}
    member_closure = {"memberCount": len(members), "members": members}
    opening = {
        "schema": "opennv-ttw-fo3-opening-profile/v1",
        "status": "transported-bounded-ttw-fo3-opening-command-contract",
        "campaign": "Fallout3",
        "edition": "TTW",
        "sourceProfile": copy.deepcopy(source_profile),
        "sourceNamespace": copy.deepcopy(source_namespace),
        "saveCompatibilityId": save_compatibility_id,
    }
    player_section = actor_sections["player"][0]
    projection = {
        "schema": "opennv-ttw-fo3-cg00-profile-projection/v1",
        "status": "validated-runtime-consumable-identity-projection-assets-pending",
        "campaign": "Fallout3",
        "edition": "TTW",
        "ownedPayloadsEmitted": False,
        "archiveMembersIndexed": True,
        "runtimeReady": False,
        "effectiveRecordClosure": record_closure,
        "effectiveMemberClosure": member_closure,
        "openingCommandContract": opening,
        "earlyBirthSequence": {
            "assetsPrepared": False,
            "actorPackageSections": actor_sections,
            "sceneParticipants": participants,
            "playerCamera": {
                "targetNode": "Camera1st",
                "packageSourceIdentity": copy.deepcopy(
                    player_section["packageSourceIdentity"]
                ),
                "idleSourceIdentity": copy.deepcopy(
                    player_section["idleSourceIdentity"]
                ),
                "animationMemberIdentity": copy.deepcopy(
                    player_section["animationMemberIdentity"]
                ),
                "skeletonMemberIdentity": camera_skeleton,
            },
        },
        "identityEnvelope": {
            "sourceProfile": source_profile,
            "sourceNamespace": source_namespace,
            "effectiveSource": {
                "pluginStackId": plugin_stack_id,
                "saveCompatibilityId": save_compatibility_id,
                "sourceProfileSha256": profile_sha256,
                "sourceNamespaceSha256": namespace_sha256,
                "standaloneFallout3ProfileAccepted": False,
                "standaloneFallout3CacheReused": False,
                "standaloneNewVegasProfileAccepted": False,
                "standaloneNewVegasCacheReused": False,
            },
            "recordClosureSha256": canonical_sha256(record_closure),
            "memberClosureSha256": canonical_sha256(member_closure),
            "openingCommandContractSha256": canonical_sha256(opening),
        },
        "cacheBoundary": {
            "compatibilityId": f"ttw-fo3-opening:{plugin_stack_id}",
            "standaloneFallout3ProfileAccepted": False,
            "standaloneFallout3CacheReused": False,
            "standaloneNewVegasProfileAccepted": False,
            "standaloneNewVegasCacheReused": False,
        },
    }
    projection_path = root / "projection.json"
    projection_path.write_text(json.dumps(projection), encoding="utf-8")
    return projection, projection_path, file_sha256(projection_path)


class TtwFo3Stage10WorldMaterializationTest(unittest.TestCase):
    def test_emits_exact_source_joins_and_fail_closed_artifact_boundary(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            projection, projection_path, projection_sha256 = synthetic_projection(
                Path(temporary_directory)
            )
            opening = projection["openingCommandContract"]
            result = project_ttw_fo3_stage10_world_inputs(
                projection,
                projection_path=projection_path,
                projection_sha256=projection_sha256,
                opening_profile_binding={
                    "authority": "embedded-projection-command-contract",
                    "containerPath": str(projection_path),
                    "containerSha256": projection_sha256,
                    "jsonPointer": "/openingCommandContract",
                    "commandContractSha256": canonical_sha256(opening),
                },
            )

        self.assertEqual(result["schema"], MATERIALIZATION_SCHEMA)
        self.assertEqual(result["status"], MATERIALIZATION_STATUS)
        self.assertEqual(list(result["participants"]), list(ROLE_BASES))
        self.assertEqual(result["camera1st"]["targetNode"], "Camera1st")
        self.assertEqual(result["closure"]["recordCount"], RECORD_COUNT)
        self.assertEqual(result["closure"]["memberCount"], MEMBER_COUNT)
        self.assertEqual(result["closure"]["blockers"], list(BLOCKERS))
        self.assertEqual(result["closure"]["runtimeArtifactCount"], 0)
        self.assertTrue(
            all(
                participant["runtimeNodeArtifact"] is None
                for participant in result["participants"].values()
            )
        )
        self.assertFalse(result["ownedPayloadsEmitted"])
        self.assertFalse(result["runtimeArtifactsMaterialized"])
        self.assertFalse(result["adapterSceneIdentityReady"])
        self.assertFalse(result["runtimeReady"])

    def test_rejects_member_not_joined_to_the_effective_closure(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            projection, projection_path, projection_sha256 = synthetic_projection(
                Path(temporary_directory)
            )
            projection["effectiveMemberClosure"]["members"][0] = member_identity(
                "meshes\\wrong.kf"
            )
            projection["identityEnvelope"]["memberClosureSha256"] = canonical_sha256(
                projection["effectiveMemberClosure"]
            )
            with self.assertRaisesRegex(ValueError, "member closure join differs"):
                project_ttw_fo3_stage10_world_inputs(
                    projection,
                    projection_path=projection_path,
                    projection_sha256=projection_sha256,
                    opening_profile_binding={
                        "commandContractSha256": canonical_sha256(
                            projection["openingCommandContract"]
                        )
                    },
                )

    def test_rejects_standalone_profile_authority(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            projection, projection_path, projection_sha256 = synthetic_projection(
                Path(temporary_directory)
            )
            projection["identityEnvelope"]["effectiveSource"][
                "standaloneFallout3ProfileAccepted"
            ] = True
            with self.assertRaisesRegex(ValueError, "admits standalone"):
                project_ttw_fo3_stage10_world_inputs(
                    projection,
                    projection_path=projection_path,
                    projection_sha256=projection_sha256,
                    opening_profile_binding={
                        "commandContractSha256": canonical_sha256(
                            projection["openingCommandContract"]
                        )
                    },
                )


if __name__ == "__main__":
    unittest.main()
