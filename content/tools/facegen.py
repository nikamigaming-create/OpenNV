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
EGT_HEADER_FIELD_COUNT = 3
EGT_TEXTURE_CONTROL_BYTES = 4
EGT_TEXTURE_FLAG_INTENSITY_MASK = 0x03
EGT_TEXTURE_FLAG_ENABLE = 0x04
EGT_TEXTURE_FLAG_SLOT_SHIFT = 3
EGT_TEXTURE_FLAG_SLOT_MASK = 0x07
EGT_TEXTURE_FLAG_MAXED = 0x40
EGT_TEXTURE_FLAG_INVERT = 0x80
EGT_FACE_TEXTURE_SLOT = 7
# Owned FNV precomputed FaceGen detail textures close against the EGT packed
# intensity ladder.  Each step increases signed-byte contribution by 4x.
EGT_INTENSITY_SCALES = (1.0 / 128.0, 1.0 / 32.0, 1.0 / 8.0, 1.0 / 2.0)
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


def _combine_geometry_basis_deltas(
    basis_deltas: tuple[tuple[tuple[float, float, float], ...], ...],
    control_axes: tuple[tuple[float, ...], ...],
    *,
    vertex_offset: int,
    vertex_count: int,
) -> tuple[tuple[tuple[float, float, float], ...], ...]:
    """Compose linear FaceGen control axes into per-vertex EGM deltas."""
    if vertex_offset < 0 or vertex_count <= 0:
        raise ValueError("FaceGen control geometry range is invalid")
    if not basis_deltas:
        raise ValueError("FaceGen control geometry has no symmetric basis")
    source_vertex_count = len(basis_deltas[0])
    if any(len(morph) != source_vertex_count for morph in basis_deltas):
        raise ValueError("FaceGen control geometry basis vertex counts differ")
    if vertex_offset + vertex_count > source_vertex_count:
        raise ValueError(
            "FaceGen control geometry range exceeds EGM vertices: "
            f"offset={vertex_offset} count={vertex_count} egm={source_vertex_count}"
        )
    results = []
    for control_index, axis in enumerate(control_axes):
        if len(axis) != len(basis_deltas):
            raise ValueError(
                "FaceGen control axis differs from the EGM basis: "
                f"control={control_index} axis={len(axis)} basis={len(basis_deltas)}"
            )
        if not all(math.isfinite(value) for value in axis):
            raise ValueError(f"FaceGen control axis {control_index} is non-finite")
        rows = []
        for vertex_index in range(vertex_offset, vertex_offset + vertex_count):
            rows.append(
                tuple(
                    sum(
                        axis[basis_index] * basis_deltas[basis_index][vertex_index][component]
                        for basis_index in range(len(basis_deltas))
                    )
                    for component in range(3)
                )
            )
        results.append(tuple(rows))
    return tuple(results)


def facegen_geometry_control_deltas(
    egm_payload: bytes,
    control_axes: tuple[tuple[float, ...], ...],
    *,
    vertex_offset: int,
    vertex_count: int,
) -> tuple[tuple[tuple[float, float, float], ...], ...]:
    """Decode exact owned EGM modes and compose runtime slider target deltas."""
    data = EgmFormat.Data()
    data.read(io.BytesIO(egm_payload))
    basis = tuple(
        tuple(
            tuple(float(component) for component in delta)
            for delta in morph.get_relative_vertices()
        )
        for morph in data.sym_morphs
    )
    return _combine_geometry_basis_deltas(
        basis,
        control_axes,
        vertex_offset=vertex_offset,
        vertex_count=vertex_count,
    )


def synthesize_texture_detail(egt_payload: bytes, weights: tuple[float, ...]) -> Image.Image:
    if len(egt_payload) < EGT_HEADER_BYTES or egt_payload[:EGT_SIGNATURE_BYTES] != b"FREGT003":
        raise ValueError("Unexpected FaceGen EGT signature")
    width, height, texture_modes = struct.unpack_from(
        f"<{EGT_HEADER_FIELD_COUNT}I", egt_payload, EGT_SIGNATURE_BYTES
    )
    if width <= 0 or height <= 0 or texture_modes <= 0:
        raise ValueError(
            f"Unsupported FaceGen EGT dimensions/modes: {width}x{height} "
            f"textures={texture_modes}"
        )
    if len(weights) != texture_modes:
        raise ValueError(f"FaceGen texture mismatch: weights={len(weights)} modes={texture_modes}")
    pixels = width * height
    expected = EGT_HEADER_BYTES + texture_modes * (
        EGT_TEXTURE_CONTROL_BYTES + pixels * RGB_CHANNEL_COUNT
    )
    if len(egt_payload) != expected:
        raise ValueError(f"FaceGen EGT byte count mismatch: expected={expected} actual={len(egt_payload)}")
    channels = [[SIGNED_DETAIL_NEUTRAL] * pixels for _ in range(RGB_CHANNEL_COUNT)]
    offset = EGT_HEADER_BYTES
    for weight in weights:
        _unknown_1, _unknown_2, _unknown_3, flags = struct.unpack_from(
            "<4B", egt_payload, offset
        )
        offset += EGT_TEXTURE_CONTROL_BYTES
        intensity = flags & EGT_TEXTURE_FLAG_INTENSITY_MASK
        enabled = flags & EGT_TEXTURE_FLAG_ENABLE
        slot = (flags >> EGT_TEXTURE_FLAG_SLOT_SHIFT) & EGT_TEXTURE_FLAG_SLOT_MASK
        maxed = flags & EGT_TEXTURE_FLAG_MAXED
        inverted = flags & EGT_TEXTURE_FLAG_INVERT
        if slot != EGT_FACE_TEXTURE_SLOT or maxed:
            raise ValueError(
                "Unsupported FaceGen EGT texture flags: "
                f"slot={slot} maxed={bool(maxed)} flags=0x{flags:02x}"
            )
        scale = EGT_INTENSITY_SCALES[intensity]
        if inverted:
            scale = -scale
        for channel in channels:
            values = struct.unpack_from(f"<{pixels}b", egt_payload, offset)
            offset += pixels
            if enabled and weight != 0.0:
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
    # EGT scanlines use the opposite vertical origin from the NIF UVs and the
    # decoded DDS/PNG texture boundary.  Retail-precomputed FaceGen details
    # arrive in the latter orientation, so direct synthesis must normalize to
    # that same contract before the image is hashed and emitted.
    return Image.frombytes("RGBA", (width, height), bytes(output)).transpose(
        Image.Transpose.FLIP_TOP_BOTTOM
    )


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
