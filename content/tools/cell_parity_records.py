"""Map official CELL records and children into canonical load-order rows."""

from __future__ import annotations

import hashlib
import struct
from collections import Counter
from dataclasses import asdict, dataclass

from cell_catalog import (
    INITIALLY_DISABLED_RECORD_FLAG,
    REFERENCE_TRANSFORM_BYTES,
    TELEPORT_DESTINATION_BYTES,
    TELEPORT_DESTINATION_TRANSFORM_OFFSET,
    cell_parent_form_id,
    normalize_model_path,
    parse_cell_lighting,
    parse_form_id,
    parse_reference_scale,
    parse_transform,
    subrecords_by_signature,
    worldspace_parent_form_id,
)
from plugin_records import Record, iter_plugin_records, zstring
from plugin_stack import (
    FORM_ID_HEX_CHARACTERS,
    FormKey,
    PluginContext,
    form_link,
    runtime_form_id,
)


CELL_RECORD_TYPE = "CELL"
WORLDSPACE_RECORD_TYPE = "WRLD"
DOOR_RECORD_TYPE = "DOOR"
LANDSCAPE_RECORD_TYPE = "LAND"
NAVIGATION_RECORD_TYPE = "NAVM"
PLACED_REFERENCE_RECORD_TYPES = frozenset({"REFR", "ACHR", "ACRE", "PGRE"})
DELETED_RECORD_FLAG = 0x00000020
CELL_INTERIOR_FLAG = 0x01
CELL_FLAGS_BYTES = 1
CELL_COORDINATE_BYTES = 8
FORM_ID_BYTES = 4
MODEL_PATH_SUBRECORD = "MODL"


@dataclass
class CellMergeState:
    cells: dict[FormKey, dict[str, object]]
    children: dict[FormKey, dict[str, object]]
    raw_counts: dict[str, dict[str, object]]
    override_counts: dict[str, int]
    deletion_counts: dict[str, int]
    invalid_compression_checksums: dict[str, int]
    source_anomalies: list[dict[str, object]]


def source_anomaly_row(
    context: PluginContext,
    record: Record,
    parent_cell_raw_form_id: int | None,
    classification: str,
) -> dict[str, object]:
    return {
        "sourcePlugin": context.name,
        "recordType": record.signature,
        "rawFormId": f"{record.form_id:0{FORM_ID_HEX_CHARACTERS}x}",
        "recordFlags": f"{record.flags:0{FORM_ID_HEX_CHARACTERS}x}",
        "parentCellRawFormId": (
            f"{parent_cell_raw_form_id:0{FORM_ID_HEX_CHARACTERS}x}"
            if parent_cell_raw_form_id is not None
            else None
        ),
        "recordDataSha256": hashlib.sha256(record.data).hexdigest(),
        "classification": classification,
    }


def record_source_row(
    context: PluginContext,
    record: Record,
    load_order_indices: dict[str, int],
) -> dict[str, object]:
    key = context.form_key(record.form_id)
    assert key is not None
    return {
        "formKey": key.text,
        "runtimeFormId": runtime_form_id(key, load_order_indices),
        "recordType": record.signature,
        "sourcePlugin": context.name,
        "sourceLocalFormId": f"{record.form_id:0{FORM_ID_HEX_CHARACTERS}x}",
        "recordFlags": f"{record.flags:0{FORM_ID_HEX_CHARACTERS}x}",
        "recordDataSha256": hashlib.sha256(record.data).hexdigest(),
        "compressionChecksumValid": record.compression_checksum_valid,
    }


def subrecord_counts(values: dict[str, list[bytes]]) -> dict[str, int]:
    return {signature: len(payloads) for signature, payloads in sorted(values.items())}


def _single_payload(
    values: dict[str, list[bytes]],
    signature: str,
    gaps: list[str],
) -> bytes | None:
    matches = values.get(signature, [])
    if len(matches) == 1:
        return matches[0]
    if matches:
        gaps.append(f"multiple-{signature.casefold()}-subrecords")
    return None


def cell_row(
    context: PluginContext,
    record: Record,
    load_order_indices: dict[str, int],
) -> dict[str, object]:
    values = subrecords_by_signature(record)
    gaps: list[str] = []
    data = _single_payload(values, "DATA", gaps)
    coordinates_data = _single_payload(values, "XCLC", gaps)
    lighting_data = _single_payload(values, "XCLL", gaps)
    if data is None:
        gaps.append("missing-data-cell-flags")
    elif len(data) != CELL_FLAGS_BYTES:
        gaps.append("unsupported-data-cell-flags")
        data = None
    if coordinates_data is not None and len(coordinates_data) < CELL_COORDINATE_BYTES:
        gaps.append("short-xclc")
        coordinates_data = None
    coordinates = (
        list(struct.unpack_from("<ii", coordinates_data))
        if coordinates_data is not None
        else None
    )
    lighting = None
    if lighting_data is not None:
        try:
            lighting = asdict(parse_cell_lighting(lighting_data, record))
        except ValueError:
            gaps.append("unsupported-xcll-layout")
    worldspace_raw = worldspace_parent_form_id(record)
    row = record_source_row(context, record, load_order_indices)
    row.update(
        {
            "editorId": zstring(values["EDID"][0]) if len(values.get("EDID", [])) == 1 else "",
            "cellFlags": data[0] if data else 0,
            "interior": bool(data and data[0] & CELL_INTERIOR_FLAG),
            "coordinates": coordinates,
            "worldspace": form_link(context, worldspace_raw, load_order_indices),
            "lighting": lighting,
            "subrecordSignatureCounts": subrecord_counts(values),
            "parseGaps": sorted(gaps),
        }
    )
    return row


def child_kind(record: Record, values: dict[str, list[bytes]]) -> str:
    if record.signature in PLACED_REFERENCE_RECORD_TYPES or "NAME" in values:
        return "placed-reference"
    if record.signature == LANDSCAPE_RECORD_TYPE:
        return "landscape"
    if record.signature == NAVIGATION_RECORD_TYPE:
        return "navigation"
    return "unclassified-cell-child"


def child_row(
    context: PluginContext,
    record: Record,
    load_order_indices: dict[str, int],
) -> dict[str, object]:
    parent_raw = cell_parent_form_id(record)
    if parent_raw is None:
        raise ValueError(
            f"Cell child {record.signature} {record.form_id:08x} has no CELL group"
        )
    values = subrecords_by_signature(record)
    kind = child_kind(record, values)
    gaps: list[str] = []
    base = None
    transform = None
    scale = None
    if kind == "placed-reference":
        name_data = _single_payload(values, "NAME", gaps)
        if name_data is None:
            gaps.append("missing-name")
        elif len(name_data) < FORM_ID_BYTES:
            gaps.append("short-name")
        else:
            base = form_link(
                context,
                parse_form_id(name_data, record, "NAME"),
                load_order_indices,
            )
        transform_data = _single_payload(values, "DATA", gaps)
        if transform_data is None:
            gaps.append("missing-data-transform")
        elif len(transform_data) != REFERENCE_TRANSFORM_BYTES:
            gaps.append("unsupported-data-transform")
        else:
            transform = asdict(parse_transform(transform_data, record))
        try:
            scale = parse_reference_scale(values, record)
        except ValueError:
            gaps.append("unsupported-reference-scale")

    teleport = None
    teleport_data = _single_payload(values, "XTEL", gaps)
    if teleport_data is not None:
        if len(teleport_data) < TELEPORT_DESTINATION_BYTES:
            gaps.append("short-xtel")
        else:
            teleport = {
                "destinationReference": form_link(
                    context,
                    parse_form_id(teleport_data, record, "XTEL"),
                    load_order_indices,
                ),
                "destinationTransformGameUnits": asdict(
                    parse_transform(
                        teleport_data[
                            TELEPORT_DESTINATION_TRANSFORM_OFFSET:TELEPORT_DESTINATION_BYTES
                        ],
                        record,
                    )
                ),
            }

    enable_parent = None
    enable_parent_data = _single_payload(values, "XESP", gaps)
    if enable_parent_data is not None:
        if len(enable_parent_data) < FORM_ID_BYTES:
            gaps.append("short-xesp")
        else:
            enable_parent = form_link(
                context,
                parse_form_id(enable_parent_data, record, "XESP"),
                load_order_indices,
            )

    row = record_source_row(context, record, load_order_indices)
    row.update(
        {
            "cell": form_link(context, parent_raw, load_order_indices),
            "childKind": kind,
            "baseOrActor": base,
            "initiallyDisabled": bool(record.flags & INITIALLY_DISABLED_RECORD_FLAG),
            "transformGameUnits": transform,
            "scale": scale,
            "teleport": teleport,
            "enableParent": enable_parent,
            "subrecordSignatureCounts": subrecord_counts(values),
            "parseGaps": sorted(set(gaps)),
        }
    )
    return row


def _remove_effective_record(state: CellMergeState, key: FormKey) -> None:
    state.cells.pop(key, None)
    state.children.pop(key, None)


def _merge_effective_record(
    state: CellMergeState,
    key: FormKey,
    row: dict[str, object],
    target: dict[FormKey, dict[str, object]],
) -> None:
    other = state.children if target is state.cells else state.cells
    if key in other:
        raise ValueError(f"Form {key.text} changes between CELL and child ownership")
    previous = target.get(key)
    if previous is not None:
        previous_type = str(previous["recordType"])
        current_type = str(row["recordType"])
        if previous_type != current_type:
            raise ValueError(
                f"Form {key.text} changes CELL-graph type from {previous_type} to {current_type}"
            )
        state.override_counts[current_type] = state.override_counts.get(current_type, 0) + 1
    target[key] = row


def apply_cell_plugin(
    state: CellMergeState,
    context: PluginContext,
    load_order_indices: dict[str, int],
) -> None:
    cell_count = 0
    child_counts: Counter[str] = Counter()
    for record in iter_plugin_records(context.path):
        parent = cell_parent_form_id(record)
        if record.signature != CELL_RECORD_TYPE and parent is None:
            continue
        if record.signature == CELL_RECORD_TYPE:
            cell_count += 1
        else:
            child_counts[record.signature] += 1
        if record.compression_checksum_valid is False:
            state.invalid_compression_checksums[record.signature] = (
                state.invalid_compression_checksums.get(record.signature, 0) + 1
            )
            state.source_anomalies.append(
                source_anomaly_row(
                    context,
                    record,
                    parent,
                    "invalid-compression-checksum",
                )
            )
        if not context.declares_form_id_namespace(record.form_id):
            state.source_anomalies.append(
                source_anomaly_row(
                    context,
                    record,
                    parent,
                    "undeclared-form-namespace",
                )
            )
            continue
        key = context.form_key(record.form_id)
        assert key is not None
        if record.flags & DELETED_RECORD_FLAG:
            _remove_effective_record(state, key)
            state.deletion_counts[record.signature] = (
                state.deletion_counts.get(record.signature, 0) + 1
            )
            continue
        if record.signature == CELL_RECORD_TYPE:
            _merge_effective_record(
                state,
                key,
                cell_row(context, record, load_order_indices),
                state.cells,
            )
        else:
            _merge_effective_record(
                state,
                key,
                child_row(context, record, load_order_indices),
                state.children,
            )
    state.raw_counts[context.name] = {
        "cells": cell_count,
        "cellChildren": dict(sorted(child_counts.items())),
    }


def build_cell_merge_state(
    contexts: tuple[PluginContext, ...],
    load_order_indices: dict[str, int],
) -> CellMergeState:
    state = CellMergeState({}, {}, {}, {}, {}, {}, [])
    for context in contexts:
        apply_cell_plugin(state, context, load_order_indices)
    return state


def linked_record_row(
    context: PluginContext,
    record: Record,
    load_order_indices: dict[str, int],
    requirements: Counter[str],
) -> dict[str, object]:
    values = subrecords_by_signature(record)
    models = values.get(MODEL_PATH_SUBRECORD, [])
    row = record_source_row(context, record, load_order_indices)
    row.update(
        {
            "editorId": zstring(values["EDID"][0]) if len(values.get("EDID", [])) == 1 else "",
            "modelPaths": [normalize_model_path(payload) for payload in models],
            "subrecordSignatureCounts": subrecord_counts(values),
            "requiredBy": dict(sorted(requirements.items())),
        }
    )
    return row


def resolve_linked_records(
    contexts: tuple[PluginContext, ...],
    load_order_indices: dict[str, int],
    requirements: dict[FormKey, Counter[str]],
) -> dict[FormKey, dict[str, object]]:
    resolved: dict[FormKey, dict[str, object]] = {}
    record_types: dict[FormKey, str] = {}
    for context in contexts:
        for record in iter_plugin_records(context.path):
            if record.form_id == 0:
                continue
            if not context.declares_form_id_namespace(record.form_id):
                continue
            key = context.form_key(record.form_id)
            assert key is not None
            if key not in requirements:
                continue
            if record.flags & DELETED_RECORD_FLAG:
                resolved.pop(key, None)
                record_types.pop(key, None)
                continue
            previous_type = record_types.get(key)
            if previous_type is not None and previous_type != record.signature:
                raise ValueError(
                    f"Linked form {key.text} changes type from {previous_type} to {record.signature}"
                )
            resolved[key] = linked_record_row(
                context,
                record,
                load_order_indices,
                requirements[key],
            )
            record_types[key] = record.signature
    return resolved
