#!/usr/bin/env python3
"""Validate one static CELL compile against its plan, corpus, archives, and files."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

from PIL import Image

from bsa_archive import canonical_member_path
from cell_compile_plan import MANIFEST_FILE_NAME as PLAN_MANIFEST_FILE_NAME
from cell_parity_corpus import MANIFEST_FILE_NAME as CORPUS_MANIFEST_FILE_NAME
from cell_scene import godot_position, godot_rotation_quaternion
from cell_static_contract import (
    ASSETS_FILE_NAME,
    BLOCKED_PRESENTATION_STATUS,
    BLOCKED_REFERENCE_STATUS,
    BLOCKERS_FILE_NAME,
    CELL_FILE_NAME,
    COMPILED_REFERENCE_STATUS,
    MANIFEST_FILE_NAME,
    MANIFEST_SCHEMA,
    OUTPUT_SCHEMA,
    PASS_PRESENTATION_STATUS,
    STATIC_COMPILER_SOURCE_NAMES,
    STATIC_RUNTIME_PENDING_REFERENCE_STATUS,
    TEXTURES_FILE_NAME,
    canonical_sha256,
    cell_origin,
    default_plan_recipe_path,
    default_profile_path,
    load_profile,
    mesh_member_path,
    recipe_path,
    toolchain_manifest,
)
from cell_static_source import find_job, source_rows_for_job
from corpus_io import read_jsonl
from export_static_nif_gltf import SCHEMA as STATIC_NIF_SCHEMA, compiler_provenance
from owned_archive_stack import load_owned_archive_stack
from plugin_stack import file_sha256
from runtime_configuration import load_runtime_configuration
from material_contract import material_bindings
from texture_pipeline import DDS_CUBEMAP_FACE_COUNT
from validate_cell_compile_plan import validate_plan


EXIT_VALIDATION_ERROR = 2
PNG_FORMAT = "PNG"
PRODUCER_SOURCE_NAMES = STATIC_COMPILER_SOURCE_NAMES


def validate_descriptor(path: Path, descriptor: dict[str, object]) -> list[dict[str, object]]:
    if set(descriptor) != {"file", "bytes", "sha256", "rows"}:
        raise ValueError(f"Static CELL compile descriptor fields differ: {path.name}")
    if not path.is_file():
        raise ValueError(f"Static CELL compile output is missing: {path}")
    if path.stat().st_size != int(descriptor["bytes"]):
        raise ValueError(f"Static CELL compile byte count differs: {path.name}")
    if file_sha256(path) != str(descriptor["sha256"]).lower():
        raise ValueError(f"Static CELL compile hash differs: {path.name}")
    rows = read_jsonl(path)
    if len(rows) != int(descriptor["rows"]):
        raise ValueError(f"Static CELL compile row count differs: {path.name}")
    return rows


def validate_json_descriptor(path: Path, descriptor: dict[str, object]) -> dict[str, object]:
    if set(descriptor) != {"file", "bytes", "sha256", "rows"}:
        raise ValueError(f"Static CELL compile descriptor fields differ: {path.name}")
    if not path.is_file():
        raise ValueError(f"Static CELL compile output is missing: {path}")
    if path.stat().st_size != int(descriptor["bytes"]):
        raise ValueError(f"Static CELL compile byte count differs: {path.name}")
    if file_sha256(path) != str(descriptor["sha256"]).lower():
        raise ValueError(f"Static CELL compile hash differs: {path.name}")
    if int(descriptor["rows"]) != 1:
        raise ValueError(f"Static CELL compile JSON row count differs: {path.name}")
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"Static CELL compile JSON is not an object: {path.name}")
    return document


def validate_producer_sources(manifest: dict[str, object]) -> None:
    tools_root = Path(__file__).resolve().parent
    sources = manifest.get("producerSources")
    expected_names = {f"tools/{name}" for name in PRODUCER_SOURCE_NAMES}
    if not isinstance(sources, list) or {
        str(row["file"]) for row in sources
    } != expected_names or len(sources) != len(expected_names):
        raise ValueError("Static CELL compiler producer-source set differs")
    for row in sources:
        relative = Path(str(row["file"]))
        path = tools_root / relative.name
        if not path.is_file() or file_sha256(path) != str(row["sha256"]).lower():
            raise ValueError(f"Static CELL compiler source changed: {relative}")


def validate_relative_file(
    root: Path,
    relative_text: str,
    expected_bytes: int,
    expected_sha256: str,
    expected_parent: Path,
) -> Path:
    relative = Path(relative_text)
    if relative.is_absolute() or ".." in relative.parts:
        raise ValueError(f"Static CELL nested output is not relative: {relative}")
    path = (root / relative).resolve()
    if path.parent != expected_parent.resolve() or not path.is_file():
        raise ValueError(f"Static CELL nested output path differs: {relative}")
    if path.stat().st_size != expected_bytes or file_sha256(path) != expected_sha256.lower():
        raise ValueError(f"Static CELL nested output descriptor differs: {relative}")
    return path


def validate_compile(
    root: Path,
    data_root: Path,
    corpus_root: Path,
    plan_root: Path,
    profile_path: Path,
    plan_recipe_path: Path,
) -> dict[str, int]:
    manifest_path = root / MANIFEST_FILE_NAME
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("schema") != MANIFEST_SCHEMA:
        raise ValueError(f"Unexpected static CELL compile schema: {manifest_path}")
    if set(manifest) != {
        "schema",
        "status",
        "cellFormKey",
        "profileId",
        "profileCanonicalSha256",
        "profileFileSha256",
        "runtimeConfiguration",
        "toolchain",
        "sourceCorpusManifestSha256",
        "sourcePlanManifestSha256",
        "producerSources",
        "ownedArchives",
        "archiveRecipe",
        "counts",
        "promotionPolicy",
        "outputs",
    }:
        raise ValueError("Static CELL compile manifest fields differ")
    profile = load_profile(profile_path)
    if (
        manifest.get("profileId") != profile.get("id")
        or manifest.get("profileCanonicalSha256")
        != canonical_sha256(profile)
        or manifest.get("profileFileSha256") != file_sha256(profile_path)
    ):
        raise ValueError("Static CELL compile profile differs")
    validate_producer_sources(manifest)

    validate_plan(plan_root, corpus_root, plan_recipe_path)
    plan_manifest_path = plan_root / PLAN_MANIFEST_FILE_NAME
    corpus_manifest_path = corpus_root / CORPUS_MANIFEST_FILE_NAME
    if (
        manifest.get("sourcePlanManifestSha256") != file_sha256(plan_manifest_path)
        or manifest.get("sourceCorpusManifestSha256") != file_sha256(corpus_manifest_path)
    ):
        raise ValueError("Static CELL compile source manifests differ")
    plan_manifest = json.loads(plan_manifest_path.read_text(encoding="utf-8"))
    corpus_manifest = json.loads(corpus_manifest_path.read_text(encoding="utf-8"))
    cell_key = str(manifest["cellFormKey"])
    job = find_job(plan_root, plan_manifest, cell_key)
    cell, children, bases, portals = source_rows_for_job(
        corpus_root,
        corpus_manifest,
        job,
    )

    archive_recipe_path = recipe_path(str(profile["archiveRecipe"]))
    if (
        manifest.get("archiveRecipe")
        != {
            "file": archive_recipe_path.name,
            "sha256": file_sha256(archive_recipe_path),
        }
    ):
        raise ValueError("Static CELL compile archive recipe differs")
    archives = load_owned_archive_stack(data_root, archive_recipe_path)
    if manifest.get("ownedArchives") != archives.manifest():
        raise ValueError("Static CELL compile owned archive stack differs")

    expected_output_files = {
        "cell": CELL_FILE_NAME,
        "assets": ASSETS_FILE_NAME,
        "textures": TEXTURES_FILE_NAME,
        "blockers": BLOCKERS_FILE_NAME,
    }
    outputs = manifest.get("outputs")
    if not isinstance(outputs, dict) or set(outputs) != set(expected_output_files):
        raise ValueError("Static CELL compile output set differs")
    rows: dict[str, list[dict[str, object]]] = {}
    cell_document: dict[str, object] | None = None
    for name, file_name in expected_output_files.items():
        descriptor = outputs[name]
        if descriptor.get("file") != file_name:
            raise ValueError(f"Static CELL compile output name differs: {name}")
        if name == "cell":
            cell_document = validate_json_descriptor(root / file_name, descriptor)
        else:
            rows[name] = validate_descriptor(root / file_name, descriptor)
    if cell_document is None or cell_document.get("schema") != OUTPUT_SCHEMA:
        raise ValueError("Static CELL compiled document schema differs")
    if set(cell_document) != {
        "schema",
        "status",
        "cell",
        "job",
        "originGameUnits",
        "worldUnitsToMeters",
        "placements",
        "portals",
        "assetIds",
        "textureIds",
        "blockerCount",
        "runtimeStatus",
        "parityStatus",
    }:
        raise ValueError("Static CELL compiled document fields differ")
    assets = rows["assets"]
    textures = rows["textures"]
    blockers = rows["blockers"]
    if cell_document.get("cell") != cell:
        raise ValueError("Static CELL compiled source CELL differs")
    if cell_document.get("portals") != portals:
        raise ValueError("Static CELL compiled portals differ")
    expected_job = {
        "cellFormKey": job["cellFormKey"],
        "capabilitySetId": job["capabilitySetId"],
        "requiredGates": job["requiredGates"],
        "requiredShots": job["requiredShots"],
    }
    if cell_document.get("job") != expected_job:
        raise ValueError("Static CELL compiled job contract differs")
    if manifest.get("promotionPolicy") != profile["promotion"]:
        raise ValueError("Static CELL compile promotion policy differs")

    configuration = load_runtime_configuration()
    if manifest.get("runtimeConfiguration") != configuration.manifest():
        raise ValueError("Static CELL compile runtime configuration differs")
    if manifest.get("toolchain") != toolchain_manifest():
        raise ValueError("Static CELL compile toolchain differs")
    origin = cell_origin(cell, profile)
    if (
        cell_document.get("originGameUnits") != list(origin)
        or float(cell_document["worldUnitsToMeters"])
        != configuration.world_units_to_meters
    ):
        raise ValueError("Static CELL compiled coordinate contract differs")
    source_children = {str(row["formKey"]): row for row in children}
    placements = cell_document.get("placements")
    if not isinstance(placements, list):
        raise ValueError("Static CELL compile has no placement outcomes")
    placement_keys = [str(row["childFormKey"]) for row in placements]
    if len(placement_keys) != len(set(placement_keys)) or set(placement_keys) != set(source_children):
        raise ValueError("Static CELL compile does not account for every source child")
    blocker_reasons_by_child: dict[str, set[str]] = {}
    blocker_rows = [json.dumps(row, sort_keys=True, separators=(",", ":")) for row in blockers]
    if blocker_rows != sorted(blocker_rows) or len(blocker_rows) != len(set(blocker_rows)):
        raise ValueError("Static CELL blockers are duplicated or unsorted")
    for row in blockers:
        if set(row) != {"scope", "owner", "reason", "detail"}:
            raise ValueError("Static CELL blocker fields differ")
        if row["scope"] not in {"child", "asset", "texture"}:
            raise ValueError(f"Static CELL blocker scope differs: {row['scope']}")
        if row["scope"] == "child":
            if str(row["owner"]) not in source_children:
                raise ValueError(f"Static CELL blocker child is unknown: {row['owner']}")
            blocker_reasons_by_child.setdefault(str(row["owner"]), set()).add(
                str(row["reason"])
            )
    asset_ids = {str(row["assetId"]) for row in assets}
    if len(asset_ids) != len(assets):
        raise ValueError("Static CELL compile repeats an asset ID")
    for placement in placements:
        key = str(placement["childFormKey"])
        child = source_children[key]
        transform = child.get("transformGameUnits")
        expected_position = None if transform is None else transform["position"]
        expected_rotation = None if transform is None else transform["rotation_radians"]
        expected_godot = (
            None
            if expected_position is None
            else godot_position(tuple(float(value) for value in expected_position), origin)
        )
        expected_quaternion = (
            None
            if expected_rotation is None
            else godot_rotation_quaternion(tuple(float(value) for value in expected_rotation))
        )
        expected_reasons = blocker_reasons_by_child.get(key, set())
        base_link = child.get("baseOrActor")
        expected_base = (
            bases.get(str(base_link["key"])) if isinstance(base_link, dict) else None
        )
        if (
            placement.get("childRuntimeFormId") != child["runtimeFormId"]
            or placement.get("base") != base_link
            or placement.get("baseRecordType")
            != (None if expected_base is None else expected_base["recordType"])
            or placement.get("baseEditorId")
            != (None if expected_base is None else expected_base.get("editorId"))
            or placement.get("positionGameUnits") != expected_position
            or placement.get("positionGodotUnits") != expected_godot
            or placement.get("positionGodotMeters")
            != (
                None
                if expected_godot is None
                else [value * configuration.world_units_to_meters for value in expected_godot]
            )
            or placement.get("rotationGameRadians") != expected_rotation
            or placement.get("rotationGodotQuaternion") != expected_quaternion
            or placement.get("scale") != child["scale"]
            or placement.get("blockerReasons") != sorted(expected_reasons)
        ):
            raise ValueError(f"Static CELL placement differs: {key}")
        asset_id = placement.get("assetId")
        if asset_id is not None and str(asset_id) not in asset_ids:
            raise ValueError(f"Static CELL placement asset is unresolved: {key}")
        if (
            placement["presentationStatus"] == COMPILED_REFERENCE_STATUS
        ) != (asset_id is not None):
            raise ValueError(f"Static CELL placement presentation status differs: {key}")
        expected_readiness = (
            BLOCKED_REFERENCE_STATUS
            if expected_reasons
            else STATIC_RUNTIME_PENDING_REFERENCE_STATUS
        )
        if placement["readinessStatus"] != expected_readiness:
            raise ValueError(f"Static CELL placement readiness differs: {key}")

    referenced_asset_ids = {
        str(row["assetId"]) for row in placements if row.get("assetId") is not None
    }
    if referenced_asset_ids != asset_ids:
        raise ValueError("Static CELL compile contains unreferenced assets")

    expected_files = {
        manifest_path.resolve(),
        (root / CELL_FILE_NAME).resolve(),
        (root / ASSETS_FILE_NAME).resolve(),
        (root / TEXTURES_FILE_NAME).resolve(),
        (root / BLOCKERS_FILE_NAME).resolve(),
    }
    seen_asset_ids: set[str] = set()
    seen_model_paths: set[str] = set()
    texture_ids_by_requested = {
        str(row["requestedPath"]): str(row["textureId"]) for row in textures
    }
    if len(texture_ids_by_requested) != len(textures):
        raise ValueError("Static CELL compile repeats a requested texture")
    bound_texture_ids: set[str] = set()
    for asset in assets:
        asset_id = str(asset["assetId"])
        model_path = str(asset["requestedModelPath"])
        if asset_id in seen_asset_ids or model_path in seen_model_paths:
            raise ValueError("Static CELL compile repeats an asset")
        seen_asset_ids.add(asset_id)
        seen_model_paths.add(model_path)
        member = archives.extract(mesh_member_path(model_path))
        expected_asset_id = hashlib.sha256(
            f"{member.logical_path}:{member.sha256}".encode("utf-8")
        ).hexdigest()[:configuration.content_compiler.asset_id_hex_characters]
        if (
            asset_id != expected_asset_id
            or asset["logicalPath"] != member.logical_path
            or asset["sourceSha256"] != member.sha256
            or int(asset["sourceBytes"]) != len(member.data)
            or asset["sourceArchive"] != member.source_archive
            or asset["sourceArchiveSha256"] != member.source_archive_sha256
        ):
            raise ValueError(f"Static CELL asset source differs: {model_path}")
        asset_parent = (root / "generated" / "assets" / asset_id).resolve()
        compiled_outputs = asset.get("outputs")
        if not isinstance(compiled_outputs, dict) or "sidecar" not in compiled_outputs:
            raise ValueError(f"Static CELL asset output ledger differs: {model_path}")
        output_files = [str(row["file"]) for row in compiled_outputs.values()]
        if len(output_files) != len(set(output_files)):
            raise ValueError(f"Static CELL asset repeats an output file: {model_path}")
        for descriptor in compiled_outputs.values():
            if set(descriptor) != {"file", "bytes", "sha256"}:
                raise ValueError(f"Static CELL asset descriptor fields differ: {model_path}")
            path = validate_relative_file(
                root,
                str(descriptor["file"]),
                int(descriptor["bytes"]),
                str(descriptor["sha256"]),
                asset_parent,
            )
            expected_files.add(path)
        sidecar_path = root / str(compiled_outputs["sidecar"]["file"])
        sidecar = json.loads(sidecar_path.read_text(encoding="utf-8"))
        expected_sidecar_outputs = {
            name: {
                "file": f"generated/assets/{asset_id}/{descriptor['file']}",
                "bytes": int(descriptor["bytes"]),
                "sha256": str(descriptor["sha256"]),
            }
            for name, descriptor in sidecar["outputs"].items()
        }
        expected_sidecar_outputs["sidecar"] = compiled_outputs["sidecar"]
        if (
            sidecar.get("schema") != STATIC_NIF_SCHEMA
            or sidecar.get("status") != "geometry-only"
            or sidecar.get("compiler") != compiler_provenance()
            or sidecar["source"]["sha256"] != member.sha256
            or sidecar["source"]["logicalPath"] != member.logical_path
            or asset["coverage"] != sidecar["coverage"]
            or asset["surfaces"] != sidecar["surfaces"]
            or compiled_outputs != expected_sidecar_outputs
        ):
            raise ValueError(f"Static CELL asset sidecar source differs: {model_path}")
        expected_bindings = [
            {
                "requestedPath": requested,
                "textureId": texture_ids_by_requested.get(requested),
            }
            for requested in sorted(
                {
                    texture
                    for surface in sidecar["surfaces"]
                    for texture in surface["textures"]
                    if texture
                }
            )
        ]
        if asset.get("textureBindings") != expected_bindings:
            raise ValueError(f"Static CELL asset texture bindings differ: {model_path}")
        if asset.get("materials") != material_bindings(
            sidecar,
            texture_ids_by_requested,
            configuration.content_compiler,
        ):
            raise ValueError(f"Static CELL asset material bindings differ: {model_path}")
        bound_texture_ids.update(
            str(row["textureId"])
            for row in expected_bindings
            if row["textureId"] is not None
        )

    seen_texture_ids: set[str] = set()
    seen_texture_paths: set[str] = set()
    for texture in textures:
        texture_id = str(texture["textureId"])
        requested = str(texture["requestedPath"])
        if texture_id in seen_texture_ids or requested in seen_texture_paths:
            raise ValueError("Static CELL compile repeats a texture")
        seen_texture_ids.add(texture_id)
        seen_texture_paths.add(requested)
        member = archives.extract(str(texture["archivePath"]))
        requested_canonical = canonical_member_path(requested)
        aliases = {
            canonical_member_path(str(source)): canonical_member_path(str(target))
            for source, target in profile.get("textureAliases", {}).items()
        }
        expected_archive_path = aliases.get(requested_canonical, requested_canonical)
        expected_texture_id = hashlib.sha256(
            f"{requested_canonical}:{member.sha256}".encode("utf-8")
        ).hexdigest()[:configuration.content_compiler.asset_id_hex_characters]
        if (
            texture_id != expected_texture_id
            or texture["archivePath"] != expected_archive_path
            or texture["sourceSha256"] != member.sha256
            or int(texture["sourceBytes"]) != len(member.data)
            or texture["sourceArchive"] != member.source_archive
            or texture["sourceArchiveSha256"] != member.source_archive_sha256
        ):
            raise ValueError(f"Static CELL texture source differs: {requested}")
        texture_parent = (root / "generated" / "textures").resolve()
        png = validate_relative_file(
            root,
            str(texture["png"]),
            int(texture["pngBytes"]),
            str(texture["pngSha256"]),
            texture_parent,
        )
        expected_files.add(png)
        with Image.open(png) as image:
            image.load()
            if (
                image.format != PNG_FORMAT
                or list(image.size) != [int(texture["width"]), int(texture["height"])]
            ):
                raise ValueError(f"Static CELL texture dimensions differ: {requested}")
        expected_normal = requested_canonical.endswith("_n.dds")
        if texture["normalGreenInverted"] != expected_normal:
            raise ValueError(f"Static CELL normal-map policy differs: {requested}")
        cube_faces = texture["cubeFaces"]
        if cube_faces and len(cube_faces) != DDS_CUBEMAP_FACE_COUNT:
            raise ValueError(f"Static CELL cubemap face count differs: {requested}")
        for face in cube_faces:
            validated = validate_relative_file(
                root,
                str(face["png"]),
                int(face["bytes"]),
                str(face["pngSha256"]),
                texture_parent,
            )
            expected_files.add(validated)
            with Image.open(validated) as image:
                image.load()
                if image.format != PNG_FORMAT:
                    raise ValueError(f"Static CELL cubemap output is not PNG: {requested}")

    if bound_texture_ids != seen_texture_ids:
        raise ValueError("Static CELL compile contains unreferenced textures")

    actual_files = {path.resolve() for path in root.rglob("*") if path.is_file()}
    if actual_files != expected_files:
        raise ValueError("Static CELL compile contains unaccounted files")
    expected_status = BLOCKED_PRESENTATION_STATUS if blockers else PASS_PRESENTATION_STATUS
    if manifest.get("status") != expected_status or cell_document.get("status") != expected_status:
        raise ValueError("Static CELL compile status differs from blockers")
    counts = {
        "sourceChildren": len(children),
        "compiledPlacements": sum(
            row["presentationStatus"] == COMPILED_REFERENCE_STATUS for row in placements
        ),
        "assets": len(assets),
        "textures": len(textures),
        "portals": len(portals),
        "blockers": len(blockers),
    }
    if manifest.get("counts") != counts:
        raise ValueError("Static CELL compile manifest counts differ")
    if (
        cell_document.get("assetIds") != sorted(asset_ids)
        or cell_document.get("textureIds") != sorted(seen_texture_ids)
        or int(cell_document.get("blockerCount", -1)) != len(blockers)
    ):
        raise ValueError("Static CELL compiled identifier ledger differs")
    if cell_document.get("runtimeStatus") != "pending" or cell_document.get("parityStatus") != "pending":
        raise ValueError("Static CELL compile promoted runtime or parity without evidence")
    return counts


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--compile-root", type=Path, required=True)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--plan-root", type=Path, required=True)
    parser.add_argument("--profile", type=Path, default=default_profile_path())
    parser.add_argument("--plan-recipe", type=Path, default=default_plan_recipe_path())
    args = parser.parse_args()
    try:
        counts = validate_compile(
            args.compile_root.resolve(),
            args.data_root.resolve(),
            args.corpus_root.resolve(),
            args.plan_root.resolve(),
            args.profile.resolve(),
            args.plan_recipe.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_STATIC_CELL_COMPILE_FAIL {error}", file=sys.stderr)
        return EXIT_VALIDATION_ERROR
    print(
        "OPENNV_STATIC_CELL_COMPILE_PASS "
        + " ".join(f"{name}={value}" for name, value in sorted(counts.items()))
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
