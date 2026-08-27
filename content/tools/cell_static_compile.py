#!/usr/bin/env python3
"""Compile one planned CELL's supported static presentation from owned archives."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import shutil
import sys
import tempfile
from pathlib import Path

from cell_compile_plan import MANIFEST_FILE_NAME as PLAN_MANIFEST_FILE_NAME
from cell_landscape_compile import compile_landscape
from cell_parity_corpus import MANIFEST_FILE_NAME as CORPUS_MANIFEST_FILE_NAME
from cell_scene import godot_position, godot_rotation_quaternion
from cell_static_contract import (
    ACCOUNTED_NONVISUAL_REFERENCE_STATUS,
    ASSETS_FILE_NAME,
    BLOCKED_PRESENTATION_STATUS,
    BLOCKED_REFERENCE_STATUS,
    BLOCKERS_FILE_NAME,
    CELL_FILE_NAME,
    COMPILED_LANDSCAPE_REFERENCE_STATUS,
    COMPILED_LIGHT_REFERENCE_STATUS,
    COMPILED_REFERENCE_STATUS,
    MANIFEST_FILE_NAME,
    MANIFEST_SCHEMA,
    OUTPUT_SCHEMA,
    PASS_PRESENTATION_STATUS,
    STATIC_COMPILER_SOURCE_NAMES,
    STATIC_RUNTIME_PENDING_REFERENCE_STATUS,
    LANDSCAPE_PRESENTATION_KIND,
    NONVISUAL_PRESENTATION_KIND,
    OWNED_DDS_TEXTURE_KIND,
    POINT_LIGHT_PRESENTATION_KIND,
    STATIC_NIF_ASSET_KIND,
    STATIC_MODEL_PRESENTATION_KIND,
    LANDSCAPE_TEXTURE_KIND,
    TEXTURES_FILE_NAME,
    blocker,
    canonical_sha256,
    cell_origin,
    child_presentation_policy,
    child_transform,
    compiled_light_contract,
    default_plan_recipe_path,
    default_profile_path,
    load_profile,
    mesh_member_path,
    presentation_policy,
    recipe_path,
    relative_output,
    stable_exception_detail,
    toolchain_manifest,
)
from cell_static_source import find_job, source_rows_for_job
from corpus_io import atomic_bytes, atomic_json, jsonl_bytes, output_descriptor
from export_static_nif_gltf import export_static_nif
from owned_archive_stack import load_owned_archive_stack
from plugin_stack import file_sha256
from runtime_configuration import load_runtime_configuration
from material_contract import material_bindings, texture_binding_requests
from texture_pipeline import OwnedTexturePipeline
from validate_cell_compile_plan import validate_plan


EXIT_DATA_ERROR = 2
PRODUCER_SOURCE_NAMES = STATIC_COMPILER_SOURCE_NAMES


def compile_model(
    model_path: str,
    archives,
    staging_root: Path,
    compiler_configuration,
    strict: bool,
) -> tuple[dict[str, object] | None, dict[str, object] | None, dict[str, object] | None]:
    logical_path = mesh_member_path(model_path)
    try:
        member = archives.extract(logical_path)
        asset_id = hashlib.sha256(
            f"{member.logical_path}:{member.sha256}".encode("utf-8")
        ).hexdigest()[:compiler_configuration.asset_id_hex_characters]
        source_path = staging_root / "source" / Path(member.logical_path.replace("\\", "/"))
        source_path.parent.mkdir(parents=True, exist_ok=True)
        atomic_bytes(source_path, member.data)
        asset_root = staging_root / "generated" / "assets" / asset_id
        gltf_path = asset_root / "model.gltf"
        sidecar_path = asset_root / "model.opennv.json"
        sidecar = export_static_nif(
            source_path,
            member.logical_path,
            gltf_path,
            sidecar_path,
            compiler_configuration,
            strict=strict,
        )
    except Exception as error:
        return None, None, blocker(
            "asset",
            model_path,
            "asset-export-failed",
            stable_exception_detail(error, staging_root),
        )
    compiled_outputs = {
        name: {
            "file": relative_output(asset_root / str(descriptor["file"]), staging_root),
            "bytes": int(descriptor["bytes"]),
            "sha256": str(descriptor["sha256"]),
        }
        for name, descriptor in sidecar["outputs"].items()
    }
    compiled_outputs["sidecar"] = {
        "file": relative_output(sidecar_path, staging_root),
        "bytes": sidecar_path.stat().st_size,
        "sha256": file_sha256(sidecar_path),
    }
    asset = {
        "assetKind": STATIC_NIF_ASSET_KIND,
        "assetId": asset_id,
        "requestedModelPath": model_path,
        "logicalPath": member.logical_path,
        "sourceArchive": member.source_archive,
        "sourceArchiveSha256": member.source_archive_sha256,
        "sourceBytes": len(member.data),
        "sourceSha256": member.sha256,
        "outputs": compiled_outputs,
        "coverage": sidecar["coverage"],
        "surfaces": sidecar["surfaces"],
    }
    return asset, sidecar, None


def texture_row(artifact, staging_root: Path) -> dict[str, object]:
    return {
        "textureKind": OWNED_DDS_TEXTURE_KIND,
        "textureId": artifact.asset_id,
        "requestedPath": artifact.requested_path,
        "archivePath": artifact.archive_path,
        "sourceSha256": artifact.source_sha256,
        "sourceBytes": artifact.source_bytes,
        "sourceArchive": artifact.source_archive,
        "sourceArchiveSha256": artifact.source_archive_sha256,
        "png": relative_output(artifact.png_path, staging_root),
        "pngBytes": artifact.png_path.stat().st_size,
        "pngSha256": artifact.png_sha256,
        "width": artifact.width,
        "height": artifact.height,
        "normalGreenInverted": artifact.normal_green_inverted,
        "cubeFaces": [
            {
                "png": relative_output(path, staging_root),
                "bytes": path.stat().st_size,
                "pngSha256": sha256,
            }
            for path, sha256 in zip(
                artifact.cube_face_paths,
                artifact.cube_face_sha256,
            )
        ],
    }


def producer_sources() -> list[dict[str, object]]:
    tools_root = Path(__file__).resolve().parent
    return [
        {"file": f"tools/{name}", "sha256": file_sha256(tools_root / name)}
        for name in PRODUCER_SOURCE_NAMES
    ]


def compile_cell(
    data_root: Path,
    corpus_root: Path,
    plan_root: Path,
    cell_key: str,
    output_root: Path,
    profile: dict[str, object],
    plan_recipe_path: Path,
    profile_path: Path,
) -> dict[str, object]:
    if output_root.exists():
        raise FileExistsError(f"Refusing to overwrite static CELL compile: {output_root}")
    validate_plan(plan_root, corpus_root, plan_recipe_path)
    plan_manifest_path = plan_root / PLAN_MANIFEST_FILE_NAME
    plan_manifest = json.loads(plan_manifest_path.read_text(encoding="utf-8"))
    job = find_job(plan_root, plan_manifest, cell_key)
    corpus_manifest_path = corpus_root / CORPUS_MANIFEST_FILE_NAME
    corpus_manifest = json.loads(corpus_manifest_path.read_text(encoding="utf-8"))
    cell, children, bases, portals = source_rows_for_job(
        corpus_root,
        corpus_manifest,
        job,
    )
    configuration = load_runtime_configuration()
    archive_recipe_path = recipe_path(str(profile["archiveRecipe"]))
    archives = load_owned_archive_stack(data_root, archive_recipe_path)
    texture_aliases = {
        str(source): str(target)
        for source, target in profile.get("textureAliases", {}).items()
    }
    origin = cell_origin(cell, profile)
    supported_children = set(profile["supportedChildRecordTypes"])
    base_linked_children = set(profile["baseLinkedChildRecordTypes"])
    supported_bases = set(profile["supportedBaseRecordTypes"])
    model_extension = str(profile["modelExtension"])
    unique_models: set[str] = set()
    child_reasons: dict[str, list[dict[str, object]]] = {}
    child_base: dict[str, dict[str, object] | None] = {}
    child_policies: dict[str, dict[str, object] | None] = {}
    child_lights: dict[str, dict[str, object] | None] = {}
    child_landscapes: dict[str, dict[str, object] | None] = {}
    child_scales: dict[str, float | None] = {}
    child_transforms: dict[
        str,
        tuple[tuple[float, float, float] | None, tuple[float, float, float] | None],
    ] = {}
    for child in children:
        key = str(child["formKey"])
        reasons: list[dict[str, object]] = []
        record_type = str(child["recordType"])
        if record_type not in supported_children:
            reasons.append(
                blocker("child", key, "unsupported-child-record-type", record_type)
            )
        reasons.extend(
            blocker("child", key, "source-parse-gap", str(gap))
            for gap in child.get("parseGaps", [])
        )
        base_link = child.get("baseOrActor")
        base = bases.get(str(base_link["key"])) if isinstance(base_link, dict) else None
        child_base[key] = base
        policy = None
        if record_type in base_linked_children and base is not None:
            policy = presentation_policy(profile, str(base["recordType"]))
        elif record_type not in base_linked_children:
            policy = child_presentation_policy(profile, record_type)
        child_policies[key] = policy
        child_lights[key] = None
        child_landscapes[key] = None
        if record_type in base_linked_children:
            if base is None:
                reasons.append(blocker("child", key, "child-has-no-static-base"))
            elif base["recordType"] not in supported_bases or policy is None:
                reasons.append(
                    blocker(
                        "child",
                        key,
                        "unsupported-base-record-type",
                        str(base["recordType"]),
                    )
                )
            else:
                model_paths = base.get("modelPaths", [])
                expected_models = int(policy["modelPathCount"])
                if not isinstance(model_paths, list) or len(model_paths) != expected_models:
                    reasons.append(
                        blocker(
                            "child",
                            key,
                            "unsupported-model-path-count",
                            str(len(model_paths) if isinstance(model_paths, list) else -1),
                        )
                    )
                elif policy["kind"] == STATIC_MODEL_PRESENTATION_KIND:
                    model_path = str(model_paths[0])
                    if not model_path.casefold().endswith(model_extension):
                        reasons.append(
                            blocker("child", key, "unsupported-model-extension", model_path)
                        )
                    else:
                        unique_models.add(model_path)
                elif policy["kind"] == POINT_LIGHT_PRESENTATION_KIND:
                    try:
                        child_lights[key] = compiled_light_contract(
                            base,
                            child,
                            configuration.world_units_to_meters,
                        )
                    except ValueError as error:
                        reasons.append(
                            blocker("child", key, "invalid-light-contract", str(error))
                        )
                supported_subrecords = set(policy["supportedReferenceSubrecords"])
                for signature in sorted(
                    set(child["subrecordSignatureCounts"]) - supported_subrecords
                ):
                    reasons.append(
                        blocker(
                            "child",
                            key,
                            "unsupported-reference-subrecord",
                            signature,
                        )
                    )
        elif policy is None:
            reasons.append(
                blocker("child", key, "unsupported-child-presentation", record_type)
            )
        else:
            for signature in sorted(
                set(child["subrecordSignatureCounts"])
                - set(policy["supportedChildSubrecords"])
            ):
                reasons.append(
                    blocker(
                        "child",
                        key,
                        "unsupported-child-subrecord",
                        signature,
                    )
                )
        if child["initiallyDisabled"]:
            reasons.append(blocker("child", key, "initially-disabled-state-not-implemented"))
        if child.get("enableParent") is not None:
            reasons.append(blocker("child", key, "enable-parent-state-not-implemented"))
        if child.get("teleport") is not None:
            reasons.append(blocker("child", key, "xtel-runtime-not-implemented"))
        if policy is not None and policy["kind"] == LANDSCAPE_PRESENTATION_KIND:
            position = origin
            rotation = (0.0, 0.0, 0.0)
            scale = 1.0
        elif policy is not None and policy["kind"] == NONVISUAL_PRESENTATION_KIND:
            position = None
            rotation = None
            scale = None
        else:
            position, rotation, transform_reason = child_transform(child)
            if transform_reason is not None:
                reasons.append(blocker("child", key, transform_reason))
            try:
                scale = float(child["scale"])
            except (KeyError, TypeError, ValueError):
                scale = float("nan")
            if not math.isfinite(scale) or scale <= 0.0:
                reasons.append(blocker("child", key, "invalid-scale"))
        child_transforms[key] = (position, rotation)
        child_scales[key] = (
            None if scale is None or not math.isfinite(scale) else scale
        )
        child_reasons[key] = reasons

    output_root.parent.mkdir(parents=True, exist_ok=True)
    manifest: dict[str, object]
    with tempfile.TemporaryDirectory(prefix="opennv-cell-", dir=output_root.parent) as directory:
        staging_root = Path(directory) / "payload"
        staging_root.mkdir()
        assets: dict[str, dict[str, object]] = {}
        sidecars: dict[str, dict[str, object]] = {}
        blockers: list[dict[str, object]] = []
        for model_path in sorted(unique_models):
            asset, sidecar, asset_blocker = compile_model(
                model_path,
                archives,
                staging_root,
                configuration.content_compiler,
                bool(profile["exportStrict"]),
            )
            if asset_blocker is not None:
                blockers.append(asset_blocker)
            else:
                assert asset is not None and sidecar is not None
                assets[model_path] = asset
                sidecars[model_path] = sidecar

        binding_requests_by_model = {
            model_path: [
                request
                for surface in sidecar["surfaces"]
                for request in texture_binding_requests(surface)
            ]
            for model_path, sidecar in sidecars.items()
        }
        policies_by_texture: dict[str, set[str]] = {}
        for requests in binding_requests_by_model.values():
            for request in requests:
                policies_by_texture.setdefault(request["path"], set()).add(
                    request["missingOwnedMember"]
                )
        requested_textures = sorted(policies_by_texture)
        textures: dict[str, dict[str, object]] = {}
        if profile["compileTextures"]:
            texture_pipeline = OwnedTexturePipeline(
                archives,
                staging_root,
                texture_aliases,
                configuration.content_compiler,
            )
            for requested in requested_textures:
                member_source_count = texture_pipeline.member_source_count(requested)
                policies = policies_by_texture[requested]
                if (
                    member_source_count == 0
                    and policies == {"unbound-no-substitution"}
                ):
                    continue
                try:
                    textures[requested] = texture_row(
                        texture_pipeline.prepare(requested),
                        staging_root,
                    )
                except Exception as error:
                    blockers.append(
                        blocker(
                            "texture",
                            requested,
                            "texture-compile-failed",
                            stable_exception_detail(error, staging_root),
                        )
                    )
        elif requested_textures:
            blockers.extend(
                blocker("texture", requested, "texture-compilation-disabled")
                for requested in requested_textures
            )

        compiled_texture_ids = {
            requested: str(texture["textureId"])
            for requested, texture in textures.items()
        }
        for model_path, asset in assets.items():
            requested_for_asset = sorted(
                {request["path"] for request in binding_requests_by_model[model_path]}
            )
            asset["textureBindings"] = [
                {
                    "requestedPath": requested,
                    "textureId": (
                        None
                        if requested not in textures
                        else textures[requested]["textureId"]
                    ),
                    "missingOwnedMember": (
                        "error"
                        if "error" in policies_by_texture[requested]
                        else "unbound-no-substitution"
                    ),
                }
                for requested in requested_for_asset
            ]
            asset["materials"] = material_bindings(
                sidecars[model_path],
                compiled_texture_ids,
                configuration.content_compiler,
            )

        landscape_assets: dict[str, dict[str, object]] = {}
        landscape_textures: dict[str, dict[str, object]] = {}
        landscape_runtime_textures: dict[str, dict[str, object]] = {}
        for child in sorted(children, key=lambda row: str(row["formKey"])):
            key = str(child["formKey"])
            policy = child_policies[key]
            if policy is None or policy["kind"] != LANDSCAPE_PRESENTATION_KIND:
                continue
            if not profile["compileTextures"]:
                child_reasons[key].append(
                    blocker("child", key, "landscape-texture-compilation-disabled")
                )
                continue
            try:
                asset, texture_rows, landscape = compile_landscape(
                    data_root,
                    corpus_manifest,
                    cell,
                    child,
                    archives,
                    staging_root,
                    origin,
                    configuration.content_compiler,
                    texture_aliases,
                )
                landscape_assets[key] = asset
                for texture in texture_rows:
                    texture_id = str(texture["textureId"])
                    target = (
                        landscape_textures
                        if texture["textureKind"] == LANDSCAPE_TEXTURE_KIND
                        else landscape_runtime_textures
                    )
                    previous = target.get(texture_id)
                    if previous is not None and previous != texture:
                        raise ValueError(
                            f"Landscape texture ID has conflicting manifests: {texture_id}"
                        )
                    target[texture_id] = texture
                child_landscapes[key] = landscape
            except Exception as error:
                child_reasons[key].append(
                    blocker(
                        "child",
                        key,
                        "landscape-compile-failed",
                        stable_exception_detail(error, staging_root),
                    )
                )

        placements = []
        for child in sorted(children, key=lambda row: str(row["formKey"])):
            key = str(child["formKey"])
            reasons = list(child_reasons[key])
            base = child_base[key]
            policy = child_policies[key]
            presentation_kind = None if policy is None else str(policy["kind"])
            asset = None
            if (
                base is not None
                and presentation_kind == STATIC_MODEL_PRESENTATION_KIND
                and len(base.get("modelPaths", [])) == int(policy["modelPathCount"])
            ):
                model_path = str(base["modelPaths"][0])
                asset = assets.get(model_path)
                if model_path in unique_models and asset is None:
                    reasons.append(blocker("child", key, "required-asset-not-compiled", model_path))
                elif asset is not None:
                    reasons.extend(
                        blocker(
                            "child",
                            key,
                            "required-texture-not-compiled",
                            str(binding["requestedPath"]),
                        )
                        for binding in asset["textureBindings"]
                        if binding["textureId"] is None
                        and binding["missingOwnedMember"] == "error"
                    )
            elif presentation_kind == LANDSCAPE_PRESENTATION_KIND:
                asset = landscape_assets.get(key)
                if asset is None and not any(
                    row["reason"] == "landscape-compile-failed" for row in reasons
                ):
                    reasons.append(
                        blocker("child", key, "required-landscape-not-compiled")
                    )
            blockers.extend(reasons)
            position, rotation = child_transforms[key]
            godot_units = godot_position(position, origin) if position is not None else None
            light = child_lights[key]
            landscape = child_landscapes[key]
            presentation_status = BLOCKED_REFERENCE_STATUS
            if asset is not None and presentation_kind == LANDSCAPE_PRESENTATION_KIND:
                presentation_status = COMPILED_LANDSCAPE_REFERENCE_STATUS
            elif asset is not None:
                presentation_status = COMPILED_REFERENCE_STATUS
            elif light is not None:
                presentation_status = COMPILED_LIGHT_REFERENCE_STATUS
            elif presentation_kind == NONVISUAL_PRESENTATION_KIND:
                presentation_status = ACCOUNTED_NONVISUAL_REFERENCE_STATUS
            placements.append(
                {
                    "childFormKey": key,
                    "childRuntimeFormId": child["runtimeFormId"],
                    "base": child["baseOrActor"],
                    "baseRecordType": None if base is None else base["recordType"],
                    "baseEditorId": None if base is None else base.get("editorId"),
                    "presentationKind": presentation_kind,
                    "assetId": None if asset is None else asset["assetId"],
                    "light": light,
                    "landscape": landscape,
                    "positionGameUnits": None if position is None else list(position),
                    "positionGodotUnits": godot_units,
                    "positionGodotMeters": (
                        None
                        if godot_units is None
                        else [
                            value * configuration.world_units_to_meters
                            for value in godot_units
                        ]
                    ),
                    "rotationGameRadians": (
                        None if rotation is None else list(rotation)
                    ),
                    "rotationGodotQuaternion": (
                        None
                        if rotation is None
                        else godot_rotation_quaternion(rotation)
                    ),
                    "scale": child_scales[key],
                    "presentationStatus": presentation_status,
                    "readinessStatus": (
                        BLOCKED_REFERENCE_STATUS
                        if reasons
                        else STATIC_RUNTIME_PENDING_REFERENCE_STATUS
                    ),
                    "blockerReasons": sorted({str(row["reason"]) for row in reasons}),
                }
            )

        blockers.sort(key=lambda row: json.dumps(row, sort_keys=True, separators=(",", ":")))
        source_root = staging_root / "source"
        if source_root.is_dir():
            shutil.rmtree(source_root)
        all_assets = sorted(
            [*assets.values(), *landscape_assets.values()],
            key=lambda row: str(row["assetId"]),
        )
        all_texture_rows: dict[str, dict[str, object]] = {}
        for texture in [
            *textures.values(),
            *landscape_textures.values(),
            *landscape_runtime_textures.values(),
        ]:
            texture_id = str(texture["textureId"])
            previous = all_texture_rows.get(texture_id)
            if previous is not None and previous != texture:
                comparable_fields = set(previous) | set(texture)
                comparable_fields -= {"textureKind", "landscapeRole"}
                if any(previous.get(field) != texture.get(field) for field in comparable_fields):
                    raise ValueError(
                        f"Static CELL texture ID has conflicting manifests: {texture_id}"
                    )
                continue
            all_texture_rows[texture_id] = texture
        all_textures = sorted(
            all_texture_rows.values(),
            key=lambda row: str(row["textureId"]),
        )
        if len({str(row["assetId"]) for row in all_assets}) != len(all_assets):
            raise ValueError("Static CELL compile repeats an asset identity")
        if len({str(row["textureId"]) for row in all_textures}) != len(all_textures):
            raise ValueError("Static CELL compile repeats a texture identity")
        status = BLOCKED_PRESENTATION_STATUS if blockers else PASS_PRESENTATION_STATUS
        cell_document = {
            "schema": OUTPUT_SCHEMA,
            "status": status,
            "cell": cell,
            "job": {
                "cellFormKey": job["cellFormKey"],
                "capabilitySetId": job["capabilitySetId"],
                "requiredGates": job["requiredGates"],
                "requiredShots": job["requiredShots"],
            },
            "originGameUnits": list(origin),
            "worldUnitsToMeters": configuration.world_units_to_meters,
            "placements": placements,
            "portals": portals,
            "assetIds": [row["assetId"] for row in all_assets],
            "textureIds": [row["textureId"] for row in all_textures],
            "blockerCount": len(blockers),
            "runtimeStatus": "pending",
            "parityStatus": "pending",
        }
        atomic_json(staging_root / CELL_FILE_NAME, cell_document)
        atomic_bytes(staging_root / ASSETS_FILE_NAME, jsonl_bytes(all_assets))
        atomic_bytes(staging_root / TEXTURES_FILE_NAME, jsonl_bytes(all_textures))
        atomic_bytes(staging_root / BLOCKERS_FILE_NAME, jsonl_bytes(blockers))
        outputs = {
            "cell": output_descriptor(staging_root / CELL_FILE_NAME, 1),
            "assets": output_descriptor(staging_root / ASSETS_FILE_NAME, len(all_assets)),
            "textures": output_descriptor(staging_root / TEXTURES_FILE_NAME, len(all_textures)),
            "blockers": output_descriptor(staging_root / BLOCKERS_FILE_NAME, len(blockers)),
        }
        manifest = {
            "schema": MANIFEST_SCHEMA,
            "status": status,
            "cellFormKey": cell_key,
            "profileId": profile["id"],
            "profileCanonicalSha256": canonical_sha256(profile),
            "profileFileSha256": file_sha256(profile_path),
            "runtimeConfiguration": configuration.manifest(),
            "toolchain": toolchain_manifest(),
            "sourceCorpusManifestSha256": file_sha256(corpus_manifest_path),
            "sourcePlanManifestSha256": file_sha256(plan_manifest_path),
            "producerSources": producer_sources(),
            "ownedArchives": archives.manifest(),
            "archiveRecipe": {
                "file": archive_recipe_path.name,
                "sha256": file_sha256(archive_recipe_path),
            },
            "counts": {
                "sourceChildren": len(children),
                "compiledPlacements": sum(
                    row["presentationStatus"]
                    in {
                        COMPILED_REFERENCE_STATUS,
                COMPILED_LIGHT_REFERENCE_STATUS,
                COMPILED_LANDSCAPE_REFERENCE_STATUS,
                ACCOUNTED_NONVISUAL_REFERENCE_STATUS,
            }
                    for row in placements
                ),
                "lights": sum(row["light"] is not None for row in placements),
                "landscapes": sum(
                    row["landscape"] is not None for row in placements
                ),
                "assets": len(all_assets),
                "textures": len(all_textures),
                "portals": len(portals),
                "blockers": len(blockers),
            },
            "promotionPolicy": profile["promotion"],
            "outputs": outputs,
        }
        atomic_json(staging_root / MANIFEST_FILE_NAME, manifest)
        os.replace(staging_root, output_root)
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--plan-root", type=Path, required=True)
    parser.add_argument("--cell-form-key", required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--profile", type=Path, default=default_profile_path())
    parser.add_argument("--plan-recipe", type=Path, default=default_plan_recipe_path())
    args = parser.parse_args()
    try:
        profile_path = args.profile.resolve()
        profile = load_profile(profile_path)
        manifest = compile_cell(
            args.data_root.resolve(),
            args.corpus_root.resolve(),
            args.plan_root.resolve(),
            args.cell_form_key,
            args.output_root.resolve(),
            profile,
            args.plan_recipe.resolve(),
            profile_path,
        )
    except Exception as error:
        print(f"OPENNV_STATIC_CELL_COMPILE_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_STATIC_CELL_COMPILE "
        + json.dumps(
            {
                "manifest": str((args.output_root / MANIFEST_FILE_NAME).resolve()),
                "status": manifest["status"],
                "cellFormKey": manifest["cellFormKey"],
                "counts": manifest["counts"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
