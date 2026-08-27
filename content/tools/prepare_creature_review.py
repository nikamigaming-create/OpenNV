#!/usr/bin/env python3
"""Compile one classified creature review from owned records and BSA assets."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path, PureWindowsPath

from actor_gltf import (
    ActorAnimation,
    ActorComponent,
    ActorGltfInput,
    export_actor_gltf,
    retail_render_parts_from_snapshot,
)
from actor_review_contract import (
    PENDING_GODOT_STATUS,
    REVIEW_CONTRACT_SCHEMA,
)
from actor_source_stack import (
    build_actor_source_stack,
    parse_form_key,
    require_creature,
)
from bsa_archive import ExtractedMember, canonical_member_path
from owned_archive_stack import load_owned_archive_stack
from plugin_stack import FORM_ID_RADIX, build_plugin_stack, file_sha256
from runtime_configuration import configured_recipe_path, load_runtime_configuration


ACTOR_REVIEW_SCENE_SCHEMA = "opennv-actor-review-scene/v1"
COMPILED_PENDING_STATUS = "compiled-retail-observed-pending-godot-capture"
CREATURE_RECORD_TYPE = "CREA"
MESH_ROOT = "meshes"
NIF_SUFFIX = ".nif"
NO_SOURCE_SLOT = 0xFFFFFFFF
EXIT_DATA_ERROR = 2


@dataclass(frozen=True)
class RetailAttachmentModel:
    role: str
    source_form_id: str
    source_slot: int
    model_path: str


def _load_json(path: Path) -> dict[str, object]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError(f"Expected one JSON object: {path}")
    return document


def _asset_row(member: ExtractedMember) -> dict[str, object]:
    return {
        "logicalPath": member.logical_path,
        "bytes": len(member.data),
        "sha256": member.sha256,
        "sourceArchive": member.source_archive,
        "sourceArchiveSha256": member.source_archive_sha256,
    }


def _mesh_member(path: str) -> str:
    canonical = canonical_member_path(path)
    return canonical if canonical.startswith(f"{MESH_ROOT}\\") else f"{MESH_ROOT}\\{canonical}"


def _creature_model_member(skeleton_path: str, model_path: str) -> str:
    model = PureWindowsPath(canonical_member_path(model_path))
    if model.suffix.lower() != NIF_SUFFIX:
        raise ValueError(f"CREA model is not a NIF: {model_path}")
    if len(model.parts) == 1:
        skeleton = PureWindowsPath(canonical_member_path(skeleton_path))
        model = skeleton.parent / model
    return _mesh_member(str(model))


def _retail_animation_paths(contract: dict[str, object]) -> tuple[str, tuple[str, ...]]:
    sample_paths = []
    ordered_paths = []
    for shot in contract["retail"]["shots"]:
        for sample in shot["samples"]:
            paths = tuple(str(layer["file"]) for layer in sample["animationLayers"])
            if not paths:
                raise ValueError("Retail creature sample has no animation layers")
            sample_paths.append({path.casefold(): path for path in paths})
            for path in paths:
                if path.casefold() not in {value.casefold() for value in ordered_paths}:
                    ordered_paths.append(path)
    primary_keys = [next(iter(paths)) for paths in sample_paths]
    if len(set(primary_keys)) != 1:
        raise ValueError(
            "Retail creature evidence changes its first published animation layer across samples"
        )
    primary_key = primary_keys[0]
    primary = sample_paths[0][primary_key]
    additional = tuple(path for path in ordered_paths if path.casefold() != primary_key)
    return primary, additional


def _retail_equipped_weapon_attachment(
    contract: dict[str, object],
) -> RetailAttachmentModel | None:
    snapshot = contract["retail"]["appearance"]["snapshot"]
    weapon = snapshot["equippedWeapon"]
    if (
        weapon["state"] != "equipped"
        or weapon["renderState"] != "visible-source-bound"
    ):
        return None
    source_form_id = str(weapon["sourceFormId"])
    model_path = str(weapon["modelPath"])
    matching = [
        part
        for part in snapshot["renderParts"]
        if part["role"] == "weapon"
        and part["sourceFormId"] == source_form_id
        and part["modelPath"] == model_path
        and bool(part["required"])
        and bool(part["attached"])
        and bool(part["drawable"])
        and bool(part["visible"])
    ]
    source_slots = {int(part["sourceSlot"]) for part in matching}
    if not matching or len(source_slots) != 1:
        raise ValueError("Retail equipped weapon has no unique visible attachment slot")
    if PureWindowsPath(canonical_member_path(model_path)).suffix.lower() != NIF_SUFFIX:
        raise ValueError(f"Retail equipped weapon model is not a NIF: {model_path}")
    return RetailAttachmentModel("weapon", source_form_id, source_slots.pop(), model_path)


def _validate_contract_sources(
    data_root: Path,
    contract: dict[str, object],
    expected_record_type: str = CREATURE_RECORD_TYPE,
) -> tuple[tuple[object, ...], dict[str, object]]:
    if contract.get("schema") != REVIEW_CONTRACT_SCHEMA or contract.get("status") != PENDING_GODOT_STATUS:
        raise ValueError("Compilation requires a pending actor review contract")
    if contract["assembly"]["recordType"] != expected_record_type:
        raise ValueError(
            "Actor review contract record type differs from the selected compiler: "
            f"expected={expected_record_type} actual={contract['assembly']['recordType']}"
        )
    manifest_descriptor = contract["provenance"]["corpusManifest"]
    manifest_path = Path(str(manifest_descriptor["path"]))
    if file_sha256(manifest_path).lower() != str(manifest_descriptor["sha256"]).lower():
        raise ValueError("Actor review contract corpus manifest hash changed")
    manifest = _load_json(manifest_path)
    plugin_names = [str(row["file"]) for row in manifest["inputs"]]
    contexts = build_plugin_stack(data_root, plugin_names)
    actual = [(row.name, row.sha256, row.bytes) for row in contexts]
    expected = [
        (str(row["file"]), str(row["sha256"]), int(row["bytes"]))
        for row in contract["provenance"]["officialPlugins"]
    ]
    if actual != expected:
        raise ValueError("Owned plugin stack differs from the actor review contract")
    return contexts, manifest


def prepare_creature_review(
    data_root: Path,
    contract_path: Path,
    cache_root: Path,
    archive_recipe_path: Path,
) -> dict[str, object]:
    contract = _load_json(contract_path)
    contexts, corpus_manifest = _validate_contract_sources(data_root, contract)
    sources = build_actor_source_stack(contexts)
    model_key = parse_form_key(str(contract["assembly"]["categorySources"]["model"]))
    source = sources.base(model_key)
    creature = require_creature(source)
    if creature.skeleton_path != contract["assembly"]["skeletonPath"] or list(creature.model_paths) != list(
        contract["assembly"]["modelPaths"]
    ):
        raise ValueError("Effective CREA model fields differ from the review contract")
    if creature.skeleton_path is None or not creature.model_paths:
        raise ValueError(f"CREA {model_key.text} has no complete skeleton/model assembly")

    configuration = load_runtime_configuration()
    actor_rig = configuration.actor_rig
    rig_profile = actor_rig.profiles[CREATURE_RECORD_TYPE]
    archives = load_owned_archive_stack(data_root, archive_recipe_path)
    skeleton = archives.extract(_mesh_member(creature.skeleton_path))
    model_members = [
        archives.extract(_creature_model_member(creature.skeleton_path, path))
        for path in creature.model_paths
    ]
    weapon_attachment = _retail_equipped_weapon_attachment(contract)
    weapon_member = None if weapon_attachment is None else archives.extract(
        _mesh_member(weapon_attachment.model_path)
    )
    primary_animation_path, additional_animation_paths = _retail_animation_paths(contract)
    primary_animation = archives.extract(_mesh_member(primary_animation_path))
    additional_animations = [
        archives.extract(_mesh_member(path))
        for path in additional_animation_paths
    ]

    review_key = str(contract["review"]["reviewKey"])
    base_runtime_form = (
        f"0x{int(str(contract['review']['baseRuntimeFormId']), FORM_ID_RADIX):08X}"
    )
    retail_render_parts = retail_render_parts_from_snapshot(
        contract["retail"]["appearance"]["snapshot"]
    )
    stable_id = hashlib.sha256(review_key.encode("utf-8")).hexdigest()[
        :configuration.content_compiler.stable_id_hex_characters
    ]
    final_root = cache_root / "generated" / "actor-reviews" / stable_id
    if final_root.exists():
        raise FileExistsError(f"Refusing to overwrite creature review cache: {final_root}")
    final_root.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=f"{stable_id}-", dir=final_root.parent) as directory:
        staging_root = Path(directory)
        gltf_path = staging_root / "actor.gltf"
        sidecar_path = staging_root / "actor.opennv.json"
        sidecar = export_actor_gltf(
            ActorGltfInput(
                review_key,
                creature.name or creature.editor_id,
                creature.skeleton_path,
                skeleton.data,
                (),
                (),
                tuple(
                    ActorComponent(
                        f"creature-model-{index}",
                        member.logical_path,
                        member.data,
                        source_form_id=base_runtime_form,
                        source_slot=NO_SOURCE_SLOT,
                    )
                    for index, member in enumerate(model_members)
                ) + (() if weapon_attachment is None or weapon_member is None else (
                    ActorComponent(
                        weapon_attachment.role,
                        weapon_member.logical_path,
                        weapon_member.data,
                        source_form_id=weapon_attachment.source_form_id,
                        source_slot=weapon_attachment.source_slot,
                    ),
                )),
                primary_animation.logical_path,
                primary_animation.data,
                skeleton_root_node=rig_profile.skeleton_root_node,
                rigid_attachment_node=rig_profile.unparented_rigid_node,
                biped_head_node=actor_rig.biped_head_node,
                additional_animations=tuple(
                    ActorAnimation(member.logical_path, member.data)
                    for member in additional_animations
                ),
                retail_render_parts=retail_render_parts,
            ),
            [archives],
            gltf_path,
            sidecar_path,
            configuration.content_compiler,
        )
        scene = {
            "schema": ACTOR_REVIEW_SCENE_SCHEMA,
            "status": COMPILED_PENDING_STATUS,
            "reviewKey": review_key,
            "baseFormKey": contract["review"]["baseFormKey"],
            "recordType": CREATURE_RECORD_TYPE,
            "configuration": configuration.manifest(),
            "source": {
                "plugin": source.context.name,
                "pluginSha256": source.context.sha256,
                "localFormId": f"{creature.form_id:08x}",
                "skeleton": _asset_row(skeleton),
                "models": [_asset_row(member) for member in model_members],
                "runtimeAttachments": [] if weapon_attachment is None or weapon_member is None else [
                    {
                        "role": weapon_attachment.role,
                        "sourceFormId": weapon_attachment.source_form_id,
                        "sourceSlot": weapon_attachment.source_slot,
                        "asset": _asset_row(weapon_member),
                    }
                ],
                "animations": [
                    _asset_row(member)
                    for member in (primary_animation, *additional_animations)
                ],
                "archiveStack": archives.manifest(),
            },
            "retailContract": {
                "path": str(contract_path.resolve()),
                "sha256": file_sha256(contract_path),
                "projectionStatus": "exact-retail-final-eye-d3d9-perspective",
                "animationLayersRetained": True,
            },
            "corpusManifest": {
                "path": contract["provenance"]["corpusManifest"]["path"],
                "sha256": file_sha256(Path(str(contract["provenance"]["corpusManifest"]["path"]))),
                "status": corpus_manifest["status"],
            },
            "outputs": {
                "gltf": gltf_path.name,
                "gltfSha256": sidecar["outputs"]["gltf"]["sha256"],
                "sidecar": sidecar_path.name,
                "sidecarSha256": file_sha256(sidecar_path),
                "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
            },
            "coverage": sidecar["coverage"],
            "evidencePolicy": {
                "compiledCacheIsNotVisualEvidence": True,
                "godotEvidenceStatus": "pending",
                "matchedComparisonStatus": "pending",
            },
        }
        scene_path = staging_root / "actor-review-scene.json"
        scene_path.write_text(json.dumps(scene, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        os.replace(staging_root, final_root)
    result_path = final_root / "actor-review-scene.json"
    scene["manifest"] = str(result_path.resolve())
    return scene


def default_archive_recipe_path() -> Path:
    return configured_recipe_path("visualArchives")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--review-contract", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--archive-recipe", type=Path, default=default_archive_recipe_path())
    args = parser.parse_args()
    try:
        scene = prepare_creature_review(
            args.data_root.resolve(),
            args.review_contract.resolve(),
            args.cache_root.resolve(),
            args.archive_recipe.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_CREATURE_REVIEW_ERROR {error}", file=sys.stderr)
        return EXIT_DATA_ERROR
    print(
        "OPENNV_CREATURE_REVIEW "
        + json.dumps(
            {
                "manifest": scene["manifest"],
                "reviewKey": scene["reviewKey"],
                "status": scene["status"],
                "coverage": scene["coverage"],
            },
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
