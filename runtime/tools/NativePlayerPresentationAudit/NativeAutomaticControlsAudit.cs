using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerPresentationAudit
{
    private async Task AuditAutomaticControls(FalloutPluginStack records, RuntimeNativePlayer player, FalloutPlayerInventory inventory)
    {
        var weapon = FalloutWeaponPresentation.Read(records, records.RuntimeFormKey(0x8f21e));
        inventory.Add(records, weapon.Form, 1, 1, true);
        inventory.Add(records, weapon.Ammunition[0], 120, 1, true);
        inventory.Equip(records, weapon.Form);
        for (var frame = 0; frame < 180; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        using var reload = new InputEventKey { PhysicalKeycode = Key.R, Pressed = true };
        using var reloadRelease = new InputEventKey { PhysicalKeycode = Key.R, Pressed = false };
        player._UnhandledInput(reload); player._UnhandledInput(reloadRelease);
        for (var frame = 0; frame < 240; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var before = inventory.Item(weapon.Ammunition[0])!.Count;
        using var fire = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
        using var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false };
        player._UnhandledInput(fire);
        for (var frame = 0; frame < 60; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        player._UnhandledInput(release);
        for (var frame = 0; frame < 5; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var after = inventory.Item(weapon.Ammunition[0])!.Count;
        for (var frame = 0; frame < 90; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var firing = JsonSerializer.SerializeToElement(player.FiringState);
        if (before - after < 2 || inventory.Item(weapon.Ammunition[0])!.Count != after ||
            firing.GetProperty("error").ValueKind != JsonValueKind.Null || firing.GetProperty("effectErrors").EnumerateObject().Any())
            throw new InvalidOperationException("Continuous automatic AttackLoop did not consume rounds while held and stop on release: " + firing);
        GD.Print($"OPENNV_AUTOMATIC_CONTROL_PASS weapon={weapon.Form} rounds={before - after} held=true released=true sourceCadence=true");
    }
    private async Task AuditIncrementalReloadControls(FalloutPluginStack records, RuntimeNativePlayer player, FalloutPlayerInventory inventory)
    {
        var weapon = FalloutWeaponPresentation.Read(records, records.RuntimeFormKey(0x8f216));
        inventory.Add(records, weapon.Form, 1, 1, true);
        var ammunition = weapon.Ammunition[0];
        inventory.Add(records, ammunition, 12, 1, true);
        inventory.Equip(records, weapon.Form);
        for (var frame = 0; frame < 180; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        using var reload = new InputEventKey { PhysicalKeycode = Key.R, Pressed = true };
        using var reloadRelease = new InputEventKey { PhysicalKeycode = Key.R, Pressed = false };
        using var fire = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
        using var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false };
        int Loaded() => inventory.SelectedWeapon(weapon.Form).Extra.Magazine?.Loaded ?? 0;
        long Shots() => JsonSerializer.SerializeToElement(player.FiringState).GetProperty("shots").GetInt64();
        var carried = inventory.Item(ammunition)!.Count;
        var shots = Shots();
        player._UnhandledInput(reload); player._UnhandledInput(reloadRelease);
        player._UnhandledInput(fire); player._UnhandledInput(release);
        if (Loaded() != 0) throw new InvalidOperationException("Empty-loop fire invented an insert before its source event.");
        for (var frame = 0; Loaded() == 0 && frame < 300; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        if (Loaded() != 1 || inventory.Item(ammunition)!.Count != carried || Shots() != shots)
            throw new InvalidOperationException("Incremental opening/first event did not insert exactly one actual round.");
        player._UnhandledInput(fire); player._UnhandledInput(release);
        for (var frame = 0; frame < 5; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        if (Loaded() != 2 || inventory.Item(ammunition)!.Count != carried || Shots() != shots)
            throw new InvalidOperationException("Explicit loop stop lost its admitted round or dispatched an unrequested shot.");
        player._UnhandledInput(reload); player._UnhandledInput(reloadRelease);
        var previous = Loaded();
        for (var frame = 0; Loaded() < weapon.ClipSize && frame < 600; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var current = Loaded();
            if (current < previous || current > previous + 1 || inventory.Item(ammunition)!.Count != carried)
                throw new InvalidOperationException("Incremental loop skipped a source round or consumed carried ammunition.");
            previous = current;
        }
        var handling = JsonSerializer.SerializeToElement(player.WeaponHandlingState);
        if (Loaded() != weapon.ClipSize || handling.GetProperty("incrementalReload").GetProperty("phase").GetString() != "Closing" ||
            handling.GetProperty("firstPerson").GetProperty("path").ValueKind != JsonValueKind.String ||
            handling.GetProperty("thirdPerson").GetProperty("path").ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Final insert did not preserve the actual first/third-person closing tails.");
        player._UnhandledInput(fire); player._UnhandledInput(release);
        for (var frame = 0; Shots() == shots && frame < 180; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        if (Shots() != shots + 1 || Loaded() != weapon.ClipSize - 1 || inventory.Item(ammunition)!.Count != carried - 1)
            throw new InvalidOperationException("Completed logical reload tail blocked a newly accepted ordinary attack.");
        GD.Print("OPENNV_INCREMENTAL_RELOAD_CONTROL_PASS emptyStopRefused=true insertOne=true stopPrefix=true carriedConserved=true finalTail=true newAttack=true fixture=true pixels=unverified");
    }

}
