"""Strict owned-data decoders for Fallout FaceGen LIP and TRI animation."""

from __future__ import annotations

import math
import struct
from dataclasses import dataclass

from runtime_configuration import (
    FaceGenAnimationConfiguration,
    FaceGenLipConfiguration,
    FaceGenTriConfiguration,
)


IEEE754_SINGLE_BYTES = struct.calcsize("<f")
SIGNED_DELTA_BYTES = struct.calcsize("<h")


@dataclass(frozen=True)
class FaceGenLipAnimation:
    version: int
    stored_size: int
    flags: int
    frame_count: int
    start_frame: int
    metadata_word: int
    sample_rate_hz: float
    target_names: tuple[str, ...]
    frames: tuple[tuple[float, ...], ...]

    def sample(self, seconds: float) -> tuple[float, ...]:
        neutral = tuple(0.0 for _name in self.target_names)
        if not math.isfinite(seconds) or not self.frames:
            return neutral
        position = seconds * self.sample_rate_hz - self.start_frame
        maximum_position = len(self.frames) - 1
        if position < 0.0 or position > maximum_position:
            return neutral
        lower = int(position)
        upper = min(lower + 1, maximum_position)
        factor = position - lower
        return tuple(
            first + (second - first) * factor
            for first, second in zip(
                self.frames[lower],
                self.frames[upper],
                strict=True,
            )
        )


@dataclass(frozen=True)
class FaceGenDifferentialMorph:
    name: str
    scale: float
    deltas: tuple[tuple[float, float, float], ...]


@dataclass(frozen=True)
class FaceGenStaticMorph:
    name: str
    replacements: tuple[tuple[int, tuple[float, float, float]], ...]


@dataclass(frozen=True)
class FaceGenTri:
    vertex_count: int
    base_vertices: tuple[tuple[float, float, float], ...]
    triangles: tuple[tuple[int, int, int], ...]
    quads: tuple[tuple[int, int, int, int], ...]
    differential_morphs: tuple[FaceGenDifferentialMorph, ...]
    static_morphs: tuple[FaceGenStaticMorph, ...]


class _Cursor:
    def __init__(self, payload: bytes, byte_order: str):
        self.payload = payload
        self.offset = 0
        self.byte_order = byte_order

    @property
    def remaining(self) -> int:
        return len(self.payload) - self.offset

    def read(self, count: int, label: str) -> bytes:
        if count < 0 or count > self.remaining:
            raise ValueError(f"FaceGen {label} is truncated")
        result = self.payload[self.offset : self.offset + count]
        self.offset += count
        return result

    def unsigned(self, width: int, label: str) -> int:
        return int.from_bytes(self.read(width, label), self.byte_order, signed=False)

    def signed(self, width: int, label: str) -> int:
        return int.from_bytes(self.read(width, label), self.byte_order, signed=True)

    def scalar(self, width: int, label: str) -> float:
        if width != IEEE754_SINGLE_BYTES:
            raise ValueError("FaceGen scalar width is unsupported")
        value = struct.unpack("<f", self.read(width, label))[0]
        if not math.isfinite(value):
            raise ValueError(f"FaceGen {label} is non-finite")
        return value


def decode_lip(
    payload: bytes,
    configuration: FaceGenAnimationConfiguration,
) -> FaceGenLipAnimation:
    contract = configuration.lip
    cursor = _Cursor(payload, contract.byte_order)
    header = {
        name: cursor.unsigned(contract.integer_bytes, f"LIP {name}")
        for name in contract.file_header_fields
    }
    version = header["version"]
    stored_size = header["storedSize"]
    flags = header["flags"]
    if version != contract.version:
        raise ValueError(f"Unsupported FaceGen LIP version: {version}")
    if flags & contract.big_endian_flag:
        raise ValueError("Big-endian FaceGen LIP is unsupported")
    supported_flags = contract.compressed_flag | contract.big_endian_flag
    if flags & ~supported_flags:
        raise ValueError(f"FaceGen LIP has unsupported flags: {flags:#x}")
    if (
        stored_size < contract.stored_size_bias_bytes
        or stored_size > contract.maximum_decoded_bytes
    ):
        raise ValueError(f"FaceGen LIP stored size is invalid: {stored_size}")
    expected_decoded_size = stored_size - contract.stored_size_bias_bytes
    if flags & contract.compressed_flag:
        decoded = _decode_zero_runs(cursor, expected_decoded_size, contract)
    else:
        marker = cursor.unsigned(1, "LIP uncompressed marker")
        if marker != contract.uncompressed_marker:
            raise ValueError("FaceGen LIP uncompressed marker is invalid")
        decoded = cursor.read(cursor.remaining, "LIP uncompressed payload")
        if len(decoded) != expected_decoded_size:
            raise ValueError("FaceGen LIP uncompressed payload size differs")
    if cursor.remaining:
        raise ValueError("FaceGen LIP contains unread source bytes")

    decoded_header_size = len(contract.decoded_header_fields) * contract.integer_bytes
    if len(decoded) < decoded_header_size:
        raise ValueError("FaceGen LIP decoded header is truncated")
    decoded_cursor = _Cursor(decoded, contract.byte_order)
    decoded_header: dict[str, int] = {}
    for name in contract.decoded_header_fields:
        decoded_header[name] = (
            decoded_cursor.signed(contract.integer_bytes, f"LIP {name}")
            if name == "startFrame"
            else decoded_cursor.unsigned(contract.integer_bytes, f"LIP {name}")
        )
    frame_count = decoded_header["frameCount"]
    if frame_count <= 0 or frame_count > contract.maximum_frames:
        raise ValueError(f"FaceGen LIP frame count is invalid: {frame_count}")
    required_values = frame_count * len(contract.target_names)
    required_size = decoded_header_size + required_values * contract.value_bytes
    omitted = required_size - len(decoded)
    if omitted not in (0, contract.implicit_trailing_zero_bytes):
        raise ValueError(
            "FaceGen LIP decoded target payload size differs: "
            f"required={required_size} actual={len(decoded)}"
        )
    if omitted:
        decoded += bytes(omitted)
        decoded_cursor = _Cursor(decoded, contract.byte_order)
        decoded_cursor.read(decoded_header_size, "LIP decoded header")
    frames = []
    for _frame in range(frame_count):
        values = tuple(
            decoded_cursor.scalar(contract.value_bytes, "LIP target")
            for _target in contract.target_names
        )
        if any(abs(value) > contract.maximum_absolute_weight for value in values):
            raise ValueError("FaceGen LIP target weight exceeds its contract")
        frames.append(values)
    if decoded_cursor.remaining:
        raise ValueError("FaceGen LIP contains trailing decoded bytes")
    return FaceGenLipAnimation(
        version,
        stored_size,
        flags,
        frame_count,
        decoded_header["startFrame"],
        decoded_header["metadataWord"],
        contract.sample_rate_hz,
        contract.target_names,
        tuple(frames),
    )


def _decode_zero_runs(
    cursor: _Cursor,
    expected_size: int,
    contract: FaceGenLipConfiguration,
) -> bytes:
    result = bytearray()
    while cursor.remaining:
        value = cursor.unsigned(1, "LIP compressed byte")
        if value != contract.run_marker:
            if len(result) >= expected_size:
                raise ValueError("FaceGen LIP compressed payload exceeds its declared size")
            result.append(value)
            continue
        count = cursor.unsigned(contract.run_length_bytes, "LIP zero-run length")
        if count <= 0 or len(result) + count > expected_size:
            raise ValueError("FaceGen LIP contains an invalid zero run")
        result.extend(bytes(count))
    if len(result) != expected_size:
        raise ValueError(
            "FaceGen LIP compressed payload is truncated: "
            f"expected={expected_size} actual={len(result)}"
        )
    return bytes(result)


def decode_tri(
    payload: bytes,
    configuration: FaceGenAnimationConfiguration,
) -> FaceGenTri:
    contract = configuration.tri
    cursor = _Cursor(payload, contract.byte_order)
    signature = cursor.read(len(contract.signature.encode("ascii")), "TRI signature")
    if signature != contract.signature.encode("ascii"):
        raise ValueError(f"Unsupported FaceGen TRI signature: {signature!r}")
    header = {
        name: cursor.signed(contract.integer_bytes, f"TRI {name}")
        for name in contract.header_fields
    }
    count_names = tuple(name for name in contract.header_fields if name != "extensionFlags")
    if any(header[name] < 0 for name in count_names) or header["extensionFlags"] < 0:
        raise ValueError("FaceGen TRI header contains a negative value")
    extension_flags = header["extensionFlags"]
    if extension_flags & ~contract.uv_extension_flag:
        raise ValueError(f"FaceGen TRI has unsupported extension flags: {extension_flags:#x}")
    cursor.read(contract.reserved_bytes, "TRI reserved header")

    vertex_count = header["vertexCount"]
    added_vertex_count = header["addedVertexCount"]
    vertices = _read_vectors(
        cursor,
        vertex_count + added_vertex_count,
        contract.position_components,
        contract.scalar_bytes,
        "TRI vertex",
    )
    triangles = _read_index_rows(
        cursor,
        header["triangleCount"],
        contract.triangle_indices,
        contract.integer_bytes,
        vertex_count,
        "TRI triangle",
    )
    quads = _read_index_rows(
        cursor,
        header["quadCount"],
        contract.quad_indices,
        contract.integer_bytes,
        vertex_count,
        "TRI quad",
    )

    for _label in range(header["labelledVertexCount"]):
        cursor.read(contract.labelled_vertex_prefix_bytes, "TRI labelled-vertex prefix")
        _read_facegen_string(cursor, contract)
    for _label in range(header["labelledSurfaceCount"]):
        cursor.read(contract.labelled_surface_prefix_bytes, "TRI labelled-surface prefix")
        _read_facegen_string(cursor, contract)

    if extension_flags & contract.uv_extension_flag:
        uv_vertex_count = header["uvVertexCount"]
        authored_uv_count = uv_vertex_count or vertex_count
        _read_vectors(
            cursor,
            authored_uv_count,
            contract.uv_components,
            contract.scalar_bytes,
            "TRI UV",
        )
        if uv_vertex_count:
            _read_index_rows(
                cursor,
                header["triangleCount"],
                contract.triangle_indices,
                contract.integer_bytes,
                uv_vertex_count,
                "TRI UV triangle",
            )
            _read_index_rows(
                cursor,
                header["quadCount"],
                contract.quad_indices,
                contract.integer_bytes,
                uv_vertex_count,
                "TRI UV quad",
            )

    used_names: set[str] = set()
    differential_morphs = []
    for _morph in range(header["differentialMorphCount"]):
        name = _unique_morph_name(cursor, contract, used_names)
        scale = cursor.scalar(contract.scalar_bytes, f"TRI {name} scale")
        if contract.delta_component_bytes != SIGNED_DELTA_BYTES:
            raise ValueError("FaceGen TRI delta width is unsupported")
        deltas = []
        for _vertex in range(vertex_count):
            delta = tuple(
                cursor.signed(contract.delta_component_bytes, f"TRI {name} delta") * scale
                for _axis in range(contract.position_components)
            )
            if len(delta) != contract.position_components or not all(
                math.isfinite(value) for value in delta
            ):
                raise ValueError(f"FaceGen TRI morph {name!r} has invalid deltas")
            deltas.append(delta)
        differential_morphs.append(FaceGenDifferentialMorph(name, scale, tuple(deltas)))

    added_offset = vertex_count
    static_morphs = []
    for _morph in range(header["staticMorphCount"]):
        name = _unique_morph_name(cursor, contract, used_names)
        index_count = cursor.signed(contract.integer_bytes, f"TRI {name} index count")
        if index_count < 0 or added_offset + index_count > len(vertices):
            raise ValueError(f"FaceGen TRI static morph {name!r} has invalid replacement count")
        replacements = []
        replacement_indices: set[int] = set()
        for replacement_offset in range(index_count):
            vertex_index = cursor.signed(contract.integer_bytes, f"TRI {name} vertex index")
            if (
                vertex_index < 0
                or vertex_index >= vertex_count
                or vertex_index in replacement_indices
            ):
                raise ValueError(f"FaceGen TRI static morph {name!r} has an invalid vertex index")
            replacement_indices.add(vertex_index)
            replacements.append((vertex_index, vertices[added_offset + replacement_offset]))
        added_offset += index_count
        static_morphs.append(FaceGenStaticMorph(name, tuple(replacements)))
    if added_offset != len(vertices):
        raise ValueError("FaceGen TRI leaves added vertices unassigned")
    if cursor.remaining:
        raise ValueError(f"FaceGen TRI contains trailing bytes: {cursor.remaining}")
    return FaceGenTri(
        vertex_count,
        tuple(vertices[:vertex_count]),
        tuple(triangles),
        tuple(quads),
        tuple(differential_morphs),
        tuple(static_morphs),
    )


def _read_vectors(
    cursor: _Cursor,
    count: int,
    components: int,
    scalar_bytes: int,
    label: str,
) -> list[tuple[float, ...]]:
    required = count * components * scalar_bytes
    if required > cursor.remaining:
        raise ValueError(f"FaceGen {label} array is truncated")
    return [
        tuple(cursor.scalar(scalar_bytes, label) for _axis in range(components))
        for _row in range(count)
    ]


def _read_index_rows(
    cursor: _Cursor,
    count: int,
    indices_per_row: int,
    integer_bytes: int,
    upper_bound: int,
    label: str,
) -> list[tuple[int, ...]]:
    required = count * indices_per_row * integer_bytes
    if required > cursor.remaining:
        raise ValueError(f"FaceGen {label} array is truncated")
    rows = [
        tuple(cursor.unsigned(integer_bytes, label) for _index in range(indices_per_row))
        for _row in range(count)
    ]
    if any(index >= upper_bound for row in rows for index in row):
        raise ValueError(f"FaceGen {label} array has an invalid index")
    return rows


def _read_facegen_string(cursor: _Cursor, contract: FaceGenTriConfiguration) -> str:
    length = cursor.signed(contract.integer_bytes, "TRI string length")
    if length < 0:
        raise ValueError("FaceGen TRI string has a negative length")
    payload = cursor.read(length, "TRI string")
    return payload.rstrip(b"\0").decode("utf-8", errors="strict")


def _unique_morph_name(
    cursor: _Cursor,
    contract: FaceGenTriConfiguration,
    used_names: set[str],
) -> str:
    name = _read_facegen_string(cursor, contract)
    if not name or name in used_names:
        raise ValueError(f"FaceGen TRI morph name is empty or repeated: {name!r}")
    used_names.add(name)
    return name
