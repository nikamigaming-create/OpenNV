from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path


TOOLS = Path(__file__).resolve().parents[1] / "tools"
if str(TOOLS) not in sys.path:
    sys.path.insert(0, str(TOOLS))

from export_static_nif_gltf import (  # noqa: E402
    compiler_provenance,
    compiler_provenance_source_paths,
)
from gltf_io import compiler_sources_sha256  # noqa: E402
from runtime_configuration import configured_recipe_path  # noqa: E402
from prepare_gallery_capture_shots import (  # noqa: E402
    CAPTURE_MANIFEST_SCHEMA,
    CAPTURE_SHOT_SCHEMA,
    _capture_shot_contract,
    prepare_capture_shots,
)
from prepare_wasteland_gallery import (  # noqa: E402
    GALLERY_SCHEMA,
    LOCATION_CONTRACT_SCHEMA,
    SHOT_SCHEMA,
    SUBJECT_COMPILERS,
    _document_sha256,
    _load_gallery,
    _location_scene_key,
    _reuse_compiled_location,
    _shot_contract,
    _subject_location_recipe,
)


class WastelandGalleryTest(unittest.TestCase):
    def test_static_compiler_identity_excludes_route_and_actor_dependencies(self):
        sources = compiler_provenance_source_paths()
        source_names = {path.name for path in sources}
        self.assertIn("export_static_nif_gltf.py", source_names)
        self.assertNotIn("cell_catalog.py", source_names)
        self.assertNotIn("actor_catalog.py", source_names)
        self.assertNotIn("opening_catalog.py", source_names)
        self.assertEqual(
            compiler_provenance()["sha256"],
            compiler_sources_sha256(sources),
        )
        self.assertNotEqual(
            compiler_sources_sha256(sources),
            compiler_sources_sha256(
                path for path in sources if path.name != "export_static_nif_gltf.py"
            ),
        )
        resolved_sources = {path.resolve() for path in sources}
        for contract_name in ("nifDecoder", "materialBinding"):
            contract = configured_recipe_path(contract_name).resolve()
            self.assertIn(contract, resolved_sources)
            self.assertNotEqual(
                compiler_sources_sha256(sources),
                compiler_sources_sha256(
                    path for path in sources if path.resolve() != contract
                ),
            )

    def test_gallery_content_and_compiler_routing_are_declarative(self):
        recipe_path = (
            Path(__file__).resolve().parents[1]
            / "recipes"
            / "fnv-wasteland-gallery-v1.json"
        )
        gallery = _load_gallery(recipe_path)
        self.assertEqual(gallery["schema"], GALLERY_SCHEMA)
        self.assertEqual(len(gallery["subjects"]), gallery["expectedSubjectCount"])
        self.assertEqual(
            {profile["compiler"] for profile in gallery["subjectProfiles"].values()},
            set(SUBJECT_COMPILERS),
        )
        for subject in gallery["subjects"]:
            profile = gallery["subjectProfiles"][subject["profile"]]
            self.assertIn(profile["compiler"], SUBJECT_COMPILERS)
            self.assertNotIn("recordType", subject)
            self.assertEqual(Path(subject["outputFile"]).name, subject["outputFile"])

    def test_exterior_locations_require_explicit_authored_xtel_pairs(self):
        recipe_path = (
            Path(__file__).resolve().parents[1]
            / "recipes"
            / "fnv-wasteland-gallery-v1.json"
        )
        gallery = json.loads(recipe_path.read_text(encoding="utf-8"))
        exterior = next(
            location
            for location in gallery["locations"]
            if location["locationClass"] == "exterior"
        )
        exterior["scene"]["overrides"].pop("entryDoorReferenceFormId")
        with tempfile.TemporaryDirectory() as temporary:
            invalid_path = Path(temporary) / "gallery.json"
            invalid_path.write_text(json.dumps(gallery), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "authored XTEL door pair"):
                _load_gallery(invalid_path)

    def test_location_reuse_is_bound_to_recipe_configuration_and_compiler(self):
        location = {
            "id": "test-location",
            "scene": {
                "profile": "interior",
                "recipeId": "test-location-recipe",
                "expectedCellFormId": "00000001",
                "expectedInterior": True,
                "overrides": {"id": "test-location-recipe"},
                "removeFields": [],
            },
        }
        profile = {"compiler": "interior"}
        recipe = {"schema": "test-recipe/v1", "id": "test-location-recipe"}
        master_sha256 = "a" * 64
        configuration_sha256 = "b" * 64
        gallery_compiler_sha256 = "c" * 64
        asset_compiler = compiler_provenance()
        archive_stack = {
            "schema": "opennv-owned-visual-archive-stack/v1",
            "resolutionPolicy": "last-declared-containing-member-wins",
            "archives": [
                {"file": "Owned.bsa", "bytes": 1, "sha256": "e" * 64}
            ],
        }
        archive_stack_sha256 = _document_sha256(archive_stack)
        location_contract = {
            "schema": LOCATION_CONTRACT_SCHEMA,
            "manifestKey": location["id"],
            "locationId": location["id"],
            "subjectId": None,
            "sceneProfile": location["scene"]["profile"],
            "sceneCompiler": profile["compiler"],
            "sceneContractSha256": _document_sha256(location["scene"]),
            "mergedRecipeSha256": _document_sha256(recipe),
            "runtimeConfigurationSha256": configuration_sha256,
            "galleryCompilerSha256": gallery_compiler_sha256,
            "ownedArchiveStackSha256": archive_stack_sha256,
            "retailGrassObservation": None,
        }
        scene = {
            "recipe": recipe["id"],
            "cell": {"formId": "00000001", "interior": True},
            "coordinates": {"originGameUnits": [0.0, 0.0, 0.0]},
            "source": {
                "masterSha256": master_sha256,
                "ownedArchiveStack": archive_stack,
            },
            "configuration": {"sha256": configuration_sha256},
            "compiler": asset_compiler,
            "galleryLocationContract": location_contract,
        }
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            scene_path = (
                root
                / "generated"
                / "cells"
                / recipe["id"]
                / "cell-scene.json"
            )
            scene_path.parent.mkdir(parents=True)
            scene_path.write_text(json.dumps(scene), encoding="utf-8")
            reused = _reuse_compiled_location(
                location,
                profile,
                recipe,
                root,
                master_sha256,
                configuration_sha256,
                gallery_compiler_sha256,
                asset_compiler,
                archive_stack_sha256,
            )
            self.assertEqual(reused["locationContract"], location_contract)
            with self.assertRaisesRegex(ValueError, "identity mismatch"):
                _reuse_compiled_location(
                    location,
                    profile,
                    recipe,
                    root,
                    master_sha256,
                    "d" * 64,
                    gallery_compiler_sha256,
                    asset_compiler,
                    archive_stack_sha256,
                )
            del scene["galleryLocationContract"]
            scene_path.write_text(json.dumps(scene), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "identity mismatch"):
                _reuse_compiled_location(
                    location,
                    profile,
                    recipe,
                    root,
                    master_sha256,
                    configuration_sha256,
                    gallery_compiler_sha256,
                    asset_compiler,
                    archive_stack_sha256,
                )

    def test_exterior_scene_identity_is_subject_bound(self):
        location = {"id": "goodsprings", "locationClass": "exterior"}
        sunny = {"id": "sunny"}
        victor = {"id": "victor"}
        recipe = {"id": "gallery-goodsprings-cell-v1"}
        self.assertNotEqual(
            _location_scene_key(location, sunny),
            _location_scene_key(location, victor),
        )
        sunny_recipe = _subject_location_recipe(recipe, location, sunny)
        victor_recipe = _subject_location_recipe(recipe, location, victor)
        self.assertNotEqual(sunny_recipe["id"], victor_recipe["id"])
        self.assertEqual(recipe["id"], "gallery-goodsprings-cell-v1")

    def test_shot_contract_retains_actor_and_rendered_scene_identity(self):
        subject = {
            "id": "test-subject",
            "ordinal": 1,
            "label": "Test Subject",
            "locationId": "test-location",
            "referenceFormId": "00000011",
            "baseFormId": "00000010",
            "enableState": {"mode": "authored"},
            "outputFile": "01-test.png",
        }
        profile = {"recordType": "NPC_"}
        location = {
            "id": "test-location",
            "location": "Test Location",
            "locationClass": "exterior",
            "actorCellFormId": "00000040",
            "scene": {
                "expectedCellFormId": "00000020",
                "expectedWorldspaceFormId": "00000030",
                "expectedInterior": False,
            },
        }
        contract = _shot_contract(
            subject,
            profile,
            location,
            {"path": "evidence.json", "bytes": 1, "sha256": "a" * 64},
        )
        self.assertEqual(contract["schema"], SHOT_SCHEMA)
        self.assertEqual(contract["locationId"], subject["locationId"])
        self.assertEqual(contract["actor"], {"cellFormId": "00000040"})
        self.assertEqual(
            contract["scene"],
            {
                "cellFormId": "00000020",
                "worldspaceFormId": "00000030",
                "interior": False,
            },
        )

    def test_pre_evidence_capture_contract_is_recipe_derived_and_unambiguous(self):
        subject = {
            "id": "test-subject",
            "ordinal": 1,
            "label": "Test Subject",
            "locationId": "test-location",
            "referenceFormId": "00000011",
            "baseFormId": "00000010",
            "enableState": {"mode": "authored"},
            "outputFile": "01-test.png",
        }
        location = {
            "id": "test-location",
            "location": "Test Location",
            "locationClass": "exterior",
            "actorCellFormId": "00000040",
            "scene": {
                "expectedCellFormId": "00000020",
                "expectedWorldspaceFormId": "00000030",
                "expectedInterior": False,
            },
        }
        descriptor = {"path": "source.json", "bytes": 1, "sha256": "a" * 64}
        contract = _capture_shot_contract(
            subject,
            {"recordType": "NPC_"},
            location,
            descriptor,
            descriptor,
        )
        self.assertEqual(contract["schema"], CAPTURE_SHOT_SCHEMA)
        self.assertNotIn("retailEvidence", contract)
        self.assertNotIn("cellFormId", contract)
        self.assertEqual(contract["actor"], {"cellFormId": "00000040"})
        self.assertEqual(contract["scene"]["cellFormId"], "00000020")

    def test_capture_manifest_is_one_to_one_with_gallery_recipe(self):
        recipe_path = (
            Path(__file__).resolve().parents[1]
            / "recipes"
            / "fnv-wasteland-gallery-v1.json"
        )
        gallery = _load_gallery(recipe_path)
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "capture-contracts"
            manifest = prepare_capture_shots(recipe_path, output)
            self.assertEqual(manifest["schema"], CAPTURE_MANIFEST_SCHEMA)
            self.assertEqual(manifest["shotCount"], gallery["expectedSubjectCount"])
            self.assertEqual(
                [row["id"] for row in manifest["shots"]],
                [subject["id"] for subject in gallery["subjects"]],
            )
            for row in manifest["shots"]:
                shot = json.loads(Path(row["path"]).read_text(encoding="utf-8"))
                self.assertEqual(shot["schema"], CAPTURE_SHOT_SCHEMA)
                self.assertNotIn("retailEvidence", shot)


if __name__ == "__main__":
    unittest.main()
