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

from opening_catalog import (  # noqa: E402
    AnimationObjectSource,
    FlowSourceCatalog,
    IdleAnimationSource,
    ReferenceTransformSource,
    _compile_dialogue_voice,
    _compile_gameplay_vitals,
    _compile_guide_animation_objects,
    _compile_guide_furniture_occupancy,
    _compile_guide_package,
    _compile_player_package,
    _compile_player_appearance,
    _resolve_command_record_identities,
    _resolve_actor_animation_commands,
    _script_commands,
)
from bsa_archive import ExtractedMember  # noqa: E402
from plugin_records import Record  # noqa: E402


SYNTHETIC_PACKAGE_FORM = 0x10
SYNTHETIC_IDLE_BEGIN_FORM = 0x20
SYNTHETIC_IDLE_LOOP_FORM = 0x30
SYNTHETIC_DESTINATION_FORM = 0x40
SYNTHETIC_VOICE_TYPE_FORM = 0x50
SYNTHETIC_ACTOR_BASE_FORM = 0x60
SYNTHETIC_INFO_FORM = 0x70
RUN_IN_SEQUENCE_FLAG = 0x01
DO_ONCE_FLAG = 0x04
ALWAYS_RUN_FLAG = 0x2000


class SyntheticAudioArchives:
    def __init__(self, payloads: dict[str, bytes]):
        self.payloads = payloads
        self.members = frozenset(payloads)

    def extract(self, logical_path: str) -> ExtractedMember:
        payload = self.payloads[logical_path]
        return ExtractedMember(
            logical_path,
            payload,
            False,
            0,
            len(payload),
            "Synthetic Voices.bsa",
            "synthetic-archive-sha256",
        )

    @staticmethod
    def manifest() -> dict[str, object]:
        return {"schema": "synthetic-owned-audio-stack/v1"}


def subrecord(signature: str, data: bytes = b"") -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


class OpeningCatalogTest(unittest.TestCase):
    def test_player_appearance_joins_playable_race_sex_hair_eye_and_facegen(self):
        race_form = 0x19
        hair_male_form = 0x20
        hair_female_form = 0x21
        eyes_form = 0x22
        player = Record(
            "NPC_",
            0x07,
            0,
            b"".join(
                (
                    subrecord("EDID", b"Player\0"),
                    subrecord("RNAM", struct.pack("<I", race_form)),
                    subrecord("HNAM", struct.pack("<I", hair_male_form)),
                    subrecord("ENAM", struct.pack("<I", eyes_form)),
                    subrecord("FGGS", struct.pack("<50f", *([0.1] * 50))),
                    subrecord("FGGA", struct.pack("<30f", *([0.0] * 30))),
                    subrecord("FGTS", struct.pack("<50f", *([0.2] * 50))),
                )
            ),
            (),
        )
        race_data = bytearray(36)
        struct.pack_into("<I", race_data, 32, 1)
        race = Record(
            "RACE",
            race_form,
            0,
            b"".join(
                (
                    subrecord("EDID", b"SyntheticRace\0"),
                    subrecord("FULL", b"Synthetic Race\0"),
                    subrecord("DATA", bytes(race_data)),
                    subrecord(
                        "HNAM",
                        struct.pack("<2I", hair_male_form, hair_female_form),
                    ),
                    subrecord("ENAM", struct.pack("<I", eyes_form)),
                )
            ),
            (),
        )

        def part(record_type, form_id, editor, label, flags):
            return Record(
                record_type,
                form_id,
                0,
                b"".join(
                    (
                        subrecord("EDID", editor.encode("ascii") + b"\0"),
                        subrecord("FULL", label.encode("ascii") + b"\0"),
                        subrecord("MODL", f"characters\\{editor}.nif\0".encode()),
                        subrecord("ICON", f"characters\\{editor}.dds\0".encode()),
                        subrecord("DATA", bytes([flags])),
                    )
                ),
                (),
            )

        records = {
            race_form: race,
            hair_male_form: part(
                "HAIR", hair_male_form, "MaleHair", "Male Hair", 0x05
            ),
            hair_female_form: part(
                "HAIR", hair_female_form, "FemaleHair", "Female Hair", 0x03
            ),
            eyes_form: part("EYES", eyes_form, "Eyes", "Eyes", 0x01),
        }
        result, textures = _compile_player_appearance(
            FlowSourceCatalog(
                actor_values=[],
                traits=[],
                scripts={},
                idle_animations_by_editor={},
                idle_animations_by_form={},
                packages_by_editor={},
                packages_by_form={},
                actors_by_form={},
                voice_types_by_form={},
                references_by_form={},
                image_space_modifiers_by_editor={},
                needed={},
                player_base=player,
                appearance_records_by_form=records,
            ),
            "if nButton== 0\r\n player.sexChange male 1\r\n"
            "elseif nButton== 1\r\n player.sexChange female 1",
            {"schema": "synthetic-facegen-control-space/v1"},
        )

        self.assertEqual(result["player"]["defaultRaceFormId"], "00000019")
        self.assertEqual(result["sexEngineValues"], ["male", "female"])
        self.assertEqual(len(result["races"]), 1)
        self.assertEqual(
            result["races"][0]["sex"]["male"]["defaultHairFormId"],
            "00000020",
        )
        self.assertEqual(
            result["races"][0]["sex"]["female"]["defaultHairFormId"],
            "00000021",
        )
        self.assertEqual(result["player"]["faceGen"]["symmetricGeometry"]["count"], 50)
        self.assertEqual(
            result["player"]["faceGen"]["controlSpace"]["schema"],
            "synthetic-facegen-control-space/v1",
        )
        self.assertEqual(len(textures), 3)

    def test_gameplay_vitals_join_player_actor_values_and_owned_settings(self):
        player_acbs = bytearray(24)
        struct.pack_into("<h", player_acbs, 8, 1)
        player_data = struct.pack("<I7B", 100, 5, 5, 5, 5, 5, 5, 5)
        player = Record(
            "NPC_",
            0x07,
            0,
            b"".join(
                (
                    subrecord("EDID", b"Player\0"),
                    subrecord("ACBS", bytes(player_acbs)),
                    subrecord("DATA", player_data),
                )
            ),
            (),
        )
        setting_values = {
            "fAVDHealthEnduranceMult": 20.0,
            "fAVDHealthLevelMult": 5.0,
            "fAVDActionPointsBase": 65.0,
            "fAVDActionPointsMult": 3.0,
            "iXPBumpBase": 150,
        }
        settings = {}
        for index, (editor_id, value) in enumerate(setting_values.items(), start=1):
            payload = (
                struct.pack("<f", value)
                if editor_id.startswith("f")
                else struct.pack("<i", value)
            )
            settings[editor_id.casefold()] = Record(
                "GMST",
                0x100 + index,
                0,
                subrecord("EDID", editor_id.encode("ascii") + b"\0")
                + subrecord("DATA", payload),
                (),
            )
        actor_values = [
            {
                "editorId": editor_id,
                "formId": f"{0x200 + index:08x}",
                "dataSha256": f"actor-value-{index}",
            }
            for index, editor_id in enumerate(
                ("AVHealth", "AVActionPoints", "AVXP", "AVEndurance", "AVAgility"),
                start=1,
            )
        ]
        sources = FlowSourceCatalog(
            actor_values=actor_values,
            traits=[],
            scripts={},
            idle_animations_by_editor={},
            idle_animations_by_form={},
            packages_by_editor={},
            packages_by_form={},
            actors_by_form={},
            voice_types_by_form={},
            references_by_form={},
            image_space_modifiers_by_editor={},
            needed={},
            game_settings_by_editor=settings,
            player_base=player,
        )

        contract = _compile_gameplay_vitals(sources)

        self.assertEqual(contract["playerBase"]["initialLevel"], 1)
        self.assertEqual(contract["playerBase"]["baseHealth"], 100)
        self.assertEqual(contract["initialExperiencePoints"], 0)
        self.assertEqual(
            {value["editorId"]: value["value"] for value in contract["gameSettings"]},
            {**setting_values, "iXPBase": 200},
        )
        self.assertEqual(len(contract["actorValues"]), 5)
        self.assertEqual(
            contract["gameSettings"][-1]["evidenceId"],
            "fnv-1.4.0.525-gmst-ixpbase-v1",
        )

    def test_command_contract_resolves_owned_record_identities(self):
        commands = [
            {"kind": "additem", "itemEditorId": "SyntheticItem", "count": 1},
            {"kind": "setStage", "questEditorId": "SyntheticQuest", "stage": 2},
            {"kind": "setGlobal", "globalEditorId": "SyntheticGlobal", "value": 3.0},
            {
                "kind": "actorValueDelta",
                "ownerEditorId": "SyntheticQuest",
                "value": "Science",
                "delta": 1,
            },
            {
                "kind": "actorIntent",
                "referenceEditorId": "SyntheticActor",
                "operation": "evp",
            },
        ]
        records = [
            {
                "formId": "00000010",
                "recordType": "WEAP",
                "text": [{"signature": "EDID", "value": "SyntheticItem"}],
            },
            {
                "formId": "00000020",
                "recordType": "QUST",
                "text": [{"signature": "EDID", "value": "SyntheticQuest"}],
            },
            {
                "formId": "00000030",
                "recordType": "GLOB",
                "text": [{"signature": "EDID", "value": "SyntheticGlobal"}],
            },
            {
                "formId": "00000040",
                "recordType": "ACHR",
                "text": [{"signature": "EDID", "value": "SyntheticActor"}],
            },
        ]

        contract = _resolve_command_record_identities(commands, records)

        self.assertEqual(contract["commandCount"], len(commands))
        self.assertTrue(contract["allDeclaredRecordReferencesResolved"])
        self.assertEqual(commands[0]["itemFormId"], "00000010")
        self.assertEqual(commands[1]["questFormId"], "00000020")
        self.assertEqual(commands[2]["globalFormId"], "00000030")
        self.assertEqual(commands[3]["ownerFormId"], "00000020")
        self.assertEqual(commands[4]["referenceFormId"], "00000040")

    def test_command_contract_rejects_unaccounted_runtime_kind(self):
        with self.assertRaisesRegex(ValueError, "command kind is unaccounted"):
            _resolve_command_record_identities([{"kind": "syntheticUnknown"}], [])

    def test_script_commands_preserve_player_package_camera_effect_and_controls(self):
        commands = _script_commands(
            "\n".join(
                (
                    "player.addscriptpackage PackageAlpha",
                    "player.addscriptpackage packagealpha",
                    "player.removescriptpackage",
                    "ApplyImageSpaceModifier WakeEffect *",
                    "RemoveImageSpaceModifier WakeEffect",
                    "DisablePlayerControls 1 1 1 1 0 0 1",
                    "Player.RemoveItem DeviceAlpha 1 1",
                )
            )
        )

        self.assertEqual(
            [command["kind"] for command in commands],
            [
                "addScriptPackage",
                "addScriptPackage",
                "removeScriptPackage",
                "imageSpaceModifier",
                "imageSpaceModifier",
                "playerControls",
                "removeitem",
            ],
        )
        self.assertTrue(commands[3]["crossFade"])
        self.assertEqual(commands[5]["arguments"][-1], "sneaking")

    def test_player_package_resolves_event_and_idle_semantics(self):
        package_data = struct.pack("<IBBHHH", 4, 6, 0, 2, 3, 0)
        payload = b"".join(
            (
                subrecord("EDID", b"SyntheticPackage\0"),
                subrecord("PKDT", package_data),
                subrecord("IDLF", bytes((RUN_IN_SEQUENCE_FLAG,))),
                subrecord("IDLC", bytes((2,))),
                subrecord("IDLT", struct.pack("<f", 0.5)),
                subrecord(
                    "IDLA",
                    struct.pack(
                        "<2I",
                        SYNTHETIC_IDLE_BEGIN_FORM,
                        SYNTHETIC_IDLE_LOOP_FORM,
                    ),
                ),
                subrecord("POBA"),
                subrecord("INAM", struct.pack("<I", SYNTHETIC_IDLE_BEGIN_FORM)),
                subrecord("POEA"),
                subrecord("INAM", struct.pack("<I", 0)),
                subrecord("POCA"),
                subrecord("INAM", struct.pack("<I", SYNTHETIC_IDLE_LOOP_FORM)),
            )
        )
        record = Record("PACK", SYNTHETIC_PACKAGE_FORM, 0, payload, ())
        idles = {
            SYNTHETIC_IDLE_BEGIN_FORM: IdleAnimationSource(
                SYNTHETIC_IDLE_BEGIN_FORM,
                "SyntheticBegin",
                "meshes\\synthetic-begin.kf",
            ),
            SYNTHETIC_IDLE_LOOP_FORM: IdleAnimationSource(
                SYNTHETIC_IDLE_LOOP_FORM,
                "SyntheticLoop",
                "meshes\\synthetic-loop.kf",
            ),
        }

        result = _compile_player_package(
            record,
            {"POBA": "begin", "POEA": "end", "POCA": "change"},
            {"runInSequence": RUN_IN_SEQUENCE_FLAG, "doOnce": DO_ONCE_FLAG},
            idles,
        )

        self.assertTrue(result["idleSelection"]["runInSequence"])
        self.assertFalse(result["idleSelection"]["doOnce"])
        self.assertEqual(result["events"]["begin"], "00000020")
        self.assertIsNone(result["events"]["end"])
        self.assertEqual(result["events"]["change"], "00000030")

    def test_guide_package_preserves_condition_destination_and_idle(self):
        condition = bytearray(28)
        condition[0] = 0x60
        struct.pack_into("<f", condition, 4, 55.0)
        struct.pack_into("<H", condition, 8, 58)
        struct.pack_into("<I", condition, 12, 0x00104C1C)
        payload = b"".join(
            (
                subrecord("EDID", b"SyntheticGuideTravel\0"),
                subrecord(
                    "PKDT",
                    struct.pack("<IBBHHH", ALWAYS_RUN_FLAG, 6, 0, 32, 0, 0),
                ),
                subrecord("CTDA", bytes(condition)),
                subrecord(
                    "PLDT",
                    struct.pack("<III", 0, SYNTHETIC_DESTINATION_FORM, 0),
                ),
                subrecord("IDLC", bytes((1,))),
                subrecord("IDLA", struct.pack("<I", SYNTHETIC_IDLE_BEGIN_FORM)),
            )
        )
        record = Record("PACK", SYNTHETIC_PACKAGE_FORM, 0, payload, ())
        destination = ReferenceTransformSource(
            SYNTHETIC_DESTINATION_FORM,
            "SyntheticMarker",
            "REFR",
            (1.0, 2.0, 3.0),
            (0.0, 0.0, 1.0),
        )
        sources = FlowSourceCatalog(
            actor_values=[],
            traits=[],
            scripts={},
            idle_animations_by_editor={},
            idle_animations_by_form={
                SYNTHETIC_IDLE_BEGIN_FORM: IdleAnimationSource(
                    SYNTHETIC_IDLE_BEGIN_FORM,
                    "SyntheticIdle",
                    "meshes\\synthetic-idle.kf",
                )
            },
            packages_by_editor={},
            packages_by_form={},
            actors_by_form={},
            voice_types_by_form={},
            references_by_form={SYNTHETIC_DESTINATION_FORM: destination},
            image_space_modifiers_by_editor={},
            needed={},
        )
        contract = {
            "packageTypeNames": {"6": "travel"},
            "conditionFunctionNames": {"58": "getStage"},
            "locationTypeNames": {"0": "nearReference"},
            "targetTypeNames": {"0": "reference"},
            "packageFlagBits": {"alwaysRun": ALWAYS_RUN_FLAG},
        }

        result, idle_paths = _compile_guide_package(record, contract, sources)

        self.assertTrue(result["alwaysRun"])
        self.assertEqual(result["packageTypeName"], "travel")
        self.assertEqual(result["conditions"][0]["functionName"], "getStage")
        self.assertEqual(
            result["location"]["reference"]["editorId"],
            "SyntheticMarker",
        )
        self.assertEqual(idle_paths, ("meshes\\synthetic-idle.kf",))

    def test_guide_animation_object_joins_idle_anio_nif_and_attachment(self):
        animation_object_form = 0x35
        payload = b"owned-animation-object"
        logical_path = "meshes\\animobjects\\owned.nif"
        sources = FlowSourceCatalog(
            actor_values=[],
            traits=[],
            scripts={},
            idle_animations_by_editor={},
            idle_animations_by_form={
                SYNTHETIC_IDLE_BEGIN_FORM: IdleAnimationSource(
                    SYNTHETIC_IDLE_BEGIN_FORM,
                    "SyntheticIdle",
                    "meshes\\synthetic-idle.kf",
                )
            },
            packages_by_editor={},
            packages_by_form={},
            actors_by_form={},
            voice_types_by_form={},
            references_by_form={},
            image_space_modifiers_by_editor={},
            needed={},
            animation_objects_by_idle_form={
                SYNTHETIC_IDLE_BEGIN_FORM: (
                    AnimationObjectSource(
                        animation_object_form,
                        "SyntheticAnimationObject",
                        logical_path,
                        SYNTHETIC_IDLE_BEGIN_FORM,
                        "owned-anio-record-sha256",
                    ),
                )
            },
        )
        archives = SyntheticAudioArchives({logical_path: payload})

        with patch(
            "opening_catalog.authored_rigid_attachment_node",
            return_value="Bip01 R Hand",
        ):
            result = _compile_guide_animation_objects(
                [SYNTHETIC_IDLE_BEGIN_FORM],
                sources,
                archives,  # type: ignore[arg-type]
            )

        self.assertEqual(len(result), 1)
        self.assertEqual(result[0]["formId"], f"{animation_object_form:08x}")
        self.assertEqual(
            result[0]["idleAnimationFormId"],
            f"{SYNTHETIC_IDLE_BEGIN_FORM:08x}",
        )
        self.assertEqual(result[0]["modelLogicalPath"], logical_path)
        self.assertEqual(result[0]["sha256"], hashlib.sha256(payload).hexdigest())
        self.assertEqual(result[0]["attachmentNode"], "Bip01 R Hand")

    def test_guide_furniture_occupancy_admits_strict_loop_and_exit(self):
        seated_payload = b"owned-seated-loop"
        exit_payload = b"owned-chair-exit"
        seated_path = "meshes\\characters\\_male\\idleanims\\seat.kf"
        exit_path = "meshes\\characters\\_male\\idleanims\\exit.kf"
        seated_form = 0x31
        exit_form = 0x32
        initial_package = 0x41
        release_package = 0x42
        furniture_reference = 0x43
        object_idle = 0x44
        quest_form = "00000045"
        furniture_base = 0x46
        heading_delta_form = 0x47
        heading_delta_editor = "fFurnitureMarker14HeadingDelta"
        heading_delta_value = 3.1415998935699463
        placement_settings = {
            "x": (0x48, "fFurnitureMarker14DeltaX", 2.5),
            "y": (0x49, "fFurnitureMarker14DeltaY", 57.0),
            "z": (0x4A, "fFurnitureMarker14DeltaZ", -29.0),
        }
        furniture_path = "meshes\\furniture\\synthetic-chair.nif"
        furniture_payload = b"owned-furniture-nif"
        furniture_record = Record(
            "FURN",
            furniture_base,
            0,
            subrecord("EDID", b"SyntheticChair\0")
            + subrecord("MODL", b"furniture\\synthetic-chair.nif\0"),
            (),
        )
        heading_delta_record = Record(
            "GMST",
            heading_delta_form,
            0,
            subrecord("EDID", heading_delta_editor.encode("ascii") + b"\0")
            + subrecord("DATA", struct.pack("<f", heading_delta_value)),
            (),
        )
        placement_records = {
            axis: Record(
                "GMST",
                form_id,
                0,
                subrecord("EDID", editor_id.encode("ascii") + b"\0")
                + subrecord("DATA", struct.pack("<f", value)),
                (),
            )
            for axis, (form_id, editor_id, value) in placement_settings.items()
        }
        sources = FlowSourceCatalog(
            actor_values=[],
            traits=[],
            scripts={},
            idle_animations_by_editor={},
            idle_animations_by_form={
                seated_form: IdleAnimationSource(
                    seated_form,
                    "SyntheticChairLoop",
                    seated_path,
                    "seated-record-sha256",
                ),
                exit_form: IdleAnimationSource(
                    exit_form,
                    "SyntheticChairExit",
                    exit_path,
                    "exit-record-sha256",
                ),
            },
            packages_by_editor={},
            packages_by_form={},
            actors_by_form={},
            voice_types_by_form={},
            references_by_form={
                furniture_reference: ReferenceTransformSource(
                    furniture_reference,
                    "SyntheticChairRef",
                    "REFR",
                    (1.0, 2.0, 3.0),
                    (0.0, 0.0, 0.0),
                    furniture_base,
                    "furniture-reference-record-sha256",
                )
            },
            image_space_modifiers_by_editor={},
            needed={},
            game_settings_by_editor={
                heading_delta_editor.casefold(): heading_delta_record,
                **{
                    editor_id.casefold(): placement_records[axis]
                    for axis, (_form_id, editor_id, _value) in placement_settings.items()
                },
            },
            furniture_by_form={furniture_base: furniture_record},
        )
        packages = [
            {
                "formId": f"{initial_package:08x}",
                "location": {"formId": f"{furniture_reference:08x}"},
                "idleAnimationFormIds": [f"{object_idle:08x}"],
                "conditions": [],
            },
            {
                "formId": f"{release_package:08x}",
                "location": {"formId": "00000046"},
                "idleAnimationFormIds": [f"{object_idle:08x}"],
                "conditions": [
                    {
                        "functionName": "getStage",
                        "parameter1": quest_form,
                        "operatorFlags": 0x60,
                        "comparisonValue": 40.0,
                    }
                ],
            },
        ]
        contract = {
            "furnitureOccupancy": {
                "initialPackageFormId": f"{initial_package:08x}",
                "releasePackageFormId": f"{release_package:08x}",
                "referenceFormId": f"{furniture_reference:08x}",
                "releaseStage": 40,
                "markerId": 14,
                "animationObjectIdleFormId": f"{object_idle:08x}",
                "furniture": {
                    "referenceRecordSha256": "furniture-reference-record-sha256",
                    "baseFormId": f"{furniture_base:08x}",
                    "editorId": "SyntheticChair",
                    "recordSha256": hashlib.sha256(furniture_record.data).hexdigest(),
                    "modelLogicalPath": furniture_path,
                    "modelSha256": hashlib.sha256(furniture_payload).hexdigest(),
                    "marker": {
                        "extraDataName": "FRN",
                        "index": 2,
                        "positionRef1": 14,
                        "positionRef2": 14,
                        "offsetNifGameUnits": [-0.25, 50.0, -25.0],
                        "orientation": 3141,
                        "animationType": 1,
                        "actorPlacementOffsetGameSettings": {
                            "semantics": "replace-marker-offset-for-actor-placement",
                            **{
                                axis: {
                                    "formId": f"{form_id:08x}",
                                    "editorId": editor_id,
                                    "recordSha256": hashlib.sha256(
                                        placement_records[axis].data
                                    ).hexdigest(),
                                    "valueGameUnits": value,
                                }
                                for axis, (form_id, editor_id, value) in placement_settings.items()
                            },
                        },
                        "actorForwardHeadingDeltaGameSetting": {
                            "formId": f"{heading_delta_form:08x}",
                            "editorId": heading_delta_editor,
                            "recordSha256": hashlib.sha256(
                                heading_delta_record.data
                            ).hexdigest(),
                            "valueRadians": heading_delta_value,
                        },
                    },
                },
                "seatedLoop": {
                    "formId": f"{seated_form:08x}",
                    "editorId": "SyntheticChairLoop",
                    "recordSha256": "seated-record-sha256",
                    "logicalPath": seated_path,
                    "sha256": hashlib.sha256(seated_payload).hexdigest(),
                    "sequenceName": "SyntheticChairLoopSequence",
                    "cycleType": 0,
                },
                "exit": {
                    "formId": f"{exit_form:08x}",
                    "editorId": "SyntheticChairExit",
                    "recordSha256": "exit-record-sha256",
                    "logicalPath": exit_path,
                    "sha256": hashlib.sha256(exit_payload).hexdigest(),
                    "sequenceName": "SyntheticChairExitSequence",
                    "cycleType": 2,
                },
            }
        }
        archives = SyntheticAudioArchives(
            {
                seated_path: seated_payload,
                exit_path: exit_payload,
                furniture_path: furniture_payload,
            }
        )

        exit_playback = {
            "sequenceName": "SyntheticChairExitSequence",
            "startSeconds": 0.0,
            "stopSeconds": 1.0,
            "cycleType": 2,
            "controlledBlocks": 4,
        }
        exit_root_motion = {
            "sequenceName": exit_playback["sequenceName"],
            "targetNode": "Bip01",
            "startSeconds": exit_playback["startSeconds"],
            "stopSeconds": exit_playback["stopSeconds"],
            "cycleType": exit_playback["cycleType"],
            "displacementGodotGameUnits": [1.0, 0.0, 0.0],
            "speedGameUnitsPerSecond": 1.0,
        }
        with patch(
            "opening_catalog.furniture_marker_manifest",
            return_value={
                "extraDataName": "FRN",
                "index": 2,
                "positionRef1": 14,
                "positionRef2": 14,
                "offsetNifGameUnits": [-0.25, 50.0, -25.0],
                "offsetGodotGameUnits": [-0.25, -25.0, -50.0],
                "orientation": 3141,
                "orientationRadians": 3.141,
                "heading": 0.0,
                "animationType": 1,
            },
        ), patch(
            "opening_catalog.animation_sequence_manifest",
            side_effect=(
                {
                    "sequenceName": "SyntheticChairLoopSequence",
                    "startSeconds": 0.0,
                    "stopSeconds": 2.0,
                    "cycleType": 0,
                    "controlledBlocks": 3,
                },
                exit_playback,
            ),
        ), patch("opening_catalog.sample_root_motion") as root_motion:
            root_motion.return_value.manifest.return_value = exit_root_motion
            result, paths = _compile_guide_furniture_occupancy(
                contract,
                packages,
                sources,
                archives,  # type: ignore[arg-type]
                quest_form,
                "Bip01",
                30.0,
            )

        self.assertEqual(
            result["markerDisposition"],
            "compose-owned-furniture-reference-gmst-replacement-offset-and-heading-delta",
        )
        self.assertEqual(
            result["furniture"]["marker"]["offsetGodotGameUnits"],
            [-0.25, -25.0, -50.0],
        )
        heading_delta = result["furniture"]["marker"][
            "actorForwardHeadingDeltaGameSetting"
        ]
        self.assertEqual(heading_delta["formId"], f"{heading_delta_form:08x}")
        self.assertEqual(heading_delta["editorId"], heading_delta_editor)
        self.assertEqual(heading_delta["value"], heading_delta_value)
        self.assertEqual(len(heading_delta["rotationGodotQuaternion"]), 4)
        placement = result["furniture"]["marker"][
            "actorPlacementOffsetGameSettings"
        ]
        self.assertEqual(
            placement["semantics"],
            "replace-marker-offset-for-actor-placement",
        )
        self.assertEqual(placement["offsetNifGameUnits"], [2.5, 57.0, -29.0])
        self.assertEqual(placement["offsetGodotGameUnits"], [2.5, -29.0, -57.0])
        for axis, (form_id, editor_id, value) in placement_settings.items():
            self.assertEqual(placement[axis]["formId"], f"{form_id:08x}")
            self.assertEqual(placement[axis]["editorId"], editor_id)
            self.assertEqual(placement[axis]["value"], value)
        self.assertEqual(result["seatedLoop"]["formId"], f"{seated_form:08x}")
        self.assertEqual(result["seatedLoop"]["cycleType"], 0)
        self.assertEqual(result["exit"]["formId"], f"{exit_form:08x}")
        self.assertEqual(result["exit"]["cycleType"], 2)
        self.assertEqual(result["exit"]["rootMotion"], exit_root_motion)
        self.assertEqual(paths, (seated_path, exit_path))

    def test_play_idle_runtime_binding_carries_source_idle_form_id(self):
        command = {
            "kind": "playIdle",
            "idleEditorId": "SyntheticIdle",
            "referenceEditorId": "SyntheticActor",
        }

        _resolve_actor_animation_commands(
            [{"commands": [command]}],
            {"topics": [], "psychologyRootInfo": {"commands": []}},
            [
                {
                    "editorId": "SyntheticActor",
                    "recordType": "ACHR",
                    "referenceFormId": "00000080",
                }
            ],
            {
                "syntheticidle": IdleAnimationSource(
                    SYNTHETIC_IDLE_BEGIN_FORM,
                    "SyntheticIdle",
                    "meshes\\synthetic-idle.kf",
                )
            },
        )

        self.assertEqual(
            command["idleFormId"],
            f"{SYNTHETIC_IDLE_BEGIN_FORM:08x}",
        )
        self.assertEqual(command["idleRecordType"], "IDLE")

    def test_dialogue_voice_joins_vtck_info_and_paired_archive_members(self):
        info_form_id = f"{SYNTHETIC_INFO_FORM:08x}"
        base_form_id = f"{SYNTHETIC_ACTOR_BASE_FORM:08x}"
        voice_form_id = f"{SYNTHETIC_VOICE_TYPE_FORM:08x}"
        member_root = (
            "sound\\voice\\falloutnv.esm\\syntheticvoice\\"
            f"synthetictopic_{info_form_id}_1"
        )
        archives = SyntheticAudioArchives(
            {
                member_root + ".ogg": b"owned-voice",
                member_root + ".lip": b"owned-lip",
            }
        )
        info = {
            "formId": info_form_id,
            "lines": ["Owned response"],
        }
        dialogue = {
            "topics": [],
            "psychologyRootInfo": info,
        }
        roles = [
            {
                "role": "speaker",
                "referenceFormId": "00000080",
                "baseFormId": base_form_id,
            }
        ]
        sources = FlowSourceCatalog(
            actor_values=[],
            traits=[],
            scripts={},
            idle_animations_by_editor={},
            idle_animations_by_form={},
            packages_by_editor={},
            packages_by_form={},
            actors_by_form={},
            voice_types_by_form={
                SYNTHETIC_VOICE_TYPE_FORM: "SyntheticVoice",
            },
            references_by_form={},
            image_space_modifiers_by_editor={},
            needed={
                SYNTHETIC_ACTOR_BASE_FORM: {
                    "links": [
                        {
                            "signature": "VTCK",
                            "formId": voice_form_id,
                        }
                    ]
                }
            },
        )

        with tempfile.TemporaryDirectory() as directory:
            _compile_dialogue_voice(
                dialogue,
                {"dialogueVoice": {"speakerRole": "speaker"}},
                roles,
                sources,
                archives,  # type: ignore[arg-type]
                Path("FalloutNV.esm"),
                Path(directory),
            )
            response = info["responses"][0]
            voice_source = Path(response["voice"]["source"])
            lip_source = Path(response["lip"]["source"])
            self.assertEqual(voice_source.read_bytes(), b"owned-voice")
            self.assertEqual(lip_source.read_bytes(), b"owned-lip")

        self.assertNotIn("lines", info)
        self.assertEqual(response["text"], "Owned response")
        self.assertEqual(dialogue["voice"]["voiceTypeFormId"], voice_form_id)


if __name__ == "__main__":
    unittest.main()
