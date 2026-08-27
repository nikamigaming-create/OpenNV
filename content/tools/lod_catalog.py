"""Catalog owned worldspace object-LOD blocks without hand-authored placements."""

from __future__ import annotations

import re
from dataclasses import dataclass

from bsa_archive import BsaArchive


@dataclass(frozen=True)
class LodBlock:
    """One archive-owned worldspace LOD block selected for a scene."""

    family: str
    level: int
    variant: str
    x: int
    y: int
    logical_path: str

    @property
    def model_path(self) -> str:
        prefix = "meshes\\"
        if not self.logical_path.startswith(prefix):
            raise ValueError(f"LOD member is not a mesh path: {self.logical_path}")
        return self.logical_path[len(prefix) :]

    @property
    def identity(self) -> str:
        return f"{self.family}-level{self.level}-{self.variant}-x{self.x}-y{self.y}"


def lod_block_grids(block: LodBlock) -> frozenset[tuple[int, int]]:
    return frozenset(
        (x, y)
        for y in range(block.y, block.y + block.level)
        for x in range(block.x, block.x + block.level)
    )


def _normalized_path(value: object) -> str:
    path = str(value).replace("/", "\\").strip("\\").lower()
    if not path:
        raise ValueError("LOD archive path must be nonempty")
    return path


def select_lod_blocks(
    archive: BsaArchive,
    recipe: dict[str, object],
    loaded_grids: tuple[tuple[int, int], ...],
) -> tuple[tuple[LodBlock, ...], dict[str, object]]:
    """Select archive blocks around the loaded full-detail grid.

    The block coordinates are taken from the owned filenames.  The recipe only
    declares the archive namespace, level, variant preference, and bounded
    coverage radius; it never names a location-specific block.
    """

    source = recipe.get("lod")
    if source is None:
        return (), {
            "status": "not-configured",
            "mode": "none",
            "selectedBlocks": 0,
        }
    if not isinstance(source, dict):
        raise ValueError("Exterior LOD contract must be an object")
    required = {
        "mode",
        "objectArchiveRoot",
        "terrainArchiveRoot",
        "filePrefix",
        "level",
        "selectionRadiusCells",
        "preferHigh",
    }
    if set(source) != required:
        raise ValueError(
            "Exterior LOD contract must contain exactly "
            + ", ".join(sorted(required))
        )
    if source["mode"] != "owned-worldspace-object-and-terrain-blocks":
        raise ValueError(f"Unsupported exterior LOD mode: {source['mode']}")
    object_archive_root = _normalized_path(source["objectArchiveRoot"])
    terrain_archive_root = _normalized_path(source["terrainArchiveRoot"])
    if not object_archive_root.startswith("meshes\\") or not object_archive_root.endswith(
        "\\blocks"
    ):
        raise ValueError(
            "Exterior LOD objectArchiveRoot must name a meshes landscape blocks directory"
        )
    if (
        not terrain_archive_root.startswith("meshes\\landscape\\lod\\")
        or terrain_archive_root.endswith("\\blocks")
    ):
        raise ValueError(
            "Exterior LOD terrainArchiveRoot must name a terrain LOD mesh directory"
        )
    file_prefix = str(source["filePrefix"]).strip().lower()
    if not file_prefix or "\\" in file_prefix or "." in file_prefix:
        raise ValueError("Exterior LOD filePrefix must be a plain archive stem")
    level = int(source["level"])
    radius = int(source["selectionRadiusCells"])
    prefer_high = bool(source["preferHigh"])
    if level < 1 or radius < 0:
        raise ValueError("Exterior LOD level and radius must be nonnegative/positive")
    if not loaded_grids:
        raise ValueError("Exterior LOD selection requires loaded full-detail grids")

    def scan_family(
        family: str,
        archive_root: str,
    ) -> tuple[dict[tuple[int, int, int], LodBlock], int]:
        pattern = re.compile(
            rf"^{re.escape(archive_root)}\\{re.escape(file_prefix)}\.level"
            rf"(?P<level>[0-9]+)(?P<high>\.high)?\.x(?P<x>-?[0-9]+)\.y(?P<y>-?[0-9]+)\.nif$"
        )
        available: dict[tuple[int, int, int], LodBlock] = {}
        available_members = 0
        for member in archive.members:
            match = pattern.fullmatch(member)
            if match is None:
                continue
            available_members += 1
            member_level = int(match["level"])
            if member_level != level:
                continue
            variant = "high" if match["high"] else "normal"
            block = LodBlock(
                family,
                member_level,
                variant,
                int(match["x"]),
                int(match["y"]),
                member,
            )
            key = (block.level, block.x, block.y)
            previous = available.get(key)
            if previous is None:
                available[key] = block
            elif previous.variant == block.variant:
                raise ValueError(f"Duplicate owned LOD block variant: {member}")
            elif prefer_high and block.variant == "high":
                available[key] = block
            elif not prefer_high and block.variant == "normal":
                available[key] = block
        if available_members == 0:
            raise ValueError(f"No owned {family} LOD blocks found below {archive_root}")
        return available, available_members

    object_available, available_object_members = scan_family(
        "object",
        object_archive_root,
    )
    terrain_available, available_terrain_members = scan_family(
        "terrain",
        terrain_archive_root,
    )
    available = {**object_available, **terrain_available}
    if len(available) != len(object_available) + len(terrain_available):
        # Family is intentionally not part of the temporary key used above.
        # Keep the inventories disjoint before combining their values.
        available_blocks = [*object_available.values(), *terrain_available.values()]
    else:
        available_blocks = list(available.values())

    min_x = min(grid[0] for grid in loaded_grids)
    max_x = max(grid[0] for grid in loaded_grids)
    min_y = min(grid[1] for grid in loaded_grids)
    max_y = max(grid[1] for grid in loaded_grids)
    candidates = tuple(
        sorted(
            (
                block
                for block in available_blocks
                if min_x - radius <= block.x <= max_x + radius
                and min_y - radius <= block.y <= max_y + radius
            ),
            key=lambda block: (
                block.y,
                block.x,
                block.family,
                block.variant,
                block.logical_path,
            ),
        )
    )
    loaded_grid_set = frozenset(loaded_grids)
    fully_covered = tuple(
        block for block in candidates if lod_block_grids(block) <= loaded_grid_set
    )
    partially_overlapping = tuple(
        block
        for block in candidates
        if lod_block_grids(block) & loaded_grid_set
        and block not in fully_covered
    )
    selected = tuple(block for block in candidates if block not in fully_covered)
    if not selected:
        raise ValueError(
            "Owned LOD catalog has no blocks in the declared loaded-grid coverage "
            f"x={min_x}..{max_x} y={min_y}..{max_y} radius={radius}"
        )
    return selected, {
        "status": "owned-data-selected",
        "mode": str(source["mode"]),
        "objectArchiveRoot": object_archive_root,
        "terrainArchiveRoot": terrain_archive_root,
        "filePrefix": file_prefix,
        "level": level,
        "preferHigh": prefer_high,
        "selectionRadiusCells": radius,
        "loadedGridBounds": {
            "minX": min_x,
            "maxX": max_x,
            "minY": min_y,
            "maxY": max_y,
        },
        "availableMembers": available_object_members + available_terrain_members,
        "availableObjectMembers": available_object_members,
        "availableTerrainMembers": available_terrain_members,
        "availableLevelMembers": len(available_blocks),
        "availableObjectLevelMembers": len(object_available),
        "availableTerrainLevelMembers": len(terrain_available),
        "candidateBlocksBeforeNearExclusion": len(candidates),
        "excludedOverlappingNearBlocks": [],
        "excludedFullyCoveredNearBlocks": [block.identity for block in fully_covered],
        "partiallyOverlappingNearBlocks": [
            block.identity for block in partially_overlapping
        ],
        "selectedBlocks": len(selected),
        "selectedObjectBlocks": sum(block.family == "object" for block in selected),
        "selectedTerrainBlocks": sum(block.family == "terrain" for block in selected),
        "selectionReason": "owned-block-origin-within-loaded-grid-expanded-radius",
        "nearCellHolePolicy": (
            "full-detail-grid-authoritative-with-partial-LOD-triangle-clipping"
        ),
    }
