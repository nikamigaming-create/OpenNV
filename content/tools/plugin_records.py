"""Minimal, fail-closed reader for Bethesda TES4-family plugin containers."""

from __future__ import annotations

import struct
import zlib
from dataclasses import dataclass
from pathlib import Path
from typing import BinaryIO, Iterator


RECORD_HEADER_BYTES = 24
COMPRESSED_RECORD_FLAG = 0x00040000
RECORD_SIZE_OFFSET = 4
RECORD_FLAGS_OFFSET = 8
RECORD_FORM_ID_OFFSET = 12
GROUP_LABEL_OFFSET = 8
GROUP_TYPE_OFFSET = 12
COMPRESSED_SIZE_PREFIX_BYTES = 4
ZLIB_HEADER_BYTES = 2
ZLIB_TRAILER_BYTES = 4
ZLIB_MINIMUM_FRAMED_BYTES = 6
ZLIB_COMPRESSION_METHOD_MASK = 0x0F
ZLIB_DEFLATE_METHOD = 8
ZLIB_HEADER_CHECK_DIVISOR = 31
ZLIB_PRESET_DICTIONARY_FLAG = 0x20
RAW_DEFLATE_WINDOW_BITS = -15
SUBRECORD_HEADER_BYTES = 6
BITS_PER_BYTE = 8
PLUGIN_HEADER_SIGNATURE = "TES4"
MASTER_NAME_SUBRECORD_SIGNATURE = "MAST"


class PluginFormatError(ValueError):
    """Raised when a plugin container violates the bounded record contract."""


@dataclass(frozen=True)
class GroupContext:
    label: bytes
    group_type: int

    @property
    def label_u32(self) -> int:
        return struct.unpack("<I", self.label)[0]


@dataclass(frozen=True)
class Record:
    signature: str
    form_id: int
    flags: int
    data: bytes
    groups: tuple[GroupContext, ...]
    compression_checksum_valid: bool | None = None


@dataclass(frozen=True)
class Subrecord:
    signature: str
    data: bytes


def _signature(raw: bytes, offset: int) -> str:
    try:
        value = raw.decode("ascii")
    except UnicodeDecodeError as error:
        raise PluginFormatError(f"Non-ASCII signature at 0x{offset:08x}") from error
    valid_characters = all(
        character.isupper() or character.isdigit() or character == "_" for character in value
    )
    if len(value) != 4 or not valid_characters:
        raise PluginFormatError(f"Invalid signature {value!r} at 0x{offset:08x}")
    return value


def _read_exact(stream: BinaryIO, size: int, description: str) -> bytes:
    data = stream.read(size)
    if len(data) != size:
        raise PluginFormatError(f"Truncated {description}: expected {size} bytes, found {len(data)}")
    return data


def _iter_region(
    stream: BinaryIO,
    end: int,
    groups: tuple[GroupContext, ...],
    signatures: frozenset[str] | None,
) -> Iterator[Record]:
    while stream.tell() < end:
        offset = stream.tell()
        if end - offset < RECORD_HEADER_BYTES:
            raise PluginFormatError(f"Trailing bytes in container at 0x{offset:08x}")
        header = _read_exact(stream, RECORD_HEADER_BYTES, "record header")
        signature = _signature(header[:4], offset)
        size = struct.unpack_from("<I", header, RECORD_SIZE_OFFSET)[0]

        if signature == "GRUP":
            if size < RECORD_HEADER_BYTES:
                raise PluginFormatError(f"Invalid GRUP size {size} at 0x{offset:08x}")
            group_end = offset + size
            if group_end > end:
                raise PluginFormatError(f"GRUP exceeds its parent at 0x{offset:08x}")
            context = GroupContext(
                header[GROUP_LABEL_OFFSET:RECORD_FORM_ID_OFFSET],
                struct.unpack_from("<i", header, GROUP_TYPE_OFFSET)[0],
            )
            yield from _iter_region(stream, group_end, groups + (context,), signatures)
            if stream.tell() != group_end:
                raise PluginFormatError(f"GRUP ended at the wrong offset: 0x{offset:08x}")
            continue

        data_end = stream.tell() + size
        if data_end > end:
            raise PluginFormatError(f"{signature} exceeds its parent at 0x{offset:08x}")
        flags = struct.unpack_from("<I", header, RECORD_FLAGS_OFFSET)[0]
        form_id = struct.unpack_from("<I", header, RECORD_FORM_ID_OFFSET)[0]
        if signatures is not None and signature not in signatures:
            stream.seek(size, 1)
            continue
        data = _read_exact(stream, size, f"{signature} data")
        compression_checksum_valid: bool | None = None
        if flags & COMPRESSED_RECORD_FLAG:
            if len(data) < COMPRESSED_SIZE_PREFIX_BYTES:
                raise PluginFormatError(f"Compressed {signature} has no size prefix")
            expected_size = struct.unpack_from("<I", data, 0)[0]
            payload = data[COMPRESSED_SIZE_PREFIX_BYTES:]
            try:
                data = zlib.decompress(payload)
                compression_checksum_valid = True
            except zlib.error as error:
                if (
                    len(payload) < ZLIB_MINIMUM_FRAMED_BYTES
                    or payload[0] & ZLIB_COMPRESSION_METHOD_MASK != ZLIB_DEFLATE_METHOD
                    or (payload[0] << BITS_PER_BYTE | payload[1])
                    % ZLIB_HEADER_CHECK_DIVISOR
                    != 0
                    or payload[1] & ZLIB_PRESET_DICTIONARY_FLAG
                ):
                    raise PluginFormatError(
                        f"Compressed {signature} has invalid zlib data at 0x{offset:08x}"
                    ) from error
                inflater = zlib.decompressobj(RAW_DEFLATE_WINDOW_BITS)
                try:
                    data = inflater.decompress(
                        payload[ZLIB_HEADER_BYTES:-ZLIB_TRAILER_BYTES]
                    ) + inflater.flush()
                except zlib.error as raw_error:
                    raise PluginFormatError(
                        f"Compressed {signature} has invalid deflate data at 0x{offset:08x}"
                    ) from raw_error
                if not inflater.eof or inflater.unused_data or inflater.unconsumed_tail:
                    raise PluginFormatError(
                        f"Compressed {signature} has incomplete deflate data at 0x{offset:08x}"
                    ) from error
                compression_checksum_valid = False
            if len(data) != expected_size:
                raise PluginFormatError(
                    f"Compressed {signature} size mismatch: expected {expected_size}, found {len(data)}"
                )
        yield Record(signature, form_id, flags, data, groups, compression_checksum_valid)

    if stream.tell() != end:
        raise PluginFormatError(f"Container overrun: expected 0x{end:08x}, found 0x{stream.tell():08x}")


def iter_plugin_records(path: Path, signatures: frozenset[str] | None = None) -> Iterator[Record]:
    with path.open("rb") as stream:
        stream.seek(0, 2)
        end = stream.tell()
        stream.seek(0)
        yield from _iter_region(stream, end, (), signatures)


def iter_subrecords(record: Record) -> Iterator[Subrecord]:
    offset = 0
    extended_size: int | None = None
    while offset < len(record.data):
        if len(record.data) - offset < SUBRECORD_HEADER_BYTES:
            raise PluginFormatError(f"Truncated subrecord header in {record.signature} {record.form_id:08x}")
        signature = _signature(record.data[offset : offset + 4], offset)
        declared_size = struct.unpack_from("<H", record.data, offset + 4)[0]
        offset += SUBRECORD_HEADER_BYTES
        if signature == "XXXX":
            if declared_size != 4 or extended_size is not None or len(record.data) - offset < 4:
                raise PluginFormatError(f"Invalid XXXX marker in {record.signature} {record.form_id:08x}")
            extended_size = struct.unpack_from("<I", record.data, offset)[0]
            offset += 4
            continue
        size = extended_size if extended_size is not None else declared_size
        extended_size = None
        if offset + size > len(record.data):
            raise PluginFormatError(f"Subrecord {signature} exceeds {record.signature} {record.form_id:08x}")
        yield Subrecord(signature, record.data[offset : offset + size])
        offset += size
    if extended_size is not None:
        raise PluginFormatError(f"Dangling XXXX marker in {record.signature} {record.form_id:08x}")


def zstring(data: bytes) -> str:
    return data.split(b"\0", 1)[0].decode("cp1252", errors="strict")


def read_plugin_masters(path: Path) -> tuple[str, ...]:
    """Return the declared TES4 master order for one plugin."""

    headers = tuple(iter_plugin_records(path, frozenset({PLUGIN_HEADER_SIGNATURE})))
    if len(headers) != 1:
        raise PluginFormatError(
            f"Plugin must contain exactly one {PLUGIN_HEADER_SIGNATURE} record: {path}"
        )
    return tuple(
        zstring(subrecord.data)
        for subrecord in iter_subrecords(headers[0])
        if subrecord.signature == MASTER_NAME_SUBRECORD_SIGNATURE
    )
