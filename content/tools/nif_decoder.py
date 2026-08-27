"""Versioned, evidence-backed decoding for owned Fallout NIF payloads."""

from __future__ import annotations

import hashlib
import io
import json
import re
import struct
import time
from dataclasses import dataclass
from functools import cache
from pathlib import Path

if not hasattr(time, "clock"):
    time.clock = time.perf_counter

from pyffi.formats.nif import NifFormat  # type: ignore  # noqa: E402

from runtime_configuration import configured_recipe_path


DECODER_CONTRACT_SCHEMA = "opennv-nif-decoder-contract/v1"
DECODER_EVIDENCE_SCHEMA = "opennv-nif-decoder-evidence/v1"
UV_NORMALIZATION_SCHEMA = "opennv-nif-uv-normalization/v1"
SHA256_HEX_CHARACTERS = 64
HEX_RADIX = 16
UINT16_BYTES = struct.calcsize("<H")
UINT32_BYTES = struct.calcsize("<I")
BYTE_MAXIMUM = (1 << 8) - 1


@dataclass(frozen=True)
class NifBlockDirectoryEntry:
    index: int
    type_name: str
    offset: int
    size: int


@dataclass(frozen=True)
class NifDecoderContract:
    schema: str
    contract_id: str
    status: str
    canonical_sha256: str
    file_name: str
    version: int
    endian: int
    user_version: int
    user_version_2: int
    export_info_string_count: int
    footer_bytes: int
    geometry_block_types: frozenset[str]
    vertex_count_offset_bytes: int
    has_vertices_offset_bytes: int
    uv_count_prefix_bytes: int
    vertex_stride_bytes: int
    minimum_uv_sets: int
    maximum_uv_sets: int
    recovery_method: str
    source_mutation_policy: str


@dataclass(frozen=True)
class NifUvNormalization:
    block_index: int
    block_type: str
    source_byte_offset: int
    source_stored_value: int
    decoded_uv_sets: int
    block_bytes: int
    candidates_tested: tuple[int, ...]

    def to_document(self) -> dict[str, object]:
        return {
            "schema": UV_NORMALIZATION_SCHEMA,
            "status": "normalized-in-memory-for-decoding",
            "blockIndex": self.block_index,
            "blockType": self.block_type,
            "sourceByteOffset": self.source_byte_offset,
            "sourceStoredValue": self.source_stored_value,
            "decodedUvSets": self.decoded_uv_sets,
            "blockBytes": self.block_bytes,
            "candidatesTested": list(self.candidates_tested),
        }


@dataclass(frozen=True)
class NifDecodeResult:
    document: object
    normalizations: tuple[NifUvNormalization, ...]
    contract: NifDecoderContract
    format_matched: bool

    def evidence(self) -> dict[str, object]:
        return {
            "schema": DECODER_EVIDENCE_SCHEMA,
            "status": (
                "owned-format-normalized-in-memory"
                if self.normalizations
                else (
                    "owned-format-read-without-normalization"
                    if self.format_matched
                    else "noncontract-format-read-with-pyffi"
                )
            ),
            "contract": {
                "schema": self.contract.schema,
                "id": self.contract.contract_id,
                "status": self.contract.status,
                "file": self.contract.file_name,
                "canonicalSha256": self.contract.canonical_sha256,
            },
            "formatMatched": self.format_matched,
            "recoveryMethod": self.contract.recovery_method,
            "sourceMutationPolicy": self.contract.source_mutation_policy,
            "sourceBytesModified": False,
            "normalizations": [row.to_document() for row in self.normalizations],
        }


def _canonical_sha256(document: object) -> str:
    payload = json.dumps(
        document,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def _integer(parent: dict[str, object], name: str, *, minimum: int = 0) -> int:
    value = parent.get(name)
    if not isinstance(value, int) or isinstance(value, bool) or value < minimum:
        raise ValueError(f"NIF decoder contract integer is invalid: {name}")
    return value


def _object(parent: dict[str, object], name: str) -> dict[str, object]:
    value = parent.get(name)
    if not isinstance(value, dict):
        raise ValueError(f"NIF decoder contract object is missing: {name}")
    return value


@cache
def load_nif_decoder_contract() -> NifDecoderContract:
    path = configured_recipe_path("nifDecoder")
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError("NIF decoder contract must be an object")
    if set(document) != {"schema", "id", "status", "provenance", "format", "geometryData"}:
        raise ValueError("NIF decoder contract fields are invalid")
    if document.get("schema") != DECODER_CONTRACT_SCHEMA:
        raise ValueError(f"Unexpected NIF decoder contract schema: {document.get('schema')}")
    if any(
        not isinstance(document.get(field), str) or not str(document[field]).strip()
        for field in ("id", "status")
    ):
        raise ValueError("NIF decoder contract identity is invalid")
    provenance = _object(document, "provenance")
    if set(provenance) != {"classification", "status", "source", "evidence"} or any(
        not isinstance(provenance.get(field), str) or not str(provenance[field]).strip()
        for field in provenance
    ):
        raise ValueError("NIF decoder contract provenance is invalid")
    format_contract = _object(document, "format")
    geometry = _object(document, "geometryData")
    if set(format_contract) != {
        "versionHex",
        "endian",
        "userVersion",
        "userVersion2",
        "exportInfoStringCount",
        "footerBytes",
    }:
        raise ValueError("NIF decoder format fields are invalid")
    if set(geometry) != {
        "blockTypes",
        "vertexCountOffsetBytes",
        "hasVerticesOffsetBytes",
        "uvCountPrefixBytes",
        "vertexStrideBytes",
        "minimumUvSets",
        "maximumUvSets",
        "recoveryMethod",
        "sourceMutationPolicy",
    }:
        raise ValueError("NIF decoder geometryData fields are invalid")
    version_hex = format_contract.get("versionHex")
    if not isinstance(version_hex, str) or re.fullmatch(r"0x[0-9A-Fa-f]{8}", version_hex) is None:
        raise ValueError("NIF decoder contract versionHex is invalid")
    block_types = geometry.get("blockTypes")
    if (
        not isinstance(block_types, list)
        or not block_types
        or any(not isinstance(value, str) or not value for value in block_types)
        or len(set(block_types)) != len(block_types)
    ):
        raise ValueError("NIF decoder geometry blockTypes are invalid")
    minimum_uv_sets = _integer(geometry, "minimumUvSets")
    maximum_uv_sets = _integer(geometry, "maximumUvSets")
    if minimum_uv_sets > maximum_uv_sets or maximum_uv_sets > BYTE_MAXIMUM:
        raise ValueError("NIF decoder UV-set range is invalid")
    contract = NifDecoderContract(
        schema=str(document["schema"]),
        contract_id=str(document["id"]),
        status=str(document["status"]),
        canonical_sha256=_canonical_sha256(document),
        file_name=path.name,
        version=int(version_hex[2:], HEX_RADIX),
        endian=_integer(format_contract, "endian"),
        user_version=_integer(format_contract, "userVersion"),
        user_version_2=_integer(format_contract, "userVersion2"),
        export_info_string_count=_integer(format_contract, "exportInfoStringCount", minimum=1),
        footer_bytes=_integer(format_contract, "footerBytes"),
        geometry_block_types=frozenset(block_types),
        vertex_count_offset_bytes=_integer(geometry, "vertexCountOffsetBytes"),
        has_vertices_offset_bytes=_integer(geometry, "hasVerticesOffsetBytes"),
        uv_count_prefix_bytes=_integer(geometry, "uvCountPrefixBytes"),
        vertex_stride_bytes=_integer(geometry, "vertexStrideBytes", minimum=1),
        minimum_uv_sets=minimum_uv_sets,
        maximum_uv_sets=maximum_uv_sets,
        recovery_method=str(geometry.get("recoveryMethod", "")),
        source_mutation_policy=str(geometry.get("sourceMutationPolicy", "")),
    )
    if (
        not contract.contract_id
        or not contract.status
        or contract.recovery_method != "unique-exact-block-parse"
        or contract.source_mutation_policy != "in-memory-parse-buffer-only"
        or len(contract.canonical_sha256) != SHA256_HEX_CHARACTERS
    ):
        raise ValueError("NIF decoder contract identity or policy is invalid")
    if (
        contract.vertex_count_offset_bytes + UINT16_BYTES > contract.has_vertices_offset_bytes
        or contract.has_vertices_offset_bytes >= contract.uv_count_prefix_bytes
    ):
        raise ValueError("NIF decoder geometry prefix offsets are inconsistent")
    for block_type in contract.geometry_block_types:
        block_class = getattr(NifFormat, block_type, None)
        if block_class is None:
            raise ValueError(f"NIF decoder block type is unavailable: {block_type}")
    return contract


def _require_range(payload: bytes | bytearray, offset: int, size: int, label: str) -> None:
    if offset < 0 or size < 0 or offset + size > len(payload):
        raise ValueError(f"NIF {label} exceeds the owned payload")


def _block_directory(
    payload: bytes,
    contract: NifDecoderContract,
) -> tuple[list[NifBlockDirectoryEntry], bool]:
    newline = payload.find(b"\n")
    if newline < 0:
        raise ValueError("NIF has no header line")
    offset = newline + 1
    _require_range(payload, offset, UINT32_BYTES, "version")
    version = struct.unpack_from("<I", payload, offset)[0]
    if version != contract.version:
        return [], False
    offset += UINT32_BYTES
    _require_range(payload, offset, 1 + UINT32_BYTES * 3, "Fallout header")
    endian = payload[offset]
    offset += 1
    user_version, block_count, user_version_2 = struct.unpack_from("<III", payload, offset)
    offset += UINT32_BYTES * 3
    if (endian, user_version, user_version_2) != (
        contract.endian,
        contract.user_version,
        contract.user_version_2,
    ):
        raise ValueError(
            "NIF version matched the configured Fallout format but its user-version identity did not: "
            f"endian={endian} user={user_version}/{user_version_2}"
        )
    for _ in range(contract.export_info_string_count):
        _require_range(payload, offset, 1, "export-info length")
        length = payload[offset]
        offset += 1
        _require_range(payload, offset, length, "export-info string")
        offset += length
    _require_range(payload, offset, UINT16_BYTES, "block-type count")
    type_count = struct.unpack_from("<H", payload, offset)[0]
    offset += UINT16_BYTES
    block_types: list[str] = []
    for _ in range(type_count):
        _require_range(payload, offset, UINT32_BYTES, "block-type length")
        length = struct.unpack_from("<I", payload, offset)[0]
        offset += UINT32_BYTES
        _require_range(payload, offset, length, "block-type string")
        try:
            block_types.append(payload[offset : offset + length].decode("ascii"))
        except UnicodeDecodeError as error:
            raise ValueError("NIF block-type string is not ASCII") from error
        offset += length
    _require_range(payload, offset, block_count * UINT16_BYTES, "block-type indices")
    type_indices = struct.unpack_from(f"<{block_count}H", payload, offset)
    offset += block_count * UINT16_BYTES
    _require_range(payload, offset, block_count * UINT32_BYTES, "block sizes")
    block_sizes = struct.unpack_from(f"<{block_count}I", payload, offset)
    offset += block_count * UINT32_BYTES
    _require_range(payload, offset, UINT32_BYTES * 2, "string-table header")
    string_count, _maximum_string = struct.unpack_from("<II", payload, offset)
    offset += UINT32_BYTES * 2
    for _ in range(string_count):
        _require_range(payload, offset, UINT32_BYTES, "string-table length")
        length = struct.unpack_from("<I", payload, offset)[0]
        offset += UINT32_BYTES
        _require_range(payload, offset, length, "string-table value")
        offset += length
    _require_range(payload, offset, UINT32_BYTES, "group count")
    group_count = struct.unpack_from("<I", payload, offset)[0]
    offset += UINT32_BYTES
    _require_range(payload, offset, group_count * UINT32_BYTES, "group sizes")
    offset += group_count * UINT32_BYTES
    entries: list[NifBlockDirectoryEntry] = []
    for block_index, (type_index, block_size) in enumerate(zip(type_indices, block_sizes)):
        if type_index >= len(block_types):
            raise ValueError(f"NIF block {block_index} has an invalid type index")
        _require_range(payload, offset, block_size, f"block {block_index}")
        entries.append(
            NifBlockDirectoryEntry(
                index=block_index,
                type_name=block_types[type_index],
                offset=offset,
                size=block_size,
            )
        )
        offset += block_size
    if len(payload) - offset != contract.footer_bytes:
        raise ValueError(
            "NIF footer size differs from the configured format contract: "
            f"expected={contract.footer_bytes} actual={len(payload) - offset}"
        )
    return entries, True


def _exact_block_candidates(
    block_payload: bytes,
    uv_count_offset: int,
    block_type: str,
    contract: NifDecoderContract,
) -> tuple[int, ...]:
    candidates: list[int] = []
    block_class = getattr(NifFormat, block_type)
    for candidate in range(contract.minimum_uv_sets, contract.maximum_uv_sets + 1):
        parse_payload = bytearray(block_payload)
        parse_payload[uv_count_offset] = candidate
        stream = io.BytesIO(parse_payload)
        context = NifFormat.Data(
            version=contract.version,
            user_version=contract.user_version,
            user_version_2=contract.user_version_2,
        )
        context._link_stack = []
        try:
            block_class().read(stream, context)
        except Exception:
            continue
        if stream.tell() == len(parse_payload):
            candidates.append(candidate)
    return tuple(candidates)


def _normalize_geometry_uv_counts(
    payload: bytes,
    blocks: list[NifBlockDirectoryEntry],
    contract: NifDecoderContract,
) -> tuple[bytes, tuple[NifUvNormalization, ...]]:
    parse_payload = bytearray(payload)
    normalizations: list[NifUvNormalization] = []
    for block in blocks:
        if block.type_name not in contract.geometry_block_types:
            continue
        vertex_count_offset = block.offset + contract.vertex_count_offset_bytes
        has_vertices_offset = block.offset + contract.has_vertices_offset_bytes
        _require_range(payload, vertex_count_offset, UINT16_BYTES, "geometry vertex count")
        _require_range(payload, has_vertices_offset, 1, "geometry has-vertices flag")
        vertex_count = struct.unpack_from("<H", payload, vertex_count_offset)[0]
        vertex_bytes = vertex_count * contract.vertex_stride_bytes if payload[has_vertices_offset] else 0
        uv_count_offset = block.offset + contract.uv_count_prefix_bytes + vertex_bytes
        _require_range(payload, uv_count_offset, 1, "geometry UV-count field")
        if uv_count_offset >= block.offset + block.size:
            raise ValueError(f"NIF block {block.index} geometry prefix exceeds its block size")
        source_value = payload[uv_count_offset]
        if source_value <= contract.maximum_uv_sets:
            continue
        block_payload = payload[block.offset : block.offset + block.size]
        relative_offset = uv_count_offset - block.offset
        candidates = _exact_block_candidates(
            block_payload,
            relative_offset,
            block.type_name,
            contract,
        )
        if len(candidates) != 1:
            raise ValueError(
                "NIF geometry UV-count recovery was not uniquely proven by the owned block: "
                f"block={block.index} type={block.type_name} stored={source_value} "
                f"candidates={list(candidates)}"
            )
        decoded_uv_sets = candidates[0]
        parse_payload[uv_count_offset] = decoded_uv_sets
        normalizations.append(
            NifUvNormalization(
                block_index=block.index,
                block_type=block.type_name,
                source_byte_offset=uv_count_offset,
                source_stored_value=source_value,
                decoded_uv_sets=decoded_uv_sets,
                block_bytes=block.size,
                candidates_tested=tuple(
                    range(contract.minimum_uv_sets, contract.maximum_uv_sets + 1)
                ),
            )
        )
    return bytes(parse_payload), tuple(normalizations)


def decode_nif(payload: bytes) -> NifDecodeResult:
    """Decode owned NIF bytes through the configured, version-bounded contract."""

    contract = load_nif_decoder_contract()
    blocks, format_matched = _block_directory(payload, contract)
    parse_payload, normalizations = (
        _normalize_geometry_uv_counts(payload, blocks, contract)
        if format_matched
        else (payload, ())
    )
    document = NifFormat.Data()
    document.read(io.BytesIO(parse_payload))
    return NifDecodeResult(
        document=document,
        normalizations=normalizations,
        contract=contract,
        format_matched=format_matched,
    )
