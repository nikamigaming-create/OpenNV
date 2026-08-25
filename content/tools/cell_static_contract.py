"""Schemas, policies, paths, and value contracts for static CELL compilation."""

from __future__ import annotations

import hashlib
import json
import math
import platform
import sys
from importlib.metadata import version
from pathlib import Path

from bsa_archive import canonical_member_path


PROFILE_SCHEMA = "opennv-static-cell-compiler-profile/v1"
OUTPUT_SCHEMA = "opennv-static-cell-compile/v1"
MANIFEST_SCHEMA = "opennv-static-cell-compile-manifest/v1"
MANIFEST_FILE_NAME = "manifest.json"
CELL_FILE_NAME = "cell-static.json"
ASSETS_FILE_NAME = "assets.jsonl"
TEXTURES_FILE_NAME = "textures.jsonl"
BLOCKERS_FILE_NAME = "blockers.jsonl"
INTERIOR_ORIGIN_POLICY = "game-origin"
EXTERIOR_ORIGIN_POLICY = "cell-grid-origin"
BLOCKED_POLICY = "blocked"
POSITION_COMPONENTS = 3
MESH_ROOT = "meshes"
PASS_PRESENTATION_STATUS = "static-assets-compiled-runtime-pending"
BLOCKED_PRESENTATION_STATUS = "static-assets-compiled-with-blockers"
COMPILED_REFERENCE_STATUS = "compiled-static-reference"
BLOCKED_REFERENCE_STATUS = "blocked"
STATIC_RUNTIME_PENDING_REFERENCE_STATUS = "static-presentation-runtime-pending"
STATIC_COMPILER_SOURCE_NAMES = (
    "actor_material.py",
    "bsa_archive.py",
    "cell_scene.py",
    "cell_static_compile.py",
    "cell_static_contract.py",
    "cell_static_source.py",
    "corpus_io.py",
    "export_static_nif_gltf.py",
    "gltf_io.py",
    "havok_collision_gltf.py",
    "material_contract.py",
    "owned_archive_stack.py",
    "plugin_stack.py",
    "runtime_configuration.py",
    "texture_pipeline.py",
    "validate_cell_compile_plan.py",
    "validate_cell_parity_corpus.py",
)
PROFILE_REQUIRED_FIELDS = {
    "schema",
    "id",
    "archiveRecipe",
    "supportedChildRecordTypes",
    "supportedBaseRecordTypes",
    "modelExtension",
    "exportStrict",
    "compileTextures",
    "originPolicy",
    "statePolicy",
    "promotion",
}
PROFILE_OPTIONAL_FIELDS = {"textureAliases"}
PROMOTION_POLICY = {
    "compiledStaticPresentationIsNotRuntimeOrParity": True,
    "anyChildBlockerBlocksCellReadiness": True,
    "anyAssetOrTextureFailureBlocksCellReadiness": True,
}
TOOLCHAIN_PACKAGES = ("Pillow", "PyFFI")


def canonical_sha256(document: object) -> str:
    payload = json.dumps(document, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def toolchain_manifest() -> dict[str, object]:
    return {
        "pythonImplementation": platform.python_implementation(),
        "pythonVersion": platform.python_version(),
        "packages": {name: version(name) for name in TOOLCHAIN_PACKAGES},
    }


def load_profile(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("schema") != PROFILE_SCHEMA:
        raise ValueError(f"Unexpected static CELL compiler profile: {path}")
    fields = set(document)
    if (
        not PROFILE_REQUIRED_FIELDS.issubset(fields)
        or not fields.issubset(PROFILE_REQUIRED_FIELDS | PROFILE_OPTIONAL_FIELDS)
        or not str(document.get("id", "")).strip()
    ):
        raise ValueError("Static CELL compiler profile fields differ")
    for field in ("supportedChildRecordTypes", "supportedBaseRecordTypes"):
        values = document.get(field)
        if (
            not isinstance(values, list)
            or not values
            or any(not isinstance(value, str) or not value for value in values)
            or len(values) != len(set(values))
        ):
            raise ValueError(f"Static CELL compiler profile has invalid {field}")
    extension = document.get("modelExtension")
    if (
        not isinstance(extension, str)
        or not extension.startswith(".")
        or extension != extension.casefold()
    ):
        raise ValueError("Static CELL compiler profile model extension is invalid")
    if not isinstance(document.get("exportStrict"), bool):
        raise ValueError("Static CELL compiler profile exportStrict is invalid")
    if not isinstance(document.get("compileTextures"), bool):
        raise ValueError("Static CELL compiler profile compileTextures is invalid")
    origin = document.get("originPolicy")
    if (
        not isinstance(origin, dict)
        or set(origin) != {"interior", "exterior", "exteriorCellSizeGameUnits"}
        or origin.get("interior") != INTERIOR_ORIGIN_POLICY
        or origin.get("exterior") != EXTERIOR_ORIGIN_POLICY
        or not isinstance(origin.get("exteriorCellSizeGameUnits"), (int, float))
        or float(origin["exteriorCellSizeGameUnits"]) <= 0.0
    ):
        raise ValueError("Static CELL compiler profile origin policy is invalid")
    state = document.get("statePolicy")
    if not isinstance(state, dict) or state != {
        "initiallyDisabled": BLOCKED_POLICY,
        "enableParent": BLOCKED_POLICY,
        "teleport": BLOCKED_POLICY,
    }:
        raise ValueError("Static CELL compiler profile state policy is invalid")
    if document.get("promotion") != PROMOTION_POLICY:
        raise ValueError("Static CELL compiler promotion policy is invalid")
    aliases = document.get("textureAliases", {})
    if not isinstance(aliases, dict):
        raise ValueError("Static CELL compiler texture aliases are invalid")
    for source, target in aliases.items():
        if (
            not isinstance(source, str)
            or not isinstance(target, str)
            or canonical_member_path(source) != source
            or canonical_member_path(target) != target
        ):
            raise ValueError("Static CELL compiler texture alias is not canonical")
    archive_recipe = document.get("archiveRecipe")
    if (
        not isinstance(archive_recipe, str)
        or Path(archive_recipe).name != archive_recipe
        or not archive_recipe.endswith(".json")
    ):
        raise ValueError("Static CELL compiler archive recipe is invalid")
    return document


def default_profile_path() -> Path:
    return _recipes_root() / "fnv-static-cell-compiler-v1.json"


def default_plan_recipe_path() -> Path:
    return _recipes_root() / "fnv-cell-compile-plan-v1.json"


def recipe_path(file_name: str) -> Path:
    return _recipes_root() / file_name


def _recipes_root() -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / "recipes"


def cell_origin(cell: dict[str, object], profile: dict[str, object]) -> tuple[float, float, float]:
    if cell["interior"]:
        return (0.0, 0.0, 0.0)
    coordinates = cell.get("coordinates")
    if not isinstance(coordinates, list) or len(coordinates) != 2:
        raise ValueError(f"Exterior CELL coordinates are missing: {cell['formKey']}")
    size = float(profile["originPolicy"]["exteriorCellSizeGameUnits"])
    return (float(coordinates[0]) * size, float(coordinates[1]) * size, 0.0)


def mesh_member_path(model_path: str) -> str:
    canonical = canonical_member_path(model_path)
    return canonical if canonical.startswith(f"{MESH_ROOT}\\") else f"{MESH_ROOT}\\{canonical}"


def blocker(
    scope: str,
    owner: str,
    reason: str,
    detail: str | None = None,
) -> dict[str, object]:
    return {"scope": scope, "owner": owner, "reason": reason, "detail": detail}


def relative_output(path: Path, staging_root: Path) -> str:
    return path.relative_to(staging_root).as_posix()


def child_transform(
    child: dict[str, object],
) -> tuple[
    tuple[float, float, float] | None,
    tuple[float, float, float] | None,
    str | None,
]:
    transform = child.get("transformGameUnits")
    if not isinstance(transform, dict):
        return None, None, "missing-transform"
    try:
        position = tuple(float(value) for value in transform["position"])
        rotation = tuple(float(value) for value in transform["rotation_radians"])
    except (KeyError, TypeError, ValueError):
        return None, None, "invalid-transform"
    if (
        len(position) != POSITION_COMPONENTS
        or len(rotation) != POSITION_COMPONENTS
        or not all(math.isfinite(value) for value in (*position, *rotation))
    ):
        return None, None, "invalid-transform"
    return position, rotation, None


def stable_exception_detail(error: Exception, transient_root: Path) -> str:
    message = str(error)
    root_text = str(transient_root.resolve())
    message = message.replace(root_text, "<staging>")
    message = message.replace(root_text.replace("\\", "/"), "<staging>")
    return f"{type(error).__name__}: {message}"
