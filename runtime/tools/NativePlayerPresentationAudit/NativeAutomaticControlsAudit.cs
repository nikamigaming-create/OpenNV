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
}
