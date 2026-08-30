"""Admit the owned Fallout 2 classic IFACE surface into a disposable cache."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from fo1_frm import decode_frm_frame, palette_rgba_bytes
from fo1_map_objects import Fo1ResourceResolver
from fo1_profile import Fo1ProfileError
from fo2_first_slice import _archive_paths, _load_recipe, default_recipe_path
from plugin_stack import file_sha256


HUD_SCHEMA = "opennv-fo2-arroyo-classic-hud-recipe/v1"
HUD_CACHE_SCHEMA = "opennv-fo2-arroyo-classic-hud-cache/v1"
HUD_STATUS = "decoded-owned-fallout2-classic-interface"
OPAQUE_ALPHA = 255
DEFAULT_HUD_RECIPE = (
    Path(__file__).resolve().parents[1]
    / "recipes"
    / "fo2-arroyo-classic-hud-owned-v1.json"
)


def enrich_arroyo_cache_with_classic_hud(
    staging: Path,
    document: dict[str, Any],
    source_manifest: dict[str, Any],
    *,
    profile_path: Path,
    archive_recipe_path: Path | None,
    hud_recipe_path: Path = DEFAULT_HUD_RECIPE,
) -> None:
    del source_manifest
    profile_path = profile_path.resolve()
    profile = json.loads(profile_path.read_text(encoding="utf-8"))
    archive_recipe = _load_recipe(
        (archive_recipe_path or default_recipe_path()).resolve()
    )
    hud_recipe_path = hud_recipe_path.resolve()
    hud_recipe = json.loads(hud_recipe_path.read_text(encoding="utf-8"))
    if (
        hud_recipe.get("schema") != HUD_SCHEMA
        or hud_recipe.get("campaign") != "Fallout2"
        or hud_recipe.get("sourceProfileSchema") != profile.get("schema")
        or hud_recipe.get("overlayOrderHighToLow")
        != archive_recipe["overlayOrderHighToLow"]
        or hud_recipe.get("cachePolicy", {}).get("distributionAllowed") is not False
        or hud_recipe.get("cachePolicy", {}).get("containsDerivedOwnedPixels") is not True
    ):
        raise Fo1ProfileError("Fallout 2 classic HUD recipe contract drifted")

    archive_paths = _archive_paths(profile, archive_recipe)
    resolver = Fo1ResourceResolver(None, archive_paths[0], archive_paths[1:])
    destination = staging / "assets" / "classic-hud"
    destination.mkdir(parents=True, exist_ok=True)
    rows: dict[str, dict[str, Any]] = {}
    with resolver.access_scope() as accessed:
        palette = resolver.read("color.pal")
        colors = palette_rgba_bytes(palette.data)
        for asset_id, expected in sorted(hud_recipe["assets"].items()):
            resource = resolver.read(expected["logicalPath"])
            if resource.sha256 != expected["sha256"]:
                raise Fo1ProfileError(
                    f"Fallout 2 classic HUD FRM identity drifted: {expected['logicalPath']}"
                )
            decoded = decode_frm_frame(resource.data, colors, 0, 0)
            frame = decoded["frame"]
            image = frame["image"].copy()
            if (image.width, image.height) != (
                int(expected["width"]),
                int(expected["height"]),
            ):
                raise Fo1ProfileError(
                    f"Fallout 2 classic HUD dimensions drifted: {expected['logicalPath']}"
                )
            if bool(expected["opaque"]):
                image.putalpha(OPAQUE_ALPHA)
            relative = Path("assets") / "classic-hud" / f"{asset_id}.png"
            output = staging / relative
            image.save(output, format="PNG", optimize=False)
            rows[asset_id] = {
                "logicalPath": resource.logical_path,
                "source": resource.source,
                "sourceBytes": len(resource.data),
                "sourceSha256": resource.sha256,
                "frame": 0,
                "rotation": 0,
                "width": image.width,
                "height": image.height,
                "opaque": bool(expected["opaque"]),
                "png": relative.as_posix(),
                "pngBytes": output.stat().st_size,
                "pngSha256": file_sha256(output),
            }

        existing_resources = {
            (row["logicalPath"].casefold(), row["sha256"])
            for row in document["resources"]
        }
        for logical_path in sorted(accessed):
            resource = resolver.resources[logical_path]
            identity = (resource.logical_path.casefold(), resource.sha256)
            if identity in existing_resources:
                continue
            document["resources"].append(
                {
                    "logicalPath": resource.logical_path,
                    "source": resource.source,
                    "bytes": len(resource.data),
                    "sha256": resource.sha256,
                }
            )
            existing_resources.add(identity)
        document["resources"].sort(key=lambda row: row["logicalPath"].casefold())

    document["classicHud"] = {
        "schema": HUD_CACHE_SCHEMA,
        "status": HUD_STATUS,
        "mode": hud_recipe["mode"],
        "recipe": {
            "id": hud_recipe["id"],
            "file": str(hud_recipe_path),
            "sha256": file_sha256(hud_recipe_path),
        },
        "assets": rows,
        "layout": hud_recipe["layout"],
        "composition": hud_recipe["composition"],
        "cachePolicy": hud_recipe["cachePolicy"],
        "unsupported": hud_recipe["unsupported"],
    }
    document["counts"]["classicHudArtifacts"] = len(rows)
