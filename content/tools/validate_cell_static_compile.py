#!/usr/bin/env python3
"""Validate one static CELL compile against its plan, corpus, archives, and files."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from cell_compile_plan import MANIFEST_FILE_NAME as PLAN_MANIFEST_FILE_NAME
from cell_landscape_validate import (
    LandscapeExpectation,
    resolve_landscape_expectation,
)
from cell_parity_corpus import MANIFEST_FILE_NAME as CORPUS_MANIFEST_FILE_NAME
from cell_scene import godot_position, godot_rotation_quaternion
from cell_static_contract import (
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
    POINT_LIGHT_PRESENTATION_KIND,
    STATIC_MODEL_PRESENTATION_KIND,
    TEXTURES_FILE_NAME,
    canonical_sha256,
    cell_origin,
    child_presentation_policy,
    compiled_light_contract,
    default_plan_recipe_path,
    default_profile_path,
    load_profile,
    presentation_policy,
    recipe_path,
    toolchain_manifest,
)
from cell_static_source import find_job, source_rows_for_job
from cell_static_resource_validate import validate_resource_artifacts
from corpus_io import read_jsonl
from owned_archive_stack import load_owned_archive_stack
from plugin_stack import file_sha256
from runtime_configuration import load_runtime_configuration
from validate_cell_compile_plan import validate_plan


EXIT_VALIDATION_ERROR = 2
PRODUCER_SOURCE_NAMES = STATIC_COMPILER_SOURCE_NAMES
PLACEMENT_FIELDS = {
    "childFormKey",
    "childRuntimeFormId",
    "base",
    "baseRecordType",
    "baseEditorId",
    "presentationKind",
    "assetId",
    "light",
    "landscape",
    "positionGameUnits",
    "positionGodotUnits",
    "positionGodotMeters",
    "rotationGameRadians",
    "rotationGodotQuaternion",
    "scale",
    "presentationStatus",
    "readinessStatus",
    "blockerReasons",
}
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
    blocker_details_by_child: dict[str, set[tuple[str, str | None]]] = {}
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
            blocker_details_by_child.setdefault(str(row["owner"]), set()).add(
                (
                    str(row["reason"]),
                    None if row["detail"] is None else str(row["detail"]),
                )
            )
    asset_ids = {str(row["assetId"]) for row in assets}
    if len(asset_ids) != len(assets):
        raise ValueError("Static CELL compile repeats an asset ID")
    base_linked_children = set(profile["baseLinkedChildRecordTypes"])
    landscape_expectations_by_asset_id: dict[str, LandscapeExpectation] = {}
    for placement in placements:
        if set(placement) != PLACEMENT_FIELDS:
            raise ValueError("Static CELL placement fields differ")
        key = str(placement["childFormKey"])
        child = source_children[key]
        expected_reasons = blocker_reasons_by_child.get(key, set())
        expected_source_gaps = set(str(value) for value in child.get("parseGaps", []))
        actual_source_gaps = {
            detail
            for reason, detail in blocker_details_by_child.get(key, set())
            if reason == "source-parse-gap"
        }
        if actual_source_gaps != expected_source_gaps:
            raise ValueError(f"Static CELL source-gap blockers differ: {key}")
        base_link = child.get("baseOrActor")
        expected_base = (
            bases.get(str(base_link["key"])) if isinstance(base_link, dict) else None
        )
        record_type = str(child["recordType"])
        policy = None
        if record_type in base_linked_children and expected_base is not None:
            policy = presentation_policy(profile, str(expected_base["recordType"]))
        elif record_type not in base_linked_children:
            policy = child_presentation_policy(profile, record_type)
        expected_kind = None if policy is None else str(policy["kind"])
        expected_light = None
        expected_landscape = None
        expected_landscape_asset_id = None
        if expected_kind == POINT_LIGHT_PRESENTATION_KIND:
            try:
                expected_light = compiled_light_contract(
                    expected_base,
                    child,
                    configuration.world_units_to_meters,
                )
            except ValueError:
                if "invalid-light-contract" not in expected_reasons:
                    raise ValueError(
                        f"Static CELL light contract blocker is missing: {key}"
                    )
        elif expected_kind == LANDSCAPE_PRESENTATION_KIND:
            try:
                landscape_expectation = resolve_landscape_expectation(
                    data_root,
                    corpus_manifest,
                    cell,
                    child,
                    origin,
                    configuration.content_compiler,
                )
                expected_landscape = landscape_expectation.contract
                expected_landscape_asset_id = landscape_expectation.asset_id
                landscape_expectations_by_asset_id[
                    expected_landscape_asset_id
                ] = landscape_expectation
            except Exception:
                if "landscape-compile-failed" not in expected_reasons:
                    raise ValueError(
                        f"Static CELL landscape blocker is missing: {key}"
                    )
        unsupported_subrecords = set()
        if policy is not None:
            supported_field = (
                "supportedReferenceSubrecords"
                if record_type in base_linked_children
                else "supportedChildSubrecords"
            )
            unsupported_subrecords = set(child["subrecordSignatureCounts"]) - set(
                policy[supported_field]
            )
        unsupported_reason = (
            "unsupported-reference-subrecord"
            if record_type in base_linked_children
            else "unsupported-child-subrecord"
        )
        actual_unsupported_subrecords = {
            detail
            for reason, detail in blocker_details_by_child.get(key, set())
            if reason == unsupported_reason
        }
        if actual_unsupported_subrecords != unsupported_subrecords:
            raise ValueError(
                f"Static CELL reference-subrecord blockers differ: {key}"
            )
        if expected_kind == LANDSCAPE_PRESENTATION_KIND:
            expected_position = list(origin)
            expected_rotation = [0.0, 0.0, 0.0]
            expected_scale = 1.0
        else:
            transform = child.get("transformGameUnits")
            expected_position = None if transform is None else transform["position"]
            expected_rotation = None if transform is None else transform["rotation_radians"]
            expected_scale = child["scale"]
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
        if (
            placement.get("childRuntimeFormId") != child["runtimeFormId"]
            or placement.get("base") != base_link
            or placement.get("baseRecordType")
            != (None if expected_base is None else expected_base["recordType"])
            or placement.get("baseEditorId")
            != (None if expected_base is None else expected_base.get("editorId"))
            or placement.get("presentationKind") != expected_kind
            or placement.get("light") != expected_light
            or placement.get("landscape") != expected_landscape
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
            or placement.get("scale") != expected_scale
            or placement.get("blockerReasons") != sorted(expected_reasons)
        ):
            raise ValueError(f"Static CELL placement differs: {key}")
        asset_id = placement.get("assetId")
        if asset_id is not None and str(asset_id) not in asset_ids:
            raise ValueError(f"Static CELL placement asset is unresolved: {key}")
        if expected_kind == POINT_LIGHT_PRESENTATION_KIND and asset_id is not None:
            raise ValueError(f"Static CELL point light unexpectedly has an asset: {key}")
        if (
            expected_kind == LANDSCAPE_PRESENTATION_KIND
            and asset_id != expected_landscape_asset_id
        ):
            raise ValueError(f"Static CELL landscape asset differs: {key}")
        expected_presentation_status = BLOCKED_REFERENCE_STATUS
        if expected_kind == STATIC_MODEL_PRESENTATION_KIND and asset_id is not None:
            expected_presentation_status = COMPILED_REFERENCE_STATUS
        elif expected_kind == POINT_LIGHT_PRESENTATION_KIND and expected_light is not None:
            expected_presentation_status = COMPILED_LIGHT_REFERENCE_STATUS
        elif (
            expected_kind == LANDSCAPE_PRESENTATION_KIND
            and expected_landscape is not None
            and asset_id is not None
        ):
            expected_presentation_status = COMPILED_LANDSCAPE_REFERENCE_STATUS
        if placement["presentationStatus"] != expected_presentation_status:
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

    seen_texture_ids = validate_resource_artifacts(
        root,
        assets,
        textures,
        profile,
        archives,
        configuration,
        landscape_expectations_by_asset_id,
    )
    expected_status = BLOCKED_PRESENTATION_STATUS if blockers else PASS_PRESENTATION_STATUS
    if manifest.get("status") != expected_status or cell_document.get("status") != expected_status:
        raise ValueError("Static CELL compile status differs from blockers")
    counts = {
        "sourceChildren": len(children),
        "compiledPlacements": sum(
            row["presentationStatus"]
            in {
                COMPILED_REFERENCE_STATUS,
                COMPILED_LIGHT_REFERENCE_STATUS,
                COMPILED_LANDSCAPE_REFERENCE_STATUS,
            }
            for row in placements
        ),
        "lights": sum(row.get("light") is not None for row in placements),
        "landscapes": sum(
            row.get("landscape") is not None for row in placements
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
