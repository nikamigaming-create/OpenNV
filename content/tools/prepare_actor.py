#!/usr/bin/env python3
"""Prepare one recipe-pinned retail actor through the clean direct pipeline."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path

from actor_catalog import ActorCatalog, ActorReference, HumanoidActor, scan_actor_catalog
from actor_gltf import ActorComponent, ActorGltfInput, export_actor_gltf
from bsa_archive import BsaArchive, canonical_member_path
from cell_catalog import scan_cell_catalog
from cell_scene import (
    arrival_transform,
    godot_position,
    godot_yaw_radians,
    load_recipe as load_cell_recipe,
)
from facegen import compose_body_albedo, compose_skin_albedo, synthesize_texture_detail
from texture_pipeline import decode_dds


RECIPE_SCHEMA = "opennv-actor-recipe/v1"


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_recipe(recipe_id: str) -> dict[str, object]:
    path = Path(__file__).resolve().parents[1] / "recipes" / f"{recipe_id}.json"
    recipe = json.loads(path.read_text(encoding="utf-8"))
    if recipe.get("schema") != RECIPE_SCHEMA or recipe.get("id") != recipe_id:
        raise ValueError(f"Unexpected OpenNV actor recipe: {path}")
    return recipe


def form_id(value: str) -> int:
    return int(value, 16)


def model_companion(path: str, suffix: str) -> str:
    if not path.lower().endswith(".nif"):
        raise ValueError(f"Actor model has no NIF suffix: {path}")
    return path[:-4] + suffix


def texture_member(path: str) -> str:
    canonical = canonical_member_path(path)
    return canonical if canonical.startswith("textures\\") else f"textures\\{canonical}"


def extract_texture(archives: list[BsaArchive], logical_path: str) -> bytes:
    path = texture_member(logical_path)
    matches = [archive for archive in archives if path in archive.members]
    if len(matches) != 1:
        raise FileNotFoundError(f"Expected one actor texture {path!r}, found {len(matches)}")
    return matches[0].extract(path).data


def has_texture(archives: list[BsaArchive], logical_path: str) -> bool:
    path = texture_member(logical_path)
    return sum(path in archive.members for archive in archives) == 1


def resolve_proof_actor(
    catalog: ActorCatalog,
    reference_form_id: int,
    cell_form_id: int,
) -> tuple[ActorReference, HumanoidActor]:
    references = [
        reference
        for reference in catalog.references_for(cell_form_id)
        if reference.form_id == reference_form_id and reference.record_type == "ACHR"
    ]
    if len(references) != 1:
        raise ValueError(f"Expected one proof ACHR {reference_form_id:08x}, found {len(references)}")
    actor = catalog.actors.get(references[0].actor_form_id)
    if actor is None:
        raise ValueError(f"Proof ACHR has no NPC_ base: {references[0].actor_form_id:08x}")
    if actor.race_form_id is None or actor.skeleton_path is None:
        raise ValueError("Proof actor does not contain the required race/skeleton identity")
    if (len(actor.face_symmetric_geometry), len(actor.face_asymmetric_geometry), len(actor.face_symmetric_texture)) != (
        50,
        30,
        50,
    ):
        raise ValueError("Proof actor has incomplete FaceGen coordinates")
    return references[0], actor


def prepare_actor(
    data_root: Path,
    cache_root: Path,
    recipe_id: str = "goodsprings-trudy-actor-v1",
) -> dict[str, object]:
    recipe = load_recipe(recipe_id)
    master = data_root / recipe["master"]["file"]
    meshes_path = data_root / recipe["meshesArchive"]["file"]
    texture_paths = [data_root / row["file"] for row in recipe["textureArchives"]]
    expected = [
        (master, recipe["master"]["sha256"]),
        (meshes_path, recipe["meshesArchive"]["sha256"]),
        *((path, row["sha256"]) for path, row in zip(texture_paths, recipe["textureArchives"])),
    ]
    for path, expected_hash in expected:
        if not path.is_file():
            raise FileNotFoundError(path)
        actual = file_sha256(path)
        if actual.lower() != str(expected_hash).lower():
            raise ValueError(f"Actor recipe source hash mismatch: {path.name} expected={expected_hash} actual={actual}")

    catalog = scan_actor_catalog(master)
    reference, actor = resolve_proof_actor(
        catalog,
        form_id(recipe["proofActorReferenceFormId"]),
        form_id(recipe["cellFormId"]),
    )
    cell_recipe = load_cell_recipe(str(recipe["cellRecipe"]))
    cell_catalog = scan_cell_catalog(master)
    _source_door, arrival = arrival_transform(
        cell_catalog,
        form_id(cell_recipe["entryDoorReferenceFormId"]),
    )
    race = catalog.races.get(actor.race_form_id)
    head_models = race.female_head_models if actor.female and race is not None else (
        race.male_head_models if race is not None else ()
    )
    head_textures = race.female_head_textures if actor.female and race is not None else (
        race.male_head_textures if race is not None else ()
    )
    body_models = race.female_body_models if actor.female and race is not None else (
        race.male_body_models if race is not None else ()
    )
    body_textures = race.female_body_textures if actor.female and race is not None else (
        race.male_body_textures if race is not None else ()
    )
    race_face_symmetric_geometry = (
        race.female_face_symmetric_geometry if actor.female and race is not None else
        race.male_face_symmetric_geometry if race is not None else ()
    )
    race_face_asymmetric_geometry = (
        race.female_face_asymmetric_geometry if actor.female and race is not None else
        race.male_face_asymmetric_geometry if race is not None else ()
    )
    race_face_symmetric_texture = (
        race.female_face_symmetric_texture if actor.female and race is not None else
        race.male_face_symmetric_texture if race is not None else ()
    )
    if (
        race is None
        or len(head_models) < 8
        or len(head_textures) < 1
        or len(body_models) < 3
        or len(body_textures) < 3
    ):
        raise ValueError("Proof actor race has no complete sex-specific head/body table")
    hair = catalog.parts.get(actor.hair_form_id or 0)
    eyes = catalog.parts.get(actor.eyes_form_id or 0)
    head_parts = [catalog.parts.get(part) for part in actor.head_part_form_ids]
    if hair is None or hair.model_path is None or eyes is None or eyes.texture_path is None:
        raise ValueError("Proof actor has incomplete hair or eye records")
    if any(part is None for part in head_parts):
        raise ValueError("Proof actor has an unresolved head-part record")
    recipe_outfits = [form_id(value) for value in recipe.get("outfitArmorFormIds", [])]
    outfit_forms = recipe_outfits or [
        item.form_id for item in actor.inventory if item.form_id in catalog.armor
    ]
    outfits = [catalog.armor.get(value) for value in outfit_forms]
    if not outfits or any(outfit is None for outfit in outfits):
        raise ValueError(f"Proof actor has unresolved outfit armor: {outfit_forms}")
    outfit_models = [
        outfit.female_model_path if actor.female else outfit.male_model_path
        for outfit in outfits
    ]
    if any(path is None for path in outfit_models):
        raise ValueError("Proof actor outfit lacks a sex-specific model")

    meshes = BsaArchive(meshes_path)
    texture_archives = [BsaArchive(path) for path in texture_paths]

    def mesh(path: str) -> bytes:
        canonical = canonical_member_path(path)
        logical_path = canonical if canonical.startswith("meshes\\") else f"meshes\\{canonical}"
        return meshes.extract(logical_path).data

    head_model = head_models[0]
    head_texture = head_textures[0]
    if head_model is None or head_texture is None:
        raise ValueError("Proof actor race has no sex-specific head model or texture")
    head_egm = model_companion(head_model, ".egm")
    head_egt = model_companion(head_model, ".egt")
    if (
        len(race_face_symmetric_geometry),
        len(race_face_asymmetric_geometry),
        len(race_face_symmetric_texture),
    ) != (50, 30, 50):
        raise ValueError("Proof actor race has incomplete sex-specific FaceGen baseline coordinates")
    face_mod_path = f"textures\\characters\\facemods\\falloutnv.esm\\{actor.form_id:08x}_0.dds"
    if has_texture(texture_archives, face_mod_path):
        detail = decode_dds(extract_texture(texture_archives, face_mod_path), False)
        face_detail_source = "retail-precomputed"
    else:
        detail = synthesize_texture_detail(mesh(head_egt), actor.face_symmetric_texture)
        face_detail_source = "direct-egt-fallback"
    base_diffuse = decode_dds(extract_texture(texture_archives, head_texture), False)
    tone = tuple(int(value) for value in recipe["skinToneRgba"][:3])
    generated_head = compose_skin_albedo(base_diffuse, detail, tone)
    body_texture = body_textures[0]
    if body_texture is None or body_models[1] is None or body_models[2] is None:
        raise ValueError("Proof actor race has no sex-specific upper-body texture or hand meshes")
    sex_label = "female" if actor.female else "male"
    body_mod_path = (
        f"textures\\characters\\bodymods\\falloutnv.esm\\{actor.form_id:08x}modbody{sex_label}.dds"
    )
    if not has_texture(texture_archives, body_mod_path):
        raise ValueError("Proof actor has no retail precomputed body-mod texture")
    body_mod = decode_dds(extract_texture(texture_archives, body_mod_path), False)
    generated_body = compose_body_albedo(
        decode_dds(extract_texture(texture_archives, body_texture), False),
        body_mod,
    )
    left_hand_texture = body_textures[1]
    right_hand_texture = body_textures[2]
    if left_hand_texture is None or right_hand_texture is None:
        raise ValueError("Proof actor race has no sex-specific hand textures")
    generated_left_hand = compose_body_albedo(
        decode_dds(extract_texture(texture_archives, left_hand_texture), False),
        body_mod,
    )
    generated_right_hand = compose_body_albedo(
        decode_dds(extract_texture(texture_archives, right_hand_texture), False),
        body_mod,
    )

    body_texture_sources = {
        texture_member(body_texture),
        *(texture_member(value) for value in recipe.get("bodyTextureSourceAliases", [])),
    }
    rigid_outfit_forms = {
        form_id(value) for value in recipe.get("rigidOutfitArmorFormIds", [])
    }
    components = [
        ActorComponent(
            f"outfit-{index}",
            outfit_model,
            mesh(outfit_model),
            excluded_shape_prefixes=tuple(recipe["excludeOutfitShapePrefixes"]),
            rigid_to_head=outfit_forms[index] in rigid_outfit_forms,
            generated_diffuse_by_source=tuple(
                (source, generated_body) for source in sorted(body_texture_sources)
            ),
        )
        for index, outfit_model in enumerate(outfit_models)
    ]
    components.extend([
        ActorComponent(
            "left-hand",
            body_models[1],
            mesh(body_models[1]),
            generated_diffuse=generated_left_hand,
            bake_shape_transform=not actor.female,
        ),
        ActorComponent(
            "right-hand",
            body_models[2],
            mesh(body_models[2]),
            generated_diffuse=generated_right_hand,
            bake_shape_transform=not actor.female,
        ),
        ActorComponent(
            "head",
            head_model,
            mesh(head_model),
            egm_path=head_egm,
            egm_payload=mesh(head_egm),
            generated_diffuse=generated_head,
        ),
    ])
    roles = {2: "mouth", 3: "teeth-lower", 4: "teeth-upper", 5: "tongue", 6: "eye-left", 7: "eye-right"}
    for index, role in roles.items():
        path = head_models[index]
        if path is None:
            raise ValueError(f"Proof actor race has no sex-specific head component {index}")
        components.append(
            ActorComponent(
                role,
                path,
                mesh(path),
                egm_path=model_companion(path, ".egm"),
                egm_payload=mesh(model_companion(path, ".egm")),
                rigid_to_head=True,
                diffuse_override=texture_member(eyes.texture_path) if role.startswith("eye-") else None,
            )
        )
    hair_egm = model_companion(hair.model_path, f"{str(recipe['hairShape']).lower()}.egm")
    components.append(
        ActorComponent(
            "hair",
            hair.model_path,
            mesh(hair.model_path),
            egm_path=hair_egm,
            egm_payload=mesh(hair_egm),
            rigid_to_head=True,
            selected_shape=str(recipe["hairShape"]),
            tint_rgb=tuple(value / 255.0 for value in actor.hair_color_rgba[:3]),
        )
    )
    for part in (part for part in head_parts if part.model_path is not None):
        components.append(
            ActorComponent(
                f"head-part-{part.editor_id}",
                part.model_path,
                mesh(part.model_path),
                egm_path=model_companion(part.model_path, ".egm"),
                egm_payload=mesh(model_companion(part.model_path, ".egm")),
                rigid_to_head=True,
                tint_rgb=tuple(value / 255.0 for value in actor.hair_color_rgba[:3]),
            )
        )

    output_root = cache_root / "generated" / "actors" / recipe_id
    gltf_path = output_root / "actor.gltf"
    sidecar_path = output_root / "actor.opennv.json"
    sidecar = export_actor_gltf(
        ActorGltfInput(
            f"{actor.form_id:08x}",
            actor.name,
            actor.skeleton_path,
            mesh(actor.skeleton_path),
            actor.face_symmetric_geometry,
            actor.face_asymmetric_geometry,
            tuple(components),
            str(recipe["idleAnimation"]),
            mesh(str(recipe["idleAnimation"])),
        ),
        texture_archives,
        gltf_path,
        sidecar_path,
    )
    manifest = {
        "schema": "opennv-actor-scene/v3",
        "status": "skinned-animated",
        "recipe": recipe_id,
        "cellFormId": recipe["cellFormId"],
        "reference": {
            "formId": f"{reference.form_id:08x}",
            "baseFormId": f"{reference.actor_form_id:08x}",
            "initiallyDisabled": reference.initially_disabled,
            "positionGameUnits": list(reference.position),
            "positionGodotUnits": godot_position(reference.position, arrival.position),
            "rotationRadians": list(reference.rotation_radians),
            "yawRadians": reference.rotation_radians[2],
            "yawGodotRadians": godot_yaw_radians(reference.rotation_radians[2]),
        },
        "actor": {
            "name": actor.name,
            "editorId": actor.editor_id,
            "female": actor.female,
            "raceFormId": f"{actor.race_form_id:08x}",
            "hairFormId": f"{actor.hair_form_id:08x}",
            "eyesFormId": f"{actor.eyes_form_id:08x}",
            "headPartFormIds": [f"{part:08x}" for part in actor.head_part_form_ids],
            "outfitFormId": f"{outfits[0].form_id:08x}",
        },
        "idleAnimation": recipe["idleAnimation"],
        "faceDetailSource": face_detail_source,
        "faceDetailLogicalPath": face_mod_path if face_detail_source == "retail-precomputed" else head_egt,
        "bodyModLogicalPath": body_mod_path,
        "outputs": {
            "gltf": gltf_path.name,
            "sidecar": sidecar_path.name,
            "gltfSha256": sidecar["outputs"]["gltf"]["sha256"],
            "sidecarSha256": file_sha256(sidecar_path),
            "bufferSha256": sidecar["outputs"]["buffer"]["sha256"],
        },
        "coverage": sidecar["coverage"],
    }
    manifest_path = output_root / "actor-scene.json"
    _atomic_json(manifest_path, manifest)
    manifest["manifest"] = str(manifest_path.resolve())
    return manifest


def _atomic_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def prepare_actor_set(
    data_root: Path,
    cache_root: Path,
    recipe_ids: list[str],
) -> dict[str, object]:
    if len(recipe_ids) < 1 or len(set(recipe_ids)) != len(recipe_ids):
        raise ValueError("Actor-set recipes must be non-empty and unique")
    actors = [prepare_actor(data_root, cache_root, recipe_id) for recipe_id in recipe_ids]
    cell_form_ids = {str(actor["cellFormId"]) for actor in actors}
    reference_form_ids = {str(actor["reference"]["formId"]) for actor in actors}
    if len(cell_form_ids) != 1 or len(reference_form_ids) != len(actors):
        raise ValueError("Actor-set members must belong to one cell and unique references")
    document = {
        "schema": "opennv-cell-actor-scenes/v1",
        "cellFormId": next(iter(cell_form_ids)),
        "actors": [
            {
                "recipe": actor["recipe"],
                "referenceFormId": actor["reference"]["formId"],
                "baseFormId": actor["reference"]["baseFormId"],
                "scene": actor["manifest"],
                "sha256": file_sha256(Path(actor["manifest"])),
            }
            for actor in actors
        ],
    }
    path = cache_root / "generated" / "actors" / "actor-scenes.json"
    _atomic_json(path, document)
    document["manifest"] = str(path.resolve())
    return document


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--recipe", action="append")
    args = parser.parse_args()
    recipes = args.recipe or ["goodsprings-trudy-actor-v1"]
    if len(recipes) == 1:
        result = prepare_actor(args.data_root.resolve(), args.cache_root.resolve(), recipes[0])
        print("OPENNV_ACTOR_SCENE " + json.dumps(result, sort_keys=True))
    else:
        result = prepare_actor_set(args.data_root.resolve(), args.cache_root.resolve(), recipes)
        print("OPENNV_ACTOR_SCENE_SET " + json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
