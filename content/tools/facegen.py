"""Direct FaceGen geometry and texture synthesis for Fallout actor assets."""

from __future__ import annotations

import io
import math
import struct
import time

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
def compose_facegen_coordinates(
    actor_coordinates: tuple[float, ...],
    race_baseline_coordinates: tuple[float, ...],
) -> tuple[float, ...]:
    """Compose an NPC identity channel with its sex-specific RACE baseline."""

    if len(actor_coordinates) != len(race_baseline_coordinates):
        raise ValueError(
            "FaceGen coordinate count mismatch: "
            f"actor={len(actor_coordinates)} race={len(race_baseline_coordinates)}"
        )
    coordinates = tuple(
        actor_value + race_value
        for actor_value, race_value in zip(actor_coordinates, race_baseline_coordinates)
    )
    if not all(math.isfinite(value) for value in coordinates):
        raise ValueError("FaceGen coordinate composition produced a non-finite value")
    return coordinates


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
