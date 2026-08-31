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


if __name__ == "__main__":
    unittest.main()
