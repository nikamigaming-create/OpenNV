import sys
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_gltf import (  # noqa: E402
    NifFormat,
    _compensated_inverse_bind,
    _converted_matrix,
    _converted_xyz_rotation,
    _hardware_vertex_weights,
    _multiply,
    _quadratic_vector_keys,
    _rigid_attachment,
    _unsupported_actor_geometry,
    actor_animation_translations,
)
from actor_material import (  # noqa: E402
    actor_alpha_contract,
    actor_base_color_factor,
    actor_roughness,
    actor_texture_paths,
    actor_vertex_colors_enabled,
)
from runtime_configuration import load_runtime_configuration  # noqa: E402


class ActorGltfTest(unittest.TestCase):
    def test_xyz_rotation_channels_preserve_identity(self):
        class Key:
            def __init__(self):
                self.time = 0.0
                self.value = 0.0
                self.forward = 0.0
                self.backward = 0.0

        class Group:
            interpolation = 2
            keys = [Key()]

        self.assertEqual(
            _converted_xyz_rotation([Group(), Group(), Group()], 0.0),
            (0.0, 0.0, 0.0, 1.0),
        )

    def test_quadratic_translation_uses_authored_hermite_tangents(self):
        class Vector:
            def __init__(self, x, y, z):
                self.x, self.y, self.z = x, y, z

        class Key:
            def __init__(self, time, value, forward, backward):
                self.time = time
                self.value = Vector(*value)
                self.forward = Vector(*forward)
                self.backward = Vector(*backward)

        keys = [
            Key(0.0, (0.0, 2.0, -1.0), (0.0, 0.0, 0.0), (1.0, 1.0, 1.0)),
            Key(1.0, (1.0, 3.0, 0.0), (1.0, 1.0, 1.0), (0.0, 0.0, 0.0)),
        ]
        self.assertEqual(_quadratic_vector_keys(keys, 0.0), (0.0, 2.0, -1.0))
        self.assertEqual(_quadratic_vector_keys(keys, 0.25), (0.25, 2.25, -0.75))
        self.assertEqual(_quadratic_vector_keys(keys, 1.0), (1.0, 3.0, 0.0))

    def test_bethesda_shader_does_not_multiply_textures_by_legacy_black_diffuse(self):
        material = NifFormat.NiMaterialProperty()
        material.diffuse_color.r = 0.0
        material.diffuse_color.g = 0.0
        material.diffuse_color.b = 0.0
        material.alpha = 1.0
        shader = NifFormat.BSShaderPPLightingProperty()
        factor, source = actor_base_color_factor([material, shader], (1.0, 1.0, 1.0))
        self.assertEqual(factor, [1.0, 1.0, 1.0, 1.0])
        self.assertEqual(source, "bethesda-shader-texture-neutral")

    def test_shader_flags_and_zero_specular_survive_translation(self):
        shader = NifFormat.BSShaderPPLightingProperty()
        shader.shader_flags_2.sf_2_vertex_colors = 0
        self.assertFalse(actor_vertex_colors_enabled([shader]))
        shader.shader_flags_2.sf_2_vertex_colors = 1
        self.assertTrue(actor_vertex_colors_enabled([shader]))
        material = NifFormat.NiMaterialProperty()
        material.specular_color.r = 0.0
        material.specular_color.g = 0.0
        material.specular_color.b = 0.0
        self.assertEqual(
            actor_roughness(material, load_runtime_configuration().content_compiler),
            (1.0, "ni-material-zero-specular"),
        )

    def test_legacy_material_only_shape_has_no_texture_requirement(self):
        self.assertEqual(actor_texture_paths([NifFormat.NiMaterialProperty()]), ())

    def test_bethesda_texture_stages_preserve_empty_slots(self):
        shader = NifFormat.BSShaderPPLightingProperty()
        shader.texture_set = NifFormat.BSShaderTextureSet()
        shader.texture_set.num_textures = 3
        shader.texture_set.textures.update_size()
        shader.texture_set.textures[0] = b"textures\\actor\\body.dds"
        shader.texture_set.textures[1] = b""
        shader.texture_set.textures[2] = b"textures\\actor\\body_g.dds"
        self.assertEqual(
            actor_texture_paths([shader]),
            ("textures\\actor\\body.dds", "", "textures\\actor\\body_g.dds"),
        )

    def test_retail_hair_blends_while_outfit_alpha_tests(self):
        hair = NifFormat.NiAlphaProperty()
        hair.flags = 0x10ED
        hair.threshold = 0
        hair_contract = actor_alpha_contract(hair)
        self.assertEqual(hair_contract["mode"], "BLEND")
        self.assertFalse(hair_contract["testEnabled"])

        outfit = NifFormat.NiAlphaProperty()
        outfit.flags = 0x12EC
        outfit.threshold = 20
        outfit_contract = actor_alpha_contract(outfit)
        self.assertEqual(outfit_contract["mode"], "MASK")
        self.assertAlmostEqual(outfit_contract["cutoff"], 20 / 255)

    def test_nonaccum_idle_translation_is_relative_to_its_first_sample(self):
        values = [(2.0, 66.0, -1.0), (2.5, 65.75, -0.5)]
        self.assertEqual(
            actor_animation_translations("Bip01 NonAccum", values),
            [(0.0, 0.0, 0.0), (0.5, -0.25, 0.5)],
        )
        self.assertIs(actor_animation_translations("Bip01 Head", values), values)

    def test_baked_shape_transform_is_removed_from_skin_bind(self):
        inverse_bind = NifFormat.Matrix44()
        inverse_bind.set_identity()
        shape = NifFormat.Matrix44()
        shape.set_identity()
        shape.m_11 = -1.0
        shape.m_22 = -1.0
        corrected = _compensated_inverse_bind(inverse_bind, shape)
        product = _multiply(corrected, _converted_matrix(shape))
        for row in range(4):
            for column in range(4):
                self.assertAlmostEqual(product[row][column], 1.0 if row == column else 0.0)

    def test_actor_skin_uses_render_hardware_partition_weights(self):
        class Block:
            bones = [5, 9]
            vertex_map = [0, 1]
            vertex_weights = [
                [0.25, 0.75, 0.0, 0.0],
                [1.0, 0.0, 0.0, 0.0],
            ]
            bone_indices = [
                [0, 1, 0, 0],
                [1, 0, 0, 0],
            ]

        class Partition:
            skin_partition_blocks = [Block()]

        class Instance:
            skin_partition = Partition()

        class Data:
            vertices = [object(), object()]

        class Shape:
            name = b"hardware-authority"
            data = Data()
            skin_instance = Instance()

        self.assertEqual(
            _hardware_vertex_weights(Shape()),
            [[(5, 0.25), (9, 0.75)], [(9, 1.0)]],
        )

    def test_rigid_component_prefers_its_authored_skeleton_node(self):
        class Root:
            name = b"EyesOneBlue"

        class Document:
            @staticmethod
            def get_global_iterator():
                return ()

        self.assertEqual(
            _rigid_attachment(
                Document(),
                Root(),
                {"Bip01": 1, "EyesOneBlue": 27},
                "Bip01",
            ),
            ("EyesOneBlue", "nif-root-skeleton-node"),
        )

    def test_rigid_component_uses_declared_fallback_when_root_is_not_a_bone(self):
        class Root:
            name = b"BSFaceGenNiNodeSkinned"

        class Document:
            @staticmethod
            def get_global_iterator():
                return ()

        self.assertEqual(
            _rigid_attachment(
                Document(),
                Root(),
                {"HeadAnims": 12},
                "HeadAnims",
            ),
            ("HeadAnims", "configured-skeleton-node-fallback"),
        )

    def test_rigid_component_prefers_authored_prn_over_matching_root(self):
        class Root:
            name = b"EyesOneBlue"

        parent = NifFormat.NiStringExtraData()
        parent.name = b"Prn"
        parent.string_data = b"Bip01 Head"

        class Document:
            @staticmethod
            def get_global_iterator():
                return (parent,)

        self.assertEqual(
            _rigid_attachment(
                Document(),
                Root(),
                {"Bip01 Head": 11, "EyesOneBlue": 27},
                "Bip01",
            ),
            ("Bip01 Head", "nif-prn-skeleton-node"),
        )

    def test_particle_geometry_is_an_explicit_actor_capability_gap(self):
        particle = NifFormat.NiParticleSystem()
        particle.name = b"PCloud02Smoke"
        triangle = NifFormat.NiTriShape()
        triangle.name = b"Body"

        class Document:
            @staticmethod
            def get_global_iterator():
                return (particle, triangle)

        self.assertEqual(
            _unsupported_actor_geometry(Document()),
            (("NiParticleSystem", "PCloud02Smoke"),),
        )


if __name__ == "__main__":
    unittest.main()
