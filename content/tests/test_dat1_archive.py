from __future__ import annotations

import struct
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from dat1_archive import Dat1Archive, canonical_dat1_path  # noqa: E402


def literal_lzss(payload: bytes) -> bytes:
    encoded = bytearray()
    for start in range(0, len(payload), 8):
        chunk = payload[start : start + 8]
        encoded.extend(bytes(((1 << len(chunk)) - 1,)))
        encoded.extend(chunk)
    return struct.pack(">h", len(encoded)) + encoded + b"\x00\x00"


def synthetic_dat1(members: list[tuple[str, str, bytes, bool]]) -> bytes:
    folders = sorted({folder for folder, _, _, _ in members}, key=str.casefold)
    by_folder = {
        folder: sorted(
            [row for row in members if row[0] == folder],
            key=lambda row: row[1].casefold(),
        )
        for folder in folders
    }
    directory_size = 16 + sum(1 + len(folder.encode("ascii")) for folder in folders)
    for folder in folders:
        directory_size += 16
        directory_size += sum(17 + len(filename.encode("ascii")) for _, filename, _, _ in by_folder[folder])
    stored_rows = []
    offset = directory_size
    for folder in folders:
        for _, filename, decoded, compressed in by_folder[folder]:
            stored = literal_lzss(decoded) if compressed else decoded
            stored_rows.append((folder, filename, decoded, compressed, stored, offset))
            offset += len(stored)

    result = bytearray(struct.pack(">IIII", len(folders), len(members), 0, offset))
    for folder in folders:
        encoded = folder.encode("ascii")
        result.extend(bytes((len(encoded),)))
        result.extend(encoded)
    for folder in folders:
        rows = [row for row in stored_rows if row[0] == folder]
        result.extend(struct.pack(">IIII", len(rows), 0, 0, 0))
        for _, filename, decoded, compressed, stored, stored_offset in rows:
            encoded = filename.encode("ascii")
            result.extend(bytes((len(encoded),)))
            result.extend(encoded)
            result.extend(struct.pack(">I", 0x40 if compressed else 0x20))
            result.extend(
                struct.pack(
                    ">III",
                    stored_offset,
                    len(decoded),
                    len(stored) if compressed else 0,
                )
            )
    for row in stored_rows:
        result.extend(row[4])
    return bytes(result)


class Dat1ArchiveTest(unittest.TestCase):
    def test_stored_and_lzss_members_are_case_insensitive(self) -> None:
        payload = synthetic_dat1(
            [
                ("ART\\INTRFACE", "EDTRCRTE.FRM", b"stored", False),
                ("COLOR", "COLOR.PAL", b"compressed-data" * 8, True),
            ]
        )
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "synthetic.dat"
            path.write_bytes(payload)
            archive = Dat1Archive(path)
            self.assertEqual(archive.extract("art/intrface/edtrcrte.frm").data, b"stored")
            compressed = archive.extract("COLOR\\COLOR.PAL")
            self.assertTrue(compressed.compressed)
            self.assertEqual(compressed.data, b"compressed-data" * 8)

    def test_negative_raw_lzss_block_does_not_require_dictionary_updates(self) -> None:
        decoded = b"raw-block"
        packed = struct.pack(">h", -len(decoded)) + decoded + b"\x00\x00"
        payload = synthetic_dat1([("DATA", "RAW.BIN", decoded, False)])
        stored_offset = len(payload) - len(decoded)
        rewritten = bytearray(payload[:stored_offset] + packed)
        directory_flag = rewritten.index(b"RAW.BIN") + len(b"RAW.BIN")
        struct.pack_into(">I", rewritten, directory_flag, 0x40)
        struct.pack_into(">I", rewritten, directory_flag + 12, len(packed))
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "raw.dat"
            path.write_bytes(rewritten)
            self.assertEqual(Dat1Archive(path).extract("data/raw.bin").data, decoded)

    def test_paths_and_member_bounds_fail_closed(self) -> None:
        with self.assertRaises(ValueError):
            canonical_dat1_path("../escape.frm")
        with self.assertRaises(ValueError):
            canonical_dat1_path("C:/escape.frm")
        payload = bytearray(synthetic_dat1([("DATA", "SAFE.BIN", b"safe", False)]))
        offset_position = payload.index(b"SAFE.BIN") + len(b"SAFE.BIN") + 4
        struct.pack_into(">I", payload, offset_position, len(payload) + 1)
        with tempfile.TemporaryDirectory() as raw_directory:
            path = Path(raw_directory) / "bad.dat"
            path.write_bytes(payload)
            with self.assertRaises(ValueError):
                Dat1Archive(path)


if __name__ == "__main__":
    unittest.main()
