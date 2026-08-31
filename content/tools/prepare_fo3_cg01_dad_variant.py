#!/usr/bin/env python3
"""Prepare one exact source-derived Fallout 3 CG01 Dad actor variant."""

from __future__ import annotations

import argparse
import hashlib
import json
from dataclasses import dataclass
from pathlib import Path

from actor_catalog import (
    FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
    FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
    FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
)
from bsa_archive import BsaArchive, canonical_member_path
from prepare_actor import ActorAppearanceOverride, prepare_actor
from prepare_fo3_birth_presentation import (
    FORM_ID_HEX_CHARACTERS,
    FORM_ID_RADIX,
    PROFILE_SCHEMA,
    RECIPE_SCHEMA,
    VARIANT_HASH_PREFIX_CHARACTERS,
    _archive,
    _default_recipe_path as _default_birth_presentation_recipe_path,
    _facegen_values,
    _load_prepared_actor,
    _read_json,
    _required_list,
    _required_object,
    _required_sha256,
    _required_string,
    _sha256_bytes,
    _sha256_file,
    _verify_source_file,
)


@dataclass(frozen=True)
class Cg01DadVariant:
    output_identity: str
    appearance_override: ActorAppearanceOverride
    runtime_animation_paths: tuple[str, ...]


@dataclass(frozen=True)
class Cg01DadVariantBatch:
    required: tuple[str, ...]
    reused: tuple[str, ...]
    rebuilt: tuple[str, ...]
    manifests: tuple[Path, ...]


def _variant_output_root(cache_root: Path, output_identity: str) -> Path:
    if (
        not output_identity
        or any(
            not (character.isascii() and (character.isalnum() or character == "-"))
            for character in output_identity
        )
    ):
        raise ValueError("Requested CG01 Dad actor output identity is invalid")
    actors_root = (cache_root.resolve() / "generated" / "actors").resolve()
    output_root = (actors_root / output_identity).resolve()
    try:
        relative = output_root.relative_to(actors_root)
    except ValueError as error:
        raise ValueError("Requested CG01 Dad actor output escapes the actor cache") from error
    if relative.parts != (output_identity,):
        raise ValueError("Requested CG01 Dad actor output is not one exact directory")
    return output_root


def _select_exact_variant(
    variants: list[Cg01DadVariant],
    output_identity: str,
) -> Cg01DadVariant:
    matches = [row for row in variants if row.output_identity == output_identity]
    if len(matches) != 1:
        condition = "unknown" if not matches else "ambiguous"
        raise ValueError(f"Requested CG01 Dad actor output identity is {condition}")
    return matches[0]


def _dialogue_animation_contract(
    branches: list[object],
    meshes_path: Path,
    meshes_row: dict[str, object],
    archive: BsaArchive,
    player_sex: str | None,
) -> tuple[list[dict[str, object]], list[str]]:
    selected = sorted(
        (
            row
            for row in branches
            if isinstance(row, dict)
            and (player_sex is None or row.get("engineSex") == player_sex)
        ),
        key=lambda row: int(row.get("sequence", -1)),
    )
    if [int(row.get("sequence", -1)) for row in selected] != [0, 1]:
        raise ValueError("Fallout 3 CG01 Dad dialogue sequence differs")
    contract: list[dict[str, object]] = []
    paths: list[str] = []
    for branch in selected:
        speaker_idle = _required_object(branch, "speakerIdle")
        form_id = _required_string(speaker_idle, "formId")
        model_path = canonical_member_path(
            _required_string(speaker_idle, "modelPath")
        )
        if (
            len(form_id) != FORM_ID_HEX_CHARACTERS
            or any(character not in "0123456789abcdef" for character in form_id)
            or not model_path.startswith("meshes\\characters\\_male\\idleanims\\")
            or not model_path.endswith(".kf")
            or speaker_idle.get("sourceArchive") != meshes_path.name
            or _required_sha256(speaker_idle, "sourceArchiveSha256")
            != _required_sha256(meshes_row, "sha256")
        ):
            raise ValueError("Fallout 3 CG01 Dad dialogue ownership differs")
        member = archive.extract(model_path)
        if (
            len(member.data) != int(speaker_idle.get("sourceBytes", 0))
            or member.sha256 != _required_sha256(speaker_idle, "sourceSha256")
        ):
            raise ValueError("Fallout 3 CG01 Dad dialogue source changed")
        paths.append(model_path)
        row = {
            "sequence": int(branch["sequence"]),
            "infoFormId": _required_string(branch, "infoFormId"),
            "speakerIdle": speaker_idle,
        }
        if player_sex is not None:
            row["engineSex"] = player_sex
        contract.append(row)
    return contract, paths


def _resolve_variants(
    profile_path: Path,
    recipe_path: Path,
) -> tuple[Path, dict[str, object], list[Cg01DadVariant]]:
    profile, _profile_payload = _read_json(profile_path.resolve())
    if (
        profile.get("schema") != PROFILE_SCHEMA
        or profile.get("campaign") != "Fallout3"
        or profile.get("status") != "registered-owned-profile"
    ):
        raise ValueError("Fallout 3 owned profile identity is unsupported")
    install = _required_object(profile, "install")
    meshes_row = _archive(install, "meshes")
    meshes_path = _verify_source_file(meshes_row)
    recipe, _recipe_payload = _read_json(recipe_path.resolve())
    if recipe.get("schema") != RECIPE_SCHEMA:
        raise ValueError("Fallout 3 birth-presentation recipe schema is unsupported")
    actor_recipes = _required_object(recipe, "actorRecipes")
    actor_recipe_path = (
        recipe_path.resolve().parent / _required_string(actor_recipes, "cg01Dad")
    ).resolve()
    actor_recipe, _actor_recipe_payload = _read_json(actor_recipe_path)
    if actor_recipe.get("schema") != "opennv-actor-recipe/v1":
        raise ValueError("Fallout 3 CG01 Dad actor recipe is unsupported")

    opening = _required_object(profile, "opening")
    birth_source = _required_object(opening, "birthSlice")
    birth_path = Path(_required_string(birth_source, "output")).resolve()
    birth, birth_payload = _read_json(birth_path)
    if _sha256_bytes(birth_payload) != _required_sha256(birth_source, "sha256"):
        raise ValueError("Fallout 3 birth-slice manifest hash differs from its profile")
    birth_source_contract = _required_object(birth, "source")
    source_meshes = _required_object(birth_source_contract, "meshesArchive")
    source_textures = _required_object(birth_source_contract, "texturesArchive")
    actor_master = _required_object(actor_recipe, "master")
    actor_meshes = _required_object(actor_recipe, "meshesArchive")
    actor_textures = _required_list(actor_recipe, "textureArchives")
    start_graph = _required_object(birth, "startGraph")
    entry_position = _required_list(
        _required_object(_required_object(start_graph, "playerSpawn"), "transform"),
        "positionGameUnits",
    )

    character_selection = _required_object(opening, "characterSelection")
    transition = _required_object(character_selection, "cg01Stage0Transition")
    commands = _required_list(_required_object(transition, "stage0Result"), "commands")
    move_commands = [
        row
        for row in commands
        if isinstance(row, dict)
        and row.get("kind") == "moveToReference"
        and isinstance(row.get("subject"), dict)
        and isinstance(row["subject"].get("base"), dict)
        and row["subject"]["base"].get("editorId") == "CG01Dad"
    ]
    if len(move_commands) != 1:
        raise ValueError("Fallout 3 CG01 Dad stage-0 MoveTo is absent or ambiguous")
    cg01_dad = _required_object(move_commands[0], "subject")
    cg01_base = _required_object(cg01_dad, "base")
    cell_form_id = _required_string(_required_object(birth, "cell"), "formId")
    if (
        actor_recipe.get("cellFormId") != cell_form_id
        or actor_recipe.get("proofActorReferenceFormId") != cg01_dad.get("formId")
        or actor_recipe.get("expectedBaseFormId") != cg01_base.get("formId")
        or actor_recipe.get("originGameUnits") != entry_position
        or actor_recipe.get("bodyModPolicy")
        != "owned-race-base-diffuse-when-precomputed-absent"
        or actor_master.get("file")
        != birth_source_contract.get("master", {}).get("file")
        or actor_master.get("sha256")
        != birth_source_contract.get("master", {}).get("sha256")
        or actor_meshes.get("file") != source_meshes.get("file")
        or actor_meshes.get("sha256") != source_meshes.get("sha256")
        or len(actor_textures) != 1
        or not isinstance(actor_textures[0], dict)
        or actor_textures[0].get("file") != source_textures.get("file")
        or actor_textures[0].get("sha256") != source_textures.get("sha256")
    ):
        raise ValueError("Fallout 3 CG01 Dad recipe does not bind the owned profile")

    stage65 = _required_object(character_selection, "stage65Appearance")
    if (
        stage65.get("schema") != "opennv-fo3-cg00-stage-65-appearance/v1"
        or stage65.get("status") != "source-backed-command-application"
    ):
        raise ValueError("Fallout 3 stage-65 appearance contract is unsupported")
    stage65_sha256 = _sha256_bytes(
        json.dumps(stage65, sort_keys=True, separators=(",", ":")).encode("utf-8")
    )
    post_stage5 = _required_object(transition, "postStage5Transition")
    dialogue = _required_object(post_stage5, "dialogue")
    stage12_dialogue = _required_object(
        _required_object(post_stage5, "postStage12DadResponse"),
        "dialogue",
    )
    if (
        dialogue.get("dialoguePlaybackPrepared") is not True
        or dialogue.get("dialoguePlaybackImplemented") is not True
        or stage12_dialogue.get("dialoguePlaybackPrepared") is not True
        or stage12_dialogue.get("dialoguePlaybackImplemented") is not True
    ):
        raise ValueError("Fallout 3 CG01 Dad dialogue assets are not prepared")
    archive = BsaArchive(meshes_path)
    stage12_contract, stage12_paths = _dialogue_animation_contract(
        _required_list(stage12_dialogue, "branches"),
        meshes_path,
        meshes_row,
        archive,
        None,
    )
    stage12_sha256 = _sha256_bytes(
        json.dumps(stage12_contract, sort_keys=True, separators=(",", ":")).encode(
            "utf-8"
        )
    )

    variants: list[Cg01DadVariant] = []
    for selection in _required_list(stage65, "selectionResults"):
        if not isinstance(selection, dict):
            raise ValueError("Fallout 3 stage-65 selection row is malformed")
        race_form_id = _required_string(selection, "playerRaceFormId")
        player_sex = _required_string(selection, "playerSex")
        if (
            len(race_form_id) != FORM_ID_HEX_CHARACTERS
            or any(character not in "0123456789abcdef" for character in race_form_id)
            or player_sex not in {"male", "female"}
        ):
            raise ValueError("Fallout 3 stage-65 selection identity is invalid")
        parents = [
            row
            for row in _required_list(selection, "parents")
            if isinstance(row, dict)
            and row.get("referenceFormId") == cg01_dad.get("formId")
        ]
        if len(parents) != 1:
            raise ValueError("Fallout 3 stage-65 CG01 Dad result is ambiguous")
        parent = parents[0]
        facegen = _required_object(parent, "faceGen")
        symmetric, symmetric_sha256 = _facegen_values(
            _required_object(facegen, "symmetricGeometry"),
            FACEGEN_SYMMETRIC_GEOMETRY_FLOATS,
        )
        asymmetric, _asymmetric_sha256 = _facegen_values(
            _required_object(facegen, "asymmetricGeometry"),
            FACEGEN_ASYMMETRIC_GEOMETRY_FLOATS,
        )
        texture, _texture_sha256 = _facegen_values(
            _required_object(facegen, "symmetricTexture"),
            FACEGEN_SYMMETRIC_TEXTURE_FLOATS,
        )
        if (
            parent.get("baseFormId") != cg01_base.get("formId")
            or parent.get("raceFormId") != race_form_id
            or facegen.get("texturePolicy")
            != "matched-race-default-not-face-geometry-morphed"
        ):
            raise ValueError("Fallout 3 stage-65 CG01 Dad appearance differs")
        dialogue_contract, dialogue_paths = _dialogue_animation_contract(
            _required_list(dialogue, "branches"),
            meshes_path,
            meshes_row,
            archive,
            player_sex,
        )
        dialogue_sha256 = _sha256_bytes(
            json.dumps(
                dialogue_contract,
                sort_keys=True,
                separators=(",", ":"),
            ).encode("utf-8")
        )
        variant_id = (
            "stage65-"
            f"{race_form_id}-{player_sex}-"
            f"{symmetric_sha256[:VARIANT_HASH_PREFIX_CHARACTERS]}-"
            f"cg01speech-{dialogue_sha256[:VARIANT_HASH_PREFIX_CHARACTERS]}-"
            f"walked-{stage12_sha256[:VARIANT_HASH_PREFIX_CHARACTERS]}"
        )
        runtime_paths = tuple(dict.fromkeys([*dialogue_paths, *stage12_paths]))
        variants.append(
            Cg01DadVariant(
                output_identity=f"{_required_string(actor_recipe, 'id')}-{variant_id}",
                appearance_override=ActorAppearanceOverride(
                    variant_id=variant_id,
                    authority=(
                        "owned-stage-65-MatchRace-and-50-percent-MatchFaceGeometry"
                    ),
                    source_sha256=stage65_sha256,
                    reference_form_id=int(
                        _required_string(cg01_dad, "formId"),
                        FORM_ID_RADIX,
                    ),
                    base_form_id=int(
                        _required_string(cg01_base, "formId"),
                        FORM_ID_RADIX,
                    ),
                    race_form_id=int(race_form_id, FORM_ID_RADIX),
                    symmetric_geometry=symmetric,
                    asymmetric_geometry=asymmetric,
                    symmetric_texture=texture,
                ),
                runtime_animation_paths=runtime_paths,
            )
        )
    return Path(_required_string(install, "dataRoot")), actor_recipe, variants


def _prepare_selected_variant(
    data_root: Path,
    cache_root: Path,
    actor_recipe: dict[str, object],
    selected: Cg01DadVariant,
) -> Path:
    output_identity = selected.output_identity
    target_root = _variant_output_root(cache_root, selected.output_identity)
    expected_identity = (
        f"{_required_string(actor_recipe, 'id')}-"
        f"{selected.appearance_override.variant_id}"
    )
    if expected_identity != output_identity:
        raise ValueError("Resolved CG01 Dad actor output identity differs")
    manifest = prepare_actor(
        data_root.resolve(),
        cache_root.resolve(),
        _required_string(actor_recipe, "id"),
        recipe_document=actor_recipe,
        runtime_animation_paths=selected.runtime_animation_paths,
        appearance_override=selected.appearance_override,
    )
    manifest_path = Path(_required_string(manifest, "manifest")).resolve()
    if (
        manifest_path != target_root / "actor-scene.json"
        or manifest.get("schema") != "opennv-actor-scene/v5"
        or manifest.get("status") != "skinned-animated"
        or _required_object(manifest, "appearanceOverride").get("variantId")
        != selected.appearance_override.variant_id
    ):
        raise ValueError("Prepared CG01 Dad actor escaped its exact output identity")
    return manifest_path


def prepare_exact_variant(
    profile_path: Path,
    cache_root: Path,
    recipe_path: Path,
    output_identity: str,
) -> Path:
    data_root, actor_recipe, variants = _resolve_variants(
        profile_path,
        recipe_path,
    )
    selected = _select_exact_variant(variants, output_identity)
    return _prepare_selected_variant(
        data_root,
        cache_root,
        actor_recipe,
        selected,
    )


def prepare_all_required_stale_variants(
    profile_path: Path,
    cache_root: Path,
    recipe_path: Path,
) -> Cg01DadVariantBatch:
    data_root, actor_recipe, variants = _resolve_variants(
        profile_path,
        recipe_path,
    )
    ordered = sorted(variants, key=lambda row: row.output_identity)
    identities = tuple(row.output_identity for row in ordered)
    if not identities or len(identities) != len(set(identities)):
        raise ValueError("Required CG01 Dad actor variant matrix is empty or ambiguous")
    reused: list[str] = []
    stale: list[Cg01DadVariant] = []
    for variant in ordered:
        try:
            _load_prepared_actor(
                cache_root,
                actor_recipe,
                variant.appearance_override,
                variant.runtime_animation_paths,
            )
        except (FileNotFoundError, ValueError):
            stale.append(variant)
        else:
            reused.append(variant.output_identity)

    manifests = [
        _prepare_selected_variant(
            data_root,
            cache_root,
            actor_recipe,
            variant,
        )
        for variant in stale
    ]
    verified: list[Path] = []
    for variant in ordered:
        manifest = _load_prepared_actor(
            cache_root,
            actor_recipe,
            variant.appearance_override,
            variant.runtime_animation_paths,
        )
        verified.append(Path(_required_string(manifest, "manifest")).resolve())
    return Cg01DadVariantBatch(
        required=identities,
        reused=tuple(reused),
        rebuilt=tuple(row.output_identity for row in stale),
        manifests=tuple(verified),
    )


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--output-identity")
    mode.add_argument("--all-required-stale", action="store_true")
    parser.add_argument(
        "--recipe",
        type=Path,
        default=_default_birth_presentation_recipe_path(),
    )
    arguments = parser.parse_args()
    if arguments.all_required_stale:
        batch = prepare_all_required_stale_variants(
            arguments.profile,
            arguments.cache_root,
            arguments.recipe,
        )
        result = {
            "manifests": [str(path) for path in batch.manifests],
            "rebuilt": list(batch.rebuilt),
            "required": list(batch.required),
            "reused": list(batch.reused),
            "status": "prepared-all-required-stale-cg01-dad-actor-variants",
        }
    else:
        output = prepare_exact_variant(
            arguments.profile,
            arguments.cache_root,
            arguments.recipe,
            arguments.output_identity,
        )
        result = {
            "output": str(output),
            "sha256": _sha256_file(output),
            "outputIdentity": arguments.output_identity,
            "status": "prepared-exact-cg01-dad-actor-variant",
        }
    print(
        json.dumps(
            result,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
