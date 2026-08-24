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


EGT_HEADER_BYTES = 64
EGT_SIGNATURE_BYTES = 8
EGT_HEADER_FIELD_COUNT = 5
EGT_MORPH_SCALE_BYTES = 4
RGB_CHANNEL_COUNT = 3
RGBA_CHANNEL_COUNT = 4
BYTE_CHANNEL_MAXIMUM = 255
SIGNED_DETAIL_NEUTRAL = 128.0
NIF_GEOMETRY_MINIMUM_BYTES = 12
NIF_GEOMETRY_VERTEX_COUNT_OFFSET = 4
NIF_GEOMETRY_UV_FLAG_PREFIX_BYTES = 9
NIF_GEOMETRY_VERTEX_BYTES = 12
NIF_FACEGEN_UV_FLAG = 3
NIF_SINGLE_UV_ARRAY_FLAG = 1
NIF_HEADER_REMAINDER_BYTES = 17
FALLOUT_NIF_VERSION = 0x14020007
FALLOUT_NIF_ENDIAN = 1
FALLOUT_NIF_USER_VERSION = 11
FALLOUT_NIF_USER_VERSION_TWO = 34
NIF_EXPORT_INFO_STRING_COUNT = 3
UINT16_BYTES = 2
UINT32_BYTES = 4
NIF_FOOTER_BYTES = 8

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
    if len(egt_payload) < EGT_HEADER_BYTES or egt_payload[:EGT_SIGNATURE_BYTES] != b"FREGT003":
        raise ValueError("Unexpected FaceGen EGT signature")
    width, height, symmetric_modes, asymmetric_modes, _basis_version = struct.unpack_from(
        f"<{EGT_HEADER_FIELD_COUNT}I", egt_payload, EGT_SIGNATURE_BYTES
    )
    if width <= 0 or height <= 0 or asymmetric_modes != 0:
        raise ValueError(
            f"Unsupported FaceGen EGT dimensions/modes: {width}x{height} "
            f"symmetric={symmetric_modes} asymmetric={asymmetric_modes}"
        )
    if len(weights) != symmetric_modes:
        raise ValueError(f"FaceGen texture mismatch: weights={len(weights)} modes={symmetric_modes}")
    pixels = width * height
    expected = EGT_HEADER_BYTES + symmetric_modes * (
        EGT_MORPH_SCALE_BYTES + pixels * RGB_CHANNEL_COUNT
    )
    if len(egt_payload) != expected:
        raise ValueError(f"FaceGen EGT byte count mismatch: expected={expected} actual={len(egt_payload)}")
    channels = [[SIGNED_DETAIL_NEUTRAL] * pixels for _ in range(RGB_CHANNEL_COUNT)]
    offset = EGT_HEADER_BYTES
    for weight in weights:
        scale = struct.unpack_from("<f", egt_payload, offset)[0]
        offset += EGT_MORPH_SCALE_BYTES
        for channel in channels:
            values = struct.unpack_from(f"<{pixels}b", egt_payload, offset)
            offset += pixels
            if weight != 0.0:
                factor = weight * scale
                for index, value in enumerate(values):
                    channel[index] += factor * value
    output = bytearray(pixels * RGBA_CHANNEL_COUNT)
    for index in range(pixels):
        for channel in range(RGB_CHANNEL_COUNT):
            output[index * RGBA_CHANNEL_COUNT + channel] = max(
                0,
                min(BYTE_CHANNEL_MAXIMUM, round(channels[channel][index])),
            )
        output[index * RGBA_CHANNEL_COUNT + RGB_CHANNEL_COUNT] = BYTE_CHANNEL_MAXIMUM
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
    tone = tuple(4.0 * value / BYTE_CHANNEL_MAXIMUM for value in tone_rgb)
    for pixel in range(base.width * base.height):
        for channel in range(RGB_CHANNEL_COUNT):
            base_value = base_bytes[pixel * RGBA_CHANNEL_COUNT + channel]
            detail_value = detail_bytes[pixel * RGB_CHANNEL_COUNT + channel]
            value = (base_value + 2.0 * (detail_value - SIGNED_DETAIL_NEUTRAL)) * tone[channel]
            output[pixel * RGBA_CHANNEL_COUNT + channel] = max(
                0,
                min(BYTE_CHANNEL_MAXIMUM, round(value)),
            )
        output[pixel * RGBA_CHANNEL_COUNT + RGB_CHANNEL_COUNT] = base_bytes[
            pixel * RGBA_CHANNEL_COUNT + RGB_CHANNEL_COUNT
        ]
    return Image.frombytes("RGBA", base.size, bytes(output))


def compose_body_albedo(diffuse: Image.Image, body_mod: Image.Image) -> Image.Image:
    base = diffuse.convert("RGBA")
    modifier = body_mod.convert("RGB").resize(base.size, Image.Resampling.BILINEAR)
    base_bytes = base.tobytes()
    modifier_bytes = modifier.tobytes()
    output = bytearray(len(base_bytes))
    for pixel in range(base.width * base.height):
        for channel in range(RGB_CHANNEL_COUNT):
            value = (
                base_bytes[pixel * RGBA_CHANNEL_COUNT + channel]
                * modifier_bytes[pixel * RGB_CHANNEL_COUNT + channel]
                / SIGNED_DETAIL_NEUTRAL
            )
            output[pixel * RGBA_CHANNEL_COUNT + channel] = max(
                0,
                min(BYTE_CHANNEL_MAXIMUM, round(value)),
            )
        output[pixel * RGBA_CHANNEL_COUNT + RGB_CHANNEL_COUNT] = base_bytes[
            pixel * RGBA_CHANNEL_COUNT + RGB_CHANNEL_COUNT
        ]
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
        if block_size < NIF_GEOMETRY_MINIMUM_BYTES:
            raise ValueError("FaceGen NiTriShapeData block is truncated")
        vertex_count = struct.unpack_from(
            "<H",
            payload,
            block_offset + NIF_GEOMETRY_VERTEX_COUNT_OFFSET,
        )[0]
        flag_offset = (
            block_offset
            + NIF_GEOMETRY_UV_FLAG_PREFIX_BYTES
            + vertex_count * NIF_GEOMETRY_VERTEX_BYTES
        )
        if flag_offset >= block_offset + block_size:
            raise ValueError("FaceGen NiTriShapeData vertex array exceeds its block")
        if payload[flag_offset] == NIF_FACEGEN_UV_FLAG:
            data[flag_offset] = NIF_SINGLE_UV_ARRAY_FLAG
            repaired.append(flag_offset)
    if not repaired:
        raise ValueError("FaceGen NIF contains no Bethesda one-array UV flag")
    return FaceGenNifRepair(bytes(data), tuple(repaired))


def _frozen_fallout_block_directory(payload: bytes) -> list[tuple[str, int, int]]:
    newline = payload.find(b"\n")
    if newline < 0:
        raise ValueError("NIF has no header line")
    offset = newline + 1
    if offset + NIF_HEADER_REMAINDER_BYTES > len(payload):
        raise ValueError("NIF header is truncated")
    version = struct.unpack_from("<I", payload, offset)[0]
    offset += 4
    endian = payload[offset]
    offset += 1
    user_version, block_count, user_version_2 = struct.unpack_from("<III", payload, offset)
    offset += UINT32_BYTES * 3
    if (version, endian, user_version, user_version_2) != (
        FALLOUT_NIF_VERSION,
        FALLOUT_NIF_ENDIAN,
        FALLOUT_NIF_USER_VERSION,
        FALLOUT_NIF_USER_VERSION_TWO,
    ):
        raise ValueError(
            f"Unexpected Fallout FaceGen NIF header: version={version:08x} endian={endian} "
            f"user={user_version}/{user_version_2}"
        )
    for _ in range(NIF_EXPORT_INFO_STRING_COUNT):
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
    offset += block_count * UINT16_BYTES
    block_sizes = struct.unpack_from(f"<{block_count}I", payload, offset)
    offset += block_count * UINT32_BYTES
    string_count, _maximum_string = struct.unpack_from("<II", payload, offset)
    offset += UINT32_BYTES * 2
    for _ in range(string_count):
        length = struct.unpack_from("<I", payload, offset)[0]
        offset += UINT32_BYTES + length
    group_count = struct.unpack_from("<I", payload, offset)[0]
    offset += UINT32_BYTES + group_count * UINT32_BYTES
    result = []
    for type_index, block_size in zip(type_indices, block_sizes):
        if type_index >= len(block_types) or offset + block_size > len(payload):
            raise ValueError("NIF block directory is invalid")
        result.append((block_types[type_index], offset, block_size))
        offset += block_size
    if len(payload) - offset != NIF_FOOTER_BYTES:
        raise ValueError(f"Unexpected Fallout NIF footer bytes: {len(payload) - offset}")
    return result
