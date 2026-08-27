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
from runtime_configuration import configured_recipe_path


PROFILE_SCHEMA = "opennv-static-cell-compiler-profile/v2"
OUTPUT_SCHEMA = "opennv-static-cell-compile/v2"
MANIFEST_SCHEMA = "opennv-static-cell-compile-manifest/v2"
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
COMPILED_LIGHT_REFERENCE_STATUS = "compiled-point-light-reference"
COMPILED_LANDSCAPE_REFERENCE_STATUS = "compiled-landscape-reference"
ACCOUNTED_NONVISUAL_REFERENCE_STATUS = "accounted-nonvisual-reference"
BLOCKED_REFERENCE_STATUS = "blocked"
STATIC_RUNTIME_PENDING_REFERENCE_STATUS = "static-presentation-runtime-pending"
STATIC_MODEL_PRESENTATION_KIND = "static-model"
POINT_LIGHT_PRESENTATION_KIND = "point-light"
LANDSCAPE_PRESENTATION_KIND = "landscape"
NONVISUAL_PRESENTATION_KIND = "nonvisual"
STATIC_NIF_ASSET_KIND = "static-nif"
LANDSCAPE_ASSET_KIND = "landscape"
OWNED_DDS_TEXTURE_KIND = "owned-dds"
LANDSCAPE_TEXTURE_KIND = "landscape-bake"
LANDSCAPE_RUNTIME_TEXTURE_KIND = "landscape-runtime"
PRESENTATION_KINDS = {
    STATIC_MODEL_PRESENTATION_KIND,
    POINT_LIGHT_PRESENTATION_KIND,
    LANDSCAPE_PRESENTATION_KIND,
    NONVISUAL_PRESENTATION_KIND,
}
LIGHT_COLOR_COMPONENTS = 3
BYTE_CHANNEL_MINIMUM = 0
BYTE_CHANNEL_MAXIMUM = 255
STATIC_COMPILER_SOURCE_NAMES = (
    "actor_material.py",
    "bsa_archive.py",
    "cell_landscape_contract.py",
    "cell_scene.py",
    "cell_landscape_compile.py",
    "cell_static_compile.py",
    "cell_static_contract.py",
    "cell_static_source.py",
    "corpus_io.py",
    "export_static_nif_gltf.py",
    "gltf_io.py",
    "havok_collision_gltf.py",
    "landscape_catalog.py",
    "landscape_gltf.py",
    "landscape_stack.py",
    "material_contract.py",
    "nif_decoder.py",
    "owned_archive_stack.py",
    "plugin_records.py",
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
    "baseLinkedChildRecordTypes",
    "supportedBaseRecordTypes",
    "presentationPolicies",
    "childPresentationPolicies",
    "modelExtension",
    "exportStrict",
    "compileTextures",
    "landscapeMissingBasePolicy",
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
FORM_ID_HEX_CHARACTERS = 8
FORM_ID_RADIX = 16


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
    base_linked_children = document.get("baseLinkedChildRecordTypes")
    if (
        not isinstance(base_linked_children, list)
        or base_linked_children != sorted(set(base_linked_children))
        or not set(base_linked_children) <= set(document["supportedChildRecordTypes"])
    ):
        raise ValueError("Static CELL compiler base-linked child types are invalid")
    policies = document.get("presentationPolicies")
    if not isinstance(policies, dict) or set(policies) != set(
        document["supportedBaseRecordTypes"]
    ):
        raise ValueError("Static CELL compiler presentation policies differ")
    for record_type, policy in policies.items():
        if (
            not isinstance(policy, dict)
            or set(policy)
            != {"kind", "modelPathCount", "supportedReferenceSubrecords"}
            or policy.get("kind") not in PRESENTATION_KINDS
            or not isinstance(policy.get("modelPathCount"), int)
            or int(policy["modelPathCount"]) < 0
        ):
            raise ValueError(
                f"Static CELL compiler presentation policy is invalid: {record_type}"
            )
        subrecords = policy.get("supportedReferenceSubrecords")
        if (
            not isinstance(subrecords, list)
            or not subrecords
            or subrecords != sorted(set(subrecords))
            or any(not isinstance(value, str) or not value for value in subrecords)
        ):
            raise ValueError(
                f"Static CELL compiler reference subrecords are invalid: {record_type}"
            )
    child_policies = document.get("childPresentationPolicies")
    expected_direct_children = set(document["supportedChildRecordTypes"]) - set(
        base_linked_children
    )
    if not isinstance(child_policies, dict) or set(child_policies) != expected_direct_children:
        raise ValueError("Static CELL compiler child presentation policies differ")
    for record_type, policy in child_policies.items():
        if (
            not isinstance(policy, dict)
            or set(policy) != {"kind", "supportedChildSubrecords"}
            or policy.get("kind") not in PRESENTATION_KINDS
        ):
            raise ValueError(
                f"Static CELL compiler child presentation policy is invalid: {record_type}"
            )
        subrecords = policy.get("supportedChildSubrecords")
        if (
            not isinstance(subrecords, list)
            or not subrecords
            or subrecords != sorted(set(subrecords))
            or any(not isinstance(value, str) or not value for value in subrecords)
        ):
            raise ValueError(
                f"Static CELL compiler child subrecords are invalid: {record_type}"
            )
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
    missing_base = document.get("landscapeMissingBasePolicy")
    if (
        not isinstance(missing_base, dict)
        or set(missing_base)
        != {"mode", "ltexRawFormId", "expectedEditorId", "provenance"}
        or missing_base.get("mode") != "owned-game-default-ltex"
        or not isinstance(missing_base.get("ltexRawFormId"), str)
        or len(str(missing_base["ltexRawFormId"])) != FORM_ID_HEX_CHARACTERS
        or any(
            character not in "0123456789abcdefABCDEF"
            for character in str(missing_base["ltexRawFormId"])
        )
        or int(str(missing_base["ltexRawFormId"]), FORM_ID_RADIX) == 0
        or not str(missing_base.get("expectedEditorId", "")).strip()
        or not isinstance(missing_base.get("provenance"), dict)
    ):
        raise ValueError("Static CELL compiler missing LAND base policy is invalid")
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
    return configured_recipe_path("staticCellCompiler")


def default_plan_recipe_path() -> Path:
    return configured_recipe_path("cellCompilePlan")


def recipe_path(file_name: str) -> Path:
    return _recipes_root() / file_name


def presentation_policy(
    profile: dict[str, object],
    record_type: str,
) -> dict[str, object] | None:
    policies = profile["presentationPolicies"]
    assert isinstance(policies, dict)
    policy = policies.get(record_type)
    return policy if isinstance(policy, dict) else None


def child_presentation_policy(
    profile: dict[str, object],
    record_type: str,
) -> dict[str, object] | None:
    policies = profile["childPresentationPolicies"]
    assert isinstance(policies, dict)
    policy = policies.get(record_type)
    return policy if isinstance(policy, dict) else None


def compiled_light_contract(
    base: dict[str, object],
    child: dict[str, object],
    world_units_to_meters: float,
) -> dict[str, object]:
    light = base.get("light")
    required = {
        "radiusGameUnits",
        "colorRgb",
        "lightFlags",
        "falloff",
        "fieldOfViewDegrees",
        "intensity",
    }
    if not isinstance(light, dict) or set(light) != required:
        raise ValueError(f"LIGH base contract differs: {base['formKey']}")
    if not math.isfinite(world_units_to_meters) or world_units_to_meters <= 0.0:
        raise ValueError("LIGH world unit scale is invalid")
    try:
        base_radius = float(light["radiusGameUnits"])
        reference_value = child.get("radiusGameUnits")
        reference_radius = (
            None if reference_value is None else float(reference_value)
        )
        color = list(light["colorRgb"])
        flags = int(light["lightFlags"])
        falloff = float(light["falloff"])
        field_of_view = float(light["fieldOfViewDegrees"])
        intensity = float(light["intensity"])
    except (TypeError, ValueError) as error:
        raise ValueError(f"LIGH numeric contract differs: {base['formKey']}") from error
    numeric_values = [base_radius, falloff, field_of_view, intensity]
    if reference_radius is not None:
        numeric_values.append(reference_radius)
    if (
        not all(math.isfinite(value) for value in numeric_values)
        or base_radius < 0.0
        or (reference_radius is not None and reference_radius < 0.0)
        or len(color) != LIGHT_COLOR_COMPONENTS
        or any(
            not isinstance(value, int)
            or value < BYTE_CHANNEL_MINIMUM
            or value > BYTE_CHANNEL_MAXIMUM
            for value in color
        )
        or flags < 0
    ):
        raise ValueError(f"LIGH value contract differs: {base['formKey']}")
    effective_radius = base_radius if reference_radius is None else reference_radius
    return {
        "baseRadiusGameUnits": base_radius,
        "referenceRadiusGameUnits": reference_radius,
        "effectiveRadiusGameUnits": effective_radius,
        "effectiveRadiusMeters": effective_radius * world_units_to_meters,
        "colorRgb": color,
        "lightFlags": flags,
        "falloff": falloff,
        "fieldOfViewDegrees": field_of_view,
        "intensity": intensity,
    }


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
