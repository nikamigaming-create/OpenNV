"""Direct FaceGen geometry and texture synthesis for Fallout actor assets."""

from __future__ import annotations

import io
import struct
import time
from dataclasses import dataclass

if not hasattr(time, "clock"):
    time.clock = time.perf_counter

from PIL import Image
from pyffi.formats.egm import EgmFormat  # type: ignore  # noqa: E402


@dataclass(frozen=True)
class FaceGenNifRepair:
    payload: bytes
    uv_flag_offsets: tuple[int, ...]


def apply_geometry_morphs(
    positions: list[tuple[float, float, float]],
    egm_payload: bytes,
    symmetric_weights: tuple[float, ...],
    asymmetric_weights: tuple[float, ...],
    *,
    vertex_offset: int = 0,
) -> list[tuple[float, float, float]]:
    data = EgmFormat.Data()
    data.read(io.BytesIO(egm_payload))
    if len(symmetric_weights) != len(data.sym_morphs):
        raise ValueError(
            f"FaceGen symmetric geometry mismatch: weights={len(symmetric_weights)} modes={len(data.sym_morphs)}"
        )
    if len(asymmetric_weights) != len(data.asym_morphs):
        raise ValueError(
            f"FaceGen asymmetric geometry mismatch: weights={len(asymmetric_weights)} modes={len(data.asym_morphs)}"
        )
    if vertex_offset < 0 or vertex_offset + len(positions) > data.header.num_vertices:
        raise ValueError(
            f"FaceGen geometry range exceeds EGM vertices: offset={vertex_offset} "
            f"count={len(positions)} egm={data.header.num_vertices}"
        )
    result = [list(position) for position in positions]
    for morph, weight in zip(data.sym_morphs, symmetric_weights):
        if weight == 0.0:
            continue
        for index, delta in enumerate(morph.get_relative_vertices()):
            if index < vertex_offset:
                continue
            local_index = index - vertex_offset
            if local_index >= len(result):
                break
            for axis in range(3):
                result[local_index][axis] += weight * delta[axis]
    for morph, weight in zip(data.asym_morphs, asymmetric_weights):
        if weight == 0.0:
            continue
        for index, delta in enumerate(morph.get_relative_vertices()):
            if index < vertex_offset:
                continue
            local_index = index - vertex_offset
            if local_index >= len(result):
                break
            for axis in range(3):
                result[local_index][axis] += weight * delta[axis]
    return [tuple(position) for position in result]


def synthesize_texture_detail(egt_payload: bytes, weights: tuple[float, ...]) -> Image.Image:
    if len(egt_payload) < 64 or egt_payload[:8] != b"FREGT003":
        raise ValueError("Unexpected FaceGen EGT signature")
    width, height, symmetric_modes, asymmetric_modes, _basis_version = struct.unpack_from(
        "<5I", egt_payload, 8
    )
    if width <= 0 or height <= 0 or asymmetric_modes != 0:
        raise ValueError(
            f"Unsupported FaceGen EGT dimensions/modes: {width}x{height} "
            f"symmetric={symmetric_modes} asymmetric={asymmetric_modes}"
        )
    if len(weights) != symmetric_modes:
        raise ValueError(f"FaceGen texture mismatch: weights={len(weights)} modes={symmetric_modes}")
    pixels = width * height
    expected = 64 + symmetric_modes * (4 + pixels * 3)
    if len(egt_payload) != expected:
        raise ValueError(f"FaceGen EGT byte count mismatch: expected={expected} actual={len(egt_payload)}")
    channels = [[128.0] * pixels for _ in range(3)]
    offset = 64
    for weight in weights:
        scale = struct.unpack_from("<f", egt_payload, offset)[0]
        offset += 4
        for channel in channels:
            values = struct.unpack_from(f"<{pixels}b", egt_payload, offset)
            offset += pixels
            if weight != 0.0:
                factor = weight * scale
                for index, value in enumerate(values):
                    channel[index] += factor * value
    output = bytearray(pixels * 4)
    for index in range(pixels):
        for channel in range(3):
            output[index * 4 + channel] = max(0, min(255, round(channels[channel][index])))
        output[index * 4 + 3] = 255
    return Image.frombytes("RGBA", (width, height), bytes(output))


def compose_skin_albedo(
    diffuse: Image.Image,
    face_detail: Image.Image,
    tone_rgb: tuple[int, int, int],
) -> Image.Image:
    base = diffuse.convert("RGBA")
    detail = face_detail.convert("RGB").resize(base.size, Image.Resampling.LANCZOS)
    base_bytes = base.tobytes()
    detail_bytes = detail.tobytes()
    output = bytearray(len(base_bytes))
    tone = tuple(4.0 * value / 255.0 for value in tone_rgb)
    for pixel in range(base.width * base.height):
        for channel in range(3):
            base_value = base_bytes[pixel * 4 + channel]
            detail_value = detail_bytes[pixel * 3 + channel]
            value = (base_value + 2.0 * (detail_value - 128.0)) * tone[channel]
            output[pixel * 4 + channel] = max(0, min(255, round(value)))
        output[pixel * 4 + 3] = base_bytes[pixel * 4 + 3]
    return Image.frombytes("RGBA", base.size, bytes(output))


def compose_body_albedo(diffuse: Image.Image, body_mod: Image.Image) -> Image.Image:
    base = diffuse.convert("RGBA")
    modifier = body_mod.convert("RGB").resize(base.size, Image.Resampling.BILINEAR)
    base_bytes = base.tobytes()
    modifier_bytes = modifier.tobytes()
    output = bytearray(len(base_bytes))
    for pixel in range(base.width * base.height):
        for channel in range(3):
            value = base_bytes[pixel * 4 + channel] * modifier_bytes[pixel * 3 + channel] / 128.0
            output[pixel * 4 + channel] = max(0, min(255, round(value)))
        output[pixel * 4 + 3] = base_bytes[pixel * 4 + 3]
    return Image.frombytes("RGBA", base.size, bytes(output))


def repair_facegen_nif_uv_flag(payload: bytes) -> FaceGenNifRepair:
    """Repair Bethesda's FaceGen-only UV flag value 3 for the generic NIF reader.

    The FaceGen head stores one UV array but marks the byte as 3. FNV block sizes
    and the following triangle fields prove only one array is present. The source
    bytes are never modified on disk; the returned parse buffer changes that one
    byte to the generic NiGeometryData value 1.
    """

    data = bytearray(payload)
    blocks = _frozen_fallout_block_directory(payload)
    repaired = []
    for block_type, block_offset, block_size in blocks:
        if block_type != "NiTriShapeData":
            continue
        if block_size < 12:
            raise ValueError("FaceGen NiTriShapeData block is truncated")
        vertex_count = struct.unpack_from("<H", payload, block_offset + 4)[0]
        flag_offset = block_offset + 9 + vertex_count * 12
        if flag_offset >= block_offset + block_size:
            raise ValueError("FaceGen NiTriShapeData vertex array exceeds its block")
        if payload[flag_offset] == 3:
            data[flag_offset] = 1
            repaired.append(flag_offset)
    if not repaired:
        raise ValueError("FaceGen NIF contains no Bethesda one-array UV flag")
    return FaceGenNifRepair(bytes(data), tuple(repaired))


def _frozen_fallout_block_directory(payload: bytes) -> list[tuple[str, int, int]]:
    newline = payload.find(b"\n")
    if newline < 0:
        raise ValueError("NIF has no header line")
    offset = newline + 1
    if offset + 17 > len(payload):
        raise ValueError("NIF header is truncated")
    version = struct.unpack_from("<I", payload, offset)[0]
    offset += 4
    endian = payload[offset]
    offset += 1
    user_version, block_count, user_version_2 = struct.unpack_from("<III", payload, offset)
    offset += 12
    if (version, endian, user_version, user_version_2) != (0x14020007, 1, 11, 34):
        raise ValueError(
            f"Unexpected Fallout FaceGen NIF header: version={version:08x} endian={endian} "
            f"user={user_version}/{user_version_2}"
        )
    for _ in range(3):
        if offset >= len(payload):
            raise ValueError("NIF export-info string is truncated")
        length = payload[offset]
        offset += 1 + length
    type_count = struct.unpack_from("<H", payload, offset)[0]
    offset += 2
    block_types = []
    for _ in range(type_count):
        length = struct.unpack_from("<I", payload, offset)[0]
        offset += 4
        block_types.append(payload[offset : offset + length].decode("ascii"))
        offset += length
    type_indices = struct.unpack_from(f"<{block_count}H", payload, offset)
    offset += block_count * 2
    block_sizes = struct.unpack_from(f"<{block_count}I", payload, offset)
    offset += block_count * 4
    string_count, _maximum_string = struct.unpack_from("<II", payload, offset)
    offset += 8
    for _ in range(string_count):
        length = struct.unpack_from("<I", payload, offset)[0]
        offset += 4 + length
    group_count = struct.unpack_from("<I", payload, offset)[0]
    offset += 4 + group_count * 4
    result = []
    for type_index, block_size in zip(type_indices, block_sizes):
        if type_index >= len(block_types) or offset + block_size > len(payload):
            raise ValueError("NIF block directory is invalid")
        result.append((block_types[type_index], offset, block_size))
        offset += block_size
    if len(payload) - offset != 8:
        raise ValueError(f"Unexpected Fallout NIF footer bytes: {len(payload) - offset}")
    return result
