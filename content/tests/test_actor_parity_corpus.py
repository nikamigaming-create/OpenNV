from __future__ import annotations

import json
import struct
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_parity_corpus import build_corpus  # noqa: E402
from validate_actor_parity_corpus import validate_corpus  # noqa: E402


DELETED_RECORD_FLAG = 0x00000020


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, data: bytes, flags: int = 0) -> bytes:
    return struct.pack(
        "<4s4I2H",
        signature.encode("ascii"),
        len(data),
        flags,
        form_id,
        0,
        0,
        0,
    ) + data


def group(label: bytes, group_type: int, contents: bytes) -> bytes:
    return struct.pack(
        "<4sI4siHHI",
        b"GRUP",
        24 + len(contents),
        label,
        group_type,
        0,
        0,
        0,
    ) + contents


def header(*masters: str) -> bytes:
    data = subrecord("HEDR", struct.pack("<fII", 1.34, 0, 0))
    for master in masters:
        data += subrecord("MAST", master.encode("ascii") + b"\0")
        data += subrecord("DATA", bytes(8))
    return record("TES4", 0, data)


def actor(form_id: int, editor_id: str, name: str) -> bytes:
    return record(
        "NPC_",
        form_id,
        subrecord("EDID", editor_id.encode("ascii") + b"\0")
        + subrecord("FULL", name.encode("ascii") + b"\0")
        + subrecord("ACBS", struct.pack("<6I", 0, 0, 0, 0, 0, 0))
        + subrecord("EAMT", struct.pack("<H", 0)),
    )


def creature(
    form_id: int,
    editor_id: str,
    template: int | None = None,
    template_flags: int = 0,
) -> bytes:
    data = (
        subrecord("EDID", editor_id.encode("ascii") + b"\0")
        + subrecord("FULL", editor_id.encode("ascii") + b"\0")
        + subrecord("MODL", b"creatures/test/skeleton.nif\0")
        + subrecord("NIFZ", b"test.nif\0\0")
        + subrecord("ACBS", struct.pack("<6I", 0, 0, 0, 0, 0, 0))
        + subrecord("EAMT", struct.pack("<H", template_flags))
    )
    if template is not None:
        data += subrecord("TPLT", struct.pack("<I", template))
    return record("CREA", form_id, data)


def leveled_creatures(form_id: int, editor_id: str, *entries: int) -> bytes:
    data = subrecord("EDID", editor_id.encode("ascii") + b"\0")
    for entry in entries:
        data += subrecord("LVLO", struct.pack("<HHIHH", 1, 0, entry, 1, 0))
    return record("LVLC", form_id, data)


def reference(signature: str, form_id: int, base: int) -> bytes:
    return record(
        signature,
        form_id,
        subrecord("NAME", struct.pack("<I", base))
        + subrecord("DATA", struct.pack("<6f", 1.0, 2.0, 3.0, 0.0, 0.0, 0.0)),
    )


def cell_children(*references: bytes) -> bytes:
    label = struct.pack("<I", 0x100)
    return group(label, 6, group(label, 9, b"".join(references)))


def base_plugin() -> bytes:
    return (
        header()
        + group(b"NPC_", 0, actor(0x50, "BaseActor", "Base Actor"))
        + group(
            b"CREA",
            0,
            creature(0x70, "DeletedCreature")
            + creature(0x71, "TemplatedCreature", 0x80, 0x03FF)
            + creature(0x72, "TemplateOutcome"),
        )
        + group(b"LVLC", 0, leveled_creatures(0x80, "BaseCreatureList", 0x70, 0x72))
        + group(
            b"CELL",
            0,
            record("CELL", 0x100, subrecord("EDID", b"SyntheticCell\0"))
            + cell_children(reference("ACHR", 0x60, 0x50), reference("ACRE", 0x61, 0x80)),
        )
    )


def dlc_plugin() -> bytes:
    return (
        header("FalloutNV.esm")
        + group(b"NPC_", 0, actor(0x00000050, "BaseActor", "DLC Actor Override"))
        + group(
            b"CREA",
            0,
            record("CREA", 0x00000070, b"", DELETED_RECORD_FLAG)
            + creature(0x01000090, "DlcCreature"),
        )
        + group(
            b"LVLC",
            0,
            leveled_creatures(0x00000080, "BaseCreatureList", 0x00000072)
            + leveled_creatures(0x01000091, "DlcCreatureList", 0x01000090),
        )
        + group(
            b"CELL",
            0,
            cell_children(reference("ACRE", 0x00000061, 0x01000091)),
        )
    )


def read_jsonl(path: Path) -> list[dict[str, object]]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]


class ActorParityCorpusTest(unittest.TestCase):
    def test_effective_stack_merges_overrides_deletions_lists_and_templates(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            (data_root / "FalloutNV.esm").write_bytes(base_plugin())
            (data_root / "Dlc.esm").write_bytes(dlc_plugin())
            output_root = root / "corpus"
            recipe = {
                "schema": "opennv-actor-parity-corpus-recipe/v1",
                "id": "synthetic-corpus",
                "plugins": [{"file": "FalloutNV.esm"}, {"file": "Dlc.esm"}],
                "capture": {
                    "humanoidAppearanceShots": ["portrait"],
                    "creatureAppearanceShots": ["full-body"],
                    "placementShots": ["context"],
                },
            }

            manifest = build_corpus(data_root, output_root, recipe)
            validated_counts = validate_corpus(output_root)
            bases = read_jsonl(output_root / "actor-bases.jsonl")
            placements = read_jsonl(output_root / "actor-placements.jsonl")
            reviews = read_jsonl(output_root / "appearance-review.jsonl")
            bases_path = output_root / "actor-bases.jsonl"
            bases_path.write_bytes(bases_path.read_bytes() + b" ")
            with self.assertRaisesRegex(ValueError, "byte count mismatch"):
                validate_corpus(output_root)

        bases_by_key = {row["formKey"]: row for row in bases}
        self.assertEqual(manifest["status"], "inventory-complete-review-pending")
        self.assertEqual(validated_counts["allBases"], 4)
        self.assertEqual(manifest["effectiveCounts"]["allBases"], 4)
        self.assertEqual(manifest["effectiveCounts"]["allPlacements"], 2)
        self.assertEqual(manifest["effectiveCounts"]["relationshipGaps"], 0)
        self.assertNotIn("FalloutNV.esm:000070", bases_by_key)
        self.assertEqual(
            bases_by_key["FalloutNV.esm:000050"]["sourcePlugin"],
            "Dlc.esm",
        )
        self.assertEqual(
            bases_by_key["FalloutNV.esm:000071"]["templateSelectionPaths"],
            [[
                "FalloutNV.esm:000071",
                "FalloutNV.esm:000080",
                "FalloutNV.esm:000072",
            ]],
        )
        self.assertEqual(
            bases_by_key["FalloutNV.esm:000071"]["appearanceVariants"][0][
                "categorySources"
            ]["model"],
            "FalloutNV.esm:000072",
        )
        self.assertEqual(
            bases_by_key["Dlc.esm:000090"]["runtimeFormId"],
            "01000090",
        )
        creature_placement = next(row for row in placements if row["recordType"] == "ACRE")
        self.assertEqual(
            creature_placement["candidateBaseFormKeys"],
            ["Dlc.esm:000090"],
        )
        self.assertEqual(len(reviews), len(bases))
        self.assertTrue(manifest["scope"]["everyEffectiveBaseScheduled"])
        self.assertTrue(manifest["scope"]["everyEffectivePlacementScheduled"])


if __name__ == "__main__":
    unittest.main()
