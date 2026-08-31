from __future__ import annotations

import math
import sys
import time
import unittest
from pathlib import Path


if not hasattr(time, "clock"):
    time.clock = time.perf_counter

from pyffi.formats.nif import NifFormat  # type: ignore  # noqa: E402


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from ttw_fo3_stage10_collision import (  # noqa: E402
    RIGID_BODY_POLICY,
    SCHEMA,
    compile_ttw_stage10_collision_document,
)


def identity_transform(target: object) -> None:
    matrix = NifFormat.Matrix44()
    matrix.set_identity()
    target.set_transform(matrix)


def collision_document(shape: object, *, mass: float = 0.0) -> object:
    root = NifFormat.NiNode()
    root.name = "Synthetic Root"
    identity_transform(root)
    target = NifFormat.NiNode()
    target.name = "Synthetic Collision Target"
    identity_transform(target)
    target.translation.x = 10.0
    target.translation.y = 20.0
    target.translation.z = 30.0
    root.add_child(target)
    collision = NifFormat.bhkCollisionObject()
    collision.target = target
    target.collision_object = collision
    body = NifFormat.bhkRigidBody()
    collision.body = body
    body.shape = shape
    body.mass = mass
    body.rotation.w = 1.0
    body.translation.x = 99.0
    body.friction = 0.4
    body.restitution = 0.2
    body.linear_damping = 0.1
    body.angular_damping = 0.3
    body.motion_system = 7
    body.quality_type = 1
    body.havok_col_filter.layer = 2
    body.havok_col_filter.flags_and_part_number = 3
    body.havok_col_filter.unknown_short = 4
    document = NifFormat.Data(
        version=0x14020007,
        user_version=12,
        user_version_2=83,
    )
    document.roots = [root]
    return document


def box_shape() -> object:
    shape = NifFormat.bhkBoxShape()
    shape.material.material = 9
    shape.radius = 0.05
    shape.minimum_size = 1.0
    shape.dimensions.x = 1.0
    shape.dimensions.y = 2.0
    shape.dimensions.z = 3.0
    return shape


class TtwFo3Stage10CollisionTests(unittest.TestCase):
    def test_box_preserves_exact_target_filter_and_static_policy(self) -> None:
        contract = compile_ttw_stage10_collision_document(
            collision_document(box_shape()),
            "a" * 64,
        )

        self.assertEqual(contract["schema"], SCHEMA)
        self.assertTrue(contract["collisionReady"])
        self.assertFalse(contract["renderMeshSubstitutionUsed"])
        body = contract["bodies"][0]
        self.assertEqual(body["shapeTransformPolicy"], RIGID_BODY_POLICY)
        self.assertEqual(body["filter"], {
            "layer": 2,
            "flagsAndPartNumber": 3,
            "unknownShort": 4,
        })
        shape = body["shapes"][0]
        self.assertEqual(shape["godotShapeType"], "ConvexPolygonShape3D")
        self.assertEqual(shape["material"], 9)
        self.assertEqual(len(shape["pointsGodotGameUnits"]), 8)
        self.assertIn([3.0, 9.0, -6.0], shape["pointsGodotGameUnits"])
        self.assertIn([17.0, 51.0, -34.0], shape["pointsGodotGameUnits"])
        self.assertEqual(body["sourceBodyTranslationHavokUnits"], [99.0, 0.0, 0.0])

    def test_list_keeps_children_separate_and_analytic_shapes_exact(self) -> None:
        sphere = NifFormat.bhkSphereShape()
        sphere.material.material = 5
        sphere.radius = 1.0
        capsule = NifFormat.bhkCapsuleShape()
        capsule.material.material = 6
        capsule.radius = 1.0
        capsule.radius_1 = 1.0
        capsule.radius_2 = 1.0
        capsule.second_point.y = 2.0
        source_list = NifFormat.bhkListShape()
        source_list.material.material = 7
        source_list.num_sub_shapes = 2
        source_list.sub_shapes.update_size()
        source_list.sub_shapes[0] = sphere
        source_list.sub_shapes[1] = capsule

        contract = compile_ttw_stage10_collision_document(
            collision_document(source_list),
            "b" * 64,
        )

        shapes = contract["bodies"][0]["shapes"]
        self.assertEqual(len(shapes), 2)
        self.assertEqual(
            [shape["godotShapeType"] for shape in shapes],
            ["SphereShape3D", "CapsuleShape3D"],
        )
        self.assertTrue(all(
            shape["containerShapes"][0]["sourceShapeType"] == "bhkListShape"
            and shape["containerShapes"][0]["childCount"] == 2
            for shape in shapes
        ))
        self.assertEqual(shapes[0]["centerGodotGameUnits"], [10.0, 30.0, -20.0])
        self.assertEqual(shapes[0]["radiusGodotGameUnits"], 7.0)
        self.assertEqual(shapes[1]["firstPointGodotGameUnits"], [10.0, 30.0, -20.0])
        self.assertEqual(shapes[1]["secondPointGodotGameUnits"], [10.0, 30.0, -34.0])
        self.assertEqual(shapes[1]["radiusGodotGameUnits"], 7.0)
        self.assertEqual(shapes[1]["heightGodotGameUnits"], 28.0)

    def test_transform_shape_is_applied_before_target_axis_conversion(self) -> None:
        transformed = NifFormat.bhkTransformShape()
        transformed.material.material = 8
        transformed.shape = box_shape()
        transformed.transform.set_identity()
        transformed.transform.m_41 = 1.0

        contract = compile_ttw_stage10_collision_document(
            collision_document(transformed),
            "c" * 64,
        )

        points = contract["bodies"][0]["shapes"][0]["pointsGodotGameUnits"]
        self.assertIn([10.0, 9.0, -6.0], points)
        self.assertIn([24.0, 51.0, -34.0], points)
        self.assertEqual(
            contract["bodies"][0]["shapes"][0]["containerShapes"][0][
                "sourceShapeType"
            ],
            "bhkTransformShape",
        )

    def test_non_uniform_analytic_shape_fails_closed(self) -> None:
        sphere = NifFormat.bhkSphereShape()
        sphere.material.material = 5
        sphere.radius = 1.0
        transformed = NifFormat.bhkTransformShape()
        transformed.material.material = 8
        transformed.shape = sphere
        transformed.transform.set_identity()
        transformed.transform.m_11 = 2.0
        with self.assertRaisesRegex(ValueError, "non-uniform scale"):
            compile_ttw_stage10_collision_document(
                collision_document(transformed),
                "e" * 64,
            )

    def test_dynamic_sphere_publishes_exact_shape_without_dynamics_parity_claim(self) -> None:
        sphere = NifFormat.bhkSphereShape()
        sphere.material.material = 5
        sphere.radius = 1.0
        contract = compile_ttw_stage10_collision_document(
            collision_document(sphere, mass=1.0),
            "d" * 64,
        )

        self.assertEqual(contract["dynamicBodyCount"], 1)
        body = contract["bodies"][0]
        self.assertTrue(body["dynamic"])
        self.assertEqual(body["godotBodyType"], "RigidBody3D")
        self.assertEqual(
            body["physicsIntegrationDisposition"],
            "exact-collision-shape-and-source-body-properties;"
            "engine-dynamics-and-constraints-parity-not-asserted",
        )

    def test_capsule_requires_equal_source_radii(self) -> None:
        capsule = NifFormat.bhkCapsuleShape()
        capsule.material.material = 6
        capsule.radius = 1.0
        capsule.radius_1 = 1.0
        capsule.radius_2 = math.nextafter(1.1, 2.0)
        capsule.second_point.y = 2.0
        with self.assertRaisesRegex(ValueError, "one exact radius"):
            compile_ttw_stage10_collision_document(
                collision_document(capsule),
                "f" * 64,
            )


if __name__ == "__main__":
    unittest.main()
