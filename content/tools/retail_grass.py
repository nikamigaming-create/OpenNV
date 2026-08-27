"""Build a private, evidence-backed FNV grass overlay from retail draw telemetry.

The public OpenNV source tree retains only the reconstruction procedure and
shader identities.  Owned NIF/DDS payloads and the retail observation remain
in the caller's disposable cache.
"""

from __future__ import annotations

import hashlib
import json
import math
import struct
import sys
import time
from dataclasses import dataclass
from pathlib import Path, PureWindowsPath

if not hasattr(time, "clock"):
    time.clock = time.perf_counter

from pyffi.formats.nif import NifFormat

from bsa_archive import BsaArchive
from owned_archive_stack import OwnedArchiveStack
from actor_material import actor_alpha_contract
from gltf_io import (
    BufferBuilder,
    GL_ARRAY_BUFFER,
    GL_ELEMENT_ARRAY_BUFFER,
    GL_FLOAT,
    GL_UNSIGNED_INT,
    GL_UNSIGNED_SHORT,
    GL_UNSIGNED_SHORT_MAX,
    atomic_write,
    compiler_sources_sha256,
    pack_floats,
    sha256_bytes,
)
from nif_decoder import decode_nif
from runtime_configuration import (
    ContentCompilerConfiguration,
    RetailGrassCompilerConfiguration,
    RetailGrassMeshConfiguration,
    load_runtime_configuration,
)
from texture_pipeline import OwnedTexturePipeline, TexturePipeline


EVENT = "texture-sampler-contract"
SIDECAR_SCHEMA = "opennv-static-nif-gltf/v2"
GENERATOR = "OpenNV private retail grass reconstruction v1"
BYTE_CHANNEL_BITS = 8
BYTE_CHANNEL_MAXIMUM = float((1 << BYTE_CHANNEL_BITS) - 1)


def _decode_text(value: object) -> str:
    if isinstance(value, bytes):
        return value.split(b"\0", 1)[0].decode("utf-8", errors="replace")
    return str(value)


def _canonical_path(value: object) -> str:
    return _decode_text(value).replace("/", "\\").lstrip("\\").lower()


def _sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def _atomic_source(path: Path, data: bytes) -> None:
    atomic_write(path, data)


def _registers(
    record: dict[str, object],
    key: str,
    expected: int,
    *,
    permit_unconsumed_undefined_values: bool = False,
) -> list[list[float]]:
    source = record[key]
    if not isinstance(source, dict) or int(source.get("getResult", -1)) != 0:
        raise ValueError(f"Retail grass {key} readback failed")
    if int(source.get("registerCount", -1)) != expected:
        raise ValueError(f"Retail grass {key} register count changed")
    values = [
        math.nan
        if value is None and permit_unconsumed_undefined_values
        else float(value)
        for value in source.get("values", [])
    ]
    if len(values) != expected * 4 or (
        not permit_unconsumed_undefined_values
        and not all(math.isfinite(value) for value in values)
    ):
        raise ValueError(f"Retail grass {key} payload is incomplete")
    return [values[index : index + 4] for index in range(0, len(values), 4)]


def _close(
    first: list[float],
    second: list[float],
    tolerance: float,
) -> bool:
    return len(first) == len(second) and all(
        math.isclose(left, right, rel_tol=0.0, abs_tol=tolerance)
        for left, right in zip(first, second)
    )


def active_instance_count(
    primitive_count: int,
    strip_length: int,
    configuration: RetailGrassCompilerConfiguration | None = None,
) -> int:
    """Recover the number of populated c20+ instance registers in one draw."""
    contract = configuration or load_runtime_configuration().content_compiler.retail_grass
    step = strip_length + contract.draw.strip_bridge_indices
    candidate, remainder = divmod(
        primitive_count + contract.draw.primitive_count_bias,
        step,
    )
    if remainder == 0 and 0 < candidate <= contract.shader.instance_capacity:
        return candidate
    # The fixed retail batch index buffer removes its trailing bridge at the
    # 228-instance ceiling.  This exact boundary is present in the recovered
    # GRASS23x002 draw stream for both Wasteland05 and Wasteland07.
    if primitive_count == (
        contract.shader.instance_capacity * step
        - contract.draw.full_batch_trailing_bridge_indices
    ):
        return contract.shader.instance_capacity
    raise ValueError(
        "Retail grass primitive count does not map to the owned strip: "
        f"primitives={primitive_count} stripLength={strip_length}"
    )


def _declaration(record: dict[str, object]) -> tuple[tuple[int, ...], ...]:
    declaration = record.get("vertexDeclaration")
    if not isinstance(declaration, dict) or int(declaration.get("getResult", -1)) != 0:
        raise ValueError("Retail grass vertex declaration is missing")
    if int(declaration.get("getElementsResult", -1)) != 0:
        raise ValueError("Retail grass vertex declaration readback failed")
    return tuple(
        tuple(
            int(element[key])
            for key in ("stream", "offset", "type", "method", "usage", "usageIndex")
        )
        for element in declaration.get("elements", [])
    )


def _texture_sampler_event(path: Path) -> dict[str, object]:
    events = []
    with path.open("r", encoding="utf-8") as stream:
        for line in stream:
            try:
                payload = json.loads(line)
            except json.JSONDecodeError:
                continue
            if payload.get("event") == EVENT:
                events.append(payload)
    if len(events) != 1:
        raise ValueError(f"Retail grass observation requires exactly one {EVENT} event")
    return events[0]


def read_retail_grass_render_state(
    path: Path,
    configuration: RetailGrassCompilerConfiguration | None = None,
) -> dict[str, object]:
    contract = configuration or load_runtime_configuration().content_compiler.retail_grass
    event = _texture_sampler_event(path)
    records = [
        record
        for record in event.get("records", [])
        if int(record.get("vertexShader", {}).get("fnv1a32", 0))
        == contract.shader.vertex_fnv1a32
        and int(record.get("pixelShader", {}).get("fnv1a32", 0))
        == contract.shader.pixel_fnv1a32
    ]
    if not records:
        raise ValueError("Retail render-state observation has no GRASS23x002 draws")
    states = []
    for record in records:
        source = record.get("renderState")
        if not isinstance(source, dict):
            raise ValueError("Retail grass render-state payload is missing")
        state: dict[str, int] = {}
        for key, expected in contract.draw.render_state.items():
            result_key = key + "Result"
            if int(source.get(result_key, -1)) != 0:
                raise ValueError(f"Retail grass render-state readback failed: {key}")
            value = int(source.get(key, -1))
            if value != expected:
                raise ValueError(
                    f"Retail grass render state changed: {key}={value} expected={expected}"
                )
            state[key] = value
        states.append(state)
    if any(state != states[0] for state in states[1:]):
        raise ValueError("Retail grass render state changed within one source frame")
    if int(event.get("renderFrameLead", 0)) != contract.draw.render_frame_lead:
        raise ValueError("Retail grass render-state observation changed frame ownership")
    return {
        "source": {
            "path": str(path.resolve()),
            "bytes": path.stat().st_size,
            "sha256": _sha256(path),
            "sourceFrame": int(event["sourceFrame"]),
            "renderFrame": int(event["sourceFrame"])
            - int(event["renderFrameLead"]),
            "matchingDraws": len(records),
        },
        "values": states[0],
        "interpretation": {
            "cullMode": "D3DCULL_NONE",
            "zFunction": "D3DCMP_LESSEQUAL",
            "alphaFunction": "D3DCMP_GREATER",
            "sourceBlend": "D3DBLEND_SRCALPHA",
            "destinationBlend": "D3DBLEND_INVSRCALPHA",
            "blendOperation": "D3DBLENDOP_ADD",
            "colorWriteEnable": "D3DCOLORWRITEENABLE_RED|GREEN|BLUE",
        },
    }


def read_retail_grass_observation(
    path: Path,
    render_state_path: Path | None = None,
    configuration: RetailGrassCompilerConfiguration | None = None,
) -> dict[str, object]:
    contract = configuration or load_runtime_configuration().content_compiler.retail_grass
    event = _texture_sampler_event(path)
    target = event.get("target", {})
    if (
        int(target.get("width", 0)) != contract.texture.width_pixels
        or int(target.get("height", 0)) != contract.texture.height_pixels
        or int(target.get("levelCount", 0)) != contract.texture.level_count
        or int(target.get("format", 0)) != contract.texture.d3d9_format
        or str(target.get("contentHash"))
        != f"d3d9-fnv1a32:{contract.texture.fnv1a32:08x}"
        or str(target.get("topLevelHash"))
        != f"d3d9-fnv1a32:{contract.texture.top_level_fnv1a32:08x}"
        or int(event.get("matchedResourceCount", 0))
        != contract.capture.required_matched_resource_count
        or int(event.get("textureStageCount", 0))
        != contract.capture.texture_stage_count
        or int(event.get("maximumCandidates", 0))
        != contract.capture.maximum_candidates
        or int(event.get("maximumRecords", 0))
        != contract.capture.maximum_records
        or int(event.get("maximumVertexBufferBytes", 0))
        != contract.capture.maximum_vertex_buffer_bytes
        or bool(event.get("candidateLimitReached", True))
    ):
        raise ValueError("Retail observation did not match the owned grass texture")
    records = list(event.get("records", []))
    records_per_source_frame: dict[int, int] = {}
    for record in records:
        source_frame = int(record.get("sourceFrame", event["sourceFrame"]))
        records_per_source_frame[source_frame] = (
            records_per_source_frame.get(source_frame, 0) + 1
        )
    if any(
        count >= contract.capture.maximum_records
        for count in records_per_source_frame.values()
    ):
        raise ValueError("Retail grass observation reached its record ceiling")

    grass_records: dict[str, list[dict[str, object]]] = {
        mesh.suffix: [] for mesh in contract.meshes
    }
    shared_by_source_frame: dict[int, dict[str, list[float]]] = {}
    raw_total_instances = 0
    render_frame_lead = int(event.get("renderFrameLead", 0))
    if render_frame_lead != contract.draw.render_frame_lead:
        raise ValueError("Retail grass observation changed frame ownership")
    for record in records:
        vertex_shader = record.get("vertexShader", {})
        pixel_shader = record.get("pixelShader", {})
        if (
            int(vertex_shader.get("fnv1a32", 0)) != contract.shader.vertex_fnv1a32
            or int(pixel_shader.get("fnv1a32", 0)) != contract.shader.pixel_fnv1a32
        ):
            continue
        mesh = contract.meshes_by_batch_vertices.get(
            int(record.get("vertexCount", 0))
        )
        if mesh is None:
            raise ValueError(
                f"Retail GRASS23x002 used an unknown batch vertex count: {record.get('vertexCount')}"
            )
        if (
            str(record.get("drawMethod")) != "DrawIndexedPrimitive"
            or int(record.get("primitiveType", 0)) != contract.draw.primitive_type
            or int(record.get("baseVertexIndex", -1)) != 0
            or int(record.get("minimumVertexIndex", -1)) != 0
            or int(record.get("startIndex", -1)) != 0
            or _declaration(record) != contract.draw.declaration
        ):
            raise ValueError("Retail grass draw topology or declaration changed")
        vertex_buffer = record.get("vertexBuffer", {})
        if int(vertex_buffer.get("stride", 0)) != contract.draw.vertex_stride_bytes:
            raise ValueError("Retail grass vertex stride changed")
        sampler = record.get("sampler", {})
        color_space = record.get("colorSpaceState", {})
        if (
            int(sampler.get("addressU", 0)) != contract.draw.sampler["addressU"]
            or int(sampler.get("addressV", 0)) != contract.draw.sampler["addressV"]
            or int(sampler.get("magFilter", 0)) != contract.draw.sampler["magFilter"]
            or int(sampler.get("minFilter", 0)) != contract.draw.sampler["minFilter"]
            or int(sampler.get("mipFilter", 0)) != contract.draw.sampler["mipFilter"]
            or int(sampler.get("srgbTexture", -1))
            != contract.draw.sampler["srgbTexture"]
            or int(color_space.get("srgbWrite", -1))
            != contract.draw.sampler["srgbWrite"]
        ):
            raise ValueError("Retail grass sampler or encoded color-space state changed")

        vertex = _registers(
            record,
            "vertexConstants",
            contract.shader.vertex_constant_register_count,
        )
        pixel = _registers(
            record,
            "pixelConstants",
            contract.shader.pixel_constant_register_count,
            permit_unconsumed_undefined_values=True,
        )
        registers = contract.shader.registers
        if not _close(
            vertex[registers["scaleMask"]][:3],
            [1.0, 1.0, 1.0],
            contract.shader.float_tolerance,
        ) or not math.isclose(
            vertex[registers["instanceCeiling"]][
                registers["instanceCeilingComponent"]
            ],
            contract.shader.instance_register_ceiling,
            rel_tol=0.0,
            abs_tol=contract.shader.float_tolerance,
        ):
            raise ValueError("Retail grass scale mask or instance-register ceiling changed")
        current_shared = {
            "diffuseDirection": vertex[registers["diffuseDirection"]][:3],
            "diffuseColor": vertex[registers["diffuseColor"]][:3],
            "fade": vertex[registers["fade"]][2:4],
            "ambientColor": vertex[registers["ambientColor"]][:3],
            "directionalScale": [vertex[registers["directionalScale"]][0]],
            "windVectorAndAmplitude": vertex[registers["wind"]][:3],
            "fogColor": vertex[registers["fogColor"]][:3],
            "fog": vertex[registers["fog"]][:3],
        }
        source_frame = int(record.get("sourceFrame", event["sourceFrame"]))
        render_frame = int(record.get("renderFrame", source_frame - render_frame_lead))
        if render_frame != source_frame - render_frame_lead:
            raise ValueError("Retail grass record changed frame ownership")
        frame_shared = shared_by_source_frame.get(source_frame)
        if frame_shared is None:
            shared_by_source_frame[source_frame] = current_shared
        elif any(
            not _close(frame_shared[key], value, contract.shader.float_tolerance)
            for key, value in current_shared.items()
        ):
            raise ValueError(
                "Retail grass shared shader constants changed within source frame "
                f"{source_frame}"
            )

        instance_count = active_instance_count(
            int(record["primitiveCount"]),
            mesh.strip_length,
            contract,
        )
        instances = [list(value) for value in vertex[
            contract.shader.instance_first_register :
            contract.shader.instance_first_register + instance_count
        ]]
        if len(instances) != instance_count or any(
            not all(math.isfinite(component) for component in instance)
            for instance in instances
        ):
            raise ValueError("Retail grass instance constants are incomplete")
        alpha_cutoff = pixel[registers["alphaCutoff"]][0]
        if not math.isfinite(alpha_cutoff):
            raise ValueError("Retail grass alpha-cutoff constant is undefined")
        grass_records[mesh.suffix].append(
            {
                "ordinal": int(record["ordinal"]),
                "sourceFrame": source_frame,
                "renderFrame": render_frame,
                "primitiveCount": int(record["primitiveCount"]),
                "instanceCount": instance_count,
                "windPhase": float(vertex[registers["wind"]][3]),
                "alphaCutoff": float(alpha_cutoff),
                "instances": instances,
            }
        )
        raw_total_instances += instance_count

    selected_source_frame = int(event["sourceFrame"])
    if selected_source_frame not in shared_by_source_frame:
        raise ValueError(
            "Retail grass event source frame has no configured owned grass draw"
        )
    shared = shared_by_source_frame[selected_source_frame]
    source_frame_priority = sorted(
        shared_by_source_frame,
        key=lambda frame: (
            frame != selected_source_frame,
            abs(frame - selected_source_frame),
            frame,
        ),
    )
    duplicate_instances_dropped = 0
    observed_grass_records: dict[str, list[dict[str, object]]] = {}
    for suffix, batches in grass_records.items():
        seen: set[tuple[float, ...]] = set()
        retained: list[dict[str, object]] = []
        for source_frame in source_frame_priority:
            for batch in batches:
                if int(batch["sourceFrame"]) != source_frame:
                    continue
                instances = []
                for instance in batch["instances"]:
                    identity = tuple(float(value) for value in instance)
                    if identity in seen:
                        duplicate_instances_dropped += 1
                        continue
                    seen.add(identity)
                    instances.append(instance)
                if instances:
                    retained.append(
                        {
                            **batch,
                            "instanceCount": len(instances),
                            "instances": instances,
                        }
                    )
        if retained:
            observed_grass_records[suffix] = retained
    raw_matching_draws = sum(len(value) for value in grass_records.values())
    matching_draws = sum(len(value) for value in observed_grass_records.values())
    total_instances = sum(
        int(batch["instanceCount"])
        for batches in observed_grass_records.values()
        for batch in batches
    )
    if (
        not observed_grass_records
        or raw_matching_draws < contract.capture.minimum_matching_records
    ):
        raise ValueError("Retail grass observation has no configured owned grass mesh")
    for suffix, batches in observed_grass_records.items():
        instances = [tuple(instance) for batch in batches for instance in batch["instances"]]
        if len(instances) != len(set(instances)):
            raise ValueError(f"Retail grass observation duplicates {suffix} instance data")

    fog_far, fog_span, fog_power = shared["fog"]
    fog_near = fog_far - fog_span
    render_state = read_retail_grass_render_state(
        render_state_path or path,
        contract,
    )
    return {
        "schema": "opennv-retail-grass-observation/v1",
        "status": "complete-private-runtime-contract",
        "source": {
            "path": str(path.resolve()),
            "bytes": path.stat().st_size,
            "sha256": _sha256(path),
            "sourceFrame": selected_source_frame,
            "renderFrame": selected_source_frame - render_frame_lead,
            "capturedSourceFrames": source_frame_priority,
            "frameSelection": "event-source-frame-then-nearest-source-frame",
        },
        "renderStateObservation": render_state,
        "shader": {
            "vertexFnv1a32": contract.shader.vertex_fnv1a32,
            "pixelFnv1a32": contract.shader.pixel_fnv1a32,
            "textureFnv1a32": contract.texture.fnv1a32,
            "textureTopLevelFnv1a32": contract.texture.top_level_fnv1a32,
            "diffuseDirection": shared["diffuseDirection"],
            "diffuseColor": shared["diffuseColor"],
            "ambientColor": shared["ambientColor"],
            "directionalScale": shared["directionalScale"][0],
            "windVectorAndAmplitude": shared["windVectorAndAmplitude"],
            "fadeStartGameUnits": shared["fade"][0],
            "fadeRangeGameUnits": shared["fade"][1],
            "fogColor": shared["fogColor"],
            "fogNearGameUnits": fog_near,
            "fogFarGameUnits": fog_far,
            "fogPower": fog_power,
            "diffuseDomain": "encoded",
            "sampler": "wrap-wrap-linear-mip-linear",
            "renderState": render_state["values"],
        },
        "meshes": observed_grass_records,
        "coverage": {
            "matchingDraws": matching_draws,
            "rawMatchingDraws": raw_matching_draws,
            "ignoredSameTextureDraws": len(records)
            - raw_matching_draws,
            "instances": total_instances,
            "rawInstances": raw_total_instances,
            "duplicateInstancesDropped": duplicate_instances_dropped,
            "recordCeilingReached": False,
            "configuredOwnedMeshes": len(grass_records),
            "observedOwnedMeshes": len(observed_grass_records),
            "observedMeshSuffixes": sorted(observed_grass_records),
            "unobservedMeshSuffixes": sorted(set(grass_records) - set(observed_grass_records)),
        },
    }


def _identity_transform(matrix: object, tolerance: float) -> bool:
    for row in range(1, 4 + 1):
        for column in range(1, 4 + 1):
            expected = 1.0 if row == column else 0.0
            if not math.isclose(
                float(getattr(matrix, f"m_{row}{column}")),
                expected,
                rel_tol=0.0,
                abs_tol=tolerance,
            ):
                return False
    return True


def _read_owned_mesh(
    archive: BsaArchive | OwnedArchiveStack,
    contract: RetailGrassMeshConfiguration,
    cache_root: Path,
    grass: RetailGrassCompilerConfiguration,
) -> dict[str, object]:
    member = archive.extract(contract.path)
    if member.sha256 != contract.sha256:
        raise ValueError(
            f"Owned grass NIF hash changed for {contract.path}: {member.sha256}"
        )
    source_path = cache_root / "source" / Path(member.logical_path.replace("\\", "/"))
    _atomic_source(source_path, member.data)
    decoded_nif = decode_nif(member.data)
    data = decoded_nif.document
    if len(data.roots) != 1:
        raise ValueError(f"Owned grass NIF must have one root: {contract.path}")
    root = data.roots[0]
    shapes = [
        block
        for block in data.get_global_iterator()
        if isinstance(block, NifFormat.NiTriStrips) and block.data is not None
    ]
    if len(shapes) != 1:
        raise ValueError(f"Owned grass NIF must have one NiTriStrips: {contract.path}")
    shape = shapes[0]
    mesh = shape.data
    if not _identity_transform(
        shape.get_transform(root),
        grass.shader.float_tolerance,
    ):
        raise ValueError(f"Owned grass NIF gained a nonidentity shape transform: {contract.path}")
    strip_lengths = [int(value) for value in mesh.strip_lengths]
    if (
        len(mesh.vertices) != contract.source_vertices
        or strip_lengths != [contract.strip_length]
        or len(mesh.uv_sets) != 1
        or len(mesh.uv_sets[0]) != contract.source_vertices
        or len(mesh.vertex_colors) != contract.source_vertices
    ):
        raise ValueError(f"Owned grass NIF geometry changed: {contract.path}")
    properties = list(shape.properties)
    grass_shader = next(
        (value for value in properties if isinstance(value, NifFormat.TallGrassShaderProperty)),
        None,
    )
    if grass_shader is None or _canonical_path(grass_shader.file_name) != grass.texture.path:
        raise ValueError(f"Owned grass NIF texture changed: {contract.path}")
    alpha_property = next(
        (value for value in properties if isinstance(value, NifFormat.NiAlphaProperty)),
        None,
    )
    if alpha_property is None:
        raise ValueError(f"Owned grass NIF lost NiAlphaProperty: {contract.path}")
    triangles = [tuple(int(index) for index in triangle) for triangle in mesh.get_triangles()]
    if not triangles:
        raise ValueError(f"Owned grass NIF has no presentation triangles: {contract.path}")
    return {
        "member": member,
        "sourcePath": source_path,
        "name": _decode_text(shape.name),
        "positions": [
            (float(value.x), float(value.y), float(value.z)) for value in mesh.vertices
        ],
        "uvs": [(float(value.u), float(value.v)) for value in mesh.uv_sets[0]],
        "colors": [
            (float(value.r), float(value.g), float(value.b), float(value.a))
            for value in mesh.vertex_colors
        ],
        "triangles": triangles,
        "propertyTypes": [type(value).__name__ for value in properties],
        "alphaThreshold": int(alpha_property.threshold),
        "alphaContract": actor_alpha_contract(alpha_property),
        "nifVersion": f"0x{data.version:08x}",
        "userVersion": int(data.user_version),
        "userVersion2": int(data.user_version_2),
        "decoder": decoded_nif.evidence(),
    }


def _fraction(value: float) -> float:
    return value - math.floor(value)


def _cross(
    first: tuple[float, float, float],
    second: tuple[float, float, float],
) -> tuple[float, float, float]:
    return (
        first[1] * second[2] - first[2] * second[1],
        first[2] * second[0] - first[0] * second[2],
        first[0] * second[1] - first[1] * second[0],
    )


def _normalize(
    value: tuple[float, float, float],
    epsilon: float,
) -> tuple[float, float, float]:
    length = math.sqrt(sum(component * component for component in value))
    if length <= epsilon:
        raise ValueError("Retail grass instance encodes a zero-length slope basis")
    return tuple(component / length for component in value)


def _mul(value: tuple[float, float, float], amount: float) -> tuple[float, float, float]:
    return tuple(component * amount for component in value)


def _add(*values: tuple[float, float, float]) -> tuple[float, float, float]:
    return tuple(sum(value[axis] for value in values) for axis in range(3))


def _instance_basis(
    instance: list[float],
    reconstruction: object,
) -> tuple[
    tuple[float, float, float],
    tuple[float, float, float],
    tuple[float, float, float],
]:
    slope = tuple(_fraction(value) * 2.0 - 1.0 for value in instance[:3])
    x, y, z = slope
    perpendicular = (
        (0.0, -z, y)
        if abs(y) >= abs(x) and abs(z) >= abs(x)
        else (-z, 0.0, x)
    )
    basis_y = _normalize(perpendicular, reconstruction.zero_length_epsilon)
    basis_x = _cross(basis_y, slope)
    return basis_x, basis_y, slope


def _bake_mesh(
    mesh: dict[str, object],
    batches: list[dict[str, object]],
    shader: dict[str, object],
    origin: tuple[float, float, float],
    reconstruction: object,
) -> tuple[
    list[tuple[float, float, float]],
    list[tuple[float, float, float]],
    list[tuple[float, float]],
    list[tuple[float, float, float, float]],
    list[tuple[int, int, int]],
]:
    source_positions = mesh["positions"]
    source_uvs = mesh["uvs"]
    source_colors = mesh["colors"]
    source_triangles = mesh["triangles"]
    diffuse_direction = tuple(float(value) for value in shader["diffuseDirection"])
    wind_xyz = tuple(float(value) for value in shader["windVectorAndAmplitude"])
    positions: list[tuple[float, float, float]] = []
    normals: list[tuple[float, float, float]] = []
    uvs: list[tuple[float, float]] = []
    colors: list[tuple[float, float, float, float]] = []
    triangles: list[tuple[int, int, int]] = []
    for batch in batches:
        wind_phase = float(batch["windPhase"])
        for instance in batch["instances"]:
            instance = [float(value) for value in instance]
            basis_x, basis_y, slope = _instance_basis(instance, reconstruction)
            scale = reconstruction.scale_base + (
                reconstruction.scale_per_instance * instance[3]
            )
            shade = reconstruction.shade_base + (
                reconstruction.shade_fraction * _fraction(instance[3])
            )
            directional = min(1.0, max(0.0, sum(
                left * right for left, right in zip(diffuse_direction, slope)
            )))
            offset = len(positions)
            for source_position, uv, source_color in zip(
                source_positions,
                source_uvs,
                source_colors,
            ):
                phase = _fraction(
                    (
                        (instance[0] + instance[1])
                        * reconstruction.phase_spatial_scale
                        + wind_phase
                    )
                    * reconstruction.phase_radians_scale
                    + reconstruction.phase_offset
                )
                angle = phase * reconstruction.tau - reconstruction.pi
                wind_amount = math.sin(angle) * wind_xyz[2] * source_color[3] ** 2
                source_world = _add(
                    _mul(basis_x, scale * source_position[0]),
                    _mul(basis_y, scale * source_position[1]),
                    _mul(slope, scale * source_position[2]),
                    (wind_xyz[0] * wind_amount, wind_xyz[1] * wind_amount, 0.0),
                    tuple(instance[:3]),
                )
                positions.append(
                    (
                        source_world[0] - origin[0],
                        source_world[2] - origin[2],
                        -(source_world[1] - origin[1]),
                    )
                )
                converted_slope = _normalize(
                    (slope[0], slope[2], -slope[1]),
                    reconstruction.zero_length_epsilon,
                )
                normals.append(converted_slope)
                uvs.append(uv)
                colors.append(
                    (
                        source_color[0] * shade * directional,
                        source_color[1] * shade * directional,
                        source_color[2] * shade * directional,
                        shade,
                    )
                )
            triangles.extend(
                tuple(offset + index for index in triangle)
                for triangle in source_triangles
            )
    return positions, normals, uvs, colors, triangles


def compiler_provenance() -> dict[str, str]:
    if getattr(sys, "frozen", False):
        executable = Path(sys.executable)
        return {"name": GENERATOR, "sha256": sha256_bytes(executable.read_bytes())}
    root = Path(__file__).resolve().parent
    return {
        "name": GENERATOR,
        "sha256": compiler_sources_sha256([Path(__file__), root / "gltf_io.py"]),
    }


def _write_grass_gltf(
    output_root: Path,
    contract: RetailGrassMeshConfiguration,
    source: dict[str, object],
    observation: dict[str, object],
    texture_id: str,
    origin: tuple[float, float, float],
    compiler: ContentCompilerConfiguration,
) -> dict[str, object]:
    grass = compiler.retail_grass
    batches = observation["meshes"][contract.suffix]
    if source["alphaContract"]["mode"] != grass.material.alpha_mode:
        raise ValueError(f"Owned grass alpha mode changed: {contract.path}")
    alpha_cutoffs = {float(batch["alphaCutoff"]) for batch in batches}
    if len(alpha_cutoffs) != 1:
        raise ValueError(f"Retail grass alpha cutoff changed within {contract.path}")
    alpha_cutoff = next(iter(alpha_cutoffs))
    if not math.isclose(
        alpha_cutoff,
        int(source["alphaThreshold"]) / BYTE_CHANNEL_MAXIMUM,
        rel_tol=0.0,
        abs_tol=grass.shader.float_tolerance,
    ):
        raise ValueError(
            f"Retail grass alpha cutoff differs from owned NIF: {contract.path}"
        )
    positions, normals, uvs, colors, triangles = _bake_mesh(
        source,
        batches,
        observation["shader"],
        origin,
        grass.reconstruction,
    )
    if not positions or not triangles:
        raise ValueError(f"Retail grass overlay is empty for {contract.path}")
    builder = BufferBuilder()
    attributes = {
        "POSITION": builder.add(
            pack_floats(positions),
            component_type=GL_FLOAT,
            count=len(positions),
            value_type="VEC3",
            target=GL_ARRAY_BUFFER,
            minimum=[min(value[axis] for value in positions) for axis in range(3)],
            maximum=[max(value[axis] for value in positions) for axis in range(3)],
        ),
        "NORMAL": builder.add(
            pack_floats(normals),
            component_type=GL_FLOAT,
            count=len(normals),
            value_type="VEC3",
            target=GL_ARRAY_BUFFER,
        ),
        "TEXCOORD_0": builder.add(
            pack_floats(uvs),
            component_type=GL_FLOAT,
            count=len(uvs),
            value_type="VEC2",
            target=GL_ARRAY_BUFFER,
        ),
        "COLOR_0": builder.add(
            pack_floats(colors),
            component_type=GL_FLOAT,
            count=len(colors),
            value_type="VEC4",
            target=GL_ARRAY_BUFFER,
        ),
    }
    index_component = GL_UNSIGNED_SHORT if len(positions) <= GL_UNSIGNED_SHORT_MAX else GL_UNSIGNED_INT
    index_format = "H" if index_component == GL_UNSIGNED_SHORT else "I"
    indices = [index for triangle in triangles for index in triangle]
    index_accessor = builder.add(
        struct.pack(f"<{len(indices)}{index_format}", *indices),
        component_type=index_component,
        count=len(indices),
        value_type="SCALAR",
        target=GL_ELEMENT_ARRAY_BUFFER,
    )
    observation_hash = str(observation["source"]["sha256"])
    render_state_hash = str(
        observation["renderStateObservation"]["source"]["sha256"]
    )
    combined_source_hash = sha256_bytes(
        bytes.fromhex(contract.sha256)
        + bytes.fromhex(observation_hash)
        + bytes.fromhex(render_state_hash)
        + contract.suffix.encode("ascii")
    )
    asset_id = sha256_bytes(
        (combined_source_hash + ":retail-grass-overlay").encode("ascii")
    )[: compiler.asset_id_hex_characters]
    source_stem = PureWindowsPath(contract.path).stem
    surface_name = f"RetailGrass_{contract.suffix}"
    asset_logical_path = f"private-retail-grass\\{source_stem}"
    gltf_path = output_root / f"{asset_id}.gltf"
    sidecar_path = output_root / f"{asset_id}.opennv.json"
    binary_name = gltf_path.with_suffix(".bin").name
    gltf = {
        "asset": {"version": "2.0", "generator": GENERATOR},
        "scene": 0,
        "scenes": [{"nodes": [0]}],
        "nodes": [{"name": surface_name, "mesh": 0}],
        "meshes": [
            {
                "name": surface_name,
                "primitives": [
                    {"attributes": attributes, "indices": index_accessor, "material": 0}
                ],
            }
        ],
        "materials": [
            {
                "name": surface_name,
                "doubleSided": True,
                "alphaMode": grass.material.alpha_mode,
                "alphaCutoff": alpha_cutoff,
                "pbrMetallicRoughness": {
                    "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
                    "metallicFactor": 0.0,
                    "roughnessFactor": 1.0,
                },
            }
        ],
        "buffers": [{"uri": binary_name, "byteLength": len(builder.data)}],
        "bufferViews": builder.views,
        "accessors": builder.accessors,
        "extras": {"openNvSchema": SIDECAR_SCHEMA, "sourceSha256": combined_source_hash},
    }
    gltf_bytes = (json.dumps(gltf, indent=2, sort_keys=True) + "\n").encode()
    binary_bytes = bytes(builder.data)
    atomic_write(gltf_path.with_suffix(".bin"), binary_bytes)
    atomic_write(gltf_path, gltf_bytes)
    provenance = compiler_provenance()
    instance_count = sum(int(batch["instanceCount"]) for batch in batches)
    sidecar = {
        "schema": SIDECAR_SCHEMA,
        "status": "geometry-only",
        "source": {
            "logicalPath": contract.path,
            "bytes": len(source["member"].data),
            "sha256": combined_source_hash,
            "ownedNifSha256": contract.sha256,
            "retailObservationSha256": observation_hash,
            "retailRenderStateObservationSha256": render_state_hash,
            "nifVersion": source["nifVersion"],
            "userVersion": source["userVersion"],
            "userVersion2": source["userVersion2"],
        },
        "compiler": provenance,
        "outputs": {
            "gltf": {
                "file": gltf_path.name,
                "bytes": len(gltf_bytes),
                "sha256": sha256_bytes(gltf_bytes),
            },
            "buffer": {
                "file": binary_name,
                "bytes": len(binary_bytes),
                "sha256": sha256_bytes(binary_bytes),
            },
        },
        "coverage": {
            "surfaces": 1,
            "instances": instance_count,
            "vertices": len(positions),
            "triangles": len(triangles),
            "sourcePoseBakedSkinSurfaces": 0,
            "collisionExported": False,
            "collisionBlockTypes": [],
            "collisionUnsupportedReason": "retail-grass-presentation-only",
            "collisionBodies": [],
            "dynamicPhysicsExported": False,
            "dynamicPhysicsUnsupportedReasons": [],
            "dynamicPhysicsBodies": [],
            "controllers": [],
            "excludedEditorMarkerSurfaces": [],
            "excludedNonPresentationSurfaces": [],
            "presentationClip": None,
            "presentationClipRemovedSurfaces": [],
        },
        "attachmentMarkers": [],
        "surfaces": [
            {
                "stableId": sha256_bytes(
                    f"{combined_source_hash}:{surface_name}".encode("utf-8")
                )[: compiler.stable_id_hex_characters],
                "sourceBlockIndex": 0,
                "name": surface_name,
                "vertices": len(positions),
                "triangles": len(triangles),
                "attributes": sorted(attributes),
                "propertyTypes": source["propertyTypes"],
                "textures": [grass.texture.path],
                "material": {
                    "baseColor": [1.0, 1.0, 1.0],
                    "alpha": 1.0,
                    "glossiness": 0.0,
                    "specular": [0.0, 0.0, 0.0],
                    "emissive": [0.0, 0.0, 0.0],
                    "emissiveControlled": False,
                    "environmentMapScale": 1.0,
                    "textureClampMode": grass.material.texture_clamp_mode,
                    "alphaContract": source["alphaContract"],
                    "vertexColorMode": "color-alpha",
                    "diffuseTexturePresent": True,
                },
                "transformBakedToRoot": True,
                "skinSourcePoseBaked": False,
                "tangentSource": "absent-retail-grass-shader",
                "normalSource": "retail-instance-slope",
                "presentationClip": None,
            }
        ],
    }
    sidecar_bytes = (json.dumps(sidecar, indent=2, sort_keys=True) + "\n").encode()
    atomic_write(sidecar_path, sidecar_bytes)
    shader_contract = {
        "schema": grass.material_schema,
        "model": grass.material_model,
        "sourceFrame": int(observation["source"]["sourceFrame"]),
        **observation["shader"],
        "alphaCutoff": alpha_cutoff,
        "vertexLightingBake": grass.material.vertex_lighting_bake,
        "windBake": grass.material.wind_bake,
    }
    material_binding = {
        "surfaceIndex": 0,
        "name": surface_name,
        "diffuseTextureId": texture_id,
        "diffuseSampleSrgb": False,
        "retailGrassContract": shader_contract,
        "retailLightingContract": None,
        "normalTextureId": None,
        "emissiveTextureId": None,
        "environmentTextureId": None,
        "environmentMaskTextureId": None,
        "environmentMapScale": 1.0,
        "emissiveColor": [0.0, 0.0, 0.0],
        "emissiveReplace": False,
        "baseColorFactor": [1.0, 1.0, 1.0, 1.0],
        "roughness": 1.0,
        "roughnessSource": "retail-tall-grass-unlit-pixel-program",
        "alphaContract": sidecar["surfaces"][0]["material"]["alphaContract"],
        "vertexColorMode": "color-alpha",
        "doubleSided": grass.material.double_sided,
        "unshaded": grass.material.unshaded,
        "shaderFlags1Enabled": ["sf_alpha_texture", "sf_z_buffer_test"],
        "shaderFlags2Enabled": ["sf_2_z_buffer_write"],
        "textureClampMode": grass.material.texture_clamp_mode,
        "decal": False,
        "dynamicDecal": False,
        "lodObjectAtlas": False,
    }
    return {
        "asset": {
            "id": asset_id,
            "logicalPath": asset_logical_path,
            "sourceSha256": combined_source_hash,
            "model": str(gltf_path.resolve()),
            "sidecar": str(sidecar_path.resolve()),
            "surfaces": 1,
            "compiler": provenance,
            "presentationClip": None,
            "collision": {
                "enabled": False,
                "source": "presentation-only",
                "blockTypes": [],
                "unsupportedReason": "retail-grass-presentation-only",
            },
            "physics": {
                "enabled": False,
                "source": "presentation-only",
                "bodies": 0,
                "unsupportedReasons": [],
            },
            "materials": [material_binding],
        },
        "overlay": {
            "id": f"RETAIL_GRASS_{contract.suffix}",
            "assetId": asset_id,
            "logicalPath": asset_logical_path,
            "sourceSha256": combined_source_hash,
            "positionGodotUnits": [0.0, 0.0, 0.0],
            "scale": 1.0,
            "castsShadows": False,
            "selectionReason": "matched-private-retail-GRASS23x002-draw-stream",
            "instances": instance_count,
            "vertices": len(positions),
            "triangles": len(triangles),
        },
    }


def prepare_retail_grass_overlay(
    observation_path: Path,
    render_state_observation_path: Path,
    meshes_path: Path,
    texture_pipeline: TexturePipeline | OwnedTexturePipeline,
    cache_root: Path,
    origin: tuple[float, float, float],
    compiler: ContentCompilerConfiguration,
    owned_archives: OwnedArchiveStack | None = None,
) -> dict[str, object]:
    observation = read_retail_grass_observation(
        observation_path,
        render_state_observation_path,
        compiler.retail_grass,
    )
    archive = owned_archives if owned_archives is not None else BsaArchive(meshes_path)
    grass = compiler.retail_grass
    texture = texture_pipeline.prepare(grass.texture.path)
    output_root = cache_root / "generated" / "cells" / "private-retail-grass" / "assets"
    outputs = []
    observed_contracts = [
        contract
        for contract in sorted(grass.meshes, key=lambda value: value.suffix)
        if contract.suffix in observation["meshes"]
    ]
    for contract in observed_contracts:
        source = _read_owned_mesh(archive, contract, cache_root, grass)
        outputs.append(
            _write_grass_gltf(
                output_root,
                contract,
                source,
                observation,
                texture.asset_id,
                origin,
                compiler,
            )
        )
    return {
        "schema": "opennv-retail-grass-overlay/v1",
        "status": "compiled-private-runtime-observation",
        "observation": observation,
        "assets": [value["asset"] for value in outputs],
        "overlays": [value["overlay"] for value in outputs],
        "textures": [texture.manifest()],
        "coverage": {
            **observation["coverage"],
            "assets": len(outputs),
            "vertices": sum(int(value["overlay"]["vertices"]) for value in outputs),
            "triangles": sum(int(value["overlay"]["triangles"]) for value in outputs),
            "ownedNifs": [
                {
                    "logicalPath": contract.path,
                    "sha256": contract.sha256,
                }
                for contract in observed_contracts
            ],
            "configuredOwnedNifs": [
                {
                    "logicalPath": contract.path,
                    "sha256": contract.sha256,
                }
                for contract in sorted(grass.meshes, key=lambda value: value.suffix)
            ],
            "ownedTexture": {
                "logicalPath": grass.texture.path,
                "artifactId": texture.asset_id,
                "sourceSha256": texture.source_sha256,
            },
        },
    }
