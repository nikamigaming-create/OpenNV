"""Independent owned-data validation for the static CELL LAND capability."""

from __future__ import annotations

import hashlib
from dataclasses import dataclass
from pathlib import Path

from bsa_archive import canonical_member_path
from cell_landscape_contract import landscape_contract_for
from landscape_gltf import (
    SCHEMA as LANDSCAPE_SCHEMA,
    canonical_texture_path,
    compiler_provenance,
    landscape_asset_id,
    landscape_bake_contract,
    landscape_baked_requested_path,
    landscape_baked_source_hash,
    landscape_baked_texture_id,
    landscape_logical_path,
    landscape_materials,
)
from landscape_stack import OwnedLandscapeSource, resolve_owned_landscape
from owned_archive_stack import OwnedArchiveStack
from runtime_configuration import ContentCompilerConfiguration


LANDSCAPE_ASSET_EXTRA_FIELDS = {
    "sourcePlugin",
    "sourceLocalFormId",
    "collision",
    "landscape",
}
LANDSCAPE_TEXTURE_EXTRA_FIELDS = {"sources", "bakeContract"}
LANDSCAPE_QUADRANT_AXIS_COUNT = 2
SINGLE_LANDSCAPE_SURFACE = 1
SINGLE_LANDSCAPE_TEXTURE_BINDING = 1


@dataclass(frozen=True)
class LandscapeExpectation:
    source: OwnedLandscapeSource
    contract: dict[str, object]
    asset_id: str


def resolve_landscape_expectation(
    data_root: Path,
    corpus_manifest: dict[str, object],
    cell: dict[str, object],
    child: dict[str, object],
    origin: tuple[float, float, float],
    compiler: ContentCompilerConfiguration,
) -> LandscapeExpectation:
    source = resolve_owned_landscape(data_root, corpus_manifest, cell, child)
    return LandscapeExpectation(
        source=source,
        contract=landscape_contract_for(source, cell, origin),
        asset_id=landscape_asset_id(source.landscape, source.identity, compiler),
    )


def validate_landscape_asset_contract(
    asset: dict[str, object],
    expectation: LandscapeExpectation,
) -> None:
    source = expectation.source
    landscape = source.landscape
    if (
        asset["requestedModelPath"] is not None
        or asset["sourceArchive"] is not None
        or asset["sourceArchiveSha256"] is not None
        or asset["sourcePlugin"] != source.identity.source_plugin
        or asset["sourceLocalFormId"] != source.identity.source_local_form_id
        or asset["sourceSha256"] != hashlib.sha256(landscape.source_bytes).hexdigest()
        or int(asset["sourceBytes"]) != len(landscape.source_bytes)
        or asset["logicalPath"] != landscape_logical_path(landscape, source.identity)
        or asset["landscape"] != expectation.contract
        or asset["collision"]
        != {
            "enabled": True,
            "source": "LAND-height-grid",
            "blockTypes": ["LAND"],
        }
    ):
        raise ValueError(
            f"Static CELL landscape source differs: {source.identity.form_key}"
        )


def landscape_sidecar_expectation(
    sidecar: dict[str, object],
    expectation: LandscapeExpectation,
) -> dict[str, object]:
    source = expectation.source
    source_contract = sidecar.get("source")
    if not isinstance(source_contract, dict) or (
        source_contract.get("formKey") != source.identity.form_key
        or source_contract.get("cellFormKey") != source.identity.cell_form_key
        or source_contract.get("worldspaceFormKey")
        != source.identity.worldspace_form_key
        or source_contract.get("compressionChecksumValid")
        != source.landscape.compression_checksum_valid
    ):
        raise ValueError(
            "Static CELL landscape sidecar identity differs: "
            f"{source.identity.form_key}"
        )
    return {
        "schema": LANDSCAPE_SCHEMA,
        "compiler": compiler_provenance(),
        "sourceSha256": hashlib.sha256(source.landscape.source_bytes).hexdigest(),
        "logicalPath": landscape_logical_path(source.landscape, source.identity),
    }


def validate_landscape_texture_binding(
    bindings: list[dict[str, object]],
    expectation: LandscapeExpectation,
) -> str:
    if (
        len(bindings) != SINGLE_LANDSCAPE_TEXTURE_BINDING
        or bindings[0]["textureId"] is None
    ):
        raise ValueError(
            "Static CELL landscape texture binding differs: "
            f"{expectation.source.identity.form_key}"
        )
    return str(bindings[0]["textureId"])


def landscape_material_contract(
    sidecar: dict[str, object],
    texture_id: str,
    expectation: LandscapeExpectation,
) -> list[dict[str, object]]:
    surfaces = sidecar.get("surfaces")
    if not isinstance(surfaces, list) or len(surfaces) != SINGLE_LANDSCAPE_SURFACE:
        raise ValueError(
            "Static CELL landscape surface count differs: "
            f"{expectation.source.identity.form_key}"
        )
    return landscape_materials(str(surfaces[0]["name"]), texture_id)


def validate_landscape_texture_contract(
    texture: dict[str, object],
    expectation: LandscapeExpectation,
    archives: OwnedArchiveStack,
    aliases: dict[str, str],
    compiler: ContentCompilerConfiguration,
) -> None:
    source = expectation.source
    expected_texture_id = landscape_baked_texture_id(
        source.landscape,
        source.identity,
        compiler,
    )
    expected_sources = []
    for contract in source.textures.contracts():
        source_requested = canonical_texture_path(str(contract["diffusePath"]))
        source_archive_path = aliases.get(source_requested, source_requested)
        member = archives.extract(source_archive_path)
        normal_path = contract.get("normalPath")
        normal_source = None
        if normal_path:
            normal_requested = canonical_texture_path(str(normal_path))
            normal_archive_path = aliases.get(normal_requested, normal_requested)
            normal_member = archives.extract(normal_archive_path)
            normal_source = {
                "requestedPath": normal_requested,
                "archivePath": normal_archive_path,
                "sourceSha256": normal_member.sha256,
                "sourceBytes": len(normal_member.data),
                "sourceArchive": normal_member.source_archive,
                "sourceArchiveSha256": normal_member.source_archive_sha256,
            }
        expected_sources.append(
            {
                **contract,
                "requestedPath": source_requested,
                "archivePath": source_archive_path,
                "sourceSha256": member.sha256,
                "sourceBytes": len(member.data),
                "sourceArchive": member.source_archive,
                "sourceArchiveSha256": member.source_archive_sha256,
                "normalSource": normal_source,
            }
        )
    expected_source_sha256 = landscape_baked_source_hash(
        source.landscape,
        expected_sources,
    )
    expected_source_bytes = len(source.landscape.source_bytes) + sum(
        int(row["sourceBytes"])
        + (
            0
            if row["normalSource"] is None
            else int(row["normalSource"]["sourceBytes"])
        )
        for row in expected_sources
    )
    expected_dimension = (
        compiler.landscape_quadrant_pixels * LANDSCAPE_QUADRANT_AXIS_COUNT
    )
    requested = str(texture["requestedPath"])
    if (
        str(texture["textureId"]) != expected_texture_id
        or requested != landscape_baked_requested_path(expectation.asset_id)
        or texture["archivePath"] is not None
        or texture["sourceArchive"] is not None
        or texture["sourceArchiveSha256"] is not None
        or texture["sourceSha256"] != expected_source_sha256
        or int(texture["sourceBytes"]) != expected_source_bytes
        or texture["sources"] != expected_sources
        or texture["bakeContract"] != landscape_bake_contract(compiler)
        or int(texture["width"]) != expected_dimension
        or int(texture["height"]) != expected_dimension
        or texture["cubeFaces"] != []
    ):
        raise ValueError(
            f"Static CELL landscape texture source differs: {requested}"
        )
