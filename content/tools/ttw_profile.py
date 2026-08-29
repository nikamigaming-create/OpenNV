#!/usr/bin/env python3
"""Validate a user-generated Tale of Two Wastelands plugin profile."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

from corpus_io import atomic_bytes, atomic_json
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
FLATTENED_ORDER_MODE = "flattened-installer-output-plugin-mtime"
LOAD_ORDER_SNAPSHOT_SUFFIX = ".loadorder.txt"


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


def plugin_stack_id(plugins: list[dict[str, object]]) -> str:
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


def derive_flattened_installer_load_order(
    data_root_path: Path,
) -> tuple[tuple[str, ...], tuple[dict[str, object], ...]]:
    """Derive an all-active plugin order from a flattened installer output.

    TTW installer outputs encode their plugin order with strictly increasing
    modification times.  This mode is deliberately narrower than a mod-manager
    profile: every top-level plugin is active, duplicate timestamps are
    ambiguous, and every declared master must already precede its dependent.
    """

    root = _normalize_data_root(data_root_path)
    plugin_files = _effective_files((root,), PLUGIN_SUFFIXES)
    if not plugin_files:
        raise ValueError(f"Flattened installer output contains no plugins: {root}")
    ordered = sorted(
        (path for _, path in plugin_files.values()),
        key=lambda path: (path.stat().st_mtime_ns, path.name.casefold()),
    )
    mtimes = [path.stat().st_mtime_ns for path in ordered]
    if len(set(mtimes)) != len(mtimes):
        duplicates = sorted(
            {
                timestamp
                for timestamp in mtimes
                if mtimes.count(timestamp) > 1
            }
        )
        raise ValueError(
            "Flattened installer plugin modification times are not strictly ordered: "
            + ", ".join(str(value) for value in duplicates)
        )
    load_order = tuple(path.name for path in ordered)
    configured = {name.casefold(): index for index, name in enumerate(load_order)}
    missing_markers = [
        name for name in load_requirements() if name.casefold() not in configured
    ]
    if missing_markers:
        raise ValueError(
            "Not a complete TTW profile; missing active generated plugins: "
            + ", ".join(missing_markers)
        )
    evidence: list[dict[str, object]] = []
    for load_order_index, path in enumerate(ordered):
        masters = read_plugin_masters(path)
        for master in masters:
            master_index = configured.get(master.casefold())
            if master_index is None:
                raise ValueError(f"{path.name} requires absent master: {master}")
            if master_index >= load_order_index:
                raise ValueError(
                    f"{path.name} master is not earlier in flattened installer order: "
                    f"{master}"
                )
        evidence.append(
            {
                "file": path.name,
                "lastWriteTimeNs": path.stat().st_mtime_ns,
            }
        )
    return load_order, tuple(evidence)


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
    resolved_load_order = load_order_path.resolve()
    load_order = read_active_load_order(resolved_load_order)
    return inspect_ttw_profile_order(
        roots,
        load_order,
        resolved_load_order,
        declared_version,
    )


def inspect_ttw_profile_order(
    roots: tuple[Path, ...],
    load_order: tuple[str, ...],
    load_order_path: Path,
    declared_version: str | None = None,
    load_order_derivation: dict[str, object] | None = None,
) -> dict[str, object]:
    """Validate one already decoded active order over normalized roots."""

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
    stack_id = plugin_stack_id(plugin_rows)
    load_order_source: dict[str, object] = {
        "file": str(load_order_path.resolve()),
        "sha256": file_sha256(load_order_path.resolve()),
    }
    if load_order_derivation is not None:
        load_order_source["derivation"] = load_order_derivation
    document: dict[str, object] = {
        "schema": SCHEMA,
        "status": "validated-generated-plugin-profile",
        "kind": "ttw",
        "sourceRoots": [str(root) for root in roots],
        "loadOrderSource": load_order_source,
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
        help=(
            "Effective data layer in low-to-high precedence order; repeat for the "
            "base New Vegas Data folder and each MO2 mod/output folder. With "
            "--flattened-installer-output these are lower source layers."
        ),
    )
    parser.add_argument("--load-order", type=Path)
    parser.add_argument(
        "--flattened-installer-output",
        type=Path,
        help=(
            "Single flattened TTW installer output whose top-level plugin mtimes "
            "encode one strictly ordered all-active load order; it is always the "
            "highest-precedence source root."
        ),
    )
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--ttw-version")
    args = parser.parse_args()
    try:
        output = args.output.resolve()
        if args.flattened_installer_output is not None:
            if args.load_order is not None:
                raise ValueError(
                    "--flattened-installer-output cannot be combined with --load-order"
                )
            input_roots = normalize_data_roots(
                [*(args.data_root or []), args.flattened_installer_output]
            )
            flattened_root = _normalize_data_root(args.flattened_installer_output)
            if input_roots[-1] != flattened_root:
                raise ValueError(
                    "Flattened installer output must be the highest-precedence data root"
                )
        else:
            if not args.data_root or args.load_order is None:
                raise ValueError(
                    "Layered profile registration requires --data-root and --load-order"
                )
            input_roots = normalize_data_roots(args.data_root)
        if any(output.is_relative_to(root) for root in input_roots):
            raise ValueError("Profile manifest output must be outside every owned data root")
        output.parent.mkdir(parents=True, exist_ok=True)
        if args.flattened_installer_output is not None:
            load_order, order_evidence = derive_flattened_installer_load_order(
                input_roots[-1]
            )
            snapshot = output.with_name(output.stem + LOAD_ORDER_SNAPSHOT_SUFFIX)
            if any(snapshot.is_relative_to(root) for root in input_roots):
                raise ValueError(
                    "Load-order snapshot output must be outside every owned data root"
                )
            atomic_bytes(
                snapshot,
                ("\n".join(load_order) + "\n").encode("utf-8"),
            )
            document = inspect_ttw_profile_order(
                input_roots,
                load_order,
                snapshot,
                args.ttw_version,
                {
                    "mode": FLATTENED_ORDER_MODE,
                    "allPluginsActive": True,
                    "strictlyIncreasingPluginModificationTimes": True,
                    "flattenedSourceRootIndex": len(input_roots) - 1,
                    "plugins": list(order_evidence),
                },
            )
            flattened_root_index = len(input_roots) - 1
            if any(
                row["sourceRootIndex"] != flattened_root_index
                for row in document["plugins"]
            ):
                raise ValueError(
                    "A flattened installer plugin is shadowed by an ambiguous data root"
                )
        else:
            document = inspect_ttw_profile(
                list(input_roots),
                args.load_order,
                args.ttw_version,
            )
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
