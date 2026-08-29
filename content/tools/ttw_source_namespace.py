#!/usr/bin/env python3
"""Inventory the bounded effective top-level namespace of a TTW profile."""

from __future__ import annotations

import argparse
import json
import struct
import sys
from pathlib import Path

from corpus_io import atomic_json
from plugin_records import read_plugin_masters
from plugin_stack import file_sha256
from ttw_profile import (
    ARCHIVE_SUFFIX,
    PLUGIN_SUFFIXES,
    SCHEMA as PROFILE_SCHEMA,
    plugin_stack_id,
    read_active_load_order,
)


SCHEMA = "opennv-ttw-effective-source-namespace/v1"
STATUS = "validated-neutral-effective-source-namespace"
PROFILE_STATUS = "validated-generated-plugin-profile"
RESOLUTION_POLICY = "top-level-case-insensitive-last-data-root-wins"
OVERRIDE_SUFFIX = ".override"
BSA_MAGIC = b"BSA\0"
BSA_VERSION = 104
BSA_HEADER = struct.Struct("<4s8I")
BSA_DIRECTORY_NAMES_FLAG = 0x0001
BSA_FILE_NAMES_FLAG = 0x0002
SHA256_HEX_CHARACTERS = 64


def _is_sha256(value: object) -> bool:
    return (
        isinstance(value, str)
        and len(value) == SHA256_HEX_CHARACTERS
        and all(character in "0123456789abcdef" for character in value)
    )


def _profile_roots(profile: dict[str, object]) -> tuple[Path, ...]:
    rows = profile.get("sourceRoots")
    if not isinstance(rows, list) or not rows or not all(
        isinstance(row, str) for row in rows
    ):
        raise ValueError("TTW profile has no source roots")
    roots = tuple(Path(row).resolve() for row in rows)
    if len({str(root).casefold() for root in roots}) != len(roots):
        raise ValueError("TTW profile contains duplicate source roots")
    for root in roots:
        if not root.is_dir():
            raise FileNotFoundError(f"TTW profile source root is missing: {root}")
    return roots


def _winning_top_level_files(
    roots: tuple[Path, ...],
) -> dict[str, tuple[int, Path]]:
    winners: dict[str, tuple[int, Path]] = {}
    for root_index, root in enumerate(roots):
        local: dict[str, Path] = {}
        for path in root.iterdir():
            if not path.is_file():
                continue
            folded = path.name.casefold()
            if folded in local:
                raise ValueError(
                    f"TTW source root has duplicate case-insensitive file: {path.name}"
                )
            local[folded] = path
        for folded, path in local.items():
            winners[folded] = (root_index, path)
    return winners


def _safe_profile_file_name(value: object) -> str:
    if not isinstance(value, str) or not value or Path(value).name != value:
        raise ValueError(f"TTW profile has an invalid top-level file name: {value!r}")
    return value


def _validate_profile_plugins(
    profile: dict[str, object],
    roots: tuple[Path, ...],
    winners: dict[str, tuple[int, Path]],
) -> list[dict[str, object]]:
    load_order_row = profile.get("loadOrderSource")
    if not isinstance(load_order_row, dict):
        raise ValueError("TTW profile has no load-order source")
    load_order_file = load_order_row.get("file")
    load_order_sha256 = load_order_row.get("sha256")
    if not isinstance(load_order_file, str) or not _is_sha256(load_order_sha256):
        raise ValueError("TTW profile load-order source is invalid")
    load_order_path = Path(load_order_file).resolve()
    if not load_order_path.is_file():
        raise FileNotFoundError(f"TTW load-order snapshot is missing: {load_order_path}")
    if file_sha256(load_order_path) != load_order_sha256:
        raise ValueError("TTW load-order snapshot hash changed")

    rows = profile.get("plugins")
    if not isinstance(rows, list) or not rows:
        raise ValueError("TTW profile contains no plugins")
    load_order = read_active_load_order(load_order_path)
    configured_names: list[str] = []
    validated: list[dict[str, object]] = []
    for expected_index, raw_row in enumerate(rows):
        if not isinstance(raw_row, dict):
            raise ValueError("TTW profile plugin row is invalid")
        name = _safe_profile_file_name(raw_row.get("file"))
        source_root_index = raw_row.get("sourceRootIndex")
        if (
            not isinstance(source_root_index, int)
            or isinstance(source_root_index, bool)
            or not 0 <= source_root_index < len(roots)
        ):
            raise ValueError(f"TTW plugin has an invalid source root: {name}")
        if raw_row.get("loadOrderIndex") != expected_index:
            raise ValueError(f"TTW plugin load-order index changed: {name}")
        winner = winners.get(name.casefold())
        if winner is None or winner[0] != source_root_index:
            raise ValueError(f"TTW plugin effective source changed: {name}")
        path = winner[1]
        expected_bytes = raw_row.get("bytes")
        expected_sha256 = raw_row.get("sha256")
        if (
            not isinstance(expected_bytes, int)
            or isinstance(expected_bytes, bool)
            or expected_bytes < 1
            or path.stat().st_size != expected_bytes
        ):
            raise ValueError(f"TTW plugin size changed: {name}")
        if not _is_sha256(expected_sha256) or file_sha256(path) != expected_sha256:
            raise ValueError(f"TTW plugin hash changed: {name}")
        masters = read_plugin_masters(path)
        recorded_masters = raw_row.get("masters")
        if not isinstance(recorded_masters, list) or list(masters) != recorded_masters:
            raise ValueError(f"TTW plugin master list changed: {name}")
        configured_names.append(name)
        validated.append(
            {
                "file": name,
                "loadOrderIndex": expected_index,
                "sourceRootIndex": source_root_index,
                "bytes": expected_bytes,
                "sha256": expected_sha256,
                "masters": recorded_masters,
            }
        )
    if tuple(configured_names) != load_order:
        raise ValueError("TTW load-order snapshot differs from the registered plugin rows")
    expected_stack_id = plugin_stack_id(validated)
    if profile.get("pluginStackId") != expected_stack_id:
        raise ValueError("TTW profile plugin-stack identity changed")
    if profile.get("saveCompatibilityId") != f"ttw:{expected_stack_id}":
        raise ValueError("TTW profile save-compatibility identity changed")
    return validated


def _bsa_header(path: Path) -> dict[str, object]:
    with path.open("rb") as stream:
        payload = stream.read(BSA_HEADER.size)
    if len(payload) != BSA_HEADER.size:
        raise ValueError(f"TTW BSA header is truncated: {path.name}")
    (
        magic,
        version,
        folder_records_offset,
        archive_flags,
        folder_count,
        file_count,
        total_folder_name_bytes,
        total_file_name_bytes,
        file_flags,
    ) = BSA_HEADER.unpack(payload)
    if magic != BSA_MAGIC or version != BSA_VERSION:
        raise ValueError(
            f"TTW archive is not a Fallout BSA v{BSA_VERSION}: {path.name}"
        )
    required_name_flags = BSA_DIRECTORY_NAMES_FLAG | BSA_FILE_NAMES_FLAG
    if archive_flags & required_name_flags != required_name_flags:
        raise ValueError(f"TTW BSA omits its member names: {path.name}")
    if folder_records_offset < BSA_HEADER.size:
        raise ValueError(f"TTW BSA folder records overlap its header: {path.name}")
    return {
        "version": version,
        "folderRecordsOffset": folder_records_offset,
        "archiveFlags": archive_flags,
        "folderCount": folder_count,
        "fileCount": file_count,
        "totalFolderNameBytes": total_folder_name_bytes,
        "totalFileNameBytes": total_file_name_bytes,
        "fileFlags": file_flags,
    }


def _inventory_row(root_index: int, path: Path) -> dict[str, object]:
    return {
        "file": path.name,
        "sourceRootIndex": root_index,
        "bytes": path.stat().st_size,
        "sha256": file_sha256(path),
    }


def inspect_ttw_source_namespace(profile_path: Path) -> dict[str, object]:
    """Revalidate a registered profile and inventory its bounded winners."""

    resolved_profile = profile_path.resolve()
    profile = json.loads(resolved_profile.read_text(encoding="utf-8"))
    if (
        not isinstance(profile, dict)
        or profile.get("schema") != PROFILE_SCHEMA
        or profile.get("status") != PROFILE_STATUS
        or profile.get("kind") != "ttw"
    ):
        raise ValueError(f"Not a validated TTW profile: {resolved_profile}")
    roots = _profile_roots(profile)
    winners = _winning_top_level_files(roots)
    plugins = _validate_profile_plugins(profile, roots, winners)

    archive_rows: list[dict[str, object]] = []
    marker_rows: list[dict[str, object]] = []
    loose_rows: list[dict[str, object]] = []
    for root_index, path in sorted(
        winners.values(), key=lambda value: value[1].name.casefold()
    ):
        suffix = path.suffix.casefold()
        if suffix in PLUGIN_SUFFIXES:
            continue
        row = _inventory_row(root_index, path)
        if suffix == ARCHIVE_SUFFIX:
            row["header"] = _bsa_header(path)
            row["admission"] = "v104-header-validated-members-not-resolved"
            archive_rows.append(row)
        elif suffix == OVERRIDE_SUFFIX:
            if path.stat().st_size != 0:
                raise ValueError(
                    f"TTW override marker is nonempty and has unsupported semantics: "
                    f"{path.name}"
                )
            row["admission"] = "zero-byte-marker-recorded-not-applied"
            marker_rows.append(row)
        else:
            row["admission"] = "top-level-loose-file-inventoried-not-compiled"
            loose_rows.append(row)

    recorded_archives = profile.get("archives")
    expected_archive_sources = [
        (row["file"].casefold(), row["sourceRootIndex"], row["bytes"])
        for row in archive_rows
    ]
    if not isinstance(recorded_archives, list) or [
        (
            str(row.get("file", "")).casefold(),
            row.get("sourceRootIndex"),
            row.get("bytes"),
        )
        for row in recorded_archives
        if isinstance(row, dict)
    ] != expected_archive_sources:
        raise ValueError("TTW effective BSA winners differ from the registered profile")

    return {
        "schema": SCHEMA,
        "status": STATUS,
        "sourceProfile": {
            "file": str(resolved_profile),
            "bytes": resolved_profile.stat().st_size,
            "sha256": file_sha256(resolved_profile),
            "pluginStackId": profile["pluginStackId"],
            "saveCompatibilityId": profile["saveCompatibilityId"],
        },
        "sourceRoots": [str(root) for root in roots],
        "resolutionPolicy": RESOLUTION_POLICY,
        "scope": {
            "plugins": "hash-and-master-revalidated",
            "archives": "winning-top-level-bsa-v104-headers-only",
            "looseFiles": "winning-top-level-files-only",
            "overrideMarkers": "winning-zero-byte-markers-recorded-not-applied",
        },
        "plugins": plugins,
        "archives": archive_rows,
        "looseFiles": loose_rows,
        "overrideMarkers": marker_rows,
        "runtimeCompatibility": {
            "ready": False,
            "reason": (
                "The effective top-level TTW source namespace is inventoried, but "
                "archive members, nested loose files, override-member semantics, "
                "records, scripts, and runtime behavior are not compiled."
            ),
        },
        "unsupportedSemantics": [
            "bsa-member-precedence-and-extraction",
            "nested-loose-file-precedence",
            "override-marker-member-semantics",
            "plugin-record-override-merge",
            "script-command-and-event-execution",
            "ttw-world-and-save-runtime",
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Revalidate a TTW profile and inventory its bounded effective top-level "
            "source namespace without copying owned content."
        )
    )
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        document = inspect_ttw_source_namespace(args.profile)
        roots = tuple(Path(row).resolve() for row in document["sourceRoots"])
        if any(output.is_relative_to(root) for root in roots):
            raise ValueError(
                "TTW source-namespace output must be outside every owned data root"
            )
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_TTW_SOURCE_NAMESPACE_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_TTW_SOURCE_NAMESPACE "
        + json.dumps(
            {
                "manifest": str(output),
                "plugins": len(document["plugins"]),
                "archives": len(document["archives"]),
                "looseFiles": len(document["looseFiles"]),
                "overrideMarkers": len(document["overrideMarkers"]),
                "runtimeReady": document["runtimeCompatibility"]["ready"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
