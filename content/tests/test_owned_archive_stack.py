from __future__ import annotations

import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from bsa_archive import ExtractedMember  # noqa: E402
from owned_archive_stack import OwnedArchive, OwnedArchiveStack  # noqa: E402


class FakeArchive:
    def __init__(self, name: str, members: dict[str, bytes]):
        self.archive = Path(name)
        self.members = members

    def extract(self, logical_path: str) -> ExtractedMember:
        payload = self.members[logical_path]
        return ExtractedMember(logical_path, payload, False, 0, len(payload))


class OwnedArchiveStackTest(unittest.TestCase):
    def test_last_declared_member_wins_and_retains_archive_identity(self) -> None:
        base = FakeArchive("Base.bsa", {"meshes\\actor.nif": b"base"})
        dlc = FakeArchive("Dlc.bsa", {"meshes\\actor.nif": b"dlc"})
        stack = OwnedArchiveStack(
            (
                OwnedArchive("Base.bsa", base.archive, "a" * 64, 4, base),
                OwnedArchive("Dlc.bsa", dlc.archive, "b" * 64, 3, dlc),
            )
        )

        member = stack.extract("Meshes/Actor.nif")

        self.assertEqual(member.data, b"dlc")
        self.assertEqual(member.source_archive, "Dlc.bsa")
        self.assertEqual(member.source_archive_sha256, "b" * 64)

    def test_missing_member_fails_closed(self) -> None:
        base = FakeArchive("Base.bsa", {"meshes\\actor.nif": b"base"})
        stack = OwnedArchiveStack(
            (OwnedArchive("Base.bsa", base.archive, "a" * 64, 4, base),)
        )

        with self.assertRaises(FileNotFoundError):
            stack.extract("meshes/missing.nif")


if __name__ == "__main__":
    unittest.main()
