"""Validate compiled static CELL assets, textures, and filesystem closure."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from PIL import Image

from bsa_archive import canonical_member_path
from cell_landscape_validate import (
    LANDSCAPE_ASSET_EXTRA_FIELDS,
    LANDSCAPE_TEXTURE_EXTRA_FIELDS,
    LandscapeExpectation,
    landscape_material_contract,
    landscape_sidecar_expectation,
    validate_landscape_asset_contract,
    validate_landscape_runtime_texture_contract,
    validate_landscape_texture_binding,
    validate_landscape_texture_contract,
)
from cell_static_contract import (
    ASSETS_FILE_NAME,
    BLOCKERS_FILE_NAME,
    CELL_FILE_NAME,
    LANDSCAPE_ASSET_KIND,
    LANDSCAPE_RUNTIME_TEXTURE_KIND,
    LANDSCAPE_TEXTURE_KIND,
    MANIFEST_FILE_NAME,
    OWNED_DDS_TEXTURE_KIND,
    STATIC_NIF_ASSET_KIND,
    TEXTURES_FILE_NAME,
    mesh_member_path,
)
from export_static_nif_gltf import SCHEMA as STATIC_NIF_SCHEMA, compiler_provenance
from landscape_gltf import (
    LAND_QUADRANT_VERTEX_SIDE,
    LAND_WEIGHT_MAP_ROLE,
    landscape_baked_texture_id,
)
from material_contract import material_bindings, texture_binding_requests
from owned_archive_stack import OwnedArchiveStack
from plugin_stack import file_sha256
from runtime_configuration import RuntimeConfiguration
from texture_pipeline import DDS_CUBEMAP_FACE_COUNT


PNG_FORMAT = "PNG"
COMMON_ASSET_FIELDS = {
    "assetKind",
    "assetId",
    "requestedModelPath",
    "logicalPath",
    "sourceArchive",
    "sourceArchiveSha256",
    "sourceBytes",
    "sourceSha256",
    "outputs",
    "coverage",
    "surfaces",
    "textureBindings",
    "materials",
}
LANDSCAPE_ASSET_FIELDS = COMMON_ASSET_FIELDS | LANDSCAPE_ASSET_EXTRA_FIELDS
COMMON_TEXTURE_FIELDS = {
    "textureKind",
    "textureId",
    "requestedPath",
    "archivePath",
    "sourceSha256",
    "sourceBytes",
    "sourceArchive",
    "sourceArchiveSha256",
    "png",
    "pngBytes",
    "pngSha256",
    "width",
    "height",
    "normalGreenInverted",
    "cubeFaces",
}
LANDSCAPE_TEXTURE_FIELDS = COMMON_TEXTURE_FIELDS | LANDSCAPE_TEXTURE_EXTRA_FIELDS
LANDSCAPE_RUNTIME_TEXTURE_FIELDS = COMMON_TEXTURE_FIELDS | {"landscapeRole"}


def validate_relative_file(
    root: Path,
    relative_text: str,
    expected_bytes: int,
    expected_sha256: str,
    expected_parent: Path,
) -> Path:
    relative = Path(relative_text)
    if relative.is_absolute() or ".." in relative.parts:
        raise ValueError(f"Static CELL nested output is not relative: {relative}")
    path = (root / relative).resolve()
    if path.parent != expected_parent.resolve() or not path.is_file():
        raise ValueError(f"Static CELL nested output path differs: {relative}")
    if path.stat().st_size != expected_bytes or file_sha256(path) != expected_sha256.lower():
        raise ValueError(f"Static CELL nested output descriptor differs: {relative}")
    return path


def validate_resource_artifacts(
    root: Path,
    assets: list[dict[str, object]],
    textures: list[dict[str, object]],
    profile: dict[str, object],
    archives: OwnedArchiveStack,
    configuration: RuntimeConfiguration,
    landscape_expectations_by_asset_id: dict[str, LandscapeExpectation],
) -> set[str]:
    expected_files = {
        (root / MANIFEST_FILE_NAME).resolve(),
        (root / CELL_FILE_NAME).resolve(),
        (root / ASSETS_FILE_NAME).resolve(),
        (root / TEXTURES_FILE_NAME).resolve(),
        (root / BLOCKERS_FILE_NAME).resolve(),
    }
    seen_asset_ids: set[str] = set()
    seen_model_paths: set[str] = set()
    texture_ids_by_requested = {
        str(row["requestedPath"]): str(row["textureId"]) for row in textures
    }
    if len(texture_ids_by_requested) != len(textures):
        raise ValueError("Static CELL compile repeats a requested texture")
    bound_texture_ids: set[str] = set()
    landscape_expectations_by_texture_id: dict[str, LandscapeExpectation] = {}
    for asset in assets:
        asset_id = str(asset["assetId"])
        asset_kind = str(asset.get("assetKind"))
        expected_fields = (
            LANDSCAPE_ASSET_FIELDS
            if asset_kind == LANDSCAPE_ASSET_KIND
            else COMMON_ASSET_FIELDS
        )
        if set(asset) != expected_fields:
            raise ValueError(f"Static CELL asset fields differ: {asset_id}")
        if asset_kind not in {STATIC_NIF_ASSET_KIND, LANDSCAPE_ASSET_KIND}:
            raise ValueError(f"Static CELL asset kind differs: {asset_id}")
        if asset_id in seen_asset_ids:
            raise ValueError("Static CELL compile repeats an asset")
        seen_asset_ids.add(asset_id)
        member = None
        landscape_expectation = None
        if asset_kind == STATIC_NIF_ASSET_KIND:
            model_path = str(asset["requestedModelPath"])
            if model_path in seen_model_paths:
                raise ValueError("Static CELL compile repeats a model path")
            seen_model_paths.add(model_path)
            member = archives.extract(mesh_member_path(model_path))
            expected_asset_id = hashlib.sha256(
                f"{member.logical_path}:{member.sha256}".encode("utf-8")
            ).hexdigest()[:configuration.content_compiler.asset_id_hex_characters]
            if (
                asset_id != expected_asset_id
                or asset["logicalPath"] != member.logical_path
                or asset["sourceSha256"] != member.sha256
                or int(asset["sourceBytes"]) != len(member.data)
                or asset["sourceArchive"] != member.source_archive
                or asset["sourceArchiveSha256"] != member.source_archive_sha256
            ):
                raise ValueError(f"Static CELL asset source differs: {model_path}")
        else:
            landscape_expectation = landscape_expectations_by_asset_id.get(asset_id)
            if landscape_expectation is None:
                raise ValueError(f"Static CELL landscape source is unresolved: {asset_id}")
            model_path = landscape_expectation.source.identity.form_key
            validate_landscape_asset_contract(asset, landscape_expectation)

        asset_parent = (root / "generated" / "assets" / asset_id).resolve()
        compiled_outputs = asset.get("outputs")
        if not isinstance(compiled_outputs, dict) or "sidecar" not in compiled_outputs:
            raise ValueError(f"Static CELL asset output ledger differs: {model_path}")
        output_files = [str(row["file"]) for row in compiled_outputs.values()]
        if len(output_files) != len(set(output_files)):
            raise ValueError(f"Static CELL asset repeats an output file: {model_path}")
        for descriptor in compiled_outputs.values():
            if set(descriptor) != {"file", "bytes", "sha256"}:
                raise ValueError(f"Static CELL asset descriptor fields differ: {model_path}")
            path = validate_relative_file(
                root,
                str(descriptor["file"]),
                int(descriptor["bytes"]),
                str(descriptor["sha256"]),
                asset_parent,
            )
            expected_files.add(path)
        sidecar_path = root / str(compiled_outputs["sidecar"]["file"])
        sidecar = json.loads(sidecar_path.read_text(encoding="utf-8"))
        expected_sidecar_outputs = {
            name: {
                "file": f"generated/assets/{asset_id}/{descriptor['file']}",
                "bytes": int(descriptor["bytes"]),
                "sha256": str(descriptor["sha256"]),
            }
            for name, descriptor in sidecar["outputs"].items()
        }
        expected_sidecar_outputs["sidecar"] = compiled_outputs["sidecar"]
        if asset_kind == STATIC_NIF_ASSET_KIND:
            assert member is not None
            expected_schema = STATIC_NIF_SCHEMA
            expected_compiler = compiler_provenance()
            expected_source_sha256 = member.sha256
            expected_logical_path = member.logical_path
        else:
            assert landscape_expectation is not None
            expected_sidecar = landscape_sidecar_expectation(
                sidecar,
                landscape_expectation,
            )
            expected_schema = str(expected_sidecar["schema"])
            expected_compiler = expected_sidecar["compiler"]
            expected_source_sha256 = str(expected_sidecar["sourceSha256"])
            expected_logical_path = str(expected_sidecar["logicalPath"])
        if (
            sidecar.get("schema") != expected_schema
            or sidecar.get("status") != (
                "layered-material"
                if asset_kind == LANDSCAPE_ASSET_KIND
                else "geometry-only"
            )
            or sidecar.get("compiler") != expected_compiler
            or sidecar["source"]["sha256"] != expected_source_sha256
            or sidecar["source"]["logicalPath"] != expected_logical_path
            or asset["coverage"] != sidecar["coverage"]
            or asset["surfaces"] != sidecar["surfaces"]
            or compiled_outputs != expected_sidecar_outputs
        ):
            raise ValueError(f"Static CELL asset sidecar source differs: {model_path}")
        binding_policies: dict[str, set[str]] = {}
        for surface in sidecar["surfaces"]:
            for request in texture_binding_requests(surface):
                binding_policies.setdefault(request["path"], set()).add(
                    request["missingOwnedMember"]
                )
        expected_bindings = [
            {
                "requestedPath": requested,
                "textureId": texture_ids_by_requested.get(requested),
                "missingOwnedMember": (
                    "error"
                    if "error" in policies
                    else "unbound-no-substitution"
                ),
            }
            for requested, policies in sorted(binding_policies.items())
        ]
        if asset.get("textureBindings") != expected_bindings:
            raise ValueError(f"Static CELL asset texture bindings differ: {model_path}")
        if asset_kind == STATIC_NIF_ASSET_KIND:
            expected_materials = material_bindings(
                sidecar,
                texture_ids_by_requested,
                configuration.content_compiler,
            )
        else:
            assert landscape_expectation is not None
            validate_landscape_texture_binding(expected_bindings, landscape_expectation)
            expected_materials = landscape_material_contract(
                sidecar,
                expected_bindings,
                landscape_expectation,
                configuration.content_compiler,
            )
        if asset.get("materials") != expected_materials:
            raise ValueError(f"Static CELL asset material bindings differ: {model_path}")
        if asset_kind == LANDSCAPE_ASSET_KIND:
            assert landscape_expectation is not None
            landscape_expectations_by_texture_id[
                landscape_baked_texture_id(
                    landscape_expectation.source.landscape,
                    landscape_expectation.source.identity,
                    configuration.content_compiler,
                )
            ] = landscape_expectation
            landscape_expectations_by_texture_id.update(
                {
                    str(binding["textureId"]): landscape_expectation
                    for binding in expected_bindings
                }
            )
        bound_texture_ids.update(
            str(row["textureId"])
            for row in expected_bindings
            if row["textureId"] is not None
        )

    seen_texture_ids: set[str] = set()
    seen_texture_paths: set[str] = set()
    aliases = {
        canonical_member_path(str(source)): canonical_member_path(str(target))
        for source, target in profile.get("textureAliases", {}).items()
    }
    for texture in textures:
        texture_id = str(texture["textureId"])
        texture_kind = str(texture.get("textureKind"))
        expected_texture_fields = (
            LANDSCAPE_TEXTURE_FIELDS
            if texture_kind == LANDSCAPE_TEXTURE_KIND
            else LANDSCAPE_RUNTIME_TEXTURE_FIELDS
            if texture_kind == LANDSCAPE_RUNTIME_TEXTURE_KIND
            else COMMON_TEXTURE_FIELDS
        )
        if set(texture) != expected_texture_fields:
            raise ValueError(f"Static CELL texture fields differ: {texture_id}")
        if texture_kind not in {
            OWNED_DDS_TEXTURE_KIND,
            LANDSCAPE_TEXTURE_KIND,
            LANDSCAPE_RUNTIME_TEXTURE_KIND,
        }:
            raise ValueError(f"Static CELL texture kind differs: {texture_id}")
        requested = str(texture["requestedPath"])
        if texture_id in seen_texture_ids or requested in seen_texture_paths:
            raise ValueError("Static CELL compile repeats a texture")
        seen_texture_ids.add(texture_id)
        seen_texture_paths.add(requested)
        if texture_kind == OWNED_DDS_TEXTURE_KIND:
            member = archives.extract(str(texture["archivePath"]))
            requested_canonical = canonical_member_path(requested)
            expected_archive_path = aliases.get(requested_canonical, requested_canonical)
            expected_texture_id = hashlib.sha256(
                f"{requested_canonical}:{member.sha256}".encode("utf-8")
            ).hexdigest()[:configuration.content_compiler.asset_id_hex_characters]
            if (
                texture_id != expected_texture_id
                or texture["archivePath"] != expected_archive_path
                or texture["sourceSha256"] != member.sha256
                or int(texture["sourceBytes"]) != len(member.data)
                or texture["sourceArchive"] != member.source_archive
                or texture["sourceArchiveSha256"] != member.source_archive_sha256
            ):
                raise ValueError(f"Static CELL texture source differs: {requested}")
            expected_normal = requested_canonical.endswith("_n.dds")
        elif texture_kind == LANDSCAPE_TEXTURE_KIND:
            landscape_expectation = landscape_expectations_by_texture_id.get(
                texture_id
            )
            if landscape_expectation is None:
                raise ValueError(
                    f"Static CELL landscape texture source is unresolved: {texture_id}"
                )
            validate_landscape_texture_contract(
                texture,
                landscape_expectation,
                archives,
                aliases,
                configuration.content_compiler,
            )
            expected_normal = False
            if texture.get("diagnosticOnly") is not True:
                raise ValueError(
                    f"Static CELL landscape bake is not diagnostic-only: {requested}"
                )
        else:
            landscape_expectation = landscape_expectations_by_texture_id.get(texture_id)
            if landscape_expectation is None:
                raise ValueError(
                    f"Static CELL LAND runtime texture source is unresolved: {texture_id}"
                )
            validate_landscape_runtime_texture_contract(
                texture,
                landscape_expectation,
                archives,
                aliases,
                configuration.content_compiler,
            )
            expected_normal = bool(texture["normalGreenInverted"])
        texture_parent = (root / "generated" / "textures").resolve()
        png = validate_relative_file(
            root,
            str(texture["png"]),
            int(texture["pngBytes"]),
            str(texture["pngSha256"]),
            texture_parent,
        )
        expected_files.add(png)
        is_weight_map = (
            texture_kind == LANDSCAPE_RUNTIME_TEXTURE_KIND
            and texture.get("landscapeRole") == LAND_WEIGHT_MAP_ROLE
        )
        if is_weight_map:
            expected_weight_bytes = LAND_QUADRANT_VERTEX_SIDE ** 2 * 4 * 4
            if (
                png.stat().st_size != expected_weight_bytes
                or int(texture["width"]) != LAND_QUADRANT_VERTEX_SIDE
                or int(texture["height"]) != LAND_QUADRANT_VERTEX_SIDE
            ):
                raise ValueError(f"Static CELL LAND weight-map payload differs: {requested}")
        else:
            with Image.open(png) as image:
                image.load()
                if (
                    image.format != PNG_FORMAT
                    or list(image.size)
                    != [int(texture["width"]), int(texture["height"])]
                ):
                    raise ValueError(f"Static CELL texture dimensions differ: {requested}")
        if texture["normalGreenInverted"] != expected_normal:
            raise ValueError(f"Static CELL normal-map policy differs: {requested}")
        cube_faces = texture["cubeFaces"]
        if cube_faces and len(cube_faces) != DDS_CUBEMAP_FACE_COUNT:
            raise ValueError(f"Static CELL cubemap face count differs: {requested}")
        for face in cube_faces:
            validated = validate_relative_file(
                root,
                str(face["png"]),
                int(face["bytes"]),
                str(face["pngSha256"]),
                texture_parent,
            )
            expected_files.add(validated)
            with Image.open(validated) as image:
                image.load()
                if image.format != PNG_FORMAT:
                    raise ValueError(f"Static CELL cubemap output is not PNG: {requested}")

    diagnostic_texture_ids = {
        str(texture["textureId"])
        for texture in textures
        if texture.get("textureKind") == LANDSCAPE_TEXTURE_KIND
        and texture.get("diagnosticOnly") is True
    }
    if (
        diagnostic_texture_ids & bound_texture_ids
        or (bound_texture_ids | diagnostic_texture_ids) != seen_texture_ids
    ):
        raise ValueError("Static CELL compile contains unreferenced textures")
    actual_files = {path.resolve() for path in root.rglob("*") if path.is_file()}
    if actual_files != expected_files:
        raise ValueError("Static CELL compile contains unaccounted files")
    return seen_texture_ids
