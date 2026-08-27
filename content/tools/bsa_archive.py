"""Minimal direct reader for Fallout New Vegas BSA v104 members."""

from __future__ import annotations

import hashlib
import struct
import zlib
from dataclasses import dataclass
from pathlib import Path

FNV_BSA_VERSION = 104
BSA_MAGIC = b"BSA\0"
BSA_HEADER = struct.Struct("<4s8I")
BSA_FOLDER_RECORD = struct.Struct("<QII")
BSA_FILE_RECORD = struct.Struct("<QII")
BSA_FOLDER_NAME_LENGTH_BYTES = 1
BSA_ARCHIVE_DIRECTORY_NAMES_FLAG = 0x0001
BSA_ARCHIVE_FILE_NAMES_FLAG = 0x0002
BSA_ARCHIVE_COMPRESSED_FLAG = 0x0004
BSA_ARCHIVE_EMBEDDED_NAMES_FLAG = 0x0100
BSA_FILE_COMPRESSED_OVERRIDE_FLAG = 0x40000000
BSA_FILE_SIZE_MASK = 0x3FFFFFFF
BSA_FILE_RESERVED_FLAG_MASK = 0x80000000
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


@dataclass(frozen=True)
class FolderLocation:
    files: int
    stored_offset: int


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
        archive_bytes = archive.stat().st_size
        with archive.open("rb") as stream:
            header_data = stream.read(BSA_HEADER.size)
            if len(header_data) != BSA_HEADER.size:
                raise ValueError("BSA header is truncated")
            (
                magic,
                version,
                folder_records_offset,
                archive_flags,
                folder_count,
                file_count,
                total_folder_name_bytes,
                total_file_name_bytes,
                _file_flags,
            ) = BSA_HEADER.unpack(header_data)
            if magic != BSA_MAGIC:
                raise ValueError(f"Unexpected BSA magic: {magic!r}")
            if version != FNV_BSA_VERSION:
                raise ValueError(
                    f"The OpenNV FNV BSA reader requires version {FNV_BSA_VERSION}, "
                    f"found {version}"
                )
            required_name_flags = (
                BSA_ARCHIVE_DIRECTORY_NAMES_FLAG | BSA_ARCHIVE_FILE_NAMES_FLAG
            )
            if archive_flags & required_name_flags != required_name_flags:
                raise ValueError("FNV BSA does not retain directory and file names")
            if folder_records_offset < BSA_HEADER.size:
                raise ValueError("BSA folder-record offset overlaps its header")

            stream.seek(folder_records_offset)
            folder_record_data = stream.read(folder_count * BSA_FOLDER_RECORD.size)
            if len(folder_record_data) != folder_count * BSA_FOLDER_RECORD.size:
                raise ValueError("BSA folder records are truncated")
            folders = tuple(
                FolderLocation(files, stored_offset)
                for _hash, files, stored_offset in BSA_FOLDER_RECORD.iter_unpack(
                    folder_record_data
                )
            )

            indexed_files: list[tuple[str, int, int]] = []
            observed_folder_name_bytes = 0
            folder_blocks_end = 0
            minimum_folder_block_offset = (
                folder_records_offset + folder_count * BSA_FOLDER_RECORD.size
            )
            for folder in folders:
                block_offset = folder.stored_offset - total_file_name_bytes
                if block_offset < minimum_folder_block_offset:
                    raise ValueError("BSA folder block has an invalid adjusted offset")
                stream.seek(block_offset)
                folder_name_size_data = stream.read(BSA_FOLDER_NAME_LENGTH_BYTES)
                if len(folder_name_size_data) != BSA_FOLDER_NAME_LENGTH_BYTES:
                    raise ValueError("BSA folder-name length is truncated")
                folder_name_size = folder_name_size_data[0]
                folder_name_data = stream.read(folder_name_size)
                if (
                    len(folder_name_data) != folder_name_size
                    or not folder_name_data.endswith(b"\0")
                ):
                    raise ValueError("BSA folder name is truncated or unterminated")
                folder_name = canonical_member_path(
                    folder_name_data[:-1].decode("utf-8", errors="strict")
                )
                observed_folder_name_bytes += folder_name_size
                file_record_data = stream.read(folder.files * BSA_FILE_RECORD.size)
                if len(file_record_data) != folder.files * BSA_FILE_RECORD.size:
                    raise ValueError("BSA file records are truncated")
                indexed_files.extend(
                    (folder_name, raw_size, offset)
                    for _hash, raw_size, offset in BSA_FILE_RECORD.iter_unpack(
                        file_record_data
                    )
                )
                folder_blocks_end = max(folder_blocks_end, stream.tell())

            if len(indexed_files) != file_count:
                raise ValueError(
                    f"BSA file count mismatch: expected={file_count} "
                    f"actual={len(indexed_files)}"
                )
            if observed_folder_name_bytes != total_folder_name_bytes:
                raise ValueError(
                    "BSA folder-name byte count differs from its header: "
                    f"expected={total_folder_name_bytes} "
                    f"actual={observed_folder_name_bytes}"
                )

            stream.seek(folder_blocks_end)
            file_name_data = stream.read(total_file_name_bytes)
            if len(file_name_data) != total_file_name_bytes:
                raise ValueError("BSA file-name table is truncated")
            file_names = file_name_data.split(b"\0")
            if not file_names or file_names[-1] != b"":
                raise ValueError("BSA file-name table is unterminated")
            file_names.pop()
            declared_file_names = file_names[:file_count]
            trailing_padding = file_names[file_count:]
            if (
                len(declared_file_names) != file_count
                or any(not value for value in declared_file_names)
                or any(value for value in trailing_padding)
            ):
                raise ValueError(
                    "BSA file-name count differs from its header: "
                    f"expected={file_count} actual={len(file_names)}"
                )
            file_names = declared_file_names

        archive_compressed = bool(archive_flags & BSA_ARCHIVE_COMPRESSED_FLAG)
        self.embedded_names = bool(
            archive_flags & BSA_ARCHIVE_EMBEDDED_NAMES_FLAG
        )
        data_minimum_offset = folder_blocks_end + total_file_name_bytes
        members: dict[str, MemberLocation] = {}
        for (folder_name, raw_size, offset), raw_name in zip(
            indexed_files,
            file_names,
        ):
            if raw_size & BSA_FILE_RESERVED_FLAG_MASK:
                raise ValueError("BSA file record uses an unsupported reserved flag")
            stored_bytes = raw_size & BSA_FILE_SIZE_MASK
            if offset < data_minimum_offset or offset + stored_bytes > archive_bytes:
                raise ValueError("BSA member payload falls outside the archive")
            file_name = raw_name.decode("utf-8", errors="strict")
            logical_path = canonical_member_path(f"{folder_name}\\{file_name}")
            if logical_path in members:
                raise ValueError(f"Duplicate BSA member path: {logical_path}")
            compressed_override = bool(
                raw_size & BSA_FILE_COMPRESSED_OVERRIDE_FLAG
            )
            members[logical_path] = MemberLocation(
                offset,
                stored_bytes,
                archive_compressed != compressed_override,
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
