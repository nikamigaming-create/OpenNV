"""Prepare one bounded exterior CELL plus persistent references and LAND."""

from __future__ import annotations

import math
import hashlib
from pathlib import Path

from cell_catalog import BaseObject, PlacedReference, scan_cell_catalog
from cell_scene import (
    CELL_NAVIGATION_SCHEMA,
    CELL_SCENE_SCHEMA,
    EXTERIOR_RECIPE_SCHEMA,
    _atomic_json,
    arrival_transform,
    godot_position,
    godot_rotation_quaternion,
    godot_yaw_radians,
    navmesh_manifest,
    normalized_rgb,
    recipe_path,
)
from cell_catalog import INITIALLY_DISABLED_RECORD_FLAG
from bsa_archive import BsaArchive
from environment_catalog import scan_environment_catalog
from landscape_catalog import Landscape, LandscapeIdentity, scan_landscape_catalog
from landscape_gltf import export_landscape_gltf
from lod_catalog import lod_block_grids, select_lod_blocks
from owned_archive_stack import OwnedArchiveStack
from retail_grass import prepare_retail_grass_overlay
from scene_asset_pipeline import (
    form_id,
    interaction_manifest,
    prepare_scene_assets,
    reference_selection_reason,
)
from texture_pipeline import OwnedTexturePipeline, TexturePipeline
from runtime_configuration import load_runtime_configuration
from plugin_stack import (
    FORM_ID_HEX_CHARACTERS,
    FORM_ID_OBJECT_MASK,
    FormKey,
)


FORM_ID_RADIX = 16
LAND_VERTEX_AXIS_COUNT = 33
LAND_QUAD_AXIS_COUNT = LAND_VERTEX_AXIS_COUNT - 1
SINGLE_CELL_GRID_DIAMETER = 1
GRID_DIAMETER_PARITY_DIVISOR = 2
TEXTURE_ARCHIVE_ROOT = "textures\\"
CLOUD_SURFACE_SEMANTICS = {
    "cloudLayerSurface": "weather-cloud-layer-geometry",
    "horizonClearSurface": "horizon-clear",
    "horizonOvercastSurface": "horizon-overcast",
    "lowerLayerSurface": "lower-layer",
}


def single_master_landscape_identity(
    master_path: Path,
    landscape: Landscape,
) -> LandscapeIdentity:
    source_ids = (
        landscape.form_id,
        landscape.cell_form_id,
        landscape.worldspace_form_id,
    )
    if any(value & ~FORM_ID_OBJECT_MASK for value in source_ids):
        raise ValueError(
            "Single-master LAND identity contains a non-local plugin namespace"
        )
    owner = master_path.name
    return LandscapeIdentity(
        FormKey(owner, landscape.form_id).text,
        FormKey(owner, landscape.cell_form_id).text,
        FormKey(owner, landscape.worldspace_form_id).text,
        owner,
        f"{landscape.form_id:0{FORM_ID_HEX_CHARACTERS}x}",
    )


def environment_texture_member(authored_path: str) -> str:
    normalized = authored_path.replace("/", "\\").lstrip("\\").lower()
    if normalized.startswith(TEXTURE_ARCHIVE_ROOT):
        return normalized
    return TEXTURE_ARCHIVE_ROOT + normalized


def environment_sky_models(recipe: dict[str, object]) -> dict[str, dict[str, object]]:
    source = recipe.get("skyModels")
    if not isinstance(source, dict) or set(source) != {"atmosphere", "clouds"}:
        raise ValueError("Exterior recipe requires atmosphere and clouds sky models")
    atmosphere = source["atmosphere"]
    clouds = source["clouds"]
    if (
        not isinstance(atmosphere, dict)
        or set(atmosphere) != {"path", "surface"}
        or not isinstance(clouds, dict)
        or set(clouds) != {"path", *CLOUD_SURFACE_SEMANTICS}
    ):
        raise ValueError("Exterior sky model surface routing is incomplete")
    cloud_routes = [
        {
            "name": str(clouds[property_name]),
            "semantic": semantic,
        }
        for property_name, semantic in CLOUD_SURFACE_SEMANTICS.items()
    ]
    if any(not route["name"] for route in cloud_routes) or len(
        {route["name"] for route in cloud_routes}
    ) != len(cloud_routes):
        raise ValueError("Exterior cloud surface routes must be unique and nonempty")
    result = {
        "atmosphere": {
            "path": str(atmosphere["path"]).replace("/", "\\").lstrip("\\").lower(),
            "surfaceRoutes": [
                {"name": str(atmosphere["surface"]), "semantic": "atmosphere"}
            ],
        },
        "clouds": {
            "path": str(clouds["path"]).replace("/", "\\").lstrip("\\").lower(),
            "surfaceRoutes": cloud_routes,
        },
    }
    if any(not str(value["path"]).endswith(".nif") for value in result.values()):
        raise ValueError("Exterior sky models must be owned NIF paths")
    return result


def unique_texture_manifests(
    manifests: list[dict[str, object]],
) -> list[dict[str, object]]:
    by_id: dict[str, dict[str, object]] = {}
    for manifest in manifests:
        texture_id = str(manifest["id"])
        previous = by_id.get(texture_id)
        if previous is not None and previous != manifest:
            comparable_fields = (set(previous) | set(manifest)) - {"landscapeRole"}
            if any(previous.get(field) != manifest.get(field) for field in comparable_fields):
                raise ValueError(f"Exterior texture ID has conflicting manifests: {texture_id}")
            by_id[texture_id] = {**previous, **manifest}
        else:
            by_id[texture_id] = manifest
    return sorted(by_id.values(), key=lambda row: str(row["id"]))


def loaded_grid_coordinates(
    center: tuple[int, int],
    diameter: int,
) -> tuple[tuple[int, int], ...]:
    if diameter < SINGLE_CELL_GRID_DIAMETER or diameter % GRID_DIAMETER_PARITY_DIVISOR == 0:
        raise ValueError("Exterior loaded grid diameter must be a positive odd integer")
    radius = (diameter - SINGLE_CELL_GRID_DIAMETER) // GRID_DIAMETER_PARITY_DIVISOR
    return tuple(
        (x, y)
        for y in range(center[1] - radius, center[1] + radius + 1)
        for x in range(center[0] - radius, center[0] + radius + 1)
    )


def reference_grid(
    position: tuple[float, float, float],
    exterior_cell_size_game_units: float,
) -> tuple[int, int]:
    return (
        math.floor(position[0] / exterior_cell_size_game_units),
        math.floor(position[1] / exterior_cell_size_game_units),
    )


def prepare_exterior_scene(
    master_path: Path,
    meshes_path: Path,
    texture_archive_paths: list[Path],
    texture_archive_rows: list[dict[str, object]],
    cache_root: Path,
    recipe: dict[str, object],
    master_sha256: str,
    retail_grass_observation: Path | None = None,
    retail_grass_render_state_observation: Path | None = None,
    owned_archives: OwnedArchiveStack | None = None,
) -> dict[str, object]:
    configuration = load_runtime_configuration()
    scene_archives = owned_archives if owned_archives is not None else BsaArchive(meshes_path)
    units_to_meters = configuration.world_units_to_meters
    exterior_cell_size_game_units = (
        configuration.content_compiler.exterior_cell_size_game_units
    )
    if recipe.get("schema") != EXTERIOR_RECIPE_SCHEMA:
        raise ValueError(f"Unexpected exterior recipe schema: {recipe.get('schema')}")
    expected_master = str(recipe["master"]["sha256"])
    if master_sha256 != expected_master:
        raise ValueError(
            f"Exterior recipe master hash mismatch: expected={expected_master} actual={master_sha256}"
        )

    catalog = scan_cell_catalog(master_path)
    cell_form_id = int(str(recipe["cellFormId"]), FORM_ID_RADIX)
    persistent_cell_form_id = int(str(recipe["persistentCellFormId"]), FORM_ID_RADIX)
    worldspace_form_id = int(str(recipe["worldspaceFormId"]), FORM_ID_RADIX)
    cell = catalog.cells.get(cell_form_id)
    persistent_cell = catalog.cells.get(persistent_cell_form_id)
    if (
        cell is None
        or cell.interior
        or cell.coordinates is None
        or cell.worldspace_form_id != worldspace_form_id
        or persistent_cell is None
        or persistent_cell.interior
        or persistent_cell.worldspace_form_id != worldspace_form_id
    ):
        raise ValueError("Exterior recipe CELL/worldspace relationship is invalid")
    streaming = recipe.get("streaming")
    streaming_fields = set(streaming) if isinstance(streaming, dict) else set()
    bounded_proof_fields = {"mode", "loadedGridDiameter"}
    retail_ini_fields = {
        "mode",
        "loadedGridDiameter",
        "source",
        "section",
        "key",
    }
    if (
        not isinstance(streaming, dict)
        or (
            streaming.get("mode") == "bounded-proof"
            and streaming_fields != bounded_proof_fields
        )
        or (
            streaming.get("mode") == "retail-ini"
            and streaming_fields != retail_ini_fields
        )
        or streaming.get("mode") not in {"bounded-proof", "retail-ini"}
    ):
        raise ValueError("Exterior recipe requires one declared streaming contract")
    loaded_grid_diameter = int(streaming["loadedGridDiameter"])
    requested_grids = loaded_grid_coordinates(cell.coordinates, loaded_grid_diameter)
    cells_by_grid = {
        candidate.coordinates: candidate
        for candidate in catalog.cells.values()
        if not candidate.interior
        and candidate.worldspace_form_id == worldspace_form_id
        and candidate.coordinates is not None
    }
    missing_grids = [grid for grid in requested_grids if grid not in cells_by_grid]
    if missing_grids:
        raise ValueError(f"Exterior loaded grid has missing source CELLs: {missing_grids}")
    loaded_cells = [cells_by_grid[grid] for grid in requested_grids]
    loaded_cell_form_ids = {candidate.form_id for candidate in loaded_cells}
    navigation_source_cell_ids = {
        *loaded_cell_form_ids,
        persistent_cell_form_id,
    }
    navigation_navmeshes = sorted(
        (
            navmesh
            for source_cell_form_id in navigation_source_cell_ids
            for navmesh in catalog.navmeshes_for(source_cell_form_id)
        ),
        key=lambda value: value.form_id,
    )
    if len({navmesh.form_id for navmesh in navigation_navmeshes}) != len(
        navigation_navmeshes
    ):
        raise ValueError("Exterior loaded grid selected duplicate NAVM records")
    loaded_grid_set = set(requested_grids)
    selection = recipe["selection"]
    included_record_types = {
        str(value) for value in selection["includeBaseRecordTypes"]
    }
    lod_configuration = recipe.get("distantReferences")
    if lod_configuration is None and streaming["mode"] == "bounded-proof":
        distant_reference_radius = 0
        distant_reference_types: set[str] = set()
    else:
        if not isinstance(lod_configuration, dict) or set(lod_configuration) != {
            "distantReferenceRadiusCells",
            "distantReferenceTypes",
        }:
            raise ValueError("Exterior distant-reference contract must be complete")
        distant_reference_radius = int(lod_configuration["distantReferenceRadiusCells"])
        distant_reference_types = {
            str(value) for value in lod_configuration["distantReferenceTypes"]
        }
    if distant_reference_radius < 0:
        raise ValueError("Exterior distant reference radius cannot be negative")
    if distant_reference_radius and not distant_reference_types:
        raise ValueError("Exterior distant reference types are required for a distant radius")
    lod_blocks, lod_contract = select_lod_blocks(
        scene_archives,
        recipe,
        requested_grids,
    )
    entry_door = int(str(recipe["entryDoorReferenceFormId"]), FORM_ID_RADIX)
    reciprocal_door = int(str(recipe["reciprocalDoorReferenceFormId"]), FORM_ID_RADIX)
    source_door, arrival = arrival_transform(catalog, entry_door)
    if source_door != reciprocal_door:
        raise ValueError(
            f"Exterior entry XTEL mismatch: expected={reciprocal_door:08x} actual={source_door:08x}"
        )
    origin = arrival.position
    candidates = [
        reference
        for loaded_cell in loaded_cells
        for reference in catalog.references_for(loaded_cell.form_id)
    ]
    distant_source_cells: set[int] = set()
    if distant_reference_radius:
        for candidate_cell in cells_by_grid.values():
            if candidate_cell.coordinates in loaded_grid_set:
                continue
            assert candidate_cell.coordinates is not None
            if max(
                abs(candidate_cell.coordinates[0] - cell.coordinates[0]),
                abs(candidate_cell.coordinates[1] - cell.coordinates[1]),
            ) > distant_reference_radius:
                continue
            distant_references = []
            for reference in catalog.references_for(candidate_cell.form_id):
                base = catalog.base_objects.get(reference.base_form_id)
                if (
                    base is not None
                    and base.record_type in distant_reference_types
                    and base.record_type in included_record_types
                    and base.model_path
                ):
                    distant_references.append(reference)
            if distant_references:
                distant_source_cells.add(candidate_cell.form_id)
                candidates.extend(distant_references)
    candidates.extend(
        reference
        for reference in catalog.references_for(persistent_cell_form_id)
        if reference_grid(
            reference.transform.position,
            exterior_cell_size_game_units,
        ) in loaded_grid_set
    )
    if len({reference.form_id for reference in candidates}) != len(candidates):
        raise ValueError("Exterior recipe selected duplicate references across CELL ownership")

    selected: list[tuple[PlacedReference, BaseObject]] = []
    excluded_references: list[dict[str, str]] = []
    for reference in candidates:
        base = catalog.base_objects.get(reference.base_form_id)
        if base is None:
            continue
        selection_reason = reference_selection_reason(
            base,
            recipe,
            configuration.content_compiler,
        )
        if selection_reason != "selected":
            if base.model_path:
                excluded_references.append(
                    {
                        "formId": form_id(reference.form_id),
                        "baseEditorId": base.editor_id,
                        "modelPath": base.model_path,
                        "reason": selection_reason,
                    }
                )
            continue
        selected.append((reference, base))
    if entry_door not in {reference.form_id for reference, _base in selected}:
        raise ValueError(f"Exterior scene did not select its entry door {entry_door:08x}")

    sky_models = environment_sky_models(recipe)
    sky_model_paths = {str(value["path"]) for value in sky_models.values()}
    loaded_grid_clip = {
        "mode": "retain-outside-source-xy-rectangle",
        "minXGameUnits": min(grid[0] for grid in requested_grids)
        * exterior_cell_size_game_units,
        "maxXGameUnits": (max(grid[0] for grid in requested_grids) + 1)
        * exterior_cell_size_game_units,
        "minYGameUnits": min(grid[1] for grid in requested_grids)
        * exterior_cell_size_game_units,
        "maxYGameUnits": (max(grid[1] for grid in requested_grids) + 1)
        * exterior_cell_size_game_units,
        "coordinateSpace": "source-world-game-units-before-scene-origin",
    }
    object_authority_grid_set = set(loaded_grid_set)
    object_authority_clip = loaded_grid_clip
    if distant_reference_radius:
        center_x, center_y = cell.coordinates
        object_authority_grid_set = {
            (grid_x, grid_y)
            for grid_y in range(
                center_y - distant_reference_radius,
                center_y + distant_reference_radius + 1,
            )
            for grid_x in range(
                center_x - distant_reference_radius,
                center_x + distant_reference_radius + 1,
            )
        }
        if not loaded_grid_set <= object_authority_grid_set:
            raise ValueError(
                "Exterior distant-reference radius does not cover the loaded grid"
            )
        object_authority_clip = {
            "mode": "retain-outside-source-xy-rectangle",
            "minXGameUnits": (center_x - distant_reference_radius)
            * exterior_cell_size_game_units,
            "maxXGameUnits": (center_x + distant_reference_radius + 1)
            * exterior_cell_size_game_units,
            "minYGameUnits": (center_y - distant_reference_radius)
            * exterior_cell_size_game_units,
            "maxYGameUnits": (center_y + distant_reference_radius + 1)
            * exterior_cell_size_game_units,
            "coordinateSpace": "source-world-game-units-before-scene-origin",
        }
    partial_lod_clips = {}
    presented_lod_blocks = []
    fully_covered_by_exact_reference_tier = []
    partially_clipped_lod_blocks = []
    for block in lod_blocks:
        block_grids = lod_block_grids(block)
        authority_grids = (
            object_authority_grid_set
            if block.family == "object"
            else loaded_grid_set
        )
        if block_grids <= authority_grids:
            fully_covered_by_exact_reference_tier.append(block)
            continue
        presented_lod_blocks.append(block)
        if not block_grids & authority_grids:
            continue
        partially_clipped_lod_blocks.append(block)
        if block.family == "object":
            # The distant-reference tier instantiates the exact owned STAT,
            # SCOL, MSTT, and TREE records through its declared cell radius.
            # Object LOD must begin outside that authority boundary; submitting
            # both paths fills alpha-tested silhouettes (for example, distant
            # tower is the canonical failure) and doubles buildings/vegetation.
            partial_lod_clips[block.model_path] = object_authority_clip
            continue
        block_x = block.x * exterior_cell_size_game_units
        block_y = block.y * exterior_cell_size_game_units
        partial_lod_clips[block.model_path] = {
            **loaded_grid_clip,
            "minXGameUnits": loaded_grid_clip["minXGameUnits"] - block_x,
            "maxXGameUnits": loaded_grid_clip["maxXGameUnits"] - block_x,
            "minYGameUnits": loaded_grid_clip["minYGameUnits"] - block_y,
            "maxYGameUnits": loaded_grid_clip["maxYGameUnits"] - block_y,
            "coordinateSpace": "source-block-local-game-units-before-placement",
        }
    archive_selected_lod_blocks = len(lod_blocks)
    lod_blocks = tuple(presented_lod_blocks)
    lod_contract = {
        **lod_contract,
        "archiveSelectedBlocks": archive_selected_lod_blocks,
        "selectedBlocks": len(lod_blocks),
        "selectedObjectBlocks": sum(
            block.family == "object" for block in lod_blocks
        ),
        "selectedTerrainBlocks": sum(
            block.family == "terrain" for block in lod_blocks
        ),
        "excludedFullyCoveredNearBlocks": sorted(
            set(lod_contract["excludedFullyCoveredNearBlocks"])
            | {
                block.identity
                for block in fully_covered_by_exact_reference_tier
            }
        ),
        "partiallyOverlappingNearBlocks": [
            block.identity for block in partially_clipped_lod_blocks
        ],
        "objectFullDetailAuthorityBounds": {
            "minX": min(grid[0] for grid in object_authority_grid_set),
            "maxX": max(grid[0] for grid in object_authority_grid_set),
            "minY": min(grid[1] for grid in object_authority_grid_set),
            "maxY": max(grid[1] for grid in object_authority_grid_set),
        },
        "objectFullDetailAuthorityRadiusCells": distant_reference_radius,
        "objectFullDetailAuthorityRecordTypes": sorted(distant_reference_types),
        "nearCellHolePolicy": (
            "exact-reference-tier-authoritative-for-object-LOD-plus-"
            "loaded-LAND-grid-authoritative-for-terrain-LOD-with-triangle-clipping"
        ),
    }
    fully_clipped_lod_model_paths: set[str] = set()
    (
        assets,
        asset_sidecars,
        texture_artifacts,
        compiler,
        non_presentation_assets,
        unresolved_texture_bindings,
    ) = prepare_scene_assets(
        meshes_path,
        texture_archive_paths,
        cache_root,
        recipe,
        selected,
        configuration.content_compiler,
        sky_model_paths | {block.model_path for block in lod_blocks},
        partial_lod_clips,
        fully_clipped_lod_model_paths,
        owned_archives=owned_archives,
    )
    retained_selected = []
    for reference, base in selected:
        non_presentation = non_presentation_assets.get(str(base.model_path))
        if non_presentation is None:
            retained_selected.append((reference, base))
            continue
        excluded_references.append(
            {
                "formId": form_id(reference.form_id),
                "baseEditorId": base.editor_id,
                "modelPath": str(base.model_path),
                "reason": "owned-nif-no-presentation-geometry",
                "classificationSidecar": str(non_presentation["sidecar"]),
            }
        )
    selected = retained_selected
    if entry_door not in {reference.form_id for reference, _base in selected}:
        raise ValueError(
            f"Exterior entry door has no presentation geometry: {entry_door:08x}"
        )
    if fully_clipped_lod_model_paths:
        fully_clipped_lod_blocks = [
            block
            for block in lod_blocks
            if block.model_path in fully_clipped_lod_model_paths
        ]
        if len(fully_clipped_lod_blocks) != len(fully_clipped_lod_model_paths):
            raise ValueError(
                "Presentation clipping removed an asset outside the selected LOD block set"
            )
        lod_blocks = tuple(
            block
            for block in lod_blocks
            if block.model_path not in fully_clipped_lod_model_paths
        )
        lod_contract = {
            **lod_contract,
            "selectedBlocks": len(lod_blocks),
            "selectedObjectBlocks": sum(
                block.family == "object" for block in lod_blocks
            ),
            "selectedTerrainBlocks": sum(
                block.family == "terrain" for block in lod_blocks
            ),
            "excludedFullyCoveredNearBlocks": sorted(
                set(lod_contract["excludedFullyCoveredNearBlocks"])
                | {block.identity for block in fully_clipped_lod_blocks}
            ),
            "fullyRemovedByPresentationClipBlocks": sorted(
                block.identity for block in fully_clipped_lod_blocks
            ),
        }
    landscapes = scan_landscape_catalog(master_path, loaded_cell_form_ids)
    texture_aliases = {
        str(source): str(target) for source, target in recipe["textureAliases"].items()
    }
    terrain_pipeline = (
        OwnedTexturePipeline(
            owned_archives,
            cache_root,
            texture_aliases,
            configuration.content_compiler,
        )
        if owned_archives is not None
        else TexturePipeline(
            texture_archive_paths,
            cache_root,
            texture_aliases,
            configuration.content_compiler,
        )
    )
    grass_overlay = (
        prepare_retail_grass_overlay(
            retail_grass_observation,
            retail_grass_render_state_observation,
            meshes_path,
            terrain_pipeline,
            cache_root,
            origin,
            configuration.content_compiler,
            owned_archives,
        )
        if retail_grass_observation is not None
        and retail_grass_render_state_observation is not None
        else None
    )
    if (retail_grass_observation is None) != (
        retail_grass_render_state_observation is None
    ):
        raise ValueError(
            "Retail grass placement and render-state observations must be supplied together"
        )
    environment_catalog = scan_environment_catalog(master_path)
    environment_manifest = environment_catalog.exterior_manifest(worldspace_form_id)
    climate = environment_catalog.climates[
        environment_catalog.worldspaces[worldspace_form_id].climate_form_id
    ]
    environment_texture_paths = {
        path
        for weather in environment_catalog.weather.values()
        for path in weather.cloud_textures
        if path
    } | {
        path
        for path in (climate.sun_texture, climate.sun_glare_texture)
        if path
    }
    environment_textures = {}
    missing_environment_textures = []
    for path in sorted(environment_texture_paths):
        requested = environment_texture_member(path)
        source_count = terrain_pipeline.member_source_count(requested)
        if source_count == 0:
            missing_environment_textures.append(path)
            continue
        if source_count != 1:
            raise ValueError(
                f"Environment texture {requested!r} has {source_count} owned sources"
            )
        environment_textures[path] = terrain_pipeline.prepare(requested)
    environment_manifest["textures"] = [
        {
            "authoredPath": path,
            "artifactId": artifact.asset_id,
            "png": str(artifact.png_path.resolve()),
            "pngSha256": artifact.png_sha256,
        }
        for path, artifact in sorted(environment_textures.items())
    ]
    environment_manifest["missingTextures"] = missing_environment_textures
    environment_manifest["skyModels"] = {}
    for role, contract in sorted(sky_models.items()):
        path = str(contract["path"])
        surfaces = asset_sidecars[path]["surfaces"]
        surface_routes = list(contract["surfaceRoutes"])
        expected_names = [str(route["name"]) for route in surface_routes]
        actual_names = [str(surface["name"]) for surface in surfaces]
        if actual_names != expected_names:
            raise ValueError(
                f"Exterior {role} surface routing mismatch: "
                f"expected={expected_names} actual={actual_names}"
            )
        environment_manifest["skyModels"][role] = {
            "authoredPath": path,
            "assetId": assets[path]["id"],
            "model": assets[path]["model"],
            "sidecar": assets[path]["sidecar"],
            "surfaces": [
                {
                    "index": index,
                    "name": surface["name"],
                    "attributes": surface["attributes"],
                    "semantic": surface_routes[index]["semantic"],
                }
                for index, surface in enumerate(surfaces)
            ],
        }
    terrain_rows = []
    non_geometric_terrain_rows = []
    for loaded_cell in loaded_cells:
        landscape = landscapes.optional_landscape_for_cell(loaded_cell.form_id)
        if landscape is None:
            non_geometric = landscapes.non_geometric_landscapes[loaded_cell.form_id]
            if non_geometric.worldspace_form_id != worldspace_form_id:
                raise ValueError("Non-geometric exterior LAND belongs to another worldspace")
            non_geometric_terrain_rows.append((loaded_cell, non_geometric))
            continue
        if landscape.worldspace_form_id != worldspace_form_id:
            raise ValueError("Exterior LAND belongs to another worldspace")
        landscape_export = export_landscape_gltf(
            landscape,
            landscapes,
            loaded_cell.coordinates,
            origin,
            terrain_pipeline,
            cache_root / "generated" / "cells" / str(recipe["id"]) / "assets",
            configuration.content_compiler,
            identity=single_master_landscape_identity(master_path, landscape),
        )
        terrain_rows.append((loaded_cell, landscape, landscape_export))

    references = []
    for reference, base in selected:
        asset = assets[base.model_path]
        references.append(
            {
                "formId": form_id(reference.form_id),
                "cellFormId": form_id(reference.cell_form_id),
                "baseFormId": form_id(reference.base_form_id),
                "baseRecordType": base.record_type,
                "baseEditorId": base.editor_id,
                "assetId": asset["id"],
                "positionGameUnits": list(reference.transform.position),
                "positionGodotUnits": godot_position(reference.transform.position, origin),
                "yawRadians": reference.transform.rotation_radians[2],
                "yawGodotRadians": godot_yaw_radians(reference.transform.rotation_radians[2]),
                "rotationGodotQuaternion": godot_rotation_quaternion(reference.transform.rotation_radians),
                "scale": reference.scale,
                "initiallyDisabled": bool(reference.flags & INITIALLY_DISABLED_RECORD_FLAG),
                "teleportDestinationFormId": (
                    form_id(reference.teleport_destination_form_id)
                    if reference.teleport_destination_form_id is not None
                    else None
                ),
                "teleportDestinationTransform": (
                    {
                        "positionGameUnits": list(
                            reference.teleport_destination_transform.position
                        ),
                        "yawRadians": reference.teleport_destination_transform.rotation_radians[2],
                        "yawGodotRadians": godot_yaw_radians(
                            reference.teleport_destination_transform.rotation_radians[2]
                        ),
                    }
                    if reference.teleport_destination_transform is not None
                    else None
                ),
                "interaction": interaction_manifest(reference, base, catalog),
            }
        )
    for loaded_cell, landscape, landscape_export in terrain_rows:
        references.append(
            {
                "formId": form_id(landscape.form_id),
                "cellFormId": form_id(loaded_cell.form_id),
                "baseFormId": form_id(landscape.form_id),
                "baseRecordType": "LAND",
                "baseEditorId": f"LAND_{landscape.form_id:08x}",
                "assetId": landscape_export.asset["id"],
                "positionGameUnits": list(origin),
                "positionGodotUnits": [0.0, 0.0, 0.0],
                "yawRadians": 0.0,
                "yawGodotRadians": 0.0,
                "rotationGodotQuaternion": [0.0, 0.0, 0.0, 1.0],
                "scale": 1.0,
                "initiallyDisabled": False,
                "teleportDestinationFormId": None,
                "interaction": None,
            }
        )

    lod_references = []
    for block in lod_blocks:
        asset = assets[block.model_path]
        block_origin = (
            block.x * exterior_cell_size_game_units,
            block.y * exterior_cell_size_game_units,
            0.0,
        )
        geometry_coordinate_space = (
            "world-game-units-baked"
            if block.family == "object"
            else "block-local-game-units"
        )
        placement_source_position = (
            (0.0, 0.0, 0.0) if block.family == "object" else block_origin
        )
        lod_references.append(
            {
                "id": f"LOD_{block.identity}",
                "assetId": asset["id"],
                "logicalPath": block.logical_path,
                "sourceSha256": asset["sourceSha256"],
                "family": block.family,
                "level": block.level,
                "variant": block.variant,
                "blockOriginGameUnits": list(block_origin),
                "geometryCoordinateSpace": geometry_coordinate_space,
                "positionGodotUnits": godot_position(placement_source_position, origin),
                "rotationGodotQuaternion": [0.0, 0.0, 0.0, 1.0],
                "scale": 1.0,
                "selectionReason": lod_contract["selectionReason"],
                "presentationClip": asset["presentationClip"],
            }
        )

    lights = []
    for reference in candidates:
        light = catalog.lights.get(reference.base_form_id)
        if light is None:
            continue
        lights.append(
            {
                "formId": form_id(reference.form_id),
                "baseFormId": form_id(reference.base_form_id),
                "baseEditorId": light.editor_id,
                "positionGameUnits": list(reference.transform.position),
                "positionGodotUnits": godot_position(reference.transform.position, origin),
                "radiusGameUnits": light.radius,
                "radiusMeters": light.radius * units_to_meters,
                "color": normalized_rgb(light.color_rgb),
                "intensity": light.intensity,
                "falloff": light.falloff,
                "fieldOfView": light.field_of_view,
                "lightFlags": light.flags,
                "initiallyDisabled": bool(reference.flags & INITIALLY_DISABLED_RECORD_FLAG),
            }
        )

    environment = configuration.document["exteriorEnvironment"]
    target_landscape = landscapes.landscape_for_cell(cell_form_id)
    output_path = cache_root / "generated" / "cells" / str(recipe["id"]) / "cell-scene.json"
    all_assets = [
        *assets.values(),
        *(row[2].asset for row in terrain_rows),
        *(grass_overlay["assets"] if grass_overlay is not None else []),
    ]
    all_textures = unique_texture_manifests(
        [
            *(texture_artifacts[path].manifest() for path in sorted(texture_artifacts)),
            *(texture for row in terrain_rows for texture in row[2].runtime_textures),
            *(artifact.manifest() for artifact in environment_textures.values()),
            *(grass_overlay["textures"] if grass_overlay is not None else []),
        ]
    )
    document = {
        "schema": CELL_SCENE_SCHEMA,
        "status": "geometry-structure",
        "recipe": str(recipe["id"]),
        "recipeSha256": hashlib.sha256(
            recipe_path(str(recipe["id"])).read_bytes()
        ).hexdigest(),
        "actorRecipes": [str(value) for value in recipe["actorRecipes"]],
        "source": {
            "master": master_path.name,
            "masterSha256": master_sha256,
            "textureArchives": texture_archive_rows,
            "ownedArchiveStack": (
                owned_archives.manifest() if owned_archives is not None else None
            ),
            "retailGrassObservation": (
                grass_overlay["observation"]["source"]
                if grass_overlay is not None
                else None
            ),
            "retailGrassRenderStateObservation": (
                grass_overlay["observation"]["renderStateObservation"]["source"]
                if grass_overlay is not None
                else None
            ),
        },
        "compiler": compiler,
        "configuration": configuration.manifest(),
        "cell": {
            "formId": form_id(cell.form_id),
            "editorId": str(recipe["editorId"]),
            "interior": False,
            "worldspaceFormId": form_id(worldspace_form_id),
            "sourceCellFormIds": [
                *(form_id(candidate.form_id) for candidate in loaded_cells),
                form_id(persistent_cell_form_id),
            ],
        },
        "coordinates": {
            "source": "Gamebryo X-right/Y-forward/Z-up, radians",
            "target": "Godot X-right/Y-up/-Z-forward",
            "unitsToMeters": units_to_meters,
            "originGameUnits": list(origin),
            "grid": list(cell.coordinates),
            "loadedGridDiameter": loaded_grid_diameter,
            "loadedCellGrids": [list(grid) for grid in requested_grids],
        },
        "spawn": {
            "sourceDoorReferenceFormId": form_id(source_door),
            "targetDoorReferenceFormId": form_id(entry_door),
            "positionGameUnits": list(arrival.position),
            "positionGodotUnits": [0.0, 0.0, 0.0],
            "yawRadians": arrival.rotation_radians[2],
            "yawGodotRadians": godot_yaw_radians(arrival.rotation_radians[2]),
        },
        "proof": {
            "doorReferenceFormId": form_id(entry_door),
            "visibilityModel": "linked-authored-space",
        },
        "portal": {
            "sourceCellFormId": form_id(cell_form_id),
            "persistentCellFormId": form_id(persistent_cell_form_id),
            "entryDoorReferenceFormId": form_id(entry_door),
            "reciprocalDoorReferenceFormId": form_id(reciprocal_door),
        },
        "navigation": {
            "schema": CELL_NAVIGATION_SCHEMA,
            "navmeshes": [
                navmesh_manifest(navmesh) for navmesh in navigation_navmeshes
            ],
        },
        "lighting": {
            "mode": environment["mode"],
            "ambientColor": environment["ambientColor"],
            "directionalColor": environment["directionalColor"],
            "fogColor": environment["fogColor"],
            "fogNearGameUnits": environment["fogNearGameUnits"],
            "fogFarGameUnits": environment["fogFarGameUnits"],
            "directionalRotationDegrees": environment["directionalRotationDegrees"],
            "directionalFade": environment["directionalFade"],
            "fogClipDistanceGameUnits": environment["fogFarGameUnits"],
            "fogPower": environment["fogPower"],
            "lights": lights,
        },
        "environmentCatalog": environment_manifest,
        "diagnostics": {
            "landscapeBakes": [
                {
                    "assetId": landscape_export.asset["id"],
                    "textureId": landscape_export.diagnostic_bake["id"],
                    "png": landscape_export.diagnostic_bake["png"],
                    "pngSha256": landscape_export.diagnostic_bake["pngSha256"],
                    "boundByParityPresentation": False,
                }
                for _loaded_cell, _landscape, landscape_export in terrain_rows
            ],
        },
        "assets": sorted(all_assets, key=lambda value: value["id"]),
        "textures": sorted(all_textures, key=lambda value: value["id"]),
        "unresolvedTextureBindings": unresolved_texture_bindings,
        "references": references,
        "lodBlocks": lod_references,
        "grassOverlays": (
            grass_overlay["overlays"] if grass_overlay is not None else []
        ),
        "coverage": {
            "selectedReferences": len(selected),
            "sourceReferences": len(candidates),
            "loadedCells": len(loaded_cells),
            "loadedGridDiameter": loaded_grid_diameter,
            "navmeshes": len(navigation_navmeshes),
            "navmeshVertices": sum(
                len(navmesh.vertices) for navmesh in navigation_navmeshes
            ),
            "navmeshTriangles": sum(
                len(navmesh.triangles) for navmesh in navigation_navmeshes
            ),
            "distantReferenceRadiusCells": distant_reference_radius,
            "distantReferenceTypes": sorted(distant_reference_types),
            "distantSourceCells": len(distant_source_cells),
            "exportedAssets": len(all_assets),
            "doors": sum(1 for _reference, base in selected if base.record_type == "DOOR"),
            "excludedReferences": excluded_references,
            "nonPresentationAssets": [
                non_presentation_assets[path]
                for path in sorted(non_presentation_assets)
            ],
            "excludedEditorMarkerSurfaces": sum(
                len(sidecar["coverage"]["excludedEditorMarkerSurfaces"])
                for sidecar in asset_sidecars.values()
            ),
            "excludedNonPresentationSurfaces": sum(
                len(sidecar["coverage"]["excludedNonPresentationSurfaces"])
                for sidecar in asset_sidecars.values()
            ),
            "sourcePoseBakedSkinSurfaces": sum(
                sidecar["coverage"]["sourcePoseBakedSkinSurfaces"]
                for sidecar in asset_sidecars.values()
            ),
            "collision": "authored-bhk-packed-plus-LAND-height-grid",
            "textures": (
                "owned-dds-authored-mips-plus-decoded-bc1-alpha-"
                "plus-LAND-layer-runtime"
            ),
            "decodedTextures": len(all_textures),
            "missingOptionalMaterialTextures": unresolved_texture_bindings,
            "materialBindings": sum(len(asset["materials"]) for asset in all_assets),
            "grass": (
                grass_overlay["coverage"]
                if grass_overlay is not None
                else {
                    "status": "not-requested",
                    "instances": 0,
                    "assets": 0,
                    "vertices": 0,
                    "triangles": 0,
                }
            ),
            "authoredLights": len(lights),
            "environment": {
                "weatherRecords": len(environment_catalog.weather),
                "imageSpaceResolved": environment_manifest["baseImageSpace"] is not None,
                "decodedSkyTextures": len(environment_textures),
                "authoredSkyTextures": len(environment_texture_paths),
                "missingSkyTextures": missing_environment_textures,
                "nightSkyModel": "authored-uncompiled",
                "weatherImageSpaceValues": "preserved-unresolved",
            },
            "lod": {
                **lod_contract,
                "cellSizeGameUnits": exterior_cell_size_game_units,
                "blockStrideCells": int(lod_contract["level"]),
                "blocks": [
                    {
                        "id": row["id"],
                        "assetId": row["assetId"],
                        "logicalPath": row["logicalPath"],
                        "sourceSha256": row["sourceSha256"],
                        "family": row["family"],
                        "level": row["level"],
                        "variant": row["variant"],
                        "blockOriginGameUnits": row["blockOriginGameUnits"],
                        "geometryCoordinateSpace": row["geometryCoordinateSpace"],
                        "selectionReason": row["selectionReason"],
                        "presentationClip": row["presentationClip"],
                    }
                    for row in lod_references
                ],
            },
            "landscape": {
                "formId": form_id(target_landscape.form_id),
                "compressionChecksumValid": target_landscape.compression_checksum_valid,
                "vertices": LAND_VERTEX_AXIS_COUNT * LAND_VERTEX_AXIS_COUNT,
                "triangles": LAND_QUAD_AXIS_COUNT * LAND_QUAD_AXIS_COUNT * 2,
                "baseLayers": len(target_landscape.base_layers),
                "alphaLayers": len(target_landscape.alpha_layers),
            },
            "landscapes": [
                {
                    "cellFormId": form_id(loaded_cell.form_id),
                    "grid": list(loaded_cell.coordinates),
                    "formId": form_id(landscape.form_id),
                    "compressionChecksumValid": landscape.compression_checksum_valid,
                    "vertices": LAND_VERTEX_AXIS_COUNT * LAND_VERTEX_AXIS_COUNT,
                    "triangles": LAND_QUAD_AXIS_COUNT * LAND_QUAD_AXIS_COUNT * 2,
                    "baseLayers": len(landscape.base_layers),
                    "alphaLayers": len(landscape.alpha_layers),
                }
                for loaded_cell, landscape, _export in terrain_rows
            ],
            "nonGeometricLandscapes": [
                {
                    "cellFormId": form_id(loaded_cell.form_id),
                    "grid": list(loaded_cell.coordinates),
                    "formId": form_id(landscape.form_id),
                    "compressionChecksumValid": landscape.compression_checksum_valid,
                    "dataFlags": f"{landscape.flags:08x}",
                    "baseTextureFormIds": [
                        form_id(value) for value in landscape.base_texture_form_ids
                    ],
                    "alphaLayers": [
                        {
                            "quadrant": layer.quadrant,
                            "layerIndex": layer.layer_index,
                            "rawTextureFormId": form_id(layer.texture_form_id),
                            "opacityRows": len(layer.opacities),
                        }
                        for layer in landscape.alpha_layers
                    ],
                    "vertices": 0,
                    "triangles": 0,
                    "disposition": "authored-DATA-declares-no-vertex-geometry",
                }
                for loaded_cell, landscape in non_geometric_terrain_rows
            ],
        },
    }
    _atomic_json(output_path, document)
    document["output"] = str(output_path.resolve())
    return document
