"""Family-scoped provenance for the owned FNV content compiler."""

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path
from typing import Iterable

from gltf_io import compiler_sources_sha256, sha256_bytes
from runtime_configuration import configured_recipe_path, load_runtime_configuration


SCHEMA = "opennv-content-compiler-identities/v1"
ACTOR_ROUTE_SCHEMA = "opennv-actor-compiler-route/v1"
FAMILIES = ("static", "cell", "opening", "actor")


def _roots() -> tuple[Path, Path]:
    packaged_root = getattr(sys, "_MEIPASS", None)
    tools_root = (
        Path(packaged_root) / "compiler-sources"
        if packaged_root is not None
        else Path(__file__).resolve().parent
    )
    recipes_root = (
        Path(packaged_root) / "recipes"
        if packaged_root is not None
        else tools_root.parent / "recipes"
    )
    return tools_root, recipes_root


def _local_dependencies(*entrypoints: str) -> set[Path]:
    # Imported here to keep this module in every family's own dependency graph.
    from gltf_io import local_python_dependency_paths

    tools_root, _ = _roots()
    sources: set[Path] = set()
    for entrypoint in entrypoints:
        sources.update(
            local_python_dependency_paths(
                tools_root / entrypoint,
                tools_root,
                excluded_modules=("prepare_fo3_profile",),
            )
        )
    return sources


def _recipe_path(recipe_id: str) -> Path:
    _, recipes_root = _roots()
    path = recipes_root / f"{recipe_id}.json"
    if not path.is_file():
        raise FileNotFoundError(f"Compiler recipe is missing: {path}")
    return path


def _cell_route_recipe_paths(cell_recipe_id: str | None) -> tuple[list[Path], list[Path]]:
    configuration = load_runtime_configuration()
    pending = [
        cell_recipe_id
        or str(configuration.document["legalAssets"]["defaultCellRecipe"])
    ]
    cells: list[Path] = []
    actors: list[Path] = []
    seen: set[str] = set()
    while pending:
        recipe_id = pending.pop(0)
        if recipe_id in seen:
            continue
        seen.add(recipe_id)
        path = _recipe_path(recipe_id)
        document = json.loads(path.read_text(encoding="utf-8"))
        cells.append(path)
        actors.extend(_recipe_path(str(value)) for value in document.get("actorRecipes", []))
        if document.get("linkedExteriorRecipe"):
            pending.append(str(document["linkedExteriorRecipe"]))
        pending.extend(
            str(value["recipe"])
            for value in document.get("linkedCellRecipes", [])
        )
    return cells, actors


def _actor_route(
    cell_recipe_id: str | None,
) -> tuple[list[Path], dict[str, object]]:
    """Return the exact one-level actor selection used by prepare_legal_assets."""
    configuration = load_runtime_configuration()
    primary_id = cell_recipe_id or str(
        configuration.document["legalAssets"]["defaultCellRecipe"]
    )
    primary_path = _recipe_path(primary_id)
    primary = json.loads(primary_path.read_text(encoding="utf-8"))
    configured_links = primary.get("linkedCellRecipes")
    if configured_links is None and primary.get("linkedExteriorRecipe"):
        configured_links = [{"recipe": primary["linkedExteriorRecipe"]}]
    if configured_links is not None and (
        not isinstance(configured_links, list) or not configured_links
    ):
        raise ValueError("Linked CELL recipes must be a non-empty ordered list")
    linked_ids = (
        []
        if configured_links is None
        else [str(row["recipe"]) for row in configured_links]
    )
    linked_paths = [_recipe_path(recipe_id) for recipe_id in linked_ids]
    linked = [
        json.loads(path.read_text(encoding="utf-8")) for path in linked_paths
    ]
    actor_ids = [str(value) for value in primary["actorRecipes"]]
    for document in linked:
        actor_ids.extend(str(value) for value in document["actorRecipes"])
    actor_paths = [_recipe_path(recipe_id) for recipe_id in actor_ids]
    discovery_paths = [
        path
        for path, document in [(primary_path, primary), *zip(linked_paths, linked)]
        if document.get("actorDiscovery") is not None
    ]
    route = {
        "schema": ACTOR_ROUTE_SCHEMA,
        "primaryCellRecipe": str(primary["id"]),
        "linkedCellRecipes": linked_ids,
        "actors": [
            {
                "recipe": recipe_id,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            }
            for recipe_id, path in zip(actor_ids, actor_paths, strict=True)
        ],
        "discoveries": [
            {
                "recipe": json.loads(path.read_text(encoding="utf-8"))["id"],
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            }
            for path in discovery_paths
        ],
    }
    return [*actor_paths, *discovery_paths], route


def actor_route_manifest(cell_recipe_id: str | None = None) -> dict[str, object]:
    _, route = _actor_route(cell_recipe_id)
    payload = json.dumps(
        route,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return {
        **route,
        "sha256": hashlib.sha256(payload).hexdigest(),
    }


def compiler_provenance_source_paths(
    family: str,
    cell_recipe_id: str | None = None,
) -> list[Path]:
    if family not in FAMILIES:
        raise ValueError(f"Unknown compiler family: {family}")
    configuration = load_runtime_configuration()
    compiler_common = {
        configured_recipe_path("nifDecoder"),
        configured_recipe_path("materialBinding"),
    }
    common = {configuration.path, *compiler_common}
    if family == "static":
        sources = _local_dependencies(
            "export_static_nif_gltf.py",
            "export_nif_particle_effect.py",
        ) | common
    elif family == "cell":
        cell_recipes, _ = _cell_route_recipe_paths(cell_recipe_id)
        sources = (
            _local_dependencies("cell_scene.py", "exterior_scene.py")
            | common
            | {configured_recipe_path("visualArchives")}
            | set(cell_recipes)
        )
    elif family == "opening":
        sources = (
            _local_dependencies("opening_catalog.py")
            | common
            | {
                configured_recipe_path("visualArchives"),
                configured_recipe_path("audioArchives"),
                configured_recipe_path("opening"),
            }
        )
    else:
        actor_recipes, _ = _actor_route(cell_recipe_id)
        # Actor animation membership is authored by the opening graph. Keeping
        # that complete graph here makes an opening animation change invalidate
        # actors without coupling either family to unchanged world geometry.
        sources = (
            _local_dependencies(
                "prepare_actor.py",
                "opening_catalog.py",
                "prepare_legal_assets.py",
            )
            | compiler_common
            | {
                configured_recipe_path("visualArchives"),
                configured_recipe_path("audioArchives"),
                configured_recipe_path("opening"),
            }
            | set(actor_recipes)
        )
    return sorted({path.resolve() for path in sources}, key=lambda path: str(path).casefold())


def compiler_provenance(
    family: str,
    cell_recipe_id: str | None = None,
) -> dict[str, str]:
    source_sha256 = compiler_sources_sha256(
        compiler_provenance_source_paths(family, cell_recipe_id)
    )
    actor_configuration_sha256 = (
        load_runtime_configuration().actor_artifact_manifest()["sha256"]
        if family == "actor"
        else None
    )
    actor_route_sha256 = (
        actor_route_manifest(cell_recipe_id)["sha256"]
        if family == "actor"
        else None
    )
    identity = {
        "family": family,
        "name": (
            "OpenNV.Content packaged direct exporter v1"
            if getattr(sys, "frozen", False)
            else "OpenNV direct static NIF exporter v1"
        ),
        "sha256": (
            hashlib.sha256(
                json.dumps(
                    {
                        "actorArtifactConfigurationSha256": (
                            actor_configuration_sha256
                        ),
                        "actorRouteSha256": actor_route_sha256,
                        "sourcesSha256": source_sha256,
                    },
                    sort_keys=True,
                    separators=(",", ":"),
                ).encode("utf-8")
            ).hexdigest()
            if family == "actor"
            else source_sha256
        ),
    }
    if actor_configuration_sha256 is not None:
        identity["actorArtifactConfigurationSha256"] = str(
            actor_configuration_sha256
        )
    if actor_route_sha256 is not None:
        identity["actorRouteSha256"] = str(actor_route_sha256)
    if getattr(sys, "frozen", False):
        identity["artifactSha256"] = sha256_bytes(Path(sys.executable).read_bytes())
    return identity


def compiler_identities(cell_recipe_id: str | None = None) -> dict[str, object]:
    return {
        "schema": SCHEMA,
        "families": {
            family: compiler_provenance(family, cell_recipe_id)
            for family in FAMILIES
        },
    }


def all_compiler_provenance_source_paths() -> list[Path]:
    return sorted(
        {
            path.resolve()
            for family in FAMILIES
            for path in compiler_provenance_source_paths(family)
        },
        key=lambda path: str(path).casefold(),
    )


def identities_sha256(identities: dict[str, object]) -> str:
    return hashlib.sha256(
        json.dumps(identities, sort_keys=True, separators=(",", ":")).encode()
    ).hexdigest()
