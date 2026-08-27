from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from opening_catalog import (  # noqa: E402
    FlowSourceCatalog,
    IdleAnimationSource,
    ReferenceTransformSource,
    _compile_dialogue_voice,
    _compile_guide_package,
    _compile_player_package,
    _resolve_command_record_identities,
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
