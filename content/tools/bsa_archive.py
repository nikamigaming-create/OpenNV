"""Minimal direct reader for Fallout New Vegas BSA v104 members."""

from __future__ import annotations

import hashlib
import struct
import time
import zlib
from dataclasses import dataclass
from pathlib import Path

if not hasattr(time, "clock"):
    time.clock = time.perf_counter

from pyffi.formats.bsa import BsaFormat  # type: ignore  # noqa: E402


@dataclass(frozen=True)
class ExtractedMember:
    logical_path: str
    data: bytes
    compressed: bool
    archive_offset: int
    stored_bytes: int

    @property
    def sha256(self) -> str:
        return hashlib.sha256(self.data).hexdigest()


def text(value: object) -> str:
    if isinstance(value, bytes):
        return value.decode("utf-8", errors="strict")
    try:
        return bytes(value).decode("utf-8", errors="strict")
    except (TypeError, ValueError):
        return str(value)


def canonical_member_path(value: str) -> str:
    path = value.replace("/", "\\").strip("\\").lower()
    if not path or any(part in ("", ".", "..") for part in path.split("\\")):
        raise ValueError(f"Invalid BSA member path: {value!r}")
    return path


def decode_member_payload(payload: bytes, compressed: bool) -> bytes:
    if not compressed:
        return payload
    if len(payload) < 5:
        raise ValueError("Compressed BSA member is too short")
    expected_size = struct.unpack("<I", payload[:4])[0]
    result = zlib.decompress(payload[4:])
    if len(result) != expected_size:
        raise ValueError(f"BSA member size mismatch: expected={expected_size} actual={len(result)}")
    return result


def extract_member(archive: Path, logical_path: str) -> ExtractedMember:
    requested = canonical_member_path(logical_path)
    document = BsaFormat.Data()
    with archive.open("rb") as stream:
        document.read(stream)
    if int(document.version) != 104:
        raise ValueError(f"The first OpenNV BSA slice supports version 104, found {document.version}")

    archive_compressed = bool(int(document.archive_flags) & 0x4)
    match = None
    for folder in document.folders:
        folder_name = canonical_member_path(text(folder.name))
        for file_record in folder.files:
            candidate = canonical_member_path(f"{folder_name}\\{text(file_record.name)}")
            if candidate == requested:
                if match is not None:
                    raise ValueError(f"Duplicate BSA member path: {requested}")
                match = file_record
    if match is None:
        raise FileNotFoundError(f"BSA member not found: {requested}")

    stored_bytes = int(match.file_size.num_bytes)
    offset = int(match.offset)
    with archive.open("rb") as stream:
        stream.seek(offset)
        payload = stream.read(stored_bytes)
    if len(payload) != stored_bytes:
        raise ValueError(f"Truncated BSA member: expected={stored_bytes} actual={len(payload)}")
    compressed = archive_compressed != bool(match.file_size.is_compressed_override)
    return ExtractedMember(requested, decode_member_payload(payload, compressed), compressed, offset, stored_bytes)
