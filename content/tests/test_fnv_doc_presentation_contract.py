from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class FnvDocPresentationContractTest(unittest.TestCase):
    def test_patient_bed_identity_is_hash_bound_in_the_owned_recipe(self) -> None:
        recipe = json.loads(
            (ROOT / "content" / "recipes" / "fnv-new-game-opening-v1.json")
            .read_text(encoding="utf-8")
        )
        patient_bed = recipe["newGameFlow"]["guideActorAi"][
            "furnitureOccupancy"
        ]["patientBed"]

        self.assertEqual("00103e5b", patient_bed["referenceFormId"])
        self.assertEqual("00106a6a", patient_bed["baseFormId"])
        self.assertEqual("NVbedtwin01", patient_bed["editorId"])
        self.assertEqual(
            "634a415dd741804d5a13ea8ed60fee7362068b6c1d98f27a359f411bd47b3b37",
            patient_bed["referenceRecordSha256"],
        )
        self.assertEqual(
            "2e239593556c1d76cf030921ab6f140c9454de2b81dcfe8324e43bf5a2d8f130",
            patient_bed["recordSha256"],
        )
        self.assertEqual(
            "51b0f74cf82871e237fe64d440fbbaa77be82d1e4c530fb165acb68afbe9e33d",
            patient_bed["modelSha256"],
        )

    def test_visual_acceptance_resolves_only_the_exact_patient_bed(self) -> None:
        source = (
            ROOT
            / "runtime"
            / "src"
            / "Campaigns"
            / "NewVegas"
            / "Opening"
            / "OpeningQuestVisualCapture.cs"
        ).read_text(encoding="utf-8")
        exact_resolution = source[source.index("var patientBedSource") : source.index(
            "var collisionRoot", source.index("var patientBedSource")
        )]

        self.assertIn("FurnitureOccupancy.PatientBed", exact_resolution)
        self.assertIn("patientBedSource.ReferenceFormId", exact_resolution)
        self.assertIn("patientBedSource.BaseFormId", exact_resolution)
        self.assertIn("patientBedSource.EditorId", exact_resolution)
        self.assertIn("patientBedMatches.Length != 1", exact_resolution)
        self.assertNotIn('Contains("bed"', exact_resolution)
        self.assertNotIn("OrderBy", exact_resolution)

    def test_doc_package_travel_advances_the_owned_locomotion_clip(self) -> None:
        source = (
            ROOT
            / "runtime"
            / "src"
            / "Campaigns"
            / "NewVegas"
            / "Opening"
            / "OpeningQuestRuntime.Guide.cs"
        ).read_text(encoding="utf-8")
        start = source[source.index("private void StartGuideLocomotionAnimation") :]
        update = source[source.index("private void UpdateGuideActor") : source.index(
            "private static SourceActorAnimation", source.index("private void UpdateGuideActor")
        )]
        finish = source[source.index("private void FinishGuideTravel") : source.index(
            "private void PlayGuidePackageIdle", source.index("private void FinishGuideTravel")
        )]

        self.assertIn("ActorAnimationPlayback.Start(", start)
        self.assertIn("locomotion.LogicalPath", start)
        self.assertIn("locomotion.Sha256", start)
        self.assertIn("rootMotion.SequenceName", start)
        self.assertLess(update.index("playback.Advance(delta)"), update.index("travel.Advance(delta)"))
        self.assertIn("_guideLocomotionPlayback?.Stop()", finish)
        self.assertIn("AnimationCallbackModeProcess.Idle", source)

    def test_furniture_session_publishes_the_owned_seated_pose_before_placement(self) -> None:
        source = (
            ROOT
            / "runtime"
            / "src"
            / "World"
            / "Actors"
            / "GamebryoFurnitureSession.cs"
        ).read_text(encoding="utf-8")
        occupy = source[source.index("internal static GamebryoFurnitureSession Occupy") :]

        start = occupy.index("ActorAnimationPlayback.Start(")
        placement = occupy.index("GamebryoPackagePlacement.Publish(actor, placement)")
        self.assertLess(start, placement)
        self.assertIn("source.Loop", occupy[start:placement])
        self.assertIn("loopPositionSeconds", occupy[start:placement])

    def test_doc_look_player_uses_the_owned_presented_player_viewpoint(self) -> None:
        opening = ROOT / "runtime" / "src" / "Campaigns" / "NewVegas" / "Opening"
        guide = (opening / "OpeningQuestRuntime.Guide.cs").read_text(encoding="utf-8")
        stages = (opening / "OpeningQuestRuntime.StagePresentation.cs").read_text(
            encoding="utf-8"
        )
        visual_acceptance = (opening / "OpeningQuestVisualCapture.cs").read_text(
            encoding="utf-8"
        )

        self.assertIn("_loaded.Player.Camera.GlobalPosition", guide)
        self.assertNotIn("FaceGuideToward(_loaded.Player.GlobalPosition)", guide)
        self.assertNotIn("FaceGuideToward(_loaded.Player.GlobalPosition)", stages)
        self.assertIn("host.GuidePlayerLookTarget() - actorOrigin", visual_acceptance)


if __name__ == "__main__":
    unittest.main()
