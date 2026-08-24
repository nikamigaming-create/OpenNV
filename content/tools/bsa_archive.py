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


FNV_BSA_VERSION = 104
BSA_ARCHIVE_COMPRESSED_FLAG = 0x0004
BSA_ARCHIVE_EMBEDDED_NAMES_FLAG = 0x0100
COMPRESSED_MEMBER_MINIMUM_BYTES = 5


@dataclass(frozen=True)
class ExtractedMember:
    logical_path: str
    data: bytes
    compressed: bool
    archive_offset: int
    stored_bytes: int
    source_archive: str | None = None
    source_archive_sha256: str | None = None

    @property
    def sha256(self) -> str:
        return hashlib.sha256(self.data).hexdigest()


@dataclass(frozen=True)
class MemberLocation:
    offset: int
    stored_bytes: int
    compressed: bool


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
    if len(payload) < COMPRESSED_MEMBER_MINIMUM_BYTES:
        raise ValueError("Compressed BSA member is too short")
    expected_size = struct.unpack("<I", payload[:4])[0]
    result = zlib.decompress(payload[4:])
    if len(result) != expected_size:
        raise ValueError(f"BSA member size mismatch: expected={expected_size} actual={len(result)}")
    return result


def strip_embedded_name(payload: bytes, expected_path: str) -> bytes:
    if not payload:
        raise ValueError("Embedded-name BSA member is empty")
    name_bytes = payload[1 : 1 + payload[0]]
    if len(name_bytes) != payload[0]:
        raise ValueError("Embedded-name BSA member has a truncated name")
    embedded_path = canonical_member_path(name_bytes.decode("utf-8", errors="strict"))
    if embedded_path != expected_path:
        raise ValueError(f"BSA embedded name mismatch: expected={expected_path} actual={embedded_path}")
    return payload[1 + payload[0] :]


class BsaArchive:
    def __init__(self, archive: Path):
        self.archive = archive
        document = BsaFormat.Data()
        with archive.open("rb") as stream:
            document.read(stream)
        if int(document.version) != FNV_BSA_VERSION:
            raise ValueError(
                f"The OpenNV FNV BSA reader requires version {FNV_BSA_VERSION}, "
                f"found {document.version}"
            )

        archive_compressed = bool(int(document.archive_flags) & BSA_ARCHIVE_COMPRESSED_FLAG)
        self.embedded_names = bool(
            int(document.archive_flags) & BSA_ARCHIVE_EMBEDDED_NAMES_FLAG
        )
        members: dict[str, MemberLocation] = {}
        for folder in document.folders:
            folder_name = canonical_member_path(text(folder.name))
            for file_record in folder.files:
                logical_path = canonical_member_path(f"{folder_name}\\{text(file_record.name)}")
                if logical_path in members:
                    raise ValueError(f"Duplicate BSA member path: {logical_path}")
                members[logical_path] = MemberLocation(
                    int(file_record.offset),
                    int(file_record.file_size.num_bytes),
                    archive_compressed != bool(file_record.file_size.is_compressed_override),
                )
        self.members = members

    def extract(self, logical_path: str) -> ExtractedMember:
        requested = canonical_member_path(logical_path)
        if requested not in self.members:
            raise FileNotFoundError(f"BSA member not found: {requested}")
        location = self.members[requested]
        with self.archive.open("rb") as stream:
            stream.seek(location.offset)
            payload = stream.read(location.stored_bytes)
        if len(payload) != location.stored_bytes:
            raise ValueError(
                f"Truncated BSA member: expected={location.stored_bytes} actual={len(payload)}"
            )
        member_payload = strip_embedded_name(payload, requested) if self.embedded_names else payload
        return ExtractedMember(
            requested,
            decode_member_payload(member_payload, location.compressed),
            location.compressed,
            location.offset,
            location.stored_bytes,
        )


def extract_member(archive: Path, logical_path: str) -> ExtractedMember:
    return BsaArchive(archive).extract(logical_path)
