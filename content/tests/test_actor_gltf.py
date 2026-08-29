import sys
import unittest
from pathlib import Path
from unittest.mock import patch

from PIL import Image

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))

from actor_gltf import (  # noqa: E402
    ActorComponent,
    NifFormat,
    RetailRenderPart,
    _append_facegen_morph_targets,
    _append_runtime_surface_node,
    _authored_rigid_attachment_nodes,
    _bake_actor_shape_transform,
    _is_authored_prn_root_marker,
    _compensated_inverse_bind,
    _converted_matrix,
    _converted_xyz_rotation,
    _facegen_rigid_attachment_matrix,
    _hardware_vertex_weights,
    _multiply,
    _quadratic_vector_keys,
    _rigid_attachment,
    _resolve_retail_rigid_part,
    _sample_transform_interpolator,
    _unsupported_actor_geometry,
    _uses_retail_biped_head_basis,
    _visible_creature_geometry_names,
    actor_animation_translations,
    furniture_marker_manifest,
    _accumulation_root_source_translation,
    _world_authoritative_accumulation_root_translations,
    gltf_skeleton_inverse_binds,
)
from facegen_animation import (  # noqa: E402
    FaceGenDifferentialMorph,
    FaceGenStaticMorph,
    FaceGenTri,
)
from gltf_io import BufferBuilder  # noqa: E402
from actor_material import (  # noqa: E402
    FACEGEN_MATERIAL_SCHEMA,
    GLTF_UNLIT_EXTENSION,
    actor_alpha_contract,
    actor_base_color_factor,
    actor_roughness,
    actor_texture_paths,
    actor_vertex_colors_enabled,
    build_actor_material,
)
from runtime_configuration import load_runtime_configuration  # noqa: E402


class ActorGltfTest(unittest.TestCase):
    @staticmethod
    def _retail_part(
        geometry_name: str,
        visual_node_path: str,
        *,
        role: str = "headPart",
        texture_paths: tuple[str, ...] = (),
    ) -> RetailRenderPart:
        return RetailRenderPart(
            role=role,
            source_form_id="0x00104E84",
            source_slot=0xFFFFFFFF,
            required=True,
            attached=True,
            drawable=True,
            visible=True,
            skinned=False,
            geometry_name=geometry_name,
            visual_node_path=visual_node_path,
            texture_paths=texture_paths,
        )

    def test_retail_rigid_part_binds_by_exact_owned_texture(self):
        component = ActorComponent(
            "mouth",
            "meshes/characters/_male/mouthhuman.nif",
            b"nif",
            source_form_id="0x00104E84",
            source_slot=0xFFFFFFFF,
        )
        expected = self._retail_part(
            "FaceGenMouth",
            "root/face/mouth",
            texture_paths=("textures/characters/mouth/mouthhuman.dds",),
        )
        other = self._retail_part(
            "FaceGenTongue",
            "root/face/tongue",
            texture_paths=("textures/characters/mouth/tonguehuman.dds",),
        )

        part, authority = _resolve_retail_rigid_part(
            component,
            "MouthHuman:0",
            ("textures/characters/mouth/mouthhuman.dds",),
            (expected, other),
        )

        self.assertEqual(part, expected)
        self.assertEqual(authority, "exact-owned-texture-binding")

    def test_retail_rigid_part_binds_eye_side_by_geometry_lineage(self):
        component = ActorComponent(
            "eye-left",
            "meshes/characters/_male/eyelefthuman.nif",
            b"nif",
            diffuse_override="textures/characters/eyes/eyedefault.dds",
            source_form_id="0x00104E84",
            source_slot=0xFFFFFFFF,
        )
        left = self._retail_part(
            "FaceGenEyeLeft",
            "root/face/eye-left",
            role="eyes",
            texture_paths=("textures/characters/eyes/eyedefault.dds",),
        )
        right = self._retail_part(
            "FaceGenEyeRight",
            "root/face/eye-right",
            role="eyes",
            texture_paths=("textures/characters/eyes/eyedefault.dds",),
        )

        part, authority = _resolve_retail_rigid_part(
            component,
            "EyeLeftHumanFemale:0",
            ("textures/shared/white.dds",),
            (right, left),
        )

        self.assertEqual(part, left)
        self.assertEqual(authority, "exact-geometry-token-lineage")

    def test_retail_rigid_part_rejects_unresolved_ambiguity(self):
        component = ActorComponent(
            "head-part",
            "meshes/characters/characterassets/accessory.nif",
            b"nif",
            source_form_id="0x00104E84",
            source_slot=0xFFFFFFFF,
        )
        parts = (
            self._retail_part("AccessoryA", "root/face/a"),
            self._retail_part("AccessoryB", "root/face/b"),
        )

        with self.assertRaisesRegex(ValueError, "ambiguous retail render parts"):
            _resolve_retail_rigid_part(component, "Unknown", (), parts)

    def test_creature_rigid_part_uses_exact_geometry_across_semantic_roles(self):
        component = ActorComponent(
            "creature-model-4",
            "meshes/creatures/nvsecuritron/nvsecuritronyesmanscreen01.nif",
            b"nif",
            source_form_id="0x00104E84",
            source_slot=0xFFFFFFFF,
        )
        screen = self._retail_part(
            "Screen01:0",
            "root/screen",
            role="exposedBody",
        )
        voice = self._retail_part(
            "VoiceBox_Root:0",
            "root/voice",
            role="actor",
        )

        part, authority = _resolve_retail_rigid_part(
            component,
            "Screen01:0",
            (),
            (voice, screen),
        )

        self.assertEqual(part, screen)
        self.assertEqual(authority, "exact-runtime-geometry-name")

    def test_retail_bound_creature_rigid_surface_keeps_owned_component_basis(self):
        component = ActorComponent(
            "creature-model-4",
            "meshes/creatures/nvsecuritron/nvsecuritronyesmanscreen01.nif",
            b"nif",
        )

        self.assertTrue(
            _bake_actor_shape_transform(
                component,
                rigid=True,
                retail_bound=True,
            )
        )

    def test_retail_bound_facegen_rigid_surface_stays_attachment_local(self):
        component = ActorComponent(
            "head",
            "meshes/characters/head/headhuman.nif",
            b"nif",
            bake_shape_transform=True,
        )

        self.assertFalse(
            _bake_actor_shape_transform(
                component,
                rigid=True,
                retail_bound=True,
            )
        )

    def test_animation_object_bakes_exact_owned_shape_transform(self):
        component = ActorComponent(
            "animation-object-00083519",
            "meshes/animobjects/aocigarette.nif",
            b"nif",
            bake_shape_transform=True,
            source_form_id="00083519",
        )

        self.assertTrue(
            _bake_actor_shape_transform(
                component,
                rigid=True,
                retail_bound=False,
            )
        )

    def test_creature_visibility_joins_all_semantic_roles_by_owned_identity(self):
        component = ActorComponent(
            "creature-model-4",
            "meshes/creatures/nvsecuritron/nvsecuritronyesmanscreen01.nif",
            b"nif",
            source_form_id="0x00104E84",
            source_slot=0xFFFFFFFF,
        )
        actor = self._retail_part(
            "RobotBody",
            "root/body",
            role="actor",
        )
        screen = self._retail_part(
            "Screen01:0",
            "root/screen",
            role="exposedBody",
        )
        other_source = RetailRenderPart(
            **{
                **screen.__dict__,
                "geometry_name": "WrongActorScreen",
                "source_form_id": "0x00104E85",
            }
        )

        self.assertEqual(
            _visible_creature_geometry_names(
                component,
                (actor, screen, other_source),
            ),
            frozenset({"RobotBody", "Screen01:0"}),
        )

    def test_runtime_surface_identity_is_exact_and_independent_of_nif_punctuation(self):
        nodes = [{"children": []}]
        surface = {"shape": "Turret:0"}

        _append_runtime_surface_node(nodes, 0, 4, 2, surface, 0)

        self.assertEqual(surface["shape"], "Turret:0")
        self.assertEqual(surface["runtimeNodeName"], "ActorSurface_0")
        self.assertEqual(surface["runtimeNodeName"], nodes[1]["name"])
        self.assertEqual(nodes[1]["mesh"], 4)
        self.assertEqual(nodes[1]["skin"], 2)
        self.assertEqual(nodes[0]["children"], [1])

    def test_runtime_surface_identity_is_unique_for_each_exported_surface(self):
        nodes = [{"children": []}]
        surfaces = [{"shape": "Body:0"}, {"shape": "Body:0"}]
        for index, surface in enumerate(surfaces):
            _append_runtime_surface_node(nodes, 0, index, None, surface, index)

        self.assertEqual(
            [surface["runtimeNodeName"] for surface in surfaces],
            ["ActorSurface_0", "ActorSurface_1"],
        )
        self.assertEqual(
            [node["name"] for node in nodes[1:]],
            ["ActorSurface_0", "ActorSurface_1"],
        )

    def test_prn_root_marker_is_classified_from_owned_nif_structure(self):
        root = NifFormat.NiNode()
        marker = NifFormat.NiTriShape()
        marker.data = NifFormat.NiTriShapeData()
        marker.data.num_uv_sets = 0
        marker.data.uv_sets.update_size()
        marker.num_properties = 1
        marker.properties.update_size()
        marker.properties[0] = NifFormat.NiMaterialProperty()
        rendered = NifFormat.NiTriShape()
        rendered.data = NifFormat.NiTriShapeData()
        root.num_children = 2
        root.children.update_size()
        root.children[0] = marker
        root.children[1] = rendered

        self.assertTrue(
            _is_authored_prn_root_marker(
                root,
                marker,
                "nif-prn-skeleton-node",
                [marker, rendered],
            )
        )
        self.assertFalse(
            _is_authored_prn_root_marker(
                root,
                marker,
                "nif-root-skeleton-node",
                [marker, rendered],
            )
        )
    def test_facegen_rigid_part_keeps_owned_translation_and_retail_head_basis(self):
        matrix = _facegen_rigid_attachment_matrix([1.0, 2.0, 3.0])
        self.assertEqual(
            matrix,
            [
                [0.0, 1.0, 0.0, 1.0],
                [-1.0, 0.0, 0.0, 3.0],
                [0.0, 0.0, 1.0, -2.0],
                [0.0, 0.0, 0.0, 1.0],
            ],
        )
        nodes = [{"children": []}]
        surface = {"shape": "FaceGenHairNoHat"}
        _append_runtime_surface_node(nodes, 0, 4, None, surface, 0, matrix)
        self.assertEqual(
            nodes[1]["matrix"][12:15],
            [1.0, 3.0, -2.0],
        )

    def test_prn_head_apparel_uses_the_same_owned_biped_head_basis(self):
        self.assertTrue(
            _uses_retail_biped_head_basis(
                "outfit-1",
                "Bip01 Head",
                "nif-prn-skeleton-node",
                "Bip01 Head",
            )
        )
        self.assertFalse(
            _uses_retail_biped_head_basis(
                "outfit-0",
                "HeadAnims",
                "configured-unparented-skeleton-node",
                "Bip01 Head",
            )
        )
        self.assertFalse(
            _uses_retail_biped_head_basis(
                "creature-model-2",
                "Bip01 Head",
                "nif-prn-skeleton-node",
                "Bip01 Head",
            )
        )

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

    def test_effect_shader_source_texture_is_an_actor_diffuse(self):
        shader = NifFormat.BSEffectShaderProperty()
        shader.source_texture = b"textures\\creatures\\nvsecuritron\\yesmanhappy.dds"
        self.assertEqual(
            actor_texture_paths([shader]),
            ("textures\\creatures\\nvsecuritron\\yesmanhappy.dds",),
        )
        material = NifFormat.NiMaterialProperty()
        material.diffuse_color.r = 0.0
        material.diffuse_color.g = 0.0
        material.diffuse_color.b = 0.0
        factor, source = actor_base_color_factor(
            [material, shader],
            (1.0, 1.0, 1.0),
        )
        self.assertEqual(factor[:3], [1.0, 1.0, 1.0])
        self.assertEqual(source, "bethesda-shader-texture-neutral")

    def test_no_lighting_shader_file_name_is_an_actor_diffuse(self):
        shader = NifFormat.BSShaderNoLightingProperty()
        shader.file_name = b"textures\\creatures\\nvsecuritron\\yesmanhappy.dds"
        self.assertEqual(
            actor_texture_paths([shader]),
            ("textures\\creatures\\nvsecuritron\\yesmanhappy.dds",),
        )

    def test_no_lighting_actor_material_preserves_owned_unlit_state(self):
        class TextureLibrary:
            def source(self, path: str, *, normal: bool = False) -> int:
                self.path = path
                self.normal = normal
                return 7

        shape = NifFormat.NiTriShape()
        shape.name = b"Screen01:0"
        shader = NifFormat.BSShaderNoLightingProperty()
        shader.file_name = b"textures\\creatures\\securitron\\yesman_neutral.dds"
        shape.add_property(shader)
        material, evidence = build_actor_material(
            ActorComponent(
                "creature-model-4",
                "meshes\\creatures\\nvsecuritron\\nvsecuritronyesmanscreen01.nif",
                b"owned-nif",
            ),
            shape,
            TextureLibrary(),
            load_runtime_configuration().content_compiler,
        )

        self.assertEqual(material["extensions"], {GLTF_UNLIT_EXTENSION: {}})
        self.assertTrue(evidence["unshaded"])
        self.assertEqual(
            evidence["unshadedSource"],
            "owned-nif-no-lighting-or-effect-shader",
        )

    def test_facegen_material_keeps_retail_sampler_inputs_separate(self):
        class TextureLibrary:
            def __init__(self) -> None:
                self.calls: list[tuple[str, str, bool]] = []

            def source(self, path: str, *, normal: bool = False) -> int:
                self.calls.append(("source", path, normal))
                return len(self.calls) - 1

            def generated(self, identity: str, image: Image.Image, source_sha256: str) -> int:
                del image, source_sha256
                self.calls.append(("generated", identity, False))
                return len(self.calls) - 1

        shader = NifFormat.BSShaderPPLightingProperty()
        shader.texture_set = NifFormat.BSShaderTextureSet()
        shader.texture_set.num_textures = 2
        shader.texture_set.textures.update_size()
        shader.texture_set.textures[0] = b"textures\\characters\\male\\headhuman.dds"
        shader.texture_set.textures[1] = b"textures\\characters\\male\\headhuman_n.dds"
        nif_material = NifFormat.NiMaterialProperty()
        nif_material.specular_color.r = 0.0
        nif_material.specular_color.g = 0.0
        nif_material.specular_color.b = 0.0

        class Shape:
            name = b"FaceGenFace"
            properties = [shader, nif_material]

        textures = TextureLibrary()
        material, row = build_actor_material(
            ActorComponent(
                "head",
                "meshes\\characters\\head\\headfemale.nif",
                b"nif",
                diffuse_override="textures\\characters\\female\\headhuman.dds",
                normal_override="textures\\characters\\female\\headhuman_n.dds",
                generated_facegen_detail=Image.new("RGBA", (1, 1), (128, 128, 128, 255)),
            ),
            Shape(),
            textures,  # type: ignore[arg-type]
            load_runtime_configuration().content_compiler,
        )

        self.assertEqual(
            textures.calls[:2],
            [
                ("source", "textures\\characters\\female\\headhuman.dds", False),
                ("source", "textures\\characters\\female\\headhuman_n.dds", True),
            ],
        )
        self.assertEqual(row["resolvedDiffuse"], textures.calls[0][1])
        self.assertEqual(row["resolvedNormal"], textures.calls[1][1])
        self.assertEqual(
            row["faceGen"],
            material["extras"]["openNvFaceGenMaterial"],
        )
        self.assertEqual(row["faceGen"]["schema"], FACEGEN_MATERIAL_SCHEMA)
        self.assertEqual(row["faceGen"]["baseTextureIndex"], 0)
        self.assertEqual(row["faceGen"]["normalTextureIndex"], 1)
        self.assertEqual(row["faceGen"]["detailTextureIndex"], 2)
        self.assertEqual(row["alphaMode"], "OPAQUE")
        self.assertIsNone(row["generatedDiffuseSha256"])

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

    def test_actor_root_motion_is_separated_from_absolute_nonaccum_pose(self):
        values = [(2.0, 66.0, -1.0), (2.5, 65.75, -0.5)]
        self.assertEqual(
            actor_animation_translations(
                "Bip01",
                values,
                "Bip01",
                "Bip01 NonAccum",
            ),
            [(0.0, 0.0, 0.0), (0.0, 0.0, 0.0)],
        )
        self.assertEqual(
            actor_animation_translations(
                "Bip01 NonAccum",
                values,
                "Bip01",
                "Bip01 NonAccum",
            ),
            values,
        )
        self.assertIs(
            actor_animation_translations(
                "Bip01 Head",
                values,
                "Bip01",
                "Bip01 NonAccum",
            ),
            values,
        )

    def test_constant_transform_interpolator_publishes_owned_nonaccum_pose(self):
        interpolator = NifFormat.NiTransformInterpolator()
        interpolator.translation.x = 0.0
        interpolator.translation.y = 0.0
        interpolator.translation.z = 36.85
        interpolator.rotation.w = 1.0
        interpolator.rotation.x = 0.0
        interpolator.rotation.y = 0.0
        interpolator.rotation.z = 0.0

        translations, rotations = _sample_transform_interpolator(
            interpolator,
            [0.0, 1.0],
            0.0,
            1.0,
            "Bip01 NonAccum",
        )

        self.assertEqual(translations, [(0.0, 36.85, -0.0)] * 2)
        self.assertEqual(rotations, [(0.0, 0.0, 0.0, 1.0)] * 2)

    def test_invalid_transform_sentinel_does_not_publish_constant_channels(self):
        interpolator = NifFormat.NiTransformInterpolator()
        sentinel = -3.4028234663852886e38
        interpolator.translation.x = sentinel
        interpolator.translation.y = sentinel
        interpolator.translation.z = sentinel
        interpolator.rotation.w = sentinel
        interpolator.rotation.x = sentinel
        interpolator.rotation.y = sentinel
        interpolator.rotation.z = sentinel

        translations, rotations = _sample_transform_interpolator(
            interpolator,
            [0.0, 1.0],
            0.0,
            1.0,
            "Bip01 Spine",
        )

        self.assertEqual(translations, [])
        self.assertEqual(rotations, [])

    def test_furniture_marker_manifest_preserves_exact_owned_marker(self):
        marker = NifFormat.BSFurnitureMarker()
        marker.name = "FRN"
        marker.num_positions = 1
        marker.positions.update_size()
        position = marker.positions[0]
        position.offset.x = -0.25
        position.offset.y = 50.0
        position.offset.z = -25.0
        position.orientation = 3141
        position.position_ref_1 = 14
        position.position_ref_2 = 14
        position.heading = 0.0
        position.animation_type = 1

        class Document:
            @staticmethod
            def get_global_iterator():
                return iter((marker,))

        with patch("actor_gltf._read_nif", return_value=Document()):
            result = furniture_marker_manifest(b"owned-chair", 14)

        self.assertEqual(result["index"], 0)
        self.assertEqual(result["positionRef1"], 14)
        self.assertEqual(result["positionRef2"], 14)
        self.assertEqual(result["offsetNifGameUnits"], [-0.25, 50.0, -25.0])
        self.assertEqual(result["offsetGodotGameUnits"], [-0.25, -25.0, -50.0])
        self.assertEqual(result["orientation"], 3141)
        self.assertEqual(result["orientationRadians"], 3.141)

    def test_bip01_locomotion_translation_is_removed_from_accumulation_root(self):
        values = [(0.0, 0.0, 0.0), (0.5, 0.0, -0.25)]
        self.assertEqual(
            actor_animation_translations("Bip01", values, "Bip01", "Bip01 NonAccum"),
            [(0.0, 0.0, 0.0), (0.0, 0.0, 0.0)],
        )

    def test_hash_bound_clip_can_retain_owned_accumulation_root_curve(self):
        values = [(10.0, 0.0, 5.0), (10.5, 0.0, 4.75)]
        self.assertEqual(
            actor_animation_translations(
                "Bip01",
                values,
                "Bip01",
                "Bip01 NonAccum",
                retain_accumulation_root_translation=True,
            ),
            [(0.0, 0.0, 0.0), (0.5, 0.0, -0.25)],
        )

    def test_skeleton_rest_is_evidence_without_mutating_skin_bind_space(self):
        nodes = [
            {"name": "actor-root"},
            {"name": "Bip01", "translation": [1.0, 67.0, -0.5]},
        ]
        source = _accumulation_root_source_translation(
            nodes,
            {"Bip01": 1},
            "Bip01",
        )
        self.assertEqual(source, (1.0, 67.0, -0.5))
        self.assertEqual(nodes[1]["translation"], [1.0, 67.0, -0.5])

    def test_zero_root_clip_without_authored_root_gets_zero_track(self):
        self.assertEqual(
            _world_authoritative_accumulation_root_translations(False, False, 2),
            [(0.0, 0.0, 0.0), (0.0, 0.0, 0.0)],
        )

    def test_retained_root_clip_requires_authored_curve(self):
        with self.assertRaisesRegex(ValueError, "no authored root curve"):
            _world_authoritative_accumulation_root_translations(False, True, 2)

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

    def test_rigid_component_uses_declared_node_when_root_is_not_a_bone(self):
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
            ("HeadAnims", "configured-unparented-skeleton-node"),
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

    def test_animation_object_reads_exact_owned_prn_attachment(self):
        parent = NifFormat.NiStringExtraData()
        parent.name = b"Prn"
        parent.string_data = b"Bip01 R Hand"

        class Document:
            @staticmethod
            def get_global_iterator():
                return (parent,)

        self.assertEqual(
            _authored_rigid_attachment_nodes(Document()),
            ("Bip01 R Hand",),
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

    def test_facegen_tri_exports_authored_named_position_and_normal_targets(self):
        tri = FaceGenTri(
            3,
            ((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)),
            ((0, 1, 2),),
            (),
            (
                FaceGenDifferentialMorph(
                    "Aah",
                    1.0,
                    ((0.0, 0.0, 0.1), (0.0, 0.0, 0.1), (0.0, 0.0, 0.1)),
                ),
            ),
            (FaceGenStaticMorph("BlinkLeft", ((2, (0.0, 1.0, 0.2)),)),),
        )
        builder = BufferBuilder()
        targets, manifest = _append_facegen_morph_targets(
            tri,
            [(0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)],
            [(0.0, 1.0, 0.0)] * 3,
            [(0, 1, 2)],
            object(),
            False,
            builder,
            load_runtime_configuration().content_compiler,
        )

        self.assertEqual(manifest["targetNames"], ["Aah", "BlinkLeft"])
        self.assertEqual(len(targets), 2)
        self.assertTrue(all(set(target) == {"POSITION", "NORMAL"} for target in targets))
        self.assertEqual(len(builder.accessors), 4)


if __name__ == "__main__":
    unittest.main()
