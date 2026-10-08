using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.InputSystem;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerPresentationAudit
{
    private async Task AuditXrWeaponControls(FalloutPluginStack records, RuntimeNativePlayer player,
        FalloutPlayerInventory inventory, RuntimeConfiguration configuration)
    {
        var weapon = FalloutWeaponPresentation.Read(records, records.RuntimeFormKey(0x4335));
        inventory.Add(records, weapon.Form, 1, 1, true);
        inventory.Add(records, weapon.Ammunition[0], 30, 1, true);
        inventory.Equip(records, weapon.Form);
        await Frames(180);
        var actions = configuration.Player.DesktopInput;
        // XR actions must retain their meaning even with no desktop events.
        foreach (var action in new[] { actions.Fire.Action, actions.Reload.Action })
        {
            InputMap.EraseAction(action);
            InputMap.AddAction(action);
        }
        try
        {
            player.XrReload(true); player.XrReload(false);
            await Frames(240);
            var handling = player.CaptureWeaponHandling()!;
            if (handling.Magazines.Count != 1 || handling.Magazines[0].Loaded != weapon.ClipSize)
                throw new InvalidOperationException("XR reload did not fill its source magazine without desktop bindings.");
            var before = inventory.Item(weapon.Ammunition[0])!.Count;
            player.SetMeta("opennv_source_fighting_enabled", false);
            player.XrFire(true); player.XrFire(false);
            await Frames(60);
            if (Shots() != 0 || inventory.Item(weapon.Ammunition[0])!.Count != before)
                throw new InvalidOperationException("XR fire bypassed the source fighting gate.");
            player.SetMeta("opennv_source_fighting_enabled", true);
            player.XrFire(true); player.XrFire(true);
            await Frames(90);
            player.XrFire(false);
            await Frames(90);
            var firing = JsonSerializer.SerializeToElement(player.FiringState);
            if (Shots() != 1 || inventory.Item(weapon.Ammunition[0])!.Count != before - 1 ||
                firing.GetProperty("error").ValueKind != JsonValueKind.Null ||
                firing.GetProperty("damageError").ValueKind != JsonValueKind.Null ||
                firing.GetProperty("effectErrors").EnumerateObject().Any())
                throw new InvalidOperationException("XR trigger did not fire exactly one source shot and stop on release: " + firing);
            GD.Print("OPENNV_XR_WEAPON_CONTROL_PASS reload=true fire=true duplicatePress=true release=true sourceGate=true desktopBindings=empty fixture=true recording=off");
        }
        finally { player.XrFire(false); player.XrReload(false); DesktopInputMap.Configure(actions); }

        long Shots() => JsonSerializer.SerializeToElement(player.FiringState).GetProperty("shots").GetInt64();
        async Task Frames(int count)
        {
            for (var frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }
}
