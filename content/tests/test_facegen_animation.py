from __future__ import annotations

import struct
import sys
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from facegen_animation import decode_lip, decode_tri  # noqa: E402
from runtime_configuration import load_runtime_configuration  # noqa: E402


class FaceGenAnimationTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.configuration = (
            load_runtime_configuration().content_compiler.facegen_animation
        )

    def test_compressed_lip_decodes_implicit_tail_and_samples_linearly(self) -> None:
        contract = self.configuration.lip
        first = tuple(0.0 for _name in contract.target_names)
        second = tuple(
            1.0 if index == 0 else 0.0
            for index, _name in enumerate(contract.target_names)
        )
        decoded = (
            struct.pack("<IiI", 2, 0, 0x12345678)
            + struct.pack(f"<{len(first)}f", *first)
            + struct.pack(f"<{len(second)}f", *second)
        )
        authored = decoded[: -contract.implicit_trailing_zero_bytes]
        payload = (
            struct.pack(
                "<III",
                contract.version,
                len(authored) + contract.stored_size_bias_bytes,
                contract.compressed_flag,
            )
            + self._compress_zero_runs(authored)
        )

        animation = decode_lip(payload, self.configuration)

        self.assertEqual(animation.frame_count, 2)
        self.assertEqual(animation.metadata_word, 0x12345678)
        self.assertAlmostEqual(
            animation.sample(0.5 / contract.sample_rate_hz)[0],
            0.5,
        )
        self.assertEqual(
            animation.sample(2.0 / contract.sample_rate_hz),
            tuple(0.0 for _name in contract.target_names),
        )

    def test_lip_rejects_truncated_zero_run(self) -> None:
        contract = self.configuration.lip
        payload = struct.pack(
            "<III",
            contract.version,
            contract.stored_size_bias_bytes + len(contract.decoded_header_fields),
            contract.compressed_flag,
        ) + bytes((contract.run_marker,))
        with self.assertRaisesRegex(ValueError, "zero-run length"):
            decode_lip(payload, self.configuration)

    def test_lip_rejects_partial_implicit_tail(self) -> None:
        contract = self.configuration.lip
        decoded = struct.pack("<IiI", 1, 0, 0) + struct.pack(
            f"<{len(contract.target_names)}f",
            *(0.0 for _name in contract.target_names),
        )
        partial_tail = contract.implicit_trailing_zero_bytes // 2
        authored = decoded[:-partial_tail]
        payload = struct.pack(
            "<III",
            contract.version,
            len(authored) + contract.stored_size_bias_bytes,
            contract.compressed_flag,
        ) + self._compress_zero_runs(authored)
        with self.assertRaisesRegex(ValueError, "payload size differs"):
            decode_lip(payload, self.configuration)

    def test_tri_decodes_differential_and_static_morphs(self) -> None:
        contract = self.configuration.tri
        header = {
            "vertexCount": 3,
            "triangleCount": 1,
            "quadCount": 0,
            "labelledVertexCount": 0,
            "labelledSurfaceCount": 0,
            "uvVertexCount": 0,
            "extensionFlags": contract.uv_extension_flag,
            "differentialMorphCount": 1,
            "staticMorphCount": 1,
            "addedVertexCount": 1,
        }
        vertices = (
            (0.0, 0.0, 0.0),
            (1.0, 0.0, 0.0),
            (0.0, 1.0, 0.0),
            (0.0, 0.0, 1.0),
        )
        payload = bytearray(contract.signature.encode("ascii"))
        payload.extend(
            struct.pack(
                f"<{len(contract.header_fields)}i",
                *(header[name] for name in contract.header_fields),
            )
        )
        payload.extend(bytes(contract.reserved_bytes))
        payload.extend(struct.pack(f"<{len(vertices) * 3}f", *(v for row in vertices for v in row)))
        payload.extend(struct.pack("<3I", 0, 1, 2))
        payload.extend(struct.pack("<6f", 0.0, 0.0, 1.0, 0.0, 0.0, 1.0))
        payload.extend(self._facegen_string("Aah"))
        payload.extend(struct.pack("<f", 0.5))
        payload.extend(struct.pack("<9h", 2, 0, 0, 0, 2, 0, 0, 0, 2))
        payload.extend(self._facegen_string("BlinkLeft"))
        payload.extend(struct.pack("<ii", 1, 2))

        tri = decode_tri(bytes(payload), self.configuration)

        self.assertEqual(tri.vertex_count, 3)
        self.assertEqual(tri.triangles, ((0, 1, 2),))
        self.assertEqual(tri.differential_morphs[0].name, "Aah")
        self.assertEqual(tri.differential_morphs[0].deltas[0], (1.0, 0.0, 0.0))
        self.assertEqual(
            tri.static_morphs[0].replacements,
            ((2, (0.0, 0.0, 1.0)),),
        )

    def test_tri_rejects_unassigned_added_vertices(self) -> None:
        contract = self.configuration.tri
        header = {name: 0 for name in contract.header_fields}
        header["addedVertexCount"] = 1
        payload = bytearray(contract.signature.encode("ascii"))
        payload.extend(
            struct.pack(
                f"<{len(contract.header_fields)}i",
                *(header[name] for name in contract.header_fields),
            )
        )
        payload.extend(bytes(contract.reserved_bytes))
        payload.extend(struct.pack("<3f", 0.0, 0.0, 0.0))
        with self.assertRaisesRegex(ValueError, "unassigned"):
            decode_tri(bytes(payload), self.configuration)

    def _compress_zero_runs(self, payload: bytes) -> bytes:
        contract = self.configuration.lip
        result = bytearray()
        offset = 0
        maximum_run = (1 << (contract.run_length_bytes * 8)) - 1
        while offset < len(payload):
            if payload[offset] != contract.run_marker:
                result.append(payload[offset])
                offset += 1
                continue
            end = offset
            while (
                end < len(payload)
                and payload[end] == contract.run_marker
                and end - offset < maximum_run
            ):
                end += 1
            result.append(contract.run_marker)
            result.extend((end - offset).to_bytes(contract.run_length_bytes, "little"))
            offset = end
        return bytes(result)

    @staticmethod
    def _facegen_string(value: str) -> bytes:
        payload = value.encode("utf-8") + b"\0"
        return struct.pack("<i", len(payload)) + payload


if __name__ == "__main__":
    unittest.main()
