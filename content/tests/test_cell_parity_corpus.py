from __future__ import annotations

import hashlib
import json
import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_parity_corpus import build_corpus as build_actor_corpus  # noqa: E402
from cell_parity_corpus import build_corpus  # noqa: E402
from plugin_records import COMPRESSED_RECORD_FLAG, RECORD_HEADER_BYTES  # noqa: E402
from validate_cell_parity_corpus import validate_corpus  # noqa: E402


DELETED_RECORD_FLAG = 0x00000020


def subrecord(signature: str, data: bytes) -> bytes:
    return signature.encode("ascii") + struct.pack("<H", len(data)) + data


def record(signature: str, form_id: int, data: bytes, flags: int = 0) -> bytes:
    stored = data
    if flags & COMPRESSED_RECORD_FLAG:
        stored = struct.pack("<I", len(data)) + zlib.compress(data)
    return struct.pack(
        "<4s4I2H",
        signature.encode("ascii"),
        len(stored),
        flags,
        form_id,
        0,
        0,
        0,
    ) + stored


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


def base(signature: str, form_id: int, editor_id: str, model: str | None = None) -> bytes:
    data = subrecord("EDID", editor_id.encode("ascii") + b"\0")
    if model is not None:
        data += subrecord("MODL", model.encode("ascii") + b"\0")
    return record(signature, form_id, data)


def actor(form_id: int) -> bytes:
    return record(
        "NPC_",
        form_id,
        subrecord("EDID", b"SyntheticActor\0")
        + subrecord("FULL", b"Synthetic Actor\0")
        + subrecord("ACBS", struct.pack("<6I", 0, 0, 0, 0, 0, 0))
        + subrecord("EAMT", struct.pack("<H", 0)),
    )


def placed(
    signature: str,
    form_id: int,
    base_form_id: int,
    *,
    destination: int | None = None,
    radius: float | None = None,
) -> bytes:
    data = subrecord("NAME", struct.pack("<I", base_form_id))
    if destination is not None:
        data += subrecord(
            "XTEL",
            struct.pack("<I6f", destination, 1.0, 2.0, 3.0, 0.0, 0.0, 0.0),
        )
    if radius is not None:
        data += subrecord("XRDS", struct.pack("<f", radius))
    data += subrecord("DATA", struct.pack("<6f", 10.0, 20.0, 30.0, 0.0, 0.0, 0.0))
    return record(signature, form_id, data)


def cell(form_id: int, editor_id: str, *, exterior: bool = False) -> bytes:
    data = subrecord("EDID", editor_id.encode("ascii") + b"\0")
    data += subrecord("DATA", b"\x00" if exterior else b"\x01")
    if exterior:
        data += subrecord("XCLC", struct.pack("<ii", 4, -2))
    return record("CELL", form_id, data)


def cell_children(cell_form_id: int, *children: bytes) -> bytes:
    label = struct.pack("<I", cell_form_id)
    return group(label, 6, group(label, 9, b"".join(children)))


def base_plugin(*, missing_base: bool = False) -> bytes:
    door_base = 0x99 if missing_base else 0x20
    interior = cell(0x100, "SyntheticInterior") + cell_children(
        0x100,
        placed("REFR", 0x200, door_base, destination=0x201),
        placed("ACHR", 0x203, 0x22),
        record("NAVM", 0x301, subrecord("NVER", struct.pack("<I", 12))),
    )
    exterior = cell(0x101, "SyntheticExterior", exterior=True) + cell_children(
        0x101,
        placed("REFR", 0x201, door_base, destination=0x200),
        placed("REFR", 0x202, 0x21),
        record("LAND", 0x300, subrecord("DATA", b"land")),
    )
    return (
        header()
        + group(b"WRLD", 0, base("WRLD", 0x10, "SyntheticWorld"))
        + group(b"DOOR", 0, base("DOOR", 0x20, "SyntheticDoor", "doors/test.nif"))
        + group(b"STAT", 0, base("STAT", 0x21, "DeletedStatic", "static/old.nif"))
        + group(b"NPC_", 0, actor(0x22))
        + group(b"CELL", 0, interior)
        + group(struct.pack("<I", 0x10), 1, exterior)
    )


def dlc_plugin() -> bytes:
    deleted = record("REFR", 0x00000202, b"", DELETED_RECORD_FLAG)
    overridden = placed("REFR", 0x00000200, 0x00000020, destination=0x00000201)
    added = placed("REFR", 0x01000210, 0x01000030)
    return (
        header("FalloutNV.esm")
        + group(b"STAT", 0, base("STAT", 0x01000030, "DlcStatic", "static/new.nif"))
        + group(
            b"CELL",
            0,
            cell(0x00000100, "SyntheticInteriorOverride")
            + cell_children(0x00000100, overridden, deleted, added),
        )
    )


def implicit_marker_plugin() -> bytes:
    contents = cell(0x100, "SyntheticImplicitMarkerCell") + cell_children(
        0x100,
        placed("REFR", 0x200, 0x17),
    )
    return header() + group(b"CELL", 0, contents)


def invalid_namespace_plugin() -> tuple[bytes, str]:
    child = placed("REFR", 0x01000200, 0x01000020)
    contents = cell(0x100, "SyntheticInvalidNamespaceCell") + cell_children(
        0x100,
        child,
    )
    record_data = child[RECORD_HEADER_BYTES:]
    return header() + group(b"CELL", 0, contents), hashlib.sha256(record_data).hexdigest()


def invalid_checksum_plugin() -> tuple[bytes, str]:
    record_data = subrecord("DATA", b"land")
    landscape = bytearray(
        record("LAND", 0x300, record_data, COMPRESSED_RECORD_FLAG)
    )
    landscape[-1] ^= 0x01
    contents = cell(0x100, "SyntheticBadChecksumCell") + cell_children(
        0x100,
        bytes(landscape),
    )
    return header() + group(b"CELL", 0, contents), hashlib.sha256(record_data).hexdigest()


def missing_cell_flags_plugin() -> bytes:
    missing_flags = record(
        "CELL",
        0x100,
        subrecord("EDID", b"SyntheticMissingCellFlags\0"),
    )
    return header() + group(b"CELL", 0, missing_flags)


def light_plugin() -> bytes:
    light_data = struct.pack(
        "<iI4BIffIf",
        -1,
        256,
        100,
        80,
        40,
        0,
        0,
        1.0,
        90.0,
        0,
        0.0,
    )
    light = record(
        "LIGH",
        0x20,
        subrecord("EDID", b"SyntheticLight\0")
        + subrecord("DATA", light_data)
        + subrecord("FNAM", struct.pack("<f", 1.5)),
    )
    contents = cell(0x100, "SyntheticLightCell") + cell_children(
        0x100,
        placed("REFR", 0x200, 0x20, radius=-96.0),
    )
    return header() + group(b"LIGH", 0, light) + group(b"CELL", 0, contents)


def cell_recipe() -> dict[str, object]:
    return {
        "schema": "opennv-cell-parity-corpus-recipe/v1",
        "id": "synthetic-cells",
        "plugins": [{"file": "FalloutNV.esm"}, {"file": "Dlc.esm"}],
        "review": {
            "commonGates": ["record-graph", "runtime"],
            "landscapeGates": ["landscape"],
            "navigationGates": ["navigation"],
            "portalGates": ["portal"],
            "interiorShots": ["entry"],
            "exteriorShots": ["center"],
        },
    }


def actor_recipe() -> dict[str, object]:
    return {
        "schema": "opennv-actor-parity-corpus-recipe/v1",
        "id": "synthetic-actors",
        "plugins": [{"file": "FalloutNV.esm"}, {"file": "Dlc.esm"}],
        "capture": {
            "humanoidAppearanceShots": ["portrait"],
            "creatureAppearanceShots": ["full-body"],
            "placementShots": ["context"],
        },
    }


def read_jsonl(path: Path) -> list[dict[str, object]]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]


class CellParityCorpusTest(unittest.TestCase):
    def test_light_base_and_reference_radius_are_source_bound(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            (data_root / "FalloutNV.esm").write_bytes(light_plugin())
            output_root = root / "cells"
            recipe = cell_recipe()
            recipe["plugins"] = [{"file": "FalloutNV.esm"}]
            build_corpus(data_root, output_root, recipe)
            validate_corpus(output_root)
            children = read_jsonl(output_root / "cell-children.jsonl")
            linked = read_jsonl(output_root / "linked-records.jsonl")

        self.assertEqual(children[0]["radiusGameUnits"], -96.0)
        self.assertEqual(
            linked[0]["light"],
            {
                "radiusGameUnits": 256,
                "colorRgb": [100, 80, 40],
                "lightFlags": 0,
                "falloff": 1.0,
                "fieldOfViewDegrees": 90.0,
                "intensity": 1.5,
            },
        )

    def test_effective_cells_children_portals_and_actor_join_are_exact(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            (data_root / "FalloutNV.esm").write_bytes(base_plugin())
            (data_root / "Dlc.esm").write_bytes(dlc_plugin())
            cell_root = root / "cells"
            actor_root = root / "actors"

            manifest = build_corpus(data_root, cell_root, cell_recipe())
            build_actor_corpus(data_root, actor_root, actor_recipe())
            counts = validate_corpus(cell_root, actor_corpus_root=actor_root)
            cells = read_jsonl(cell_root / "cells.jsonl")
            children = read_jsonl(cell_root / "cell-children.jsonl")
            linked = read_jsonl(cell_root / "linked-records.jsonl")
            portals = read_jsonl(cell_root / "portal-edges.jsonl")
            reviews = read_jsonl(cell_root / "cell-review.jsonl")
            with self.assertRaises(FileExistsError):
                build_corpus(data_root, cell_root, cell_recipe())

        self.assertEqual(manifest["status"], "inventory-complete-implementation-review-pending")
        self.assertEqual(counts["cells"], 2)
        self.assertEqual(counts["cellChildren"], 6)
        self.assertEqual(counts["actorPlacementJoin"], 1)
        self.assertEqual(manifest["effectiveCounts"]["portalEdges"], 2)
        self.assertEqual(manifest["loadOrderMerge"]["deletionsApplied"], {"REFR": 1})
        self.assertEqual(
            next(row for row in cells if row["formKey"] == "FalloutNV.esm:000100")["editorId"],
            "SyntheticInteriorOverride",
        )
        self.assertNotIn("FalloutNV.esm:000202", {row["formKey"] for row in children})
        self.assertIn("Dlc.esm:000210", {row["formKey"] for row in children})
        self.assertEqual(
            {row["recordType"] for row in linked},
            {"DOOR", "NPC_", "STAT", "WRLD"},
        )
        self.assertTrue(all(row["reciprocalStatus"] == "reciprocal" for row in portals))
        self.assertEqual({row["cellFormKey"] for row in reviews}, {row["formKey"] for row in cells})
        self.assertTrue(all(not row["lookedAt"] for row in reviews))

    def test_missing_base_remains_a_blocking_relationship_gap(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            (data_root / "FalloutNV.esm").write_bytes(base_plugin(missing_base=True))
            output_root = root / "cells"
            recipe = cell_recipe()
            recipe["plugins"] = [{"file": "FalloutNV.esm"}]
            manifest = build_corpus(data_root, output_root, recipe)
            gaps = read_jsonl(output_root / "relationship-gaps.jsonl")
            with self.assertRaisesRegex(ValueError, "relationship gaps"):
                validate_corpus(output_root)

        self.assertEqual(manifest["status"], "inventory-built-with-relationship-gaps")
        self.assertIn("cell-child-base-missing", {row["reason"] for row in gaps})

    def test_engine_implicit_base_is_recipe_owned_and_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            (data_root / "FalloutNV.esm").write_bytes(implicit_marker_plugin())
            output_root = root / "cells"
            recipe = cell_recipe()
            recipe["plugins"] = [{"file": "FalloutNV.esm"}]
            recipe["engineImplicitBases"] = [
                {
                    "formKey": "FalloutNV.esm:000017",
                    "recordType": "engine-implicit-marker",
                    "kind": "plane-marker",
                    "requiredReferenceRecordTypes": ["REFR"],
                    "requiredReferenceSubrecords": ["DATA", "NAME"],
                    "runtimeSemanticsStatus": "pending",
                }
            ]
            manifest = build_corpus(data_root, output_root, recipe)
            counts = validate_corpus(output_root)
            implicit = read_jsonl(output_root / "engine-implicit-bases.jsonl")

        self.assertEqual(counts["engineImplicitBases"], 1)
        self.assertEqual(implicit[0]["kind"], "plane-marker")
        self.assertEqual(implicit[0]["runtimeSemanticsStatus"], "pending")
        self.assertEqual(manifest["effectiveCounts"]["relationshipGaps"], 0)

    def test_exact_invalid_source_record_is_accounted_without_guessing_identity(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            plugin, record_data_sha256 = invalid_namespace_plugin()
            (data_root / "FalloutNV.esm").write_bytes(plugin)
            output_root = root / "cells"
            recipe = cell_recipe()
            recipe["plugins"] = [{"file": "FalloutNV.esm"}]
            recipe["sourceAnomalies"] = [
                {
                    "sourcePlugin": "FalloutNV.esm",
                    "rawFormId": "01000200",
                    "recordType": "REFR",
                    "recordFlags": "00000000",
                    "parentCellRawFormId": "00000100",
                    "recordDataSha256": record_data_sha256,
                    "classification": "undeclared-form-namespace",
                    "runtimeSemanticsStatus": "pending",
                }
            ]
            manifest = build_corpus(data_root, output_root, recipe)
            counts = validate_corpus(output_root)
            anomalies = read_jsonl(output_root / "source-anomalies.jsonl")

        self.assertEqual(counts["sourceAnomalies"], 1)
        self.assertEqual(anomalies[0]["accountingStatus"], "exact-source-anomaly")
        self.assertEqual(manifest["effectiveCounts"]["cellChildren"], 0)
        self.assertEqual(
            manifest["status"],
            "inventory-complete-source-anomalies-accounted-implementation-review-pending",
        )

    def test_bad_compression_checksum_requires_an_exact_source_anomaly(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            plugin, record_data_sha256 = invalid_checksum_plugin()
            (data_root / "FalloutNV.esm").write_bytes(plugin)
            output_root = root / "cells"
            recipe = cell_recipe()
            recipe["plugins"] = [{"file": "FalloutNV.esm"}]
            recipe["sourceAnomalies"] = [
                {
                    "sourcePlugin": "FalloutNV.esm",
                    "rawFormId": "00000300",
                    "recordType": "LAND",
                    "recordFlags": "00040000",
                    "parentCellRawFormId": "00000100",
                    "recordDataSha256": record_data_sha256,
                    "classification": "invalid-compression-checksum",
                    "runtimeSemanticsStatus": "pending",
                }
            ]
            manifest = build_corpus(data_root, output_root, recipe)
            counts = validate_corpus(output_root)
            anomalies = read_jsonl(output_root / "source-anomalies.jsonl")

        self.assertEqual(counts["sourceAnomalies"], 1)
        self.assertEqual(anomalies[0]["classification"], "invalid-compression-checksum")
        self.assertEqual(manifest["effectiveCounts"]["invalidCompressionChecksums"], 1)

    def test_missing_cell_flags_remain_a_blocking_parse_gap(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            data_root = root / "Data"
            data_root.mkdir()
            (data_root / "FalloutNV.esm").write_bytes(missing_cell_flags_plugin())
            output_root = root / "cells"
            recipe = cell_recipe()
            recipe["plugins"] = [{"file": "FalloutNV.esm"}]
            manifest = build_corpus(data_root, output_root, recipe)
            gaps = read_jsonl(output_root / "relationship-gaps.jsonl")

        self.assertEqual(manifest["status"], "inventory-built-with-relationship-gaps")
        self.assertIn(
            "missing-data-cell-flags",
            {row["detail"] for row in gaps if row["reason"] == "cell-parse-gap"},
        )


if __name__ == "__main__":
    unittest.main()
