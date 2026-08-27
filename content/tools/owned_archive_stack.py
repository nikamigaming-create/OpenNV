"""Resolve owned assets through hash-bound official BSA precedence stacks."""

from __future__ import annotations

import json
from dataclasses import dataclass, replace
from pathlib import Path

from bsa_archive import BsaArchive, ExtractedMember, canonical_member_path
from plugin_stack import file_sha256, find_case_insensitive_file


ARCHIVE_RECIPE_SCHEMA = "opennv-owned-visual-archive-stack/v1"
AUDIO_ARCHIVE_RECIPE_SCHEMA = "opennv-owned-audio-archive-stack/v1"
ARCHIVE_RESOLUTION_POLICY = "last-declared-containing-member-wins"


@dataclass(frozen=True)
class OwnedArchive:
    name: str
    path: Path
    sha256: str
    bytes: int
    archive: BsaArchive


class OwnedArchiveStack:
    """Read-only effective BSA namespace with retained winner provenance."""

    def __init__(
        self,
        entries: tuple[OwnedArchive, ...],
        schema: str = ARCHIVE_RECIPE_SCHEMA,
        recipe_id: str | None = None,
        recipe_sha256: str | None = None,
    ):
        if not entries:
            raise ValueError("Owned archive stack cannot be empty")
        if not schema:
            raise ValueError("Owned archive stack schema cannot be empty")
        if (recipe_id is None) != (recipe_sha256 is None):
            raise ValueError("Owned archive stack recipe provenance is incomplete")
        self.entries = entries
        self.schema = schema
        self.recipe_id = recipe_id
        self.recipe_sha256 = recipe_sha256
        self.members = frozenset(
            member
            for entry in entries
            for member in entry.archive.members
        )

    def extract(self, logical_path: str) -> ExtractedMember:
        requested = canonical_member_path(logical_path)
        matches = [entry for entry in self.entries if requested in entry.archive.members]
        if not matches:
            raise FileNotFoundError(f"Official BSA member not found: {requested}")
        winner = matches[-1]
        return replace(
            winner.archive.extract(requested),
            source_archive=winner.name,
            source_archive_sha256=winner.sha256,
        )

    def manifest(self) -> dict[str, object]:
        document: dict[str, object] = {
            "schema": self.schema,
            "resolutionPolicy": ARCHIVE_RESOLUTION_POLICY,
            "archives": [
                {
                    "file": entry.name,
                    "bytes": entry.bytes,
                    "sha256": entry.sha256,
                }
                for entry in self.entries
            ],
        }
        if self.recipe_id is not None:
            document["recipe"] = {
                "id": self.recipe_id,
                "sha256": self.recipe_sha256,
            }
        return document


def load_owned_archive_stack(
    data_root: Path,
    recipe_path: Path,
    expected_schema: str = ARCHIVE_RECIPE_SCHEMA,
) -> OwnedArchiveStack:
    recipe = json.loads(recipe_path.read_text(encoding="utf-8"))
    if recipe.get("schema") != expected_schema:
        raise ValueError(f"Unexpected owned archive recipe: {recipe_path}")
    if recipe.get("id") != recipe_path.stem:
        raise ValueError(f"Owned archive recipe identity differs from its file: {recipe_path}")
    if recipe.get("resolutionPolicy") != ARCHIVE_RESOLUTION_POLICY:
        raise ValueError(f"Unsupported owned archive resolution policy: {recipe_path}")
    rows = recipe.get("archives")
    if not isinstance(rows, list) or not rows:
        raise ValueError("Owned archive recipe must contain an archive order")
    names = [str(row["file"]) for row in rows]
    if len({name.casefold() for name in names}) != len(names):
        raise ValueError("Owned archive recipe contains duplicate archive names")
    entries = []
    for name in names:
        path = find_case_insensitive_file(data_root, name)
        entries.append(
            OwnedArchive(
                name,
                path,
                file_sha256(path),
                path.stat().st_size,
                BsaArchive(path),
            )
        )
    return OwnedArchiveStack(
        tuple(entries),
        expected_schema,
        recipe_path.stem,
        file_sha256(recipe_path),
    )
