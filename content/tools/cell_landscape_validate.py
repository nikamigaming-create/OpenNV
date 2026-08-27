"""Independent owned-data validation for the static CELL LAND capability."""

from __future__ import annotations

import hashlib
from dataclasses import dataclass
from pathlib import Path

from bsa_archive import canonical_member_path
from cell_landscape_contract import landscape_contract_for
from landscape_catalog import resolved_layer_texture_form_id
from landscape_gltf import (
    LAND_WEIGHTS_PER_MAP,
    LAND_WEIGHT_MAP_ROLE,
    LANDSCAPE_CONTRACT_SOURCE,
    LANDSCAPE_DIFFUSE_DOMAIN,
    LANDSCAPE_LAYER_WEIGHT_OPERATION,
    LANDSCAPE_LIGHTING_MODEL,
    LANDSCAPE_MATERIAL_SCHEMA,
    LANDSCAPE_NORMAL_DECODE,
    LANDSCAPE_RETAIL_WEIGHT_SEMANTICS,
    LANDSCAPE_RETAIL_WEIGHT_TYPE,
    LANDSCAPE_WEIGHT_INTERPOLATION,
    LANDSCAPE_WEIGHT_STORAGE,
    LAND_QUADRANT_LAST_VERTEX,
    LAND_QUADRANT_VERTEX_SIDE,
    LAND_SHADER_SAMPLER_BUDGET,
    SCHEMA as LANDSCAPE_SCHEMA,
    canonical_texture_path,
    compiler_provenance,
    landscape_asset_id,
    landscape_bake_contract,
    landscape_baked_requested_path,
    landscape_baked_source_hash,
    landscape_baked_texture_id,
    landscape_logical_path,
    landscape_weight_map_count,
    landscape_weight_map_payload,
)
from gltf_io import sha256_bytes
from landscape_stack import OwnedLandscapeSource, resolve_owned_landscape
from owned_archive_stack import OwnedArchiveStack
from runtime_configuration import ContentCompilerConfiguration


LANDSCAPE_ASSET_EXTRA_FIELDS = {
    "sourcePlugin",
    "sourceLocalFormId",
    "collision",
    "landscape",
}
LANDSCAPE_TEXTURE_EXTRA_FIELDS = {"sources", "bakeContract", "diagnosticOnly"}
LANDSCAPE_RUNTIME_TEXTURE_EXTRA_FIELDS = {"landscapeRole"}
LANDSCAPE_QUADRANT_AXIS_COUNT = 2
LANDSCAPE_SURFACE_COUNT = 4


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
) -> None:
    if not bindings or any(binding["textureId"] is None for binding in bindings):
        raise ValueError(
            "Static CELL landscape texture binding differs: "
            f"{expectation.source.identity.form_key}"
        )


def landscape_material_contract(
    sidecar: dict[str, object],
    bindings: list[dict[str, object]],
    expectation: LandscapeExpectation,
    compiler: ContentCompilerConfiguration,
) -> list[dict[str, object]]:
    surfaces = sidecar.get("surfaces")
    if not isinstance(surfaces, list) or len(surfaces) != LANDSCAPE_SURFACE_COUNT:
        raise ValueError(
            "Static CELL landscape surface count differs: "
            f"{expectation.source.identity.form_key}"
        )
    binding_ids = {
        str(binding["requestedPath"]): str(binding["textureId"])
        for binding in bindings
    }
    result = []
    for quadrant, surface in enumerate(surfaces):
        surface_name = str(surface["name"])
        requested_textures = [str(value) for value in surface["textures"]]
        try:
            texture_ids = [binding_ids[path] for path in requested_textures]
        except KeyError as error:
            raise ValueError(
                f"Static CELL landscape material texture is unresolved: {error.args[0]}"
            ) from error
        landscape = expectation.source.landscape
        base = next(row for row in landscape.base_layers if row.quadrant == quadrant)
        base_form_id = resolved_layer_texture_form_id(landscape, base)
        base_contract = expectation.source.textures.texture_contract(base_form_id)
        cursor = 0
        base_diffuse_id = texture_ids[cursor]
        cursor += 1
        base_normal_id = None
        if base_contract.get("normalPath"):
            base_normal_id = texture_ids[cursor]
            cursor += 1
        layers = sorted(
            (row for row in landscape.alpha_layers if row.quadrant == quadrant),
            key=lambda row: row.layer_index,
        )
        layer_texture_rows = []
        for layer in layers:
            form_id = resolved_layer_texture_form_id(landscape, layer)
            contract = expectation.source.textures.texture_contract(form_id)
            diffuse_id = texture_ids[cursor]
            cursor += 1
            normal_id = None
            if contract.get("normalPath"):
                normal_id = texture_ids[cursor]
                cursor += 1
            layer_texture_rows.append((layer, form_id, diffuse_id, normal_id))
        map_count = landscape_weight_map_count(len(layers))
        if len(texture_ids) != cursor + map_count:
            raise ValueError(
                f"Static CELL landscape material texture order differs: {surface_name}"
            )
        weight_map_ids = texture_ids[cursor:]
        layer_contracts = []
        for ordinal, (layer, form_id, diffuse_id, normal_id) in enumerate(
            layer_texture_rows
        ):
            weight_ordinal = ordinal + 1
            map_index = weight_ordinal // LAND_WEIGHTS_PER_MAP
            layer_contracts.append(
                {
                    "layerIndex": layer.layer_index,
                    "ltexFormId": f"{form_id:08x}",
                    "diffuseTextureId": diffuse_id,
                    "normalTextureId": normal_id,
                    "weightMapTextureId": weight_map_ids[map_index],
                    "weightMapIndex": map_index,
                    "weightChannel": weight_ordinal % LAND_WEIGHTS_PER_MAP,
                }
            )
        result.append(
            {
                "surfaceIndex": quadrant,
                "name": surface_name,
                "diffuseTextureId": base_diffuse_id,
                "normalTextureId": base_normal_id,
                "emissiveTextureId": None,
                "environmentTextureId": None,
                "environmentMaskTextureId": None,
                "environmentMapScale": 1.0,
                "emissiveColor": [0.0, 0.0, 0.0],
                "emissiveReplace": False,
                "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
                "diffuseSampleSrgb": False,
                "roughness": 1.0,
                "alphaContract": {"mode": "OPAQUE", "cutoff": None},
                "vertexColorMode": "multiply",
                "doubleSided": False,
                "unshaded": False,
                "landscapeContract": {
                    "schema": LANDSCAPE_MATERIAL_SCHEMA,
                    "model": LANDSCAPE_LIGHTING_MODEL,
                    "diffuseDomain": LANDSCAPE_DIFFUSE_DOMAIN,
                    "normalDecode": LANDSCAPE_NORMAL_DECODE,
                    "layerWeightOperation": LANDSCAPE_LAYER_WEIGHT_OPERATION,
                    "weightInterpolation": LANDSCAPE_WEIGHT_INTERPOLATION,
                    "weightStorage": LANDSCAPE_WEIGHT_STORAGE,
                    "retailWeightSemantics": list(LANDSCAPE_RETAIL_WEIGHT_SEMANTICS),
                    "retailWeightType": LANDSCAPE_RETAIL_WEIGHT_TYPE,
                    "source": LANDSCAPE_CONTRACT_SOURCE,
                    "quadrant": quadrant,
                    "ltexFormId": f"{base_form_id:08x}",
                    "tileRepeats": compiler.landscape_tiles_per_quadrant,
                    "weightVertexSide": LAND_QUADRANT_VERTEX_SIDE,
                    "weightLastVertex": LAND_QUADRANT_LAST_VERTEX,
                    "weightMapTextureIds": weight_map_ids,
                    "baseWeightMapIndex": 0 if weight_map_ids else None,
                    "baseWeightChannel": 0 if weight_map_ids else None,
                    "shaderSamplerBudget": LAND_SHADER_SAMPLER_BUDGET,
                    "samplersUsed": 2 + len(layers) * 2 + map_count,
                    "baseDiffuseTextureId": base_diffuse_id,
                    "baseNormalTextureId": base_normal_id,
                    "layers": layer_contracts,
                },
            }
        )
    return result


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


def validate_landscape_runtime_texture_contract(
    texture: dict[str, object],
    expectation: LandscapeExpectation,
    archives: OwnedArchiveStack,
    aliases: dict[str, str],
    compiler: ContentCompilerConfiguration,
) -> None:
    role = str(texture["landscapeRole"])
    raw_requested = str(texture["requestedPath"])
    requested = (
        canonical_texture_path(raw_requested)
        if role in {"diffuse", "normal"}
        else raw_requested.replace("/", "\\").lower()
    )
    if role in {"diffuse", "normal"}:
        archive_path = aliases.get(requested, requested)
        member = archives.extract(archive_path)
        expected_id = hashlib.sha256(
            f"{requested}:{member.sha256}".encode("utf-8")
        ).hexdigest()[:compiler.asset_id_hex_characters]
        if (
            texture["textureId"] != expected_id
            or texture["requestedPath"] != requested
            or texture["archivePath"] != archive_path
            or texture["sourceSha256"] != member.sha256
            or int(texture["sourceBytes"]) != len(member.data)
            or texture["sourceArchive"] != member.source_archive
            or texture["sourceArchiveSha256"] != member.source_archive_sha256
            or texture["normalGreenInverted"] != requested.endswith("_n.dds")
        ):
            raise ValueError(f"Static CELL LAND runtime texture source differs: {requested}")
        return

    if role != LAND_WEIGHT_MAP_ROLE:
        raise ValueError(f"Static CELL LAND runtime texture role differs: {role}")
    if texture["archivePath"] is not None or texture["sourceArchive"] is not None:
        raise ValueError(
            f"Static CELL LAND weight map unexpectedly has an archive source: {requested}"
        )
    expected_weight_maps: dict[str, tuple[str, str, int]] = {}
    asset_id = expectation.asset_id
    for quadrant in range(4):
        layers = sorted(
            (row for row in expectation.source.landscape.alpha_layers if row.quadrant == quadrant),
            key=lambda row: row.layer_index,
        )
        for map_index in range(landscape_weight_map_count(len(layers))):
            payload = landscape_weight_map_payload(layers, map_index)
            weight_source = (
                expectation.source.landscape.source_bytes
                + f":quadrant={quadrant}:weight-map={map_index}:operation=".encode()
                + LANDSCAPE_LAYER_WEIGHT_OPERATION.encode()
                + b":layers="
                + ",".join(str(layer.layer_index) for layer in layers).encode()
                + payload
            )
            source_hash = sha256_bytes(weight_source)
            weight_id = hashlib.sha256(
                f"LAND-WEIGHTS:{asset_id}:{quadrant}:{map_index}:{source_hash}".encode()
            ).hexdigest()[:compiler.asset_id_hex_characters]
            path = (
                f"generated\\landscape\\{asset_id}-q{quadrant}-"
                f"weights{map_index}.rgba32f"
            )
            expected_weight_maps[weight_id] = (
                path,
                source_hash,
                len(weight_source),
            )
    expected = expected_weight_maps.get(str(texture["textureId"]))
    if expected is None:
        raise ValueError(f"Static CELL LAND weight-map identity differs: {requested}")
    expected_path, expected_hash, expected_bytes = expected
    if (
        requested != expected_path
        or texture["sourceSha256"] != expected_hash
        or int(texture["sourceBytes"]) != expected_bytes
        or texture["sourceArchiveSha256"] is not None
        or texture["normalGreenInverted"]
        or int(texture["width"]) != LAND_QUADRANT_VERTEX_SIDE
        or int(texture["height"]) != LAND_QUADRANT_VERTEX_SIDE
        or texture["cubeFaces"] != []
    ):
        raise ValueError(f"Static CELL LAND weight-map provenance differs: {requested}")
