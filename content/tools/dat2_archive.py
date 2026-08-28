"""Fail-closed reader for Fallout 2 DAT2 archives.

The implementation follows the published neutral DAT2 contract: little-endian
directory/footer metadata, case-insensitive relative paths, stored or zlib
members, and offsets relative to the archive data base.
"""

from __future__ import annotations

import hashlib
import struct
import zlib
from dataclasses import dataclass
from pathlib import Path
# Immutable format/source/diagnostic contracts; tunable behavior is recipe-owned.
DAT2_ARCHIVE_FORMAT_CONTRACT_INTEGER_12 = 12
DAT2_ARCHIVE_FORMAT_CONTRACT_INTEGER_13 = 13
DAT2_ARCHIVE_FORMAT_CONTRACT_INTEGER_8 = 8



@dataclass(frozen=True)
class Dat2Entry:
    logical_path: str
    compressed: bool
    uncompressed_size: int
    stored_size: int
    stored_offset: int


@dataclass(frozen=True)
class Dat2Member:
    logical_path: str
    data: bytes
    compressed: bool
    stored_offset: int
    stored_size: int

    @property
    def sha256(self) -> str:
        return hashlib.sha256(self.data).hexdigest()


def canonical_dat2_path(value: str) -> str:
    canonical = value.replace("/", "\\").strip("\\").casefold()
    parts = canonical.split("\\")
    if not canonical or any(part in {"", ".", ".."} for part in parts) or ":" in parts[0]:
        raise ValueError(f"invalid DAT2 member path: {value!r}")
    return canonical


class Dat2Archive:
    def __init__(self, path: Path):
        self.path = path
        file_size = path.stat().st_size
        if file_size < DAT2_ARCHIVE_FORMAT_CONTRACT_INTEGER_12:
            raise ValueError("DAT2 archive is too small")
        with path.open("rb") as stream:
            stream.seek(file_size - DAT2_ARCHIVE_FORMAT_CONTRACT_INTEGER_8)
            tree_size, data_size = struct.unpack("<II", stream.read(DAT2_ARCHIVE_FORMAT_CONTRACT_INTEGER_8))
            if tree_size < 4 or data_size > file_size:
                raise ValueError("DAT2 footer sizes are invalid")
            data_base = file_size - data_size
            tree_offset = file_size - tree_size - DAT2_ARCHIVE_FORMAT_CONTRACT_INTEGER_8
            if tree_offset < data_base or tree_offset + tree_size != file_size - DAT2_ARCHIVE_FORMAT_CONTRACT_INTEGER_8:
                raise ValueError("DAT2 directory bounds are invalid")
            stream.seek(tree_offset)
            tree = stream.read(tree_size)
        if len(tree) != tree_size:
            raise ValueError("DAT2 directory is truncated")

        cursor = 0

        def read_u32(label: str) -> int:
            nonlocal cursor
            if cursor + 4 > len(tree):
                raise ValueError(f"DAT2 directory is truncated at {label}")
            value = struct.unpack_from("<I", tree, cursor)[0]
            cursor += 4
            return value

        file_count = read_u32("file count")
        entries: dict[str, Dat2Entry] = {}
        previous_path = ""
        for index in range(file_count):
            path_length = read_u32(f"entry {index} path length")
            if path_length == 0 or cursor + path_length + DAT2_ARCHIVE_FORMAT_CONTRACT_INTEGER_13 > len(tree):
                raise ValueError(f"DAT2 entry {index} has invalid path length {path_length}")
            encoded_path = tree[cursor : cursor + path_length]
            cursor += path_length
            try:
                logical_path = canonical_dat2_path(encoded_path.decode("utf-8", errors="strict"))
            except UnicodeDecodeError as error:
                raise ValueError(f"DAT2 entry {index} path is not UTF-8") from error
            compressed_value = tree[cursor]
            cursor += 1
            if compressed_value not in {0, 1}:
                raise ValueError(f"DAT2 entry {logical_path} has invalid compression flag")
            uncompressed_size = read_u32(f"entry {logical_path} uncompressed size")
            stored_size = read_u32(f"entry {logical_path} stored size")
            stored_offset = read_u32(f"entry {logical_path} stored offset")
            if not compressed_value and uncompressed_size != stored_size:
                raise ValueError(f"stored DAT2 entry {logical_path} has mismatched sizes")
            if stored_offset + stored_size > tree_offset - data_base:
                raise ValueError(f"DAT2 entry {logical_path} escapes the data region")
            if logical_path in entries:
                raise ValueError(f"duplicate DAT2 member path: {logical_path}")
            if previous_path and previous_path > logical_path:
                raise ValueError("DAT2 directory is not sorted case-insensitively")
            previous_path = logical_path
            entries[logical_path] = Dat2Entry(
                logical_path,
                bool(compressed_value),
                uncompressed_size,
                stored_size,
                data_base + stored_offset,
            )
        if cursor != len(tree):
            raise ValueError(f"DAT2 directory has {len(tree) - cursor} trailing bytes")
        self.entries = entries
        self.file_size = file_size
        self.data_base = data_base
        self.data_size = data_size
        self.tree_offset = tree_offset
        self.tree_size = tree_size
        self.tree_sha256 = hashlib.sha256(tree).hexdigest()

    def extract(self, logical_path: str) -> Dat2Member:
        canonical = canonical_dat2_path(logical_path)
        entry = self.entries.get(canonical)
        if entry is None:
            raise FileNotFoundError(f"DAT2 member not found: {canonical}")
        with self.path.open("rb") as stream:
            stream.seek(entry.stored_offset)
            payload = stream.read(entry.stored_size)
        if len(payload) != entry.stored_size:
            raise ValueError(f"DAT2 member is truncated: {canonical}")
        data = zlib.decompress(payload) if entry.compressed else payload
        if len(data) != entry.uncompressed_size:
            raise ValueError(
                f"DAT2 inflated size mismatch for {canonical}: "
                f"expected {entry.uncompressed_size}, got {len(data)}"
            )
        return Dat2Member(
            entry.logical_path,
            data,
            entry.compressed,
            entry.stored_offset,
            entry.stored_size,
        )
