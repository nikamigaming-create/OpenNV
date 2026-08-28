#!/usr/bin/env python3
"""Register a legally owned Fallout 2 install without copying its content."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

from corpus_io import atomic_json
from dat2_archive import Dat2Archive
from plugin_stack import file_sha256


SCHEMA = "opennv-fo2-owned-profile/v1"
RECIPE_SCHEMA = "opennv-fo2-owned-profile-recipe/v1"
DAT2_FOOTER_BYTES = 8
PROFILE_PREFIX = (SCHEMA + "\0").encode("ascii")


def default_recipe_path() -> Path:
    recipes = Path(__file__).resolve().parents[1] / "recipes"
    matches = []
    for path in recipes.glob("*.json"):
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if document.get("schema") == RECIPE_SCHEMA:
            matches.append(path)
    if len(matches) != 1:
        raise ValueError(f"Expected one Fallout 2 owned-profile recipe, found {len(matches)}")
    return matches[0]


def _load_recipe(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schema") != RECIPE_SCHEMA or document.get("id") != path.stem:
        raise ValueError(f"Unexpected Fallout 2 profile recipe: {path}")
    if document.get("campaign") != "Fallout2":
        raise ValueError("The Fallout 2 recipe has the wrong campaign identity")
    archives = document.get("archives")
    if not isinstance(archives, list) or len(archives) != 3:
        raise ValueError("The Fallout 2 recipe must name exactly three source archives")
    expected = {"master.dat", "critter.dat", "patch000.dat"}
    if {str(row.get("file", "")).casefold() for row in archives} != expected:
        raise ValueError("The Fallout 2 recipe archive set changed")
    if any(row.get("format") != "fallout-dat2" for row in archives):
        raise ValueError("The Fallout 2 recipe requires the DAT2 source format")
    presentations = document.get("presentations")
    if not isinstance(presentations, list) or {
        str(row.get("id", "")) for row in presentations
    } != {"hex-tactical", "first-person", "openxr"}:
        raise ValueError("The Fallout 2 recipe must declare Hex, FPS, and VR")
    if any(row.get("ready") is not False for row in presentations):
        raise ValueError("The source-only Fallout 2 recipe cannot enable a runtime mode")
    if not isinstance(document.get("firstSliceBlocker"), str):
        raise ValueError("The Fallout 2 recipe has no first-slice blocker")
    return document


def _owned_file(root: Path, name: str) -> Path:
    matches = [
        child
        for child in root.iterdir()
        if child.is_file() and child.name.casefold() == name.casefold()
    ]
    if len(matches) != 1:
        raise FileNotFoundError(
            f"Fallout 2 requires exactly one root-level {name}; found {len(matches)}"
        )
    if matches[0].stat().st_size == 0:
        raise ValueError(f"Fallout 2 source archive is empty: {matches[0]}")
    return matches[0]


def _index_sha256(archive: Dat2Archive) -> str:
    rows = [
        (
            entry.logical_path,
            entry.compressed,
            entry.uncompressed_size,
            entry.stored_size,
            entry.stored_offset,
        )
        for entry in archive.entries.values()
    ]
    return hashlib.sha256(
        json.dumps(rows, separators=(",", ":"), ensure_ascii=True).encode("ascii")
    ).hexdigest()


def inspect_fo2_profile(
    install_root_path: Path,
    declared_version: str | None = None,
    recipe_path: Path | None = None,
) -> dict[str, object]:
    install_root = install_root_path.resolve()
    if not install_root.is_dir():
        raise FileNotFoundError(f"Fallout 2 install does not exist: {install_root}")
    recipe_path = (recipe_path or default_recipe_path()).resolve()
    recipe = _load_recipe(recipe_path)

    archives = []
    for raw in recipe["archives"]:
        requirement = dict(raw)
        source = _owned_file(install_root, str(requirement["file"]))
        archive = Dat2Archive(source)
        entries = list(archive.entries.values())
        if not entries:
            raise ValueError(f"Fallout 2 source archive has an empty DAT2 index: {source}")
        archives.append(
            {
                "file": source.name,
                "role": requirement["role"],
                "source": str(source),
                "bytes": source.stat().st_size,
                "sha256": file_sha256(source),
                "formatIdentity": {
                    "format": "fallout-dat2",
                    "byteOrder": "little-endian-directory-and-footer",
                    "footerBytes": DAT2_FOOTER_BYTES,
                    "dataBaseOffset": archive.data_base,
                    "dataRegionBytes": archive.tree_offset - archive.data_base,
                    "directoryOffset": archive.tree_offset,
                    "directoryBytes": archive.tree_size,
                    "directorySha256": archive.tree_sha256,
                    "indexSha256": _index_sha256(archive),
                    "entries": len(entries),
                    "compressedEntries": sum(entry.compressed for entry in entries),
                    "storedEntries": sum(not entry.compressed for entry in entries),
                    "decodedMemberBytes": sum(entry.uncompressed_size for entry in entries),
                    "storedMemberBytes": sum(entry.stored_size for entry in entries),
                    "firstMember": entries[0].logical_path,
                    "lastMember": entries[-1].logical_path,
                },
            }
        )

    identity = [
        (
            row["file"].casefold(),
            row["sha256"],
            row["formatIdentity"]["indexSha256"],
        )
        for row in archives
    ]
    source_profile_id = hashlib.sha256(
        PROFILE_PREFIX
        + json.dumps(identity, separators=(",", ":")).encode("ascii")
    ).hexdigest()
    presentations = {
        row["id"]: {
            "label": row["label"],
            "ready": False,
            "reason": recipe["firstSliceBlocker"],
        }
        for row in recipe["presentations"]
    }
    return {
        "schema": SCHEMA,
        "status": "registered-owned-install",
        "campaign": "Fallout2",
        "declaredVersion": declared_version,
        "sourceProfileId": source_profile_id,
        "saveCompatibilityId": f"fallout2:{source_profile_id}",
        "recipe": {
            "id": recipe["id"],
            "file": str(recipe_path),
            "sha256": file_sha256(recipe_path),
        },
        "install": {
            "root": str(install_root),
            "archives": archives,
        },
        "promotion": {
            "transported": False,
            "rendered": False,
            "interactive": False,
            "parityReviewed": False,
            "headsetAccepted": False,
        },
        "runtimeCompatibility": {
            "ready": False,
            "presentations": presentations,
            "firstSliceBlocker": recipe["firstSliceBlocker"],
        },
        "retailOrDerivedAssetsPackaged": False,
        "generatedCaches": [],
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Register a legally owned Fallout 2 install without copying assets."
    )
    parser.add_argument("--install-root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--declared-version")
    parser.add_argument("--recipe", type=Path, default=None)
    args = parser.parse_args()
    try:
        install_root = args.install_root.resolve()
        output = args.output.resolve()
        if output.is_relative_to(install_root):
            raise ValueError("Fallout 2 profile output must be outside the owned install")
        document = inspect_fo2_profile(
            install_root,
            args.declared_version,
            args.recipe,
        )
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_FO2_PROFILE_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_FO2_PROFILE "
        + json.dumps(
            {
                "manifest": str(output),
                "sourceProfileId": document["sourceProfileId"],
                "archives": len(document["install"]["archives"]),
                "members": sum(
                    row["formatIdentity"]["entries"]
                    for row in document["install"]["archives"]
                ),
                "runtimeReady": False,
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
