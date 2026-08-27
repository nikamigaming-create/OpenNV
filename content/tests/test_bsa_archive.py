from __future__ import annotations

import struct
import sys
import tempfile
import unittest
import zlib
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from bsa_archive import (  # noqa: E402
    BSA_ARCHIVE_COMPRESSED_FLAG,
    BSA_ARCHIVE_DIRECTORY_NAMES_FLAG,
    BSA_ARCHIVE_EMBEDDED_NAMES_FLAG,
    BSA_ARCHIVE_FILE_NAMES_FLAG,
    BSA_FILE_COMPRESSED_OVERRIDE_FLAG,
    BSA_FILE_RECORD,
    BSA_FOLDER_RECORD,
    BSA_HEADER,
    BSA_MAGIC,
    FNV_BSA_VERSION,
    BsaArchive,
)


def synthetic_bsa(trailing_file_name_padding: int = 0) -> bytes:
    rows = (
        ("textures\\test", "first.dds", b"compressed-owned-bytes", True),
        ("meshes\\test", "second.nif", b"plain-owned-bytes", False),
    )
    archive_flags = (
        BSA_ARCHIVE_DIRECTORY_NAMES_FLAG
        | BSA_ARCHIVE_FILE_NAMES_FLAG
        | BSA_ARCHIVE_COMPRESSED_FLAG
        | BSA_ARCHIVE_EMBEDDED_NAMES_FLAG
    )
    file_names = b"".join(
        file_name.encode("ascii") + b"\0"
        for _folder, file_name, _payload, _compressed in rows
    ) + b"\0" * trailing_file_name_padding
    folder_names = tuple(dict.fromkeys(folder for folder, *_rest in rows))
    grouped = {
        folder: tuple(row for row in rows if row[0] == folder)
        for folder in folder_names
    }
    folder_records_offset = BSA_HEADER.size
    folder_blocks_offset = (
        folder_records_offset + len(folder_names) * BSA_FOLDER_RECORD.size
    )
    folder_block_sizes = {
        folder: 1 + len(folder.encode("ascii")) + 1 +
        len(grouped[folder]) * BSA_FILE_RECORD.size
        for folder in folder_names
    }
    folder_offsets = {}
    cursor = folder_blocks_offset
    for folder in folder_names:
        folder_offsets[folder] = cursor
        cursor += folder_block_sizes[folder]
    data_offset = cursor + len(file_names)

    stored_payloads = {}
    payload_offsets = {}
    for folder, file_name, payload, compressed in rows:
        logical_path = f"{folder}\\{file_name}".encode("ascii")
        stored = (
            struct.pack("<I", len(payload)) + zlib.compress(payload)
            if compressed
            else payload
        )
        stored = bytes((len(logical_path),)) + logical_path + stored
        stored_payloads[(folder, file_name)] = stored
        payload_offsets[(folder, file_name)] = data_offset
        data_offset += len(stored)

    folder_records = b"".join(
        BSA_FOLDER_RECORD.pack(
            0,
            len(grouped[folder]),
            folder_offsets[folder] + len(file_names),
        )
        for folder in folder_names
    )
    folder_blocks = bytearray()
    for folder in folder_names:
        encoded = folder.encode("ascii") + b"\0"
        folder_blocks.extend(bytes((len(encoded),)))
        folder_blocks.extend(encoded)
        for _folder, file_name, _payload, compressed in grouped[folder]:
            stored = stored_payloads[(folder, file_name)]
            raw_size = len(stored)
            if not compressed:
                raw_size |= BSA_FILE_COMPRESSED_OVERRIDE_FLAG
            folder_blocks.extend(
                BSA_FILE_RECORD.pack(
                    0,
                    raw_size,
                    payload_offsets[(folder, file_name)],
                )
            )

    header = BSA_HEADER.pack(
        BSA_MAGIC,
        FNV_BSA_VERSION,
        folder_records_offset,
        archive_flags,
        len(folder_names),
        len(rows),
        sum(len(folder.encode("ascii")) + 1 for folder in folder_names),
        len(file_names),
        0,
    )
    payloads = b"".join(
        stored_payloads[(folder, file_name)]
        for folder, file_name, _payload, _compressed in rows
    )
    return header + folder_records + bytes(folder_blocks) + file_names + payloads


class BsaArchiveTest(unittest.TestCase):
    def test_v104_index_extracts_embedded_compressed_and_plain_members(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "synthetic.bsa"
            path.write_bytes(synthetic_bsa())
            archive = BsaArchive(path)

            first = archive.extract("Textures/Test/First.dds")
            second = archive.extract("meshes\\test\\second.nif")

        self.assertEqual(first.data, b"compressed-owned-bytes")
        self.assertTrue(first.compressed)
        self.assertEqual(second.data, b"plain-owned-bytes")
        self.assertFalse(second.compressed)

    def test_v104_index_rejects_header_file_count_disagreement(self) -> None:
        payload = bytearray(synthetic_bsa())
        file_count_offset = struct.calcsize("<4s4I")
        struct.pack_into("<I", payload, file_count_offset, 3)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bad-count.bsa"
            path.write_bytes(payload)
            with self.assertRaisesRegex(ValueError, "file count mismatch"):
                BsaArchive(path)

    def test_v104_index_accepts_declared_trailing_filename_padding(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "filename-padding.bsa"
            path.write_bytes(synthetic_bsa(trailing_file_name_padding=7))
            archive = BsaArchive(path)

        self.assertEqual(len(archive.members), 2)


if __name__ == "__main__":
    unittest.main()
