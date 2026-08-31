"""Compile the six classic premade 3D analogs from legally owned FNV data."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
from pathlib import Path

from prepare_actor import prepare_actor_set


CAST_SCHEMA = "opennv-classic-premade-analog-cast/v1"
BASE_PREVIEW_SCHEMA = "opennv-owned-player-facegen-preview-set/v3"
OUTPUT_SCHEMA = "opennv-owned-player-facegen-preview-set/v4"
OUTPUT_STATUS = (
    "compiled-default-custom-and-six-classic-premade-full-body-analogs-runtime-bound"
)
EXPECTED_CAMPAIGN_CHARACTERS = {
    "fallout1": {"max-stone", "natalia", "albert"},
    "fallout2": {"combat", "stealth", "diplomat"},
}
ANALOG_BODY_ROLES = ("outfit-0", "left-hand", "right-hand")
EQUIPMENT_SOCKET_NODE = "Bip01 R Hand"


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _json(path: Path) -> dict[str, object]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"Expected one JSON object: {path}")
    return value


def _atomic_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(
        json.dumps(value, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    os.replace(temporary, path)


def _normalized_form_id(value: object) -> str:
    text = str(value).lower().removeprefix("0x")
    if len(text) != 8 or any(character not in "0123456789abcdef" for character in text):
        raise ValueError(f"Invalid classic analog FormID: {value}")
    return text


def _validate_cast(cast: dict[str, object]) -> list[dict[str, object]]:
    if cast.get("schema") != CAST_SCHEMA or cast.get("status") != (
        "selected-owned-fnv-analog-bindings-non-parity"
    ):
        raise ValueError("Unexpected classic premade analog cast contract")
    characters = cast.get("characters")
    if not isinstance(characters, list) or len(characters) != 6:
        raise ValueError("Classic premade analog cast must contain six characters")
    rows = []
    actual: dict[str, set[str]] = {}
    recipes: set[str] = set()
    for value in characters:
        if not isinstance(value, dict):
            raise ValueError("Classic premade analog cast row is not an object")
        campaign = str(value.get("campaign", ""))
        character_id = str(value.get("characterId", ""))
        recipe = str(value.get("recipe", ""))
        sex = str(value.get("sex", ""))
        if campaign not in EXPECTED_CAMPAIGN_CHARACTERS or not character_id or not recipe:
            raise ValueError("Classic premade analog cast identity is incomplete")
        if sex not in {"male", "female"}:
            raise ValueError("Classic premade analog cast sex is invalid")
        if recipe in recipes:
            raise ValueError("Classic premade analog cast reuses a compiled recipe")
        recipes.add(recipe)
        actual.setdefault(campaign, set()).add(character_id)
        body = value.get("bodyProfile")
        if not isinstance(body, dict) or not str(body.get("id", "")):
            raise ValueError("Classic premade analog body profile is missing")
        for field in (
            "height",
            "chest",
            "shoulders",
            "waist",
            "arms",
            "thighs",
            "calves",
        ):
            number = float(body[field])
            if number < 0.7 or number > 1.3:
                raise ValueError(f"Classic premade analog body field is invalid: {field}")
        rows.append(value)
    if actual != EXPECTED_CAMPAIGN_CHARACTERS:
        raise ValueError("Classic premade analog cast roster is incomplete")
    return rows


def prepare(
    data_root: Path,
    cache_root: Path,
    base_preview_path: Path,
    cast_path: Path,
) -> dict[str, object]:
    if cache_root.exists():
        raise FileExistsError(f"Refusing to overwrite classic analog cache: {cache_root}")
    cast = _json(cast_path)
    characters = _validate_cast(cast)
    base_preview = _json(base_preview_path)
    if base_preview.get("schema") != BASE_PREVIEW_SCHEMA:
        raise ValueError("Classic premade analog base preview set has an unexpected schema")
    actor_set = prepare_actor_set(
        data_root,
        cache_root,
        [str(row["recipe"]) for row in characters],
    )
    actor_rows = {
        str(row["recipe"]): row for row in actor_set["actors"]  # type: ignore[index]
    }
    analogs = []
    for character in characters:
        recipe = str(character["recipe"])
        actor_row = actor_rows.get(recipe)
        if actor_row is None:
            raise ValueError(f"Compiled classic analog is missing: {recipe}")
        scene_path = Path(str(actor_row["scene"])).resolve()
        scene = _json(scene_path)
        actor = scene["actor"]
        if not isinstance(actor, dict):
            raise ValueError(f"Classic analog actor scene is incomplete: {recipe}")
        expected_actor = _normalized_form_id(character["sourceActorFormId"])
        expected_outfit = _normalized_form_id(character["outfitFormId"])
        if _normalized_form_id(actor_row["baseFormId"]) != expected_actor:
            raise ValueError(f"Classic analog source actor drifted: {recipe}")
        outfit_ids = [_normalized_form_id(value) for value in actor["outfitFormIds"]]
        if outfit_ids != [expected_outfit]:
            raise ValueError(f"Classic analog selected outfit drifted: {recipe}")
        expected_female = str(character["sex"]) == "female"
        if bool(actor["female"]) != expected_female:
            raise ValueError(f"Classic analog source sex drifted: {recipe}")
        output_root = scene_path.parent
        outputs = scene["outputs"]
        if not isinstance(outputs, dict):
            raise ValueError(f"Classic analog output contract is incomplete: {recipe}")
        model_path = (output_root / str(outputs["gltf"])).resolve()
        sidecar_path = (output_root / str(outputs["sidecar"])).resolve()
        if _sha256(model_path) != str(outputs["gltfSha256"]) or _sha256(
            sidecar_path
        ) != str(outputs["sidecarSha256"]):
            raise ValueError(f"Classic analog output hash drifted: {recipe}")
        sidecar = _json(sidecar_path)
        if sidecar.get("schema") != "opennv-actor-gltf/v4" or sidecar.get(
            "status"
        ) != "skinned-animated":
            raise ValueError(f"Classic analog sidecar is unsupported: {recipe}")
        if _normalized_form_id(sidecar["actorFormId"]) != expected_actor:
            raise ValueError(f"Classic analog sidecar actor drifted: {recipe}")
        surfaces = sidecar["surfaces"]
        animations = sidecar["animations"]
        textures = sidecar["textures"]
        if not isinstance(surfaces, list) or not isinstance(animations, list) or not isinstance(
            textures, list
        ):
            raise ValueError(f"Classic analog sidecar coverage is incomplete: {recipe}")
        roles = [str(surface["role"]) for surface in surfaces]
        if any(roles.count(role) < 1 for role in ANALOG_BODY_ROLES):
            raise ValueError(f"Classic analog body coverage is incomplete: {recipe}")
        animation_paths = [str(animation["logicalPath"]).casefold() for animation in animations]
        if not any("idle" in path for path in animation_paths) or not any(
            "forward" in path for path in animation_paths
        ):
            raise ValueError(f"Classic analog locomotion coverage is incomplete: {recipe}")
        skeleton = sidecar["skeleton"]
        if not isinstance(skeleton, dict) or not str(
            skeleton.get("rigidAttachmentNode", "")
        ).strip():
            raise ValueError(f"Classic analog skeleton contract drifted: {recipe}")
        analogs.append(
            {
                "campaign": character["campaign"],
                "characterId": character["characterId"],
                "characterName": character["characterName"],
                "sex": character["sex"],
                "sourceActorFormId": expected_actor,
                "sourceActorName": character["sourceActorName"],
                "outfitFormId": expected_outfit,
                "outfitName": character["outfitName"],
                "bodyRoles": list(ANALOG_BODY_ROLES),
                "bodyProfile": copy.deepcopy(character["bodyProfile"]),
                **(
                    {"appearance": copy.deepcopy(character["appearance"])}
                    if "appearance" in character
                    else {}
                ),
                "selectionRationale": character["selectionRationale"],
                "outputs": {
                    "gltf": str(model_path),
                    "gltfSha256": outputs["gltfSha256"],
                    "sidecar": str(sidecar_path),
                    "sidecarSha256": outputs["sidecarSha256"],
                },
                "coverage": {
                    "surfaces": len(surfaces),
                    "textures": len(textures),
                    "animations": len(animations),
                },
                "rigidAttachmentNode": skeleton["rigidAttachmentNode"],
                "equipmentSocketNode": EQUIPMENT_SOCKET_NODE,
                "visualParity": False,
            }
        )

    output = copy.deepcopy(base_preview)
    output["schema"] = OUTPUT_SCHEMA
    output["status"] = OUTPUT_STATUS
    output["basePreviewSet"] = {
        "path": str(base_preview_path),
        "sha256": _sha256(base_preview_path),
        "schema": BASE_PREVIEW_SCHEMA,
    }
    output["premadeAnalogCast"] = {
        "path": str(cast_path),
        "sha256": _sha256(cast_path),
        "schema": CAST_SCHEMA,
    }
    output["premadeAnalogs"] = analogs
    output["runtimeDisposition"] = (
        "custom-characters-use-sex-defaults-six-premades-use-explicit-person-outfit-body-bindings"
    )
    output_path = (
        cache_root
        / "generated"
        / "classic-premade-analogs"
        / "classic-humanoid-preview-set.json"
    )
    _atomic_json(output_path, output)
    return {
        "schema": OUTPUT_SCHEMA,
        "status": OUTPUT_STATUS,
        "manifest": str(output_path.resolve()),
        "manifestSha256": _sha256(output_path),
        "analogs": len(analogs),
        "actorSet": actor_set["manifest"],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--cache-root", type=Path, required=True)
    parser.add_argument("--base-preview-set", type=Path, required=True)
    parser.add_argument(
        "--cast",
        type=Path,
        default=(
            Path(__file__).resolve().parents[1]
            / "recipes"
            / "classic-premade-analog-cast-v1.json"
        ),
    )
    args = parser.parse_args()
    try:
        result = prepare(
            args.data_root.resolve(),
            args.cache_root.resolve(),
            args.base_preview_set.resolve(),
            args.cast.resolve(),
        )
    except Exception as error:
        print(f"OPENNV_CLASSIC_PREMADE_ANALOG_ERROR {error}")
        return 2
    print("OPENNV_CLASSIC_PREMADE_ANALOG " + json.dumps(result, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
