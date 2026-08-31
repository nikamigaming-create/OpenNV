from __future__ import annotations

import json
import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module


ROOT = Path(__file__).resolve().parents[2]
FO1_PREVIEW = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1" / "Fo1PremadePlayerPreview.cs"
FO1_SESSION = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1" / "Fo1TacticalSession.cs"
FO1_LOADER = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1" / "Fo1HexSceneLoader.cs"
FO2_DONOR = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "Temple" / "Fo2HumanoidPresentation.cs"
ACTOR_COMPLEXION_MATH = ROOT / "runtime" / "src" / "Presentation" / "Actors" / "ActorComplexionMath.cs"
FO2_PLAYER = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "Temple" / "Fo2ArroyoPlayerPresentation.cs"
FO2_RUNTIME = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "Temple" / "Fo2ArroyoCavesPlayerRuntime.cs"
FO2_CHARACTER_CONTRACT = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "CharacterStart" / "Fo2CharacterStartContract.cs"
FO2_CHARACTER_SAVE = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "CharacterStart" / "Fo2CharacterStartSave.cs"
FO2_CHARACTER_EDITOR = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "CharacterStart" / "Fo2CustomCharacterEditor.cs"
FO2_CHARACTER_PICKER = ROOT / "runtime" / "src" / "Campaigns" / "Fallout2" / "CharacterStart" / "Fo2CharacterPicker.cs"
REFLECTRON_DEVICE = ROOT / "runtime" / "src" / "Campaigns" / "NewVegas" / "Opening" / "OpeningRaceSexRenderedDeviceHost.cs"
FO2_APPEARANCE = ROOT / "runtime" / "config" / "fo2-procedural-appearance-v3.json"
RETAIL_ACTOR_MATERIAL = (
    ROOT / "runtime" / "src" / "Presentation" / "Rendering" / "RetailActorMaterial.cs"
)
RETAIL_FACEGEN_MATERIAL = (
    ROOT / "runtime" / "src" / "Presentation" / "Rendering" / "RetailFaceGenMaterial.cs"
)
CLASSIC_ANALOG_CAST = ROOT / "content" / "recipes" / "classic-premade-analog-cast-v1.json"
CLASSIC_ANALOG_BUILDER = ROOT / "content" / "tools" / "prepare_classic_premade_analogs.py"
CLASSIC_ANALOG_PROOF = ROOT / "runtime" / "src" / "Campaigns" / "Classic" / "ClassicPremadeAnalogProofHost.cs"


class ClassicHumanoidRuntimeSourceTest(unittest.TestCase):
    def test_fo1_has_no_procedural_humanoid_route(self) -> None:
        preview = FO1_PREVIEW.read_text(encoding="utf-8")
        session = read_csharp_source_module(FO1_SESSION)

        self.assertIn("owned-humanoid-donor-unavailable-fail-closed", preview)
        self.assertIn("no-substitute-humanoid-rendered-donor-selection-mismatch", preview)
        self.assertNotIn("Fo1FirstPartyHumanoid", preview)
        self.assertNotIn("Fo1FirstPartyHumanoid", session)
        self.assertIn("has no compatible hash-bound owned humanoid donor", session)
        loader = FO1_LOADER.read_text(encoding="utf-8")
        self.assertIn('new[] { "male", "female" }', loader)
        self.assertIn("PlayerDonors", loader)
        self.assertIn("SelectOwnedPlayerDonor(identity.CharacterId, profile.Sex)", session)
        self.assertIn('donor.ForClassicCharacter("fallout1", characterId, sex)', loader)
        self.assertIn("source.BodyProfile is { } bodyProfile", preview)
        self.assertIn("ApplyRetailActorLighting(", preview)
        self.assertIn("premade preview donor has no source-lit materials", preview)
        self.assertIn("ClassicGreenWireframeShader.Create(", preview)
        self.assertIn('FindBone("Bip01 Head")', preview)
        self.assertIn("front-centered-green-head-portrait", preview)
        self.assertIn("source-bound weapon/socket contracts", session)

    def test_all_six_premades_have_explicit_person_outfit_and_body_bindings(self) -> None:
        cast = json.loads(CLASSIC_ANALOG_CAST.read_text(encoding="utf-8"))
        characters = cast["characters"]

        self.assertEqual("opennv-classic-premade-analog-cast/v1", cast["schema"])
        self.assertEqual(6, len(characters))
        self.assertEqual(
            {
                "fallout1:max-stone",
                "fallout1:natalia",
                "fallout1:albert",
                "fallout2:combat",
                "fallout2:stealth",
                "fallout2:diplomat",
            },
            {f'{row["campaign"]}:{row["characterId"]}' for row in characters},
        )
        self.assertEqual(6, len({row["sourceActorFormId"] for row in characters}))
        for row in characters:
            self.assertRegex(row["sourceActorFormId"], r"^[0-9a-f]{8}$")
            self.assertRegex(row["outfitFormId"], r"^[0-9a-f]{8}$")
            self.assertTrue(row["sourceActorName"])
            self.assertTrue(row["outfitName"])
            self.assertTrue(row["bodyProfile"]["id"])
            self.assertTrue(
                (ROOT / "content" / "recipes" / f'{row["recipe"]}.json').is_file()
            )

        builder = CLASSIC_ANALOG_BUILDER.read_text(encoding="utf-8")
        donor = FO2_DONOR.read_text(encoding="utf-8")
        proof = CLASSIC_ANALOG_PROOF.read_text(encoding="utf-8")
        self.assertIn('OUTPUT_SCHEMA = "opennv-owned-player-facegen-preview-set/v4"', builder)
        self.assertIn('output["premadeAnalogs"] = analogs', builder)
        self.assertIn("RequiredAnalogKeys", donor)
        self.assertIn("ForClassicCharacter", donor)
        self.assertIn("pass-six-exact-owned-analog-bindings-centered", proof)

    def test_fo2_consumes_hash_bound_modular_full_body_donor(self) -> None:
        donor = FO2_DONOR.read_text(encoding="utf-8")
        player = FO2_PLAYER.read_text(encoding="utf-8")
        runtime = FO2_RUNTIME.read_text(encoding="utf-8")
        complexion_math = ACTOR_COMPLEXION_MATH.read_text(encoding="utf-8")

        self.assertIn("opennv-owned-player-facegen-preview-set/v3", donor)
        self.assertIn('RequiredBodyRoles = ["body", "left-hand", "right-hand"]', donor)
        self.assertIn("presentationOutfitFormId", donor)
        self.assertIn("rigidAttachmentNode", donor)
        self.assertIn("classic-humanoid-donor-preview-set", donor)
        self.assertIn("RequireFromOptions", donor)
        self.assertIn("new Fo2HumanoidVisual(", runtime)
        self.assertIn("Live3DPresentationOutfitFormId", runtime)
        self.assertIn("source-role 3D binding", runtime)
        self.assertIn("_presentation.Visible = false;", runtime)
        self.assertIn(
            "selected character and owned humanoid donor must be bound together",
            runtime,
        )
        self.assertIn("opennv-retail-actor-skin-material/v1", donor)
        self.assertIn("owned-nif-bs-shader-type-shaderskin", donor)
        self.assertIn("head-paired-cheek-uv-islands", donor)
        self.assertNotIn("upperbodymale.dds", donor)
        self.assertNotIn("upperbodyfemale.dds", donor)

        material = RETAIL_ACTOR_MATERIAL.read_text(encoding="utf-8")
        self.assertIn("opennv-retail-actor-skin-material/v1", material)
        self.assertIn("skin_complexion_target", material)
        self.assertIn("skin_encoded_to_linear", material)

        facegen_material = RETAIL_FACEGEN_MATERIAL.read_text(encoding="utf-8")
        self.assertIn("use_neck_complexion_target", facegen_material)
        self.assertIn("use_complexion_target", facegen_material)
        self.assertIn("complexion_target", facegen_material)
        self.assertIn("neck_complexion_uv_bounds", facegen_material)
        self.assertIn('"use_neck_complexion_target", false', facegen_material)
        self.assertIn('"use_neck_complexion_target", true', donor)
        self.assertIn("ActorComplexionMath.AverageFaceGenEncodedNeckColor", donor)
        self.assertIn("AverageFaceGenEncodedNeckColor", complexion_math)

    def test_fo2_character_body_state_reaches_the_gameplay_humanoid(self) -> None:
        contract = FO2_CHARACTER_CONTRACT.read_text(encoding="utf-8")
        save = FO2_CHARACTER_SAVE.read_text(encoding="utf-8")
        editor = FO2_CHARACTER_EDITOR.read_text(encoding="utf-8")
        appearance = FO2_APPEARANCE.read_text(encoding="utf-8")
        runtime = FO2_RUNTIME.read_text(encoding="utf-8")
        donor = FO2_DONOR.read_text(encoding="utf-8")

        self.assertIn("CharacterBodyProportions BodyProportions", contract)
        self.assertIn("opennv-fo2-character-appearance/v6", contract)
        self.assertIn("opennv-fo2-character-arroyo-save/v14", save)
        self.assertIn("appearance = new", save)
        self.assertIn('GetProperty("BodyProportions")', save)
        self.assertIn("SetBodyProportion", editor)
        self.assertIn("_livePreview.SetProportions(_bodyProportions)", editor)
        self.assertIn("_livePreview.SetAppearance(new Fo2HumanoidAppearance", editor)
        self.assertIn("selectedCharacter.Appearance.BodyProportions", runtime)
        self.assertIn("nativeFaceGenControls", appearance)
        self.assertIn("ApplyNativeFaceGenControl", donor)
        self.assertIn("selection.Appearance.CustomFaceEdited", donor)

    def test_fo2_public_creator_exposes_face_and_source_rules(self) -> None:
        editor = FO2_CHARACTER_EDITOR.read_text(encoding="utf-8")
        picker = FO2_CHARACTER_PICKER.read_text(encoding="utf-8")
        reflectron = REFLECTRON_DEVICE.read_text(encoding="utf-8")

        self.assertIn('"FACE", faceCenter, showPortrait, frame', reflectron)
        self.assertIn('AddEmbossedDeviceText(\n            "FACE"', reflectron)
        for section in ("sex", "race", "face", "hair"):
            self.assertIn(
                f'AddSourceSectionHitTarget(\n                "{section}"',
                reflectron,
            )
        self.assertIn("ShowSexEditor,", editor)
        self.assertIn("ShowRaceEditor,", editor)
        self.assertIn("ShowFaceEditor,", editor)
        self.assertIn("ShowHairEditor,", editor)
        self.assertIn("ShowClassicPortrait,", editor)
        self.assertIn('ActivateCreatorModeControl("BODY")', editor)
        self.assertIn("ShowRulesEditor", editor)
        self.assertIn("SetTaggedSkills", editor)
        self.assertIn("SetTraits", editor)
        self.assertIn("Fo2CharacterSelection.CreateMode", editor)
        self.assertNotIn("Fo2CharacterSelection.ModifyMode", editor)
        self.assertIn("internal Fo2CustomCharacterEditor OpenCustom()", picker)
        self.assertNotIn("OpenCustom(bool modify)", picker)


if __name__ == "__main__":
    unittest.main()
