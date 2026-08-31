from __future__ import annotations

import unittest
from pathlib import Path
from content.tests.csharp_source_module import read_csharp_source_module


ROOT = Path(__file__).resolve().parents[2]
FO1 = ROOT / "runtime" / "src" / "Campaigns" / "Fallout1"


class Fo1NativeFirstBeatCombatContractTest(unittest.TestCase):
    def test_first_beat_uses_admitted_adjacent_rat_and_melee_provenance(self) -> None:
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        start = flow.index("private static async Task<object> RunNativeFirstBeatAdjacentRatEngagement(")
        end = flow.index("private static NativeFirstBeatSavedCombat", start)
        engagement = flow[start:end]

        self.assertIn("NearestLiving(session)", engagement)
        self.assertIn("MoveTacticalAdjacentToTarget(host, loaded, target)", engagement)
        self.assertIn("Fo1HexMath.AreNeighbors(session.PlayerTile, target.Tile)", engagement)
        self.assertIn("var weapon = session.MeleeWeapon;", engagement)
        self.assertIn("session.AttackSelectedMelee()", engagement)
        self.assertIn(
            "target.HitPoints != successfulTargetHitPointsBefore - result.Damage",
            engagement,
        )
        self.assertIn(
            "session.ActionPoints != successfulActionPointsBefore - weapon.ActionPointCost",
            engagement,
        )
        self.assertIn("prototypeSha256 = weapon.PrototypeSha256", engagement)
        self.assertIn("prototypeSha256 = target.PrototypeSha256", engagement)
        self.assertIn("sourceWalkMaskOnly = true", engagement)
        self.assertIn("pathTiles = approachPath", engagement)
        self.assertIn("contactIsAdjacent = Fo1HexMath.AreNeighbors", engagement)
        self.assertIn("session.RestoreSaveForProof()", engagement)
        self.assertIn("CompleteQueuedTacticalMovementForHeadlessProof", flow)
        self.assertIn("sourceAttemptLimit", engagement)
        self.assertIn("attempts.Add(new", engagement)
        self.assertIn("successfulAttemptIndex = attempts.Count - 1", engagement)
        self.assertIn("persistence = new", engagement)
        self.assertIn("matched = true", engagement)
        self.assertNotIn("PID_", engagement)
        self.assertNotIn("Knife", engagement)
        self.assertNotIn("Giant Rat", engagement)

    def test_combat_save_is_checked_against_the_live_source_bound_result(self) -> None:
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))

        proof_start = flow.index("private static async Task CompleteNativeFirstBeatProof(")
        proof_end = flow.index("private static async Task<object> RunNativeFirstBeatAdjacentRatEngagement(")
        proof = flow[proof_start:proof_end]

        self.assertIn("var adjacentRatEngagement = await RunNativeFirstBeatAdjacentRatEngagement(", proof)
        self.assertIn("adjacentRatEngagement,", proof)
        self.assertLess(
            proof.index("RunNativeFirstBeatAdjacentRatEngagement("),
            proof.index("SaveNativeCapture("),
        )
        self.assertIn("ReadNativeFirstBeatCombatSave(session.SavePath, target.Serial)", flow)
        self.assertIn("persisted.TargetHitPoints != target.HitPoints", flow)
        self.assertIn('root.GetProperty("meleeAttacks")', flow)
        self.assertIn('root.GetProperty("meleeHits")', flow)
        self.assertIn('root.GetProperty("equippedWeaponSymbol")', flow)
        self.assertIn("mobs = _mobs.Select(mob => mob.Report()).ToArray(),", session)
        self.assertIn("meleeAttacks = _meleeAttacks,", session)
        self.assertIn("meleeHits = _meleeHits,", session)
        self.assertIn("internal void RestoreSaveForProof()", session)
        self.assertIn("Load();", session)
        self.assertIn("internal void CompleteQueuedTacticalMovementForHeadlessProof()", session)
        self.assertIn("CommitQueuedTacticalMovementStep(targetTile);", session)

    def test_map_inventory_pickup_equip_use_is_source_bound_and_persistent(self) -> None:
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        loader = (FO1 / "Fo1HexSceneLoader.cs").read_text(encoding="utf-8")
        generator = (ROOT / "content" / "tools" / "prepare_fo1_hex_scene.py").read_text(
            encoding="utf-8"
        )
        wrapper = (ROOT / "scripts" / "Test-OpenNVFallout1NativeFirstBeat.ps1").read_text(
            encoding="utf-8"
        )

        start = flow.index("private static async Task<NativeFirstBeatMapInventoryPickup>")
        end = flow.index("private static async Task<object> RunNativeFirstBeatAdjacentRatEngagement(", start)
        pickup = flow[start:end]
        self.assertIn("session.MapInventoryHosts", pickup)
        self.assertIn("MoveTacticalAdjacentToMapInventoryHost(host, loaded, mapHost)", pickup)
        self.assertIn("session.PickupAdjacentMapInventoryHost(mapHost.Serial)", pickup)
        self.assertIn("session.SwapEquippedWeapon()", pickup)
        self.assertIn("EquipLootedMapInventoryWeaponForHeadlessProof", pickup)
        self.assertIn("ReadNativeFirstBeatMapInventorySave", pickup)
        self.assertIn("session.RestoreSaveForProof()", pickup)
        self.assertIn("sourceWalkMaskOnly = true", pickup)
        self.assertIn("contactIsAdjacent = Fo1HexMath.AreNeighbors", pickup)
        self.assertNotIn("000000d3", pickup)
        self.assertNotIn("17488", pickup)
        self.assertNotIn("PID_KNIFE", pickup)

        self.assertIn('"mapInventoryHosts": map_inventory_hosts', generator)
        self.assertIn('inventory = obj["inventory"]', generator)
        self.assertIn('"opennv-fo1-map-inventory-host/v1"', generator)
        self.assertIn("symbols_by_pid", generator)
        self.assertIn('combat.GetProperty("mapInventoryHosts")', loader)
        self.assertIn("ReadMapInventoryHost", loader)
        walk_mask_start = loader.index("var walkable = new bool[Fo1HexMath.Width * Fo1HexMath.Height];")
        walk_mask_end = loader.index("var walkableCount = walkable.Count", walk_mask_start)
        walk_mask = loader[walk_mask_start:walk_mask_end]
        self.assertIn("!blocked.Contains(tile);", walk_mask)
        self.assertNotIn("!presentationBlocked.Contains(tile)", walk_mask)
        self.assertIn("PickupAdjacentMapInventoryHost", session)
        self.assertIn("lootedMapInventoryHostSerials", session)
        self.assertIn("EquipLootedMapInventoryWeaponForHeadlessProof", session)
        self.assertIn("pass-source-bound-pickup-equip-use", wrapper)
        self.assertIn("$pickup.pickup.equippedWeaponSymbol -ne $pickup.WeaponSymbol", wrapper)
        self.assertIn("$pickup.use.weapon.pid -ne $pickup.WeaponPid", wrapper)

    def test_headless_proof_is_explicit_and_never_requests_capture_output(self) -> None:
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        launch_validation = (
            ROOT / "runtime" / "src" / "RuntimeLaunchValidator.cs"
        ).read_text(encoding="utf-8")
        wrapper = (ROOT / "scripts" / "Test-OpenNVFallout1NativeFirstBeat.ps1").read_text(
            encoding="utf-8"
        )

        self.assertIn("CompleteNativeFirstBeatHeadlessProof", flow)
        self.assertIn("files = Array.Empty<object>()", flow)
        self.assertIn("rendered = false", flow)
        self.assertIn("if (!nativeFirstBeatHeadlessProof)\n            await WaitFrames(host, 1);", flow)
        self.assertIn("if (!nativeFirstBeatHeadlessProof)\n                await WaitFrames(host, 2);", flow)
        self.assertIn("--fo1-native-first-beat-proof", launch_validation)
        self.assertIn("cannot use --capture-root", launch_validation)
        self.assertIn("--fo1-native-first-beat-proof", wrapper)
        self.assertIn("--headless", wrapper)
        self.assertNotIn("capture-root", wrapper)
        self.assertIn("ClassicHumanoidInstallManifest", wrapper)
        demo_start = flow.index("internal static async Task RunDemo(")
        demo_end = flow.index("private static async Task CompleteInteractive(", demo_start)
        demo = flow[demo_start:demo_end]
        self.assertIn("if (nativeFirstBeatHeadlessProof)", demo)
        self.assertIn("default,", demo)
        self.assertLess(
            demo.index("if (nativeFirstBeatHeadlessProof)"),
            demo.index("var landing = await RevealWorld"),
        )

    def test_fo1_scene_cache_carries_the_explicit_shared_donor_join(self) -> None:
        generator = (ROOT / "content" / "tools" / "prepare_fo1_hex_scene.py").read_text(
            encoding="utf-8"
        )
        loader = (FO1 / "Fo1HexSceneLoader.cs").read_text(encoding="utf-8")

        self.assertIn("load_classic_humanoid_donor", generator)
        self.assertIn('"sharedHumanoidDonor": classic_humanoid_donor', generator)
        self.assertIn("--classic-humanoid-donor-preview-set", generator)
        self.assertIn("shared classic humanoid donor is incomplete for", generator)
        self.assertIn("VerifyClassicHumanoidDonorJoin(", loader)
        self.assertIn("classicHumanoidDonor.ForSex(\"male\")", loader)
        self.assertIn("shared classic humanoid donor {sex} join drifted", loader)

    def test_critter_prototype_hash_is_required_from_the_compiled_combat_profile(self) -> None:
        loader = (FO1 / "Fo1HexSceneLoader.cs").read_text(encoding="utf-8")
        mob = (FO1 / "Fo1Mob.cs").read_text(encoding="utf-8")

        self.assertIn('RequiredString(profile, "prototypeSha256")', loader)
        self.assertIn("string prototypeSha256,", mob)
        self.assertIn("internal string PrototypeSha256", mob)
        self.assertIn("Fallout critter has no hash-bound source prototype.", mob)
        self.assertIn("prototypeSha256 = PrototypeSha256,", mob)

    def test_cave_loot_uses_the_existing_owned_inventory_and_hud_controls(self) -> None:
        flow = read_csharp_source_module((FO1 / "Fo1NewGameFlow.cs"))
        screen = (FO1 / "Fo1ClassicInventoryScreen.cs").read_text(encoding="utf-8")
        session = read_csharp_source_module((FO1 / "Fo1TacticalSession.cs"))
        loader = (FO1 / "Fo1HexSceneLoader.cs").read_text(encoding="utf-8")
        recipe = (ROOT / "content" / "recipes" / "fo1-character-start-v1.json").read_text(
            encoding="utf-8"
        )
        wrapper = (ROOT / "scripts" / "Test-OpenNVFallout1NativeFirstBeat.ps1").read_text(
            encoding="utf-8"
        )

        start = flow.index("private static object RunNativeFirstBeatClassicInventoryHudProof(")
        end = flow.index("private static async Task<NativeFirstBeatMapInventoryPickup>", start)
        proof = flow[start:end]
        self.assertIn("RunNativeFirstBeatMapInventoryPickup(", flow)
        self.assertIn("RunNativeFirstBeatClassicInventoryHudProof(", flow)
        self.assertLess(
            flow.index("RunNativeFirstBeatMapInventoryPickup("),
            flow.index("RunNativeFirstBeatClassicInventoryHudProof("),
        )
        self.assertIn("session.InventoryKey", proof)
        self.assertIn("inventory.SelectSourceInventorySymbolForProof(rangedSymbol)", proof)
        self.assertIn("inventory.EquipSourceActiveHandForProof(rangedSymbol)", proof)
        self.assertIn("inventory.SelectSourceInventorySymbolForProof(meleeSymbol)", proof)
        self.assertIn("inventory.EquipSourceActiveHandForProof(meleeSymbol)", proof)
        self.assertIn("PhysicalKeycode = Key.Escape", proof)
        self.assertIn("ReadNativeFirstBeatClassicInventoryUiSave", proof)
        self.assertIn("session.RestoreSaveForProof()", proof)
        self.assertIn("sourceInventory = inventory.Report()", proof)
        self.assertIn("sourceHud = hud.Report()", proof)
        self.assertNotIn("PID_", proof)
        self.assertNotIn("10MM", proof)
        self.assertNotIn("Knife", proof)

        self.assertIn("SelectSourceInventorySymbolForProof", screen)
        self.assertIn("_rowButtons[slot].EmitSignal(Button.SignalName.Pressed)", screen)
        self.assertIn("EquipSourceActiveHandForProof", screen)
        self.assertIn("button.EmitSignal(Button.SignalName.Pressed)", screen)
        self.assertIn("OwnedInventoryRangedHandButton", screen)
        self.assertIn("OwnedInventoryMeleeHandButton", screen)
        self.assertIn("internal Fo1ClassicHud? ClassicHud", session)
        self.assertIn("internal string RangedWeaponSymbol", session)
        self.assertIn("inventoryDisplayNames.TryAdd(item.Symbol, item.DisplayName)", loader)
        self.assertIn('"displayName": display_names.get(symbol, symbol)',
                      (ROOT / "content" / "tools" / "prepare_fo1_hex_scene.py").read_text(encoding="utf-8"))
        self.assertIn('"PID_10MM_AP"', recipe)
        self.assertIn("10MMAP.FRM", recipe)
        self.assertIn("classicInventoryHud", wrapper)
        contract = (FO1 / "Fo1CharacterStartContract.cs").read_text(encoding="utf-8")
        self.assertIn("if (classicInventoryItemTextures.Count == 0)", contract)
        self.assertNotIn("SourcePresentationInt13", contract)


if __name__ == "__main__":
    unittest.main()
