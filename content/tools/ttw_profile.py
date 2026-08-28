#!/usr/bin/env python3
"""Validate a user-generated Tale of Two Wastelands plugin profile."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

from corpus_io import atomic_json
from plugin_records import read_plugin_masters
from plugin_stack import file_sha256


SCHEMA = "opennv-ttw-profile/v1"
PLUGIN_SUFFIXES = frozenset({".esm", ".esp"})
ARCHIVE_SUFFIX = ".bsa"
REQUIREMENTS_SCHEMA = "opennv-ttw-profile-requirements/v1"
DEFAULT_REQUIREMENTS_PATH = (
    Path(__file__).resolve().parents[1] / "recipes" / "ttw-profile-v1.json"
)
LOAD_ORDER_COMMENT_PREFIX = "#"
ACTIVE_PLUGIN_PREFIX = "*"
PLUGIN_STACK_ID_PREFIX = "opennv-ttw-plugin-stack-v1\0"


def _decode_text(path: Path) -> str:
    payload = path.read_bytes()
    try:
        return payload.decode("utf-8-sig")
    except UnicodeDecodeError:
        return payload.decode("cp1252")


def read_active_load_order(path: Path) -> tuple[str, ...]:
    """Read an MO2 ``loadorder.txt`` or ``plugins.txt`` active plugin order."""

    if not path.is_file():
        raise FileNotFoundError(f"Load-order file does not exist: {path}")
    lines = [
        line.strip()
        for line in _decode_text(path).splitlines()
        if line.strip() and not line.lstrip().startswith(LOAD_ORDER_COMMENT_PREFIX)
    ]
    plugins_file = path.name.casefold() == "plugins.txt"
    uses_active_markers = any(line.startswith(ACTIVE_PLUGIN_PREFIX) for line in lines)
    names: list[str] = []
    for line in lines:
        active = line.startswith(ACTIVE_PLUGIN_PREFIX)
        if plugins_file and uses_active_markers and not active:
            continue
        name = line[1:].strip() if active else line
        if Path(name).suffix.casefold() not in PLUGIN_SUFFIXES:
            raise ValueError(
                f"Load-order entry is not a Fallout plugin: {name!r}; "
                "select the MO2 loadorder.txt or plugins.txt file"
            )
        names.append(name)
    if not names:
        raise ValueError(f"Load order contains no active plugins: {path}")
    folded = [name.casefold() for name in names]
    if len(set(folded)) != len(folded):
        raise ValueError("Load order contains duplicate plugin names")
    return tuple(names)


def _normalize_data_root(path: Path) -> Path:
    resolved = path.resolve()
    if not resolved.is_dir():
        raise FileNotFoundError(f"Profile data root does not exist: {resolved}")
    nested = resolved / "Data"
    if nested.is_dir() and any(
        child.is_file() and child.suffix.casefold() in PLUGIN_SUFFIXES
        for child in nested.iterdir()
    ):
        return nested
    return resolved


def normalize_data_roots(paths: list[Path]) -> tuple[Path, ...]:
    roots = tuple(_normalize_data_root(path) for path in paths)
    folded = [str(root).casefold() for root in roots]
    if len(set(folded)) != len(folded):
        raise ValueError("Profile contains a duplicate data root")
    return roots


def _effective_files(
    roots: tuple[Path, ...],
    suffixes: frozenset[str],
) -> dict[str, tuple[int, Path]]:
    winners: dict[str, tuple[int, Path]] = {}
    for root_index, root in enumerate(roots):
        local: dict[str, Path] = {}
        for path in root.iterdir():
            if not path.is_file() or path.suffix.casefold() not in suffixes:
                continue
            folded = path.name.casefold()
            if folded in local:
                raise ValueError(f"Data root has duplicate case-insensitive file: {path.name}")
            local[folded] = path
        for folded, path in local.items():
            winners[folded] = (root_index, path)
    return winners


def _plugin_stack_id(plugins: list[dict[str, object]]) -> str:
    identity = {
        "schema": SCHEMA,
        "plugins": [
            {
                "file": row["file"],
                "bytes": row["bytes"],
                "sha256": row["sha256"],
                "masters": row["masters"],
            }
            for row in plugins
        ],
    }
    encoded = json.dumps(identity, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(PLUGIN_STACK_ID_PREFIX.encode("ascii") + encoded).hexdigest()


def load_requirements(path: Path = DEFAULT_REQUIREMENTS_PATH) -> tuple[str, ...]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schema") != REQUIREMENTS_SCHEMA:
        raise ValueError(f"Unexpected TTW profile requirements schema: {path}")
    rows = document.get("requiredActivePlugins")
    if not isinstance(rows, list) or not rows or not all(isinstance(row, str) for row in rows):
        raise ValueError(f"TTW profile requirements have no plugin markers: {path}")
    folded = [row.casefold() for row in rows]
    if len(set(folded)) != len(folded):
        raise ValueError(f"TTW profile requirements contain duplicate plugins: {path}")
    return tuple(rows)


def inspect_ttw_profile(
    data_root_paths: list[Path],
    load_order_path: Path,
    declared_version: str | None = None,
) -> dict[str, object]:
    """Validate TTW markers and the exact effective plugin master closure."""

    roots = normalize_data_roots(data_root_paths)
    load_order = read_active_load_order(load_order_path.resolve())
    plugin_files = _effective_files(roots, PLUGIN_SUFFIXES)
    configured = {name.casefold(): index for index, name in enumerate(load_order)}
    required_plugins = load_requirements()

    missing_markers = [
        name
        for name in required_plugins
        if name.casefold() not in configured or name.casefold() not in plugin_files
    ]
    if missing_markers:
        raise ValueError(
            "Not a complete TTW profile; missing active generated plugins: "
            + ", ".join(missing_markers)
        )

    plugin_rows: list[dict[str, object]] = []
    for load_order_index, configured_name in enumerate(load_order):
        winner = plugin_files.get(configured_name.casefold())
        if winner is None:
            raise FileNotFoundError(
                f"Active plugin is absent from all profile data roots: {configured_name}"
            )
        root_index, path = winner
        masters = read_plugin_masters(path)
        for master in masters:
            master_index = configured.get(master.casefold())
            if master_index is None:
                raise ValueError(
                    f"{configured_name} requires inactive or absent master: {master}"
                )
            if master_index >= load_order_index:
                raise ValueError(
                    f"{configured_name} master is not earlier in load order: {master}"
                )
        plugin_rows.append(
            {
                "file": configured_name,
                "loadOrderIndex": load_order_index,
                "sourceRootIndex": root_index,
                "bytes": path.stat().st_size,
                "sha256": file_sha256(path),
                "masters": list(masters),
            }
        )

    archive_files = _effective_files(roots, frozenset({ARCHIVE_SUFFIX}))
    archive_rows = [
        {
            "file": path.name,
            "sourceRootIndex": root_index,
            "bytes": path.stat().st_size,
            "admission": "discovered-not-yet-compiled",
        }
        for root_index, path in sorted(
            archive_files.values(), key=lambda value: value[1].name.casefold()
        )
    ]
    stack_id = _plugin_stack_id(plugin_rows)
    document: dict[str, object] = {
        "schema": SCHEMA,
        "status": "validated-generated-plugin-profile",
        "kind": "ttw",
        "sourceRoots": [str(root) for root in roots],
        "loadOrderSource": {
            "file": str(load_order_path.resolve()),
            "sha256": file_sha256(load_order_path.resolve()),
        },
        "declaredTtwVersion": declared_version,
        "pluginStackId": stack_id,
        "saveCompatibilityId": f"ttw:{stack_id}",
        "plugins": plugin_rows,
        "archives": archive_rows,
        "runtimeCompatibility": {
            "ready": False,
            "reason": (
                "TTW profile recognition passes, but TTW record, archive, script, "
                "and world-transition compilation is not implemented yet."
            ),
        },
    }
    return document


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Validate one locally generated TTW profile without copying or modifying it."
        )
    )
    parser.add_argument(
        "--data-root",
        action="append",
        type=Path,
        required=True,
        help=(
            "Effective data layer in low-to-high precedence order; repeat for the "
            "base New Vegas Data folder and each MO2 mod/output folder."
        ),
    )
    parser.add_argument("--load-order", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--ttw-version")
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        input_roots = normalize_data_roots(args.data_root)
        if any(output.is_relative_to(root) for root in input_roots):
            raise ValueError("Profile manifest output must be outside every owned data root")
        document = inspect_ttw_profile(
            list(input_roots),
            args.load_order,
            args.ttw_version,
        )
        output.parent.mkdir(parents=True, exist_ok=True)
        atomic_json(output, document)
    except Exception as error:
        print(f"OPENNV_TTW_PROFILE_ERROR {error}", file=sys.stderr)
        return 2
    print(
        "OPENNV_TTW_PROFILE "
        + json.dumps(
            {
                "manifest": str(output),
                "plugins": len(document["plugins"]),
                "pluginStackId": document["pluginStackId"],
                "runtimeReady": document["runtimeCompatibility"]["ready"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
