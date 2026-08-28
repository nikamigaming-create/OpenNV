"""Fail-closed reader for the original Fallout DAT1 archive format.

DAT1 stores a big-endian directory at the front of the archive.  Members are
either stored verbatim (flag 0x20) or encoded as signed-size LZSS blocks (flag
0x40).  This module only exposes bounded, case-insensitive member extraction;
it never writes archive paths to disk.
"""

from __future__ import annotations

import hashlib
import struct
from dataclasses import dataclass
from pathlib import Path
# Immutable format/source/diagnostic contracts; tunable behavior is recipe-owned.
DAT1_ARCHIVE_FORMAT_CONTRACT_HEX_0F = 0x0F
DAT1_ARCHIVE_FORMAT_CONTRACT_HEX_F0 = 0xF0
DAT1_ARCHIVE_FORMAT_CONTRACT_HEX_FFF = 0xFFF
DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_16 = 16
DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_18 = 18
DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_1000000 = 1_000_000
DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_4096 = 4096
DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_65535 = 65535
DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_8 = 8



STORED_FLAG = 0x20
LZSS_FLAG = 0x40


@dataclass(frozen=True)
class Dat1Entry:
    logical_path: str
    compressed: bool
    uncompressed_size: int
    stored_size: int
    stored_offset: int


@dataclass(frozen=True)
class Dat1Member:
    logical_path: str
    data: bytes
    compressed: bool
    stored_offset: int
    stored_size: int

    @property
    def sha256(self) -> str:
        return hashlib.sha256(self.data).hexdigest()


def canonical_dat1_path(value: str) -> str:
    canonical = value.replace("/", "\\").strip("\\").casefold()
    parts = canonical.split("\\")
    if not canonical or any(part in {"", ".", ".."} for part in parts) or ":" in parts[0]:
        raise ValueError(f"invalid DAT1 member path: {value!r}")
    return canonical


def _decode_lzss_blocks(payload: bytes, expected_size: int) -> bytes:
    cursor = 0
    output = bytearray()
    dictionary = bytearray(b" " * DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_4096)
    write_cursor = DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_4096 - DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_18
    terminated = False

    while cursor + 2 <= len(payload):
        block_size = struct.unpack_from(">h", payload, cursor)[0]
        cursor += 2
        if block_size == 0:
            terminated = True
            break
        stored_size = abs(block_size)
        if cursor + stored_size > len(payload):
            raise ValueError("DAT1 LZSS block escapes the stored member")
        block = payload[cursor : cursor + stored_size]
        cursor += stored_size

        if block_size < 0:
            output.extend(block)
        else:
            block_cursor = 0
            while block_cursor < len(block):
                flags = block[block_cursor]
                block_cursor += 1
                for bit in range(DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_8):
                    if block_cursor >= len(block):
                        break
                    if flags & (1 << bit):
                        value = block[block_cursor]
                        block_cursor += 1
                        output.append(value)
                        dictionary[write_cursor] = value
                        write_cursor = (write_cursor + 1) & DAT1_ARCHIVE_FORMAT_CONTRACT_HEX_FFF
                    else:
                        if block_cursor + 2 > len(block):
                            raise ValueError("DAT1 LZSS back-reference is truncated")
                        low = block[block_cursor]
                        high = block[block_cursor + 1]
                        block_cursor += 2
                        read_cursor = low | ((high & DAT1_ARCHIVE_FORMAT_CONTRACT_HEX_F0) << 4)
                        length = (high & DAT1_ARCHIVE_FORMAT_CONTRACT_HEX_0F) + 3
                        for _ in range(length):
                            value = dictionary[read_cursor]
                            read_cursor = (read_cursor + 1) & DAT1_ARCHIVE_FORMAT_CONTRACT_HEX_FFF
                            output.append(value)
                            dictionary[write_cursor] = value
                            write_cursor = (write_cursor + 1) & DAT1_ARCHIVE_FORMAT_CONTRACT_HEX_FFF
                    if len(output) > expected_size:
                        raise ValueError("DAT1 LZSS output exceeds the declared size")

    if not terminated and cursor != len(payload):
        raise ValueError("DAT1 LZSS member ends inside a block header")
    if cursor != len(payload):
        raise ValueError("DAT1 LZSS member has trailing stored bytes")
    if len(output) != expected_size:
        raise ValueError(
            f"DAT1 inflated size mismatch: expected {expected_size}, got {len(output)}"
        )
    return bytes(output)


class Dat1Archive:
    def __init__(self, path: Path):
        self.path = path
        file_size = path.stat().st_size
        if file_size < DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_16:
            raise ValueError("DAT1 archive is too small")
        directory = path.read_bytes()
        cursor = 0

        def read_u32(label: str) -> int:
            nonlocal cursor
            if cursor + 4 > len(directory):
                raise ValueError(f"DAT1 directory is truncated at {label}")
            value = struct.unpack_from(">I", directory, cursor)[0]
            cursor += 4
            return value

        def read_pstring(label: str) -> str:
            nonlocal cursor
            if cursor >= len(directory):
                raise ValueError(f"DAT1 directory is truncated at {label}")
            length = directory[cursor]
            cursor += 1
            if length == 0 or cursor + length > len(directory):
                raise ValueError(f"DAT1 {label} has invalid length {length}")
            raw = directory[cursor : cursor + length]
            cursor += length
            try:
                return raw.decode("ascii", errors="strict")
            except UnicodeDecodeError as error:
                raise ValueError(f"DAT1 {label} is not ASCII") from error

        folder_count = read_u32("folder count")
        if folder_count == 0 or folder_count > DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_65535:
            raise ValueError(f"DAT1 folder count is invalid: {folder_count}")
        header_values = [read_u32(f"header value {index}") for index in range(3)]
        folders = [read_pstring(f"folder {index}") for index in range(folder_count)]

        entries: dict[str, Dat1Entry] = {}
        for folder_index, folder in enumerate(folders):
            file_count = read_u32(f"folder {folder_index} file count")
            if file_count > DAT1_ARCHIVE_FORMAT_CONTRACT_INTEGER_1000000:
                raise ValueError(f"DAT1 folder {folder!r} has invalid file count {file_count}")
            _ = [read_u32(f"folder {folder_index} metadata {index}") for index in range(3)]
            previous_filename = ""
            for file_index in range(file_count):
                filename = read_pstring(f"folder {folder_index} file {file_index}")
                if cursor >= len(directory):
                    raise ValueError("DAT1 directory is truncated at compression flag")
                flag = read_u32(f"member {filename} attributes")
                if flag not in {STORED_FLAG, LZSS_FLAG}:
                    raise ValueError(f"DAT1 member {filename!r} has unsupported flag 0x{flag:02x}")
                stored_offset = read_u32(f"member {filename} offset")
                uncompressed_size = read_u32(f"member {filename} unpacked size")
                declared_stored_size = read_u32(f"member {filename} stored size")
                stored_size = (
                    declared_stored_size
                    if flag == LZSS_FLAG
                    else uncompressed_size
                )
                if stored_offset > file_size or stored_size > file_size - stored_offset:
                    raise ValueError(f"DAT1 member {filename!r} escapes the archive")
                if flag == STORED_FLAG and declared_stored_size not in {0, uncompressed_size}:
                    raise ValueError(f"stored DAT1 member {filename!r} has invalid packed size")
                logical_path = canonical_dat1_path(
                    filename if folder == "." else f"{folder}\\{filename}"
                )
                if logical_path in entries:
                    raise ValueError(f"duplicate DAT1 member path: {logical_path}")
                canonical_filename = filename.casefold()
                if previous_filename and previous_filename > canonical_filename:
                    raise ValueError(f"DAT1 folder {folder!r} is not sorted case-insensitively")
                previous_filename = canonical_filename
                entries[logical_path] = Dat1Entry(
                    logical_path,
                    flag == LZSS_FLAG,
                    uncompressed_size,
                    stored_size,
                    stored_offset,
                )

        first_member_offset = min((entry.stored_offset for entry in entries.values()), default=file_size)
        if cursor > first_member_offset:
            raise ValueError("DAT1 directory overlaps archive member data")
        self.header_values = tuple(header_values)
        self.entries = entries

    def extract(self, logical_path: str) -> Dat1Member:
        canonical = canonical_dat1_path(logical_path)
        entry = self.entries.get(canonical)
        if entry is None:
            raise FileNotFoundError(f"DAT1 member not found: {canonical}")
        with self.path.open("rb") as stream:
            stream.seek(entry.stored_offset)
            payload = stream.read(entry.stored_size)
        if len(payload) != entry.stored_size:
            raise ValueError(f"DAT1 member is truncated: {canonical}")
        data = (
            _decode_lzss_blocks(payload, entry.uncompressed_size)
            if entry.compressed
            else payload
        )
        return Dat1Member(
            entry.logical_path,
            data,
            entry.compressed,
            entry.stored_offset,
            entry.stored_size,
        )
