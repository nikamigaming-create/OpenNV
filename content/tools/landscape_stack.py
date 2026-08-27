"""Resolve one corpus-bound LAND and its LTEX/TXST graph through owned plugins."""

from __future__ import annotations

import hashlib
import struct
from dataclasses import dataclass
from pathlib import Path

from landscape_catalog import (
    CONFIGURED_MISSING_BASE_SOURCE,
    Landscape,
    LandscapeIdentity,
    landscape_missing_base_policy,
    parse_landscape,
)
from plugin_records import Record, iter_plugin_records, iter_subrecords, zstring
from plugin_stack import (
    FORM_ID_HEX_CHARACTERS,
    FORM_ID_RADIX,
    FormKey,
    PluginContext,
    build_plugin_stack,
)


DELETED_RECORD_FLAG = 0x00000020
FORM_ID_BYTES = 4
LANDSCAPE_RECORD_TYPE = "LAND"
LANDSCAPE_TEXTURE_RECORD_TYPE = "LTEX"
TEXTURE_SET_RECORD_TYPE = "TXST"


@dataclass(frozen=True)
class ResolvedLandscapeTextureCatalog:
    contracts_by_land_reference: dict[int, dict[str, object]]

    def diffuse_path(self, texture_form_id: int) -> str:
        contract = self._contract(texture_form_id)
        return str(contract["diffusePath"])

    def texture_contract(self, texture_form_id: int) -> dict[str, object]:
        return dict(self._contract(texture_form_id))

    def contracts(self) -> list[dict[str, object]]:
        return [
            dict(self.contracts_by_land_reference[form_id])
            for form_id in sorted(self.contracts_by_land_reference)
        ]

    def _contract(self, texture_form_id: int) -> dict[str, object]:
        contract = self.contracts_by_land_reference.get(texture_form_id)
        if contract is None:
            raise ValueError(
                f"LAND references unresolved LTEX {texture_form_id:08x}"
            )
        return contract


@dataclass(frozen=True)
class OwnedLandscapeSource:
    landscape: Landscape
    identity: LandscapeIdentity
    textures: ResolvedLandscapeTextureCatalog


def verified_plugin_contexts(
    data_root: Path,
    corpus_manifest: dict[str, object],
) -> tuple[PluginContext, ...]:
    inputs = sorted(
        corpus_manifest["inputs"],
        key=lambda row: int(row["loadOrderIndex"]),
    )
    contexts = build_plugin_stack(data_root, [str(row["file"]) for row in inputs])
    expected = [
        {
            "file": str(row["file"]),
            "loadOrderIndex": int(row["loadOrderIndex"]),
            "masters": list(row["masters"]),
            "sha256": str(row["sha256"]),
            "bytes": int(row["bytes"]),
        }
        for row in inputs
    ]
    actual = [
        {
            "file": context.name,
            "loadOrderIndex": context.load_order_index,
            "masters": list(context.masters),
            "sha256": context.sha256,
            "bytes": context.bytes,
        }
        for context in contexts
    ]
    if actual != expected:
        raise ValueError("Owned plugin stack differs from the CELL corpus")
    return contexts


def resolve_owned_landscape(
    data_root: Path,
    corpus_manifest: dict[str, object],
    cell: dict[str, object],
    child: dict[str, object],
) -> OwnedLandscapeSource:
    if (
        child.get("recordType") != LANDSCAPE_RECORD_TYPE
        or child.get("childKind") != "landscape"
        or child.get("cell") != {
            "key": cell["formKey"],
            "runtimeFormId": cell["runtimeFormId"],
        }
        or bool(cell.get("interior"))
        or not isinstance(cell.get("coordinates"), list)
        or not isinstance(cell.get("worldspace"), dict)
    ):
        raise ValueError(f"LAND source relationship differs: {child.get('formKey')}")
    contexts = verified_plugin_contexts(data_root, corpus_manifest)
    contexts_by_name = {context.name.casefold(): context for context in contexts}
    source_context = contexts_by_name.get(str(child["sourcePlugin"]).casefold())
    if source_context is None:
        raise ValueError(f"LAND source plugin is absent: {child['sourcePlugin']}")
    raw_form_id = int(str(child["sourceLocalFormId"]), FORM_ID_RADIX)
    matches = [
        record
        for record in iter_plugin_records(
            source_context.path,
            frozenset({LANDSCAPE_RECORD_TYPE}),
        )
        if record.form_id == raw_form_id
    ]
    if len(matches) != 1:
        raise ValueError(
            f"Expected one owned LAND {child['formKey']}, found {len(matches)}"
        )
    record = matches[0]
    form_key = source_context.form_key(record.form_id)
    if form_key is None or form_key.text != child["formKey"]:
        raise ValueError(f"LAND source FormKey differs: {child['formKey']}")
    _verify_source_record(source_context, record, child)
    landscape = parse_landscape(record)
    cell_key = source_context.form_key(landscape.cell_form_id)
    worldspace_key = source_context.form_key(landscape.worldspace_form_id)
    if (
        cell_key is None
        or cell_key.text != cell["formKey"]
        or worldspace_key is None
        or worldspace_key.text != cell["worldspace"]["key"]
    ):
        raise ValueError(f"LAND parent relationship differs: {child['formKey']}")

    raw_texture_ids = sorted(
        {
            layer.texture_form_id
            for layer in (*landscape.base_layers, *landscape.alpha_layers)
        }
    )
    ltex_keys = {
        raw_id: _required_form_key(source_context, raw_id, LANDSCAPE_TEXTURE_RECORD_TYPE)
        for raw_id in raw_texture_ids
    }
    ltex_records = _resolve_effective_records(
        contexts,
        set(ltex_keys.values()),
        LANDSCAPE_TEXTURE_RECORD_TYPE,
    )
    texture_set_keys: dict[FormKey, FormKey] = {}
    for ltex_key, (context, ltex_record) in ltex_records.items():
        values = _values(ltex_record)
        texture_set = _single(values, "TNAM", ltex_record)
        if len(texture_set) != FORM_ID_BYTES:
            raise ValueError(f"LTEX TNAM size differs: {ltex_key.text}")
        texture_set_keys[ltex_key] = _required_form_key(
            context,
            struct.unpack("<I", texture_set)[0],
            TEXTURE_SET_RECORD_TYPE,
        )
    txst_records = _resolve_effective_records(
        contexts,
        set(texture_set_keys.values()),
        TEXTURE_SET_RECORD_TYPE,
    )

    contracts: dict[int, dict[str, object]] = {}
    for raw_ltex_id, ltex_key in ltex_keys.items():
        ltex_context, ltex_record = ltex_records[ltex_key]
        txst_key = texture_set_keys[ltex_key]
        txst_context, txst_record = txst_records[txst_key]
        ltex_values = _values(ltex_record)
        txst_values = _values(txst_record)
        diffuse = _required_path(txst_values, "TX00", txst_record)
        normal = _optional_path(txst_values, "TX01", txst_record)
        contracts[raw_ltex_id] = {
            "landReferenceRawFormId": (
                f"{raw_ltex_id:0{FORM_ID_HEX_CHARACTERS}x}"
            ),
            "ltexFormKey": ltex_key.text,
            "ltexEditorId": _optional_text(ltex_values, "EDID", ltex_record) or "",
            "ltexSource": _source_contract(ltex_context, ltex_record),
            "txstFormKey": txst_key.text,
            "txstEditorId": _optional_text(txst_values, "EDID", txst_record) or "",
            "txstSource": _source_contract(txst_context, txst_record),
            "diffusePath": diffuse,
            "normalPath": normal,
        }
    if any(
        layer.source == CONFIGURED_MISSING_BASE_SOURCE
        for layer in landscape.base_layers
    ):
        policy = landscape_missing_base_policy()
        default_form_id = int(str(policy["ltexRawFormId"]), FORM_ID_RADIX)
        default_contract = contracts.get(default_form_id)
        if (
            default_contract is None
            or str(default_contract["ltexEditorId"]).casefold()
            != str(policy["expectedEditorId"]).casefold()
        ):
            raise ValueError(
                "Configured missing LAND base LTEX identity differs from owned stack"
            )
    return OwnedLandscapeSource(
        landscape,
        LandscapeIdentity(
            form_key.text,
            cell_key.text,
            worldspace_key.text,
            source_context.name,
            str(child["sourceLocalFormId"]),
        ),
        ResolvedLandscapeTextureCatalog(contracts),
    )


def _resolve_effective_records(
    contexts: tuple[PluginContext, ...],
    requested: set[FormKey],
    signature: str,
) -> dict[FormKey, tuple[PluginContext, Record]]:
    resolved: dict[FormKey, tuple[PluginContext, Record]] = {}
    for context in contexts:
        for record in iter_plugin_records(context.path, frozenset({signature})):
            if not context.declares_form_id_namespace(record.form_id):
                continue
            key = context.form_key(record.form_id)
            if key is None or key not in requested:
                continue
            if record.flags & DELETED_RECORD_FLAG:
                resolved.pop(key, None)
            else:
                resolved[key] = (context, record)
    missing = requested - set(resolved)
    if missing:
        raise ValueError(
            f"Unresolved {signature} records: "
            + ",".join(key.text for key in sorted(missing))
        )
    return resolved


def _required_form_key(
    context: PluginContext,
    raw_form_id: int,
    description: str,
) -> FormKey:
    key = context.form_key(raw_form_id)
    if key is None:
        raise ValueError(f"{description} contains an empty FormID")
    return key


def _values(record: Record) -> dict[str, list[bytes]]:
    result: dict[str, list[bytes]] = {}
    for subrecord in iter_subrecords(record):
        result.setdefault(subrecord.signature, []).append(subrecord.data)
    return result


def _single(
    values: dict[str, list[bytes]],
    signature: str,
    record: Record,
) -> bytes:
    matches = values.get(signature, [])
    if len(matches) != 1:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} must contain one {signature}"
        )
    return matches[0]


def _required_path(
    values: dict[str, list[bytes]],
    signature: str,
    record: Record,
) -> str:
    value = _normalized_path(_single(values, signature, record))
    if not value:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} has an empty {signature}"
        )
    return value


def _optional_path(
    values: dict[str, list[bytes]],
    signature: str,
    record: Record,
) -> str | None:
    matches = values.get(signature, [])
    if len(matches) > 1:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} repeats {signature}"
        )
    return _normalized_path(matches[0]) if matches else None


def _optional_text(
    values: dict[str, list[bytes]],
    signature: str,
    record: Record,
) -> str | None:
    matches = values.get(signature, [])
    if len(matches) > 1:
        raise ValueError(
            f"{record.signature} {record.form_id:08x} repeats {signature}"
        )
    return zstring(matches[0]) if matches else None


def _normalized_path(data: bytes) -> str:
    return zstring(data).replace("/", "\\").lower()


def _source_contract(
    context: PluginContext,
    record: Record,
) -> dict[str, object]:
    return {
        "sourcePlugin": context.name,
        "sourceLocalFormId": (
            f"{record.form_id:0{FORM_ID_HEX_CHARACTERS}x}"
        ),
        "recordFlags": f"{record.flags:0{FORM_ID_HEX_CHARACTERS}x}",
        "recordDataSha256": hashlib.sha256(record.data).hexdigest(),
        "compressionChecksumValid": record.compression_checksum_valid,
        "subrecordSignatureCounts": {
            signature: len(payloads)
            for signature, payloads in sorted(_values(record).items())
        },
    }


def _verify_source_record(
    context: PluginContext,
    record: Record,
    expected: dict[str, object],
) -> None:
    actual = _source_contract(context, record)
    for field in (
        "sourcePlugin",
        "sourceLocalFormId",
        "recordFlags",
        "recordDataSha256",
        "compressionChecksumValid",
        "subrecordSignatureCounts",
    ):
        if actual[field] != expected[field]:
            raise ValueError(
                f"Owned {record.signature} source {field} differs: "
                f"{expected['formKey']}"
            )
