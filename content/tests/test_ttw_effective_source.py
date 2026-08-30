from __future__ import annotations

import hashlib
import json
import os
import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from bsa_archive import ExtractedMember, canonical_member_path  # noqa: E402
from plugin_stack import PluginContext, file_sha256  # noqa: E402
from ttw_effective_source import (  # noqa: E402
    EffectiveMembers,
    EffectiveRecords,
    TtwArchiveSource,
    TtwEffectiveSource,
    TtwResourceOrder,
    load_ttw_effective_record_source,
    validated_ttw_stack,
)
from prepare_fo3_profile import enumerate_ttw_fo3_profile_inputs  # noqa: E402
from ttw_fo3_semantic_differential import (  # noqa: E402
    compile_ttw_fo3_cg00_semantic_differential,
)
from ttw_fo3_member_closure import (  # noqa: E402
    compile_ttw_fo3_cg00_member_closure,
)
from ttw_fo3_profile_projection import (  # noqa: E402
    _typed_ttw_stage_commands,
    _validated_member_identity,
    compile_ttw_fo3_cg00_profile_projection,
)


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, data: bytes, flags: int = 0) -> bytes:
    return (
        signature.encode("ascii")
        + struct.pack("<I", len(data))
        + struct.pack("<I", flags)
        + struct.pack("<I", form_id)
        + bytes(8)
        + data
    )


def plugin_payload(masters: list[str], *records: bytes) -> bytes:
    header = b"".join(
        subrecord("MAST", name.encode("ascii") + b"\0") for name in masters
    )
    return record("TES4", 0, header) + b"".join(records)


class FakeArchive:
    def __init__(self, rows: dict[str, bytes]):
        self.rows = {
            canonical_member_path(path): payload for path, payload in rows.items()
        }
        self.members = self.rows.keys()

    def extract(self, logical_path: str) -> ExtractedMember:
        requested = canonical_member_path(logical_path)
        payload = self.rows[requested]
        return ExtractedMember(
            requested,
            payload,
            False,
            100,
            len(payload),
        )


class TtwFo3ProjectionHelpersTest(unittest.TestCase):
    def test_typed_command_dialect_keeps_ttw_stage_zero_and_gene_projector(self) -> None:
        contract = _typed_ttw_stage_commands(
            [
                {
                    "stage": 0,
                    "commands": [
                        'PlayBink "Fallout INTRO Vsk.bik" 1 1 0 1',
                        "SetNumericGameSetting fFoo 2",
                        "SetNumericGameSetting fBar -3.5",
                    ],
                },
                {
                    "stage": 60,
                    "commands": ["TTW_ShowGeneProjector", "set CG00.timer to 1"],
                },
            ]
        )

        self.assertEqual(contract["stage0"][0]["kind"], "playBink")
        self.assertEqual(contract["stage0"][0]["arguments"], [1, 1, 0, 1])
        self.assertEqual(
            [row["value"] for row in contract["stage0"][1:]],
            [2.0, -3.5],
        )
        self.assertEqual(
            contract["geneProjector"]["kind"], "showTtwGeneProjector"
        )
        self.assertFalse(contract["geneProjector"]["standaloneEquivalent"])

    def test_member_identity_rejects_winner_hash_drift(self) -> None:
        identity = {
            "logicalPath": "meshes/test/idle.kf",
            "bytes": 4,
            "sha256": "a" * 64,
            "winner": {
                "kind": "bsa",
                "memberBytes": 4,
                "memberSha256": "a" * 64,
            },
        }
        self.assertEqual(
            _validated_member_identity(identity)["logicalPath"],
            "meshes\\test\\idle.kf",
        )
        identity["winner"]["memberSha256"] = "b" * 64
        with self.assertRaisesRegex(ValueError, "path/hash identity"):
            _validated_member_identity(identity)

    def test_member_identity_accepts_hash_bound_effective_loose_winner(self) -> None:
        identity = {
            "logicalPath": "meshes/test/idle.kf",
            "bytes": 5,
            "sha256": "c" * 64,
            "winner": {
                "kind": "loose",
                "sourceRootIndex": 1,
                "source": "D:/TTW/Installed/meshes/test/idle.kf",
                "bytes": 5,
                "sha256": "c" * 64,
            },
        }

        validated = _validated_member_identity(identity)

        self.assertEqual(validated["logicalPath"], "meshes\\test\\idle.kf")
        self.assertEqual(validated["winner"]["kind"], "loose")


def archive_source(
    root: Path,
    name: str,
    source_root_index: int,
    rows: dict[str, bytes],
) -> TtwArchiveSource:
    path = root / name
    path.write_bytes(name.encode("ascii"))
    return TtwArchiveSource(
        name,
        path,
        source_root_index,
        file_sha256(path),
        path.stat().st_size,
        FakeArchive(rows),  # type: ignore[arg-type]
    )


class TtwEffectiveSourceTest(unittest.TestCase):
    def test_stable_origin_local_form_id_resolves_last_active_override(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            base = root / "Fallout3.esm"
            ttw = root / "TaleOfTwoWastelands.esm"
            base.write_bytes(
                plugin_payload(
                    [],
                    record(
                        "CELL",
                        0x00028138,
                        subrecord("EDID", b"Vault101d\0"),
                    ),
                )
            )
            ttw.write_bytes(
                plugin_payload(
                    ["Fallout3.esm"],
                    record(
                        "CELL",
                        0x00028138,
                        subrecord("EDID", b"Vault101d\0"),
                    ),
                )
            )
            contexts = (
                PluginContext(
                    "Fallout3.esm",
                    base,
                    6,
                    (),
                    ("Fallout3.esm",),
                    file_sha256(base),
                    base.stat().st_size,
                ),
                PluginContext(
                    "TaleOfTwoWastelands.esm",
                    ttw,
                    16,
                    ("Fallout3.esm",),
                    ("Fallout3.esm", "TaleOfTwoWastelands.esm"),
                    file_sha256(ttw),
                    ttw.stat().st_size,
                ),
            )
            resolver = EffectiveRecords(
                contexts,
                {"fallout3.esm": 0, "taleoftwowastelands.esm": 1},
                {"fallout3.esm": 6, "taleoftwowastelands.esm": 16},
                frozenset({"CELL"}),
            )

            resolution = resolver.resolution("fallout3.ESM", 0x028138)
            contract = resolver.contract(
                {
                    "formKey": "Fallout3.esm:028138",
                    "recordType": "CELL",
                    "editorId": "Vault101d",
                    "winnerPlugin": "TaleOfTwoWastelands.esm",
                }
            )

        self.assertEqual(resolution["formKey"], "Fallout3.esm:028138")
        self.assertEqual(resolution["runtimeFormId"], "06028138")
        self.assertEqual(
            resolution["winner"]["plugin"],
            "TaleOfTwoWastelands.esm",
        )
        self.assertEqual(len(resolution["overriddenVersions"]), 1)
        self.assertEqual(contract["recordType"], "CELL")
        with self.assertRaisesRegex(ValueError, "outside the active stack"):
            resolver.resolution("Missing.esm", 0x028138)
        with self.assertRaisesRegex(ValueError, "local FormID"):
            resolver.resolution("Fallout3.esm", 0x1000000)

    def test_member_resolution_uses_explicit_bsa_order_then_loose_root_order(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            lower = root / "lower"
            upper = root / "upper"
            lower.mkdir()
            upper.mkdir()
            first = archive_source(
                root,
                "First.bsa",
                0,
                {"meshes/test/item.nif": b"archive-first"},
            )
            second = archive_source(
                root,
                "Second.bsa",
                1,
                {"meshes/test/item.nif": b"archive-second"},
            )
            unmarked = EffectiveMembers((lower, upper), (first, second), frozenset())
            resolver = EffectiveMembers(
                (lower, upper),
                (first, second),
                frozenset({"second.bsa"}),
            )

            archived_unmarked = unmarked.resolve("Meshes\\Test\\Item.nif")
            archived = resolver.resolve("Meshes\\Test\\Item.nif")
            (lower / "Meshes" / "Test").mkdir(parents=True)
            (lower / "Meshes" / "Test" / "ITEM.NIF").write_bytes(b"loose-lower")
            loose_lower = resolver.resolve("meshes/test/item.nif")
            (upper / "meshes" / "test").mkdir(parents=True)
            (upper / "meshes" / "test" / "item.nif").write_bytes(b"loose-upper")
            loose_upper = resolver.resolve("meshes/test/item.nif")

        self.assertEqual(archived_unmarked.data, b"archive-first")
        self.assertEqual(
            archived_unmarked.overridden_versions[0]["memberPrecedenceDisposition"],
            "unmarked-archive-cannot-replace-earlier-member",
        )
        self.assertEqual(archived.data, b"archive-second")
        self.assertEqual(archived.winner["archiveOrderIndex"], 1)
        self.assertTrue(archived.winner["hasSameStemOverrideMarker"])
        self.assertEqual(loose_lower.data, b"loose-lower")
        self.assertEqual(loose_lower.winner["sourceRootIndex"], 0)
        self.assertEqual(loose_upper.data, b"loose-upper")
        self.assertEqual(loose_upper.winner["sourceRootIndex"], 1)
        self.assertEqual(
            [row["kind"] for row in loose_upper.overridden_versions],
            ["bsa", "bsa", "loose"],
        )

    def test_compiler_contract_retains_ttw_cache_and_save_isolation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            profile_path = root / "ttw-profile.json"
            namespace_path = root / "ttw-effective-source.json"
            profile_path.write_text("{}", encoding="utf-8")
            namespace_path.write_text("{}", encoding="utf-8")
            recipe_path = root / "ttw-profile-v1.json"
            recipe_path.write_text("{}", encoding="utf-8")
            profile = {
                "pluginStackId": "a" * 64,
                "saveCompatibilityId": f"ttw:{'a' * 64}",
            }
            first_order = TtwResourceOrder(
                recipe_path,
                "synthetic-resource-order",
                ("First.bsa", "Second.bsa"),
                ("Second.override",),
            )
            second_order = TtwResourceOrder(
                recipe_path,
                "synthetic-resource-order",
                ("Second.bsa", "First.bsa"),
                ("Second.override",),
            )
            first = TtwEffectiveSource(
                profile_path,
                namespace_path,
                profile,
                {},
                None,  # type: ignore[arg-type]
                None,  # type: ignore[arg-type]
                first_order,
            ).compiler_contract()
            second = TtwEffectiveSource(
                profile_path,
                namespace_path,
                profile,
                {},
                None,  # type: ignore[arg-type]
                None,  # type: ignore[arg-type]
                second_order,
            ).compiler_contract()

        self.assertTrue(first["cacheCompatibilityId"].startswith("ttw-effective-source:"))
        self.assertEqual(first["saveCompatibilityId"], f"ttw:{'a' * 64}")
        self.assertFalse(first["standaloneFallout3ProfileAccepted"])
        self.assertFalse(first["standaloneFallout3CacheReused"])
        self.assertFalse(first["standaloneNewVegasProfileAccepted"])
        self.assertFalse(first["standaloneNewVegasCacheReused"])
        self.assertFalse(first["runtimeReady"])
        self.assertEqual(
            first["resourceOrder"]["overrideMarkers"],
            ["Second.override"],
        )
        self.assertNotEqual(
            first["cacheCompatibilityId"],
            second["cacheCompatibilityId"],
        )

    def test_owned_registered_stack_resolves_vault_101_winner_when_available(
        self,
    ) -> None:
        local_app_data = os.environ.get("LOCALAPPDATA")
        if not local_app_data:
            self.skipTest("LOCALAPPDATA is unavailable")
        profile_path = Path(local_app_data) / "OpenNV" / "profiles" / "ttw-profile.json"
        if not profile_path.is_file():
            self.skipTest("owned TTW profile is not registered")

        profile, _roots, contexts, indices = validated_ttw_stack(profile_path)
        resolver = EffectiveRecords(
            contexts,
            {
                str(row["file"]).casefold(): int(row["sourceRootIndex"])
                for row in profile["plugins"]
            },
            indices,
            frozenset({"CELL"}),
        )
        resolution = resolver.resolution("Fallout3.esm", 0x028138)

        self.assertEqual(len(contexts), 18)
        self.assertEqual(resolution["formKey"], "Fallout3.esm:028138")
        self.assertEqual(resolution["runtimeFormId"], "06028138")
        self.assertEqual(
            resolution["winner"]["plugin"],
            "TaleOfTwoWastelands.esm",
        )
        self.assertEqual(len(resolution["overriddenVersions"]), 1)

    def test_owned_resource_order_and_prepare_boundary_when_available(self) -> None:
        local_app_data = os.environ.get("LOCALAPPDATA")
        if not local_app_data:
            self.skipTest("LOCALAPPDATA is unavailable")
        profiles = Path(local_app_data) / "OpenNV" / "profiles"
        profile_path = profiles / "ttw-profile.json"
        namespace_path = profiles / "ttw-effective-source.json"
        if not profile_path.is_file() or not namespace_path.is_file():
            self.skipTest("owned TTW profile and namespace are not registered")

        source = load_ttw_effective_record_source(
            profile_path,
            namespace_path,
            frozenset({"CELL", "QUST", "SCPT"}),
        )
        enumeration = enumerate_ttw_fo3_profile_inputs(
            profile_path,
            namespace_path,
        )

        self.assertEqual(len(source.resource_order.archive_order), 40)
        self.assertEqual(len(source.resource_order.override_markers), 9)
        self.assertIsNone(source.members)
        self.assertEqual(
            enumeration["records"]["vault101d"]["formKey"],
            "Fallout3.esm:028138",
        )
        self.assertEqual(
            enumeration["records"]["cg00Quest"]["formKey"],
            "FalloutNV.esm:01f388",
        )
        self.assertEqual(
            enumeration["records"]["cg00Script"]["formKey"],
            "FalloutNV.esm:03a17c",
        )
        closure = enumeration["cg00SceneClosure"]
        self.assertEqual(closure["recordCount"], 76)
        self.assertEqual(
            closure["recordTypeCounts"],
            {
                "ACHR": 3,
                "CELL": 1,
                "DIAL": 2,
                "IDLE": 20,
                "IMAD": 3,
                "INFO": 12,
                "NPC_": 4,
                "PACK": 20,
                "QUST": 1,
                "REFR": 5,
                "SCPT": 1,
                "SOUN": 2,
                "VTYP": 2,
            },
        )
        self.assertTrue(closure["recordClosureReady"])
        self.assertFalse(closure["archiveMembersIndexed"])
        self.assertFalse(closure["profileEmissionReady"])
        self.assertFalse(closure["runtimeReady"])
        self.assertEqual(
            [row["role"] for row in closure["participants"]],
            ["father", "doctor", "mother"],
        )
        self.assertEqual(
            closure["participants"][0]["reference"]["formKey"],
            "Fallout3.esm:0290a7",
        )
        self.assertEqual(
            closure["participants"][0]["base"]["formKey"],
            "Fallout3.esm:0290a6",
        )
        self.assertEqual(
            closure["participants"][0]["reference"]["winner"]["plugin"],
            "Fallout3.esm",
        )
        self.assertEqual(
            closure["packageSections"]["father"][0]["package"]["formKey"],
            "FalloutNV.esm:08f778",
        )
        self.assertEqual(
            closure["packageSections"]["father"][0]["package"]["winner"][
                "plugin"
            ],
            "Fallout3.esm",
        )
        self.assertEqual(
            closure["packageSections"]["father"][0]["idle"]["formKey"],
            "FalloutNV.esm:084439",
        )
        self.assertEqual(
            closure["packageSections"]["father"][0]["idle"]["winner"][
                "plugin"
            ],
            "FalloutNV.esm",
        )
        self.assertEqual(
            closure["dialogue"]["topics"]["father"]["winner"]["plugin"],
            "TaleOfTwoWastelands.esm",
        )
        self.assertEqual(
            closure["dialogue"]["stage22Male"][1]["topicFormKey"],
            "FalloutNV.esm:02cb97",
        )
        self.assertEqual(
            closure["dialogue"]["stage22Male"][1]["voiceTypeFormKey"],
            "Fallout3.esm:05eddc",
        )
        self.assertEqual(
            closure["imageSpaceModifiers"][0]["formKey"],
            "FalloutNV.esm:0230e7",
        )
        self.assertEqual(
            closure["sounds"][0]["formKey"],
            "Fallout3.esm:023045",
        )
        self.assertFalse(enumeration["profileEmissionReady"])
        self.assertFalse(enumeration["runtimeReady"])

    def test_owned_cg00_semantic_differential_when_available(self) -> None:
        local_app_data = os.environ.get("LOCALAPPDATA")
        if not local_app_data:
            self.skipTest("LOCALAPPDATA is unavailable")
        profiles = Path(local_app_data) / "OpenNV" / "profiles"
        ttw_profile = profiles / "ttw-profile.json"
        namespace = profiles / "ttw-effective-source.json"
        standalone_profile = (
            profiles / "fallout3" / "vanilla" / "fallout3-profile.json"
        )
        if not all(
            path.is_file() for path in (ttw_profile, namespace, standalone_profile)
        ):
            self.skipTest("owned TTW and standalone Fallout 3 profiles are unavailable")
        standalone = json.loads(standalone_profile.read_text(encoding="utf-8"))
        master_row = dict(standalone["install"]["master"])
        standalone_master = Path(str(master_row["source"]))
        if not standalone_master.is_file():
            self.skipTest("owned standalone Fallout 3 master is unavailable")
        self.assertEqual(standalone_master.stat().st_size, master_row["bytes"])
        self.assertEqual(file_sha256(standalone_master), master_row["sha256"])

        differential = compile_ttw_fo3_cg00_semantic_differential(
            ttw_profile,
            namespace,
            standalone_master,
        )

        self.assertEqual(
            differential["schema"],
            "opennv-ttw-fo3-cg00-semantic-differential/v1",
        )
        self.assertEqual(differential["sourceClosure"]["recordCount"], 76)
        self.assertEqual(
            differential["matchingCategories"],
            ["packages", "actorsAndMarkers", "dialogue", "imageSpaceModifiers"],
        )
        self.assertEqual(differential["differingCategories"], ["stages", "sounds"])
        categories = {
            row["category"]: row for row in differential["categories"]
        }
        stage_difference = categories["stages"]
        ttw_stages = {
            row["stage"]: row["commands"]
            for row in stage_difference["ttw"]["stageResults"]
        }
        standalone_stages = {
            row["stage"]: row["commands"]
            for row in stage_difference["standalone"]["stageResults"]
        }
        self.assertEqual(
            ttw_stages[0][0],
            'PlayBink "Fallout INTRO Vsk.bik" 1 1 0 1',
        )
        self.assertNotIn(ttw_stages[0][0], standalone_stages[0])
        self.assertEqual(ttw_stages[60][0], "TTW_ShowGeneProjector")
        self.assertEqual(standalone_stages[60][0], "ShowRaceMenu")
        sound_difference = categories["sounds"]
        self.assertEqual(
            [row["logicalPath"] for row in sound_difference["ttw"]],
            [row["logicalPath"] for row in sound_difference["standalone"]],
        )
        self.assertNotEqual(
            [row["soundDataSha256"] for row in sound_difference["ttw"]],
            [row["soundDataSha256"] for row in sound_difference["standalone"]],
        )
        terminal_links = differential["postClosurePackageChangeLinks"]
        self.assertEqual(
            [(row["role"], row["toIdle"]["formKey"]) for row in terminal_links],
            [
                ("player", "FalloutNV.esm:069efd"),
                ("father", "FalloutNV.esm:069eef"),
                ("doctor", "FalloutNV.esm:069ef3"),
                ("mother", "FalloutNV.esm:069ef8"),
            ],
        )
        self.assertTrue(
            all(row["toIdle"]["winner"]["plugin"] == "FalloutNV.esm" for row in terminal_links)
        )
        self.assertFalse(differential["archiveMembersIndexed"])
        self.assertFalse(differential["profileEmissionReady"])
        self.assertFalse(differential["runtimeReady"])

    def test_owned_cg00_member_closure_when_available(self) -> None:
        local_app_data = os.environ.get("LOCALAPPDATA")
        if not local_app_data:
            self.skipTest("LOCALAPPDATA is unavailable")
        profiles = Path(local_app_data) / "OpenNV" / "profiles"
        ttw_profile = profiles / "ttw-profile.json"
        namespace = profiles / "ttw-effective-source.json"
        if not ttw_profile.is_file() or not namespace.is_file():
            self.skipTest("owned TTW profile and namespace are unavailable")

        closure = compile_ttw_fo3_cg00_member_closure(ttw_profile, namespace)

        self.assertEqual(
            closure["schema"],
            "opennv-ttw-fo3-cg00-member-closure/v1",
        )
        self.assertEqual(closure["recordClosure"]["recordCount"], 76)
        self.assertEqual(len(closure["packageAnimations"]), 20)
        self.assertEqual(len(closure["externalSection5Animations"]), 4)
        self.assertEqual(len(closure["dialogue"]), 12)
        self.assertEqual(
            [(row["sound"]["editorId"], len(row["members"])) for row in closure["sounds"]],
            [("QSTBirthStart", 1), ("QSTBabyCry", 7)],
        )
        self.assertEqual(closure["memberCount"], 57)
        self.assertEqual(
            len({path.casefold() for path in closure["memberLogicalPaths"]}),
            closure["memberCount"],
        )
        members = [
            *(row["member"] for row in closure["packageAnimations"]),
            *(row["member"] for row in closure["externalSection5Animations"]),
            closure["playerCameraSkeleton"],
            *(row["voice"] for row in closure["dialogue"]),
            *(row["lip"] for row in closure["dialogue"]),
            *(member for row in closure["sounds"] for member in row["members"]),
        ]
        winner_counts: dict[str, int] = {}
        for member in members:
            winner = dict(member["winner"])
            self.assertEqual(winner["kind"], "bsa")
            self.assertEqual(member["sha256"], winner["memberSha256"])
            self.assertEqual(member["bytes"], winner["memberBytes"])
            self.assertIsInstance(winner["archiveOrderIndex"], int)
            self.assertIn(
                winner["memberPrecedenceDisposition"],
                {
                    "initial-containing-archive-wins",
                    "same-stem-override-marker-replaces-earlier-archive",
                },
            )
            self.assertIsInstance(member["overriddenVersions"], list)
            archive = str(winner["archive"])
            winner_counts[archive] = winner_counts.get(archive, 0) + 1
        self.assertEqual(
            winner_counts,
            {
                "Fallout - Meshes.bsa": 25,
                "Fallout3 - Voices.bsa": 24,
                "Fallout3 - Sound.bsa": 8,
            },
        )
        resource_order = closure["source"]["resourceOrder"]
        self.assertEqual(len(resource_order["archiveOrder"]), 40)
        self.assertEqual(len(resource_order["overrideMarkers"]), 9)
        self.assertFalse(closure["ownedPayloadsEmitted"])
        self.assertTrue(closure["archiveMembersIndexed"])
        self.assertFalse(closure["profileEmissionReady"])
        self.assertFalse(closure["runtimeReady"])

    def test_owned_cg00_profile_projection_when_available(self) -> None:
        local_app_data = os.environ.get("LOCALAPPDATA")
        if not local_app_data:
            self.skipTest("LOCALAPPDATA is unavailable")
        profiles = Path(local_app_data) / "OpenNV" / "profiles"
        ttw_profile = profiles / "ttw-profile.json"
        namespace = profiles / "ttw-effective-source.json"
        standalone_profile = (
            profiles / "fallout3" / "vanilla" / "fallout3-profile.json"
        )
        if not all(
            path.is_file() for path in (ttw_profile, namespace, standalone_profile)
        ):
            self.skipTest("owned TTW and standalone Fallout 3 profiles are unavailable")
        standalone = json.loads(standalone_profile.read_text(encoding="utf-8"))
        master_row = dict(standalone["install"]["master"])
        standalone_master = Path(str(master_row["source"]))
        if not standalone_master.is_file():
            self.skipTest("owned standalone Fallout 3 master is unavailable")

        projection = compile_ttw_fo3_cg00_profile_projection(
            ttw_profile,
            namespace,
            standalone_master,
        )

        self.assertEqual(
            projection["schema"],
            "opennv-ttw-fo3-cg00-profile-projection/v1",
        )
        self.assertEqual(
            projection["standaloneRuntimeContractSchema"],
            "opennv-fo3-cg00-early-birth-sequence/v1",
        )
        self.assertEqual(projection["effectiveRecordClosure"]["recordCount"], 76)
        self.assertEqual(projection["effectiveMemberClosure"]["memberCount"], 57)
        sequence = projection["earlyBirthSequence"]
        self.assertFalse(sequence["assetsPrepared"])
        self.assertEqual(
            sequence["questIdentity"]["formKey"], "FalloutNV.esm:01f388"
        )
        self.assertEqual(sequence["questIdentity"]["runtimeFormId"], "0001f388")
        command_dialect = sequence["profileProjection"]["commandDialect"]
        self.assertEqual(command_dialect["stage0"][0]["kind"], "playBink")
        self.assertEqual(
            command_dialect["geneProjector"]["sourceCommand"],
            "TTW_ShowGeneProjector",
        )
        package_rows = [
            row
            for rows in sequence["actorPackageSections"].values()
            for row in rows
        ]
        self.assertEqual(len(package_rows), 20)
        self.assertTrue(
            all(
                row["idleSourceIdentity"]["stableLocalFormId"]
                == row["idleFormId"].casefold()
                and len(row["animationMemberIdentity"]["sha256"]) == 64
                for row in package_rows
            )
        )
        self.assertFalse(projection["runtimeLoaderCompatibility"]["schemaAmbiguous"])
        self.assertTrue(
            projection["runtimeLoaderCompatibility"]["identityEnvelopeValidated"]
        )
        self.assertTrue(
            projection["runtimeLoaderCompatibility"]["commandStateExecutorReady"]
        )
        self.assertEqual(
            projection["identityEnvelope"]["sourceProfile"]["pluginStackId"],
            projection["identityEnvelope"]["effectiveSource"]["pluginStackId"],
        )
        self.assertEqual(
            projection["openingCommandContract"]["schema"],
            "opennv-ttw-fo3-opening-profile/v1",
        )
        self.assertEqual(
            projection["cacheBoundary"]["kind"],
            "dedicated-ttw-cg00-profile-projection",
        )
        self.assertTrue(
            projection["cacheBoundary"]["compatibilityId"].startswith(
                "ttw-fo3-opening:"
            )
        )
        self.assertFalse(projection["ownedPayloadsEmitted"])
        self.assertTrue(projection["archiveMembersIndexed"])
        self.assertTrue(projection["profileEmissionReady"])
        self.assertFalse(projection["runtimeReady"])


if __name__ == "__main__":
    unittest.main()
