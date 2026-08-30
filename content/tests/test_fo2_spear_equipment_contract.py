from __future__ import annotations

import json
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class Fo2SpearEquipmentContractTest(unittest.TestCase):
    def test_owned_ga_gb_states_drive_molded_player_composites_fail_closed(self) -> None:
        male = json.loads(
            (ROOT / "content/recipes/fo2-arroyo-player-presentation-v1.json")
            .read_text(encoding="utf-8")
        )["player"]["equippedWeapon"]
        female = json.loads(
            (ROOT / "content/recipes/fo2-character-start-v2.json")
            .read_text(encoding="utf-8")
        )["femalePresentation"]["equippedWeapon"]
        self.assertEqual(
            (male["itemFid"], male["itemPid"], male["weaponAnimationCode"]),
            ("0000002a", "00000007", 4),
        )
        self.assertEqual(
            (male["idleFrmLogicalPath"], male["walkFrmLogicalPath"]),
            ("art\\critters\\hmwarrga.frm", "art\\critters\\hmwarrgb.frm"),
        )
        self.assertEqual(
            (female["idleLogicalPath"], female["walkLogicalPath"]),
            ("art\\critters\\hfprimga.frm", "art\\critters\\hfprimgb.frm"),
        )
        self.assertEqual(male["geometryDisposition"], female["geometryDisposition"])

        presentation = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2ArroyoPlayerPresentation.cs"
        ).read_text(encoding="utf-8")
        confrontation = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/Temple/Fo2TempleConfrontationRuntime.cs"
        ).read_text(encoding="utf-8")
        proof = (
            ROOT
            / "runtime/src/Campaigns/Fallout2/CharacterStart/Fo2TempleConfrontationProof.cs"
        ).read_text(encoding="utf-8")
        self.assertIn('ExpectedEquippedIdleLogicalPath = "art\\\\critters\\\\hmwarrga.frm"', presentation)
        self.assertIn('ExpectedEquippedWalkLogicalPath = "art\\\\critters\\\\hmwarrgb.frm"', presentation)
        self.assertIn("SetSpearEquipped(_contract.DefeatLoot, _state.SpearEquipped)", confrontation)
        self.assertIn("Fo2FrmReliefMesh.Build(", presentation)
        self.assertIn("sourcePixelsOnly: false", presentation)
        self.assertIn("frame.DirectionOffset + frame.FrameOffset", presentation)
        self.assertIn("BillboardModeEnum.FixedY", presentation)
        self.assertIn('SetMeta("source_composite_includes_spear", equipped)', presentation)
        self.assertIn("internal bool UsesOwnedDonor => false", presentation)
        self.assertIn("internal bool UsesOwnedFrmRelief => true", presentation)
        self.assertIn("internal bool EquippedWeaponGeometryVisible => false", presentation)
        self.assertIn("PlayerEquippedCompositeVisible", confrontation)
        self.assertIn('PlayerSourceAnimationCode != "GA"', proof)
        self.assertIn('PlayerSourceAnimationCode != "AA"', proof)
        self.assertIn("maleAndFemaleMoldedReliefsExercised = true", proof)
        self.assertIn("maleEquippedWalkRelief", proof)
        self.assertIn("femaleEquippedWalkRelief", proof)
        self.assertIn("sourceCompositeIncludesSpear", proof)
        self.assertIn("separableWeaponGeometry = false", proof)
        self.assertIn(".FirstOrDefault()", proof)
        self.assertIn('selected.Character.Profile.Sex != "Female"', proof)
        self.assertIn('selectedIdentity.Profile.Sex != "Female"', proof)
        self.assertIn("Fo2CharacterStartCatalog.FemaleLogicalPath", proof)


if __name__ == "__main__":
    unittest.main()
