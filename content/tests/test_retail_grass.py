from __future__ import annotations

import inspect
import json
import math
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from prepare_exterior_cell import prepare  # noqa: E402
from retail_grass import (  # noqa: E402
    _instance_basis,
    active_instance_count,
    read_retail_grass_observation,
)
from runtime_configuration import load_runtime_configuration  # noqa: E402


GRASS = load_runtime_configuration().content_compiler.retail_grass


def _flat_registers(count: int) -> tuple[list[list[float]], list[float]]:
    registers = [[0.0, 0.0, 0.0, 0.0] for _ in range(count)]
    return registers, [component for register in registers for component in register]


def _observation_record(mesh, ordinal: int) -> dict[str, object]:
    shader = GRASS.shader
    registers = shader.registers
    vertex, _unused = _flat_registers(shader.vertex_constant_register_count)
    vertex[registers["diffuseDirection"]][:3] = [0.0, 0.6, 0.8]
    vertex[registers["diffuseColor"]][:3] = [1.0, 0.9, 0.7]
    vertex[registers["scaleMask"]][:3] = [1.0, 1.0, 1.0]
    vertex[registers["wind"]] = [0.0, 1.0, 28.5, ordinal * 0.125]
    vertex[registers["fade"]][2:4] = [7000.0, 1000.0]
    vertex[registers["ambientColor"]][:3] = [0.38, 0.47, 0.60]
    vertex[registers["directionalScale"]][0] = 1.5
    vertex[registers["fogColor"]][:3] = [0.58, 0.65, 0.74]
    vertex[registers["fog"]][:3] = [120000.0, 119990.0, 0.5]
    vertex[registers["instanceCeiling"]][
        registers["instanceCeilingComponent"]
    ] = shader.instance_register_ceiling
    vertex[shader.instance_first_register] = [
        100.5 + ordinal,
        200.8,
        300.9,
        10.25,
    ]
    pixel, _unused = _flat_registers(shader.pixel_constant_register_count)
    pixel[registers["alphaCutoff"]][0] = 0.5
    return {
        "ordinal": ordinal,
        "drawMethod": "DrawIndexedPrimitive",
        "primitiveType": GRASS.draw.primitive_type,
        "baseVertexIndex": 0,
        "minimumVertexIndex": 0,
        "startIndex": 0,
        "vertexCount": mesh.source_vertices * shader.instance_capacity,
        "primitiveCount": mesh.strip_length + 1,
        "vertexShader": {"fnv1a32": shader.vertex_fnv1a32},
        "pixelShader": {"fnv1a32": shader.pixel_fnv1a32},
        "vertexDeclaration": {
            "getResult": 0,
            "getElementsResult": 0,
            "elements": [
                {
                    key: value
                    for key, value in zip(
                        ("stream", "offset", "type", "method", "usage", "usageIndex"),
                        declaration,
                    )
                }
                for declaration in GRASS.draw.declaration
            ],
        },
        "vertexBuffer": {"stride": GRASS.draw.vertex_stride_bytes},
        "sampler": {
            key: value
            for key, value in GRASS.draw.sampler.items()
            if key != "srgbWrite"
        },
        "colorSpaceState": {"srgbWrite": GRASS.draw.sampler["srgbWrite"]},
        "renderState": {
            **GRASS.draw.render_state,
            **{key + "Result": 0 for key in GRASS.draw.render_state},
        },
        "vertexConstants": {
            "getResult": 0,
            "registerCount": shader.vertex_constant_register_count,
            "values": [component for register in vertex for component in register],
        },
        "pixelConstants": {
            "getResult": 0,
            "registerCount": shader.pixel_constant_register_count,
            "values": [component for register in pixel for component in register],
        },
    }


class RetailGrassTest(unittest.TestCase):
    def test_active_instance_count_covers_regular_and_full_batch_draws(self) -> None:
        strip_length = 61
        step = strip_length + GRASS.draw.strip_bridge_indices
        bias = GRASS.draw.primitive_count_bias
        penultimate = GRASS.shader.instance_capacity - 1
        self.assertEqual(active_instance_count(step - bias, strip_length, GRASS), 1)
        self.assertEqual(
            active_instance_count(penultimate * step - bias, strip_length, GRASS),
            penultimate,
        )
        self.assertEqual(
            active_instance_count(
                GRASS.shader.instance_capacity * step
                - GRASS.draw.full_batch_trailing_bridge_indices,
                strip_length,
                GRASS,
            ),
            GRASS.shader.instance_capacity,
        )
        with self.assertRaisesRegex(ValueError, "does not map"):
            active_instance_count(1, strip_length, GRASS)

    def test_instance_basis_reconstructs_encoded_slope(self) -> None:
        basis_x, basis_y, slope = _instance_basis(
            [10.5, 20.8, 30.9, 0.0],
            GRASS.reconstruction,
        )
        expected_slope = (0.0, 0.6, 0.8)
        for actual, expected in zip(slope, expected_slope):
            self.assertAlmostEqual(actual, expected)
        for basis in (basis_x, basis_y, slope):
            self.assertAlmostEqual(math.sqrt(sum(value * value for value in basis)), 1.0)
        self.assertAlmostEqual(sum(x * y for x, y in zip(basis_x, slope)), 0.0)
        self.assertAlmostEqual(sum(x * y for x, y in zip(basis_y, slope)), 0.0)

    def test_observation_parser_requires_and_recovers_every_owned_mesh(self) -> None:
        meshes = sorted(GRASS.meshes, key=lambda value: value.suffix)
        records = [_observation_record(mesh, index + 1) for index, mesh in enumerate(meshes)]
        event = {
            "event": "texture-sampler-contract",
            "sourceFrame": 70,
            "renderFrameLead": GRASS.draw.render_frame_lead,
            "matchedResourceCount": 1,
            "textureStageCount": GRASS.capture.texture_stage_count,
            "maximumCandidates": GRASS.capture.maximum_candidates,
            "candidateLimitReached": False,
            "maximumRecords": GRASS.capture.maximum_records,
            "maximumVertexBufferBytes": GRASS.capture.maximum_vertex_buffer_bytes,
            "target": {
                "width": GRASS.texture.width_pixels,
                "height": GRASS.texture.height_pixels,
                "levelCount": GRASS.texture.level_count,
                "format": GRASS.texture.d3d9_format,
                "contentHash": f"d3d9-fnv1a32:{GRASS.texture.fnv1a32:08x}",
                "topLevelHash": (
                    f"d3d9-fnv1a32:{GRASS.texture.top_level_fnv1a32:08x}"
                ),
            },
            "records": records,
        }
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "retail-grass.jsonl"
            path.write_text(
                json.dumps(event, separators=(",", ":")) + "\n",
                encoding="utf-8",
            )
            parsed = read_retail_grass_observation(path)

        self.assertEqual(parsed["coverage"]["matchingDraws"], len(GRASS.meshes))
        self.assertEqual(parsed["coverage"]["instances"], len(GRASS.meshes))
        self.assertEqual(parsed["shader"]["fogNearGameUnits"], 10.0)
        self.assertEqual(parsed["shader"]["fadeStartGameUnits"], 7000.0)
        self.assertEqual(parsed["shader"]["renderState"]["cullMode"], 1)
        self.assertEqual(parsed["shader"]["renderState"]["alphaReference"], 10)
        self.assertEqual(
            sorted(parsed["meshes"]),
            sorted(mesh.suffix for mesh in GRASS.meshes),
        )

    def test_observation_parser_accepts_a_configured_visible_mesh_subset(self) -> None:
        mesh = next(value for value in GRASS.meshes if value.suffix == "06")
        record = _observation_record(mesh, 1)
        event = {
            "event": "texture-sampler-contract",
            "sourceFrame": 189,
            "renderFrameLead": GRASS.draw.render_frame_lead,
            "matchedResourceCount": GRASS.capture.required_matched_resource_count,
            "textureStageCount": GRASS.capture.texture_stage_count,
            "maximumCandidates": GRASS.capture.maximum_candidates,
            "candidateLimitReached": False,
            "maximumRecords": GRASS.capture.maximum_records,
            "maximumVertexBufferBytes": GRASS.capture.maximum_vertex_buffer_bytes,
            "target": {
                "width": GRASS.texture.width_pixels,
                "height": GRASS.texture.height_pixels,
                "levelCount": GRASS.texture.level_count,
                "format": GRASS.texture.d3d9_format,
                "contentHash": f"d3d9-fnv1a32:{GRASS.texture.fnv1a32:08x}",
                "topLevelHash": (
                    f"d3d9-fnv1a32:{GRASS.texture.top_level_fnv1a32:08x}"
                ),
            },
            "records": [record],
        }
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "retail-grass-subset.jsonl"
            path.write_text(json.dumps(event) + "\n", encoding="utf-8")
            parsed = read_retail_grass_observation(path)
        self.assertEqual(sorted(parsed["meshes"]), ["06"])
        self.assertEqual(parsed["coverage"]["observedOwnedMeshes"], 1)
        self.assertEqual(parsed["coverage"]["configuredOwnedMeshes"], len(GRASS.meshes))

    def test_observation_parser_unions_multiple_source_frames_deterministically(self) -> None:
        mesh = next(value for value in GRASS.meshes if value.suffix == "06")
        first = _observation_record(mesh, 1)
        second = json.loads(json.dumps(first))
        first["sourceFrame"] = 70
        first["renderFrame"] = 70 - GRASS.draw.render_frame_lead
        second["sourceFrame"] = 95
        second["renderFrame"] = 95 - GRASS.draw.render_frame_lead
        direction = GRASS.shader.registers["diffuseDirection"] * 4
        second["vertexConstants"]["values"][direction + 1] = 0.7
        second["vertexConstants"]["values"][direction + 2] = math.sqrt(1.0 - 0.7**2)
        event = {
            "event": "texture-sampler-contract",
            "sourceFrame": 95,
            "renderFrameLead": GRASS.draw.render_frame_lead,
            "matchedResourceCount": GRASS.capture.required_matched_resource_count,
            "textureStageCount": GRASS.capture.texture_stage_count,
            "maximumCandidates": GRASS.capture.maximum_candidates,
            "candidateLimitReached": False,
            "maximumRecords": GRASS.capture.maximum_records,
            "maximumVertexBufferBytes": GRASS.capture.maximum_vertex_buffer_bytes,
            "target": {
                "width": GRASS.texture.width_pixels,
                "height": GRASS.texture.height_pixels,
                "levelCount": GRASS.texture.level_count,
                "format": GRASS.texture.d3d9_format,
                "contentHash": f"d3d9-fnv1a32:{GRASS.texture.fnv1a32:08x}",
                "topLevelHash": (
                    f"d3d9-fnv1a32:{GRASS.texture.top_level_fnv1a32:08x}"
                ),
            },
            "records": [first, second],
        }
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "retail-grass-multi-frame.jsonl"
            path.write_text(json.dumps(event) + "\n", encoding="utf-8")
            parsed = read_retail_grass_observation(path)
        self.assertEqual(parsed["source"]["sourceFrame"], 95)
        self.assertEqual(parsed["source"]["capturedSourceFrames"], [95, 70])
        self.assertEqual(parsed["coverage"]["instances"], 1)
        self.assertEqual(parsed["coverage"]["rawInstances"], 2)
        self.assertEqual(parsed["coverage"]["duplicateInstancesDropped"], 1)
        self.assertAlmostEqual(parsed["shader"]["diffuseDirection"][1], 0.7)

    def test_exterior_prepare_keeps_retail_grass_input_optional(self) -> None:
        parameter = inspect.signature(prepare).parameters["retail_grass_observation"]
        self.assertIsNone(parameter.default)
        state_parameter = inspect.signature(prepare).parameters[
            "retail_grass_render_state_observation"
        ]
        self.assertIsNone(state_parameter.default)


if __name__ == "__main__":
    unittest.main()
