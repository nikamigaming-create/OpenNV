using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerPresentationAudit
{
    private async Task AuditCombatControls(FalloutPluginStack records, RuntimeLiveContentSource content, FalloutNativeCampaignState saved)
    {
        var inventory = new FalloutPlayerInventory();
        var grenade = records.RuntimeFormKey(0x4330);
        inventory.Add(records, grenade, 3, 1, true);
        var closes = 0;
        var wheel = new NativeCombatWheel(records, inventory, false, false, form => inventory.Equip(records, form), () => closes++);
        AddChild(wheel);
        wheel.SelectDirection(Vector2.Up); wheel.Finish(true); wheel.Finish(true);
        Require(closes == 1 && inventory.Equipped.Contains(records.RuntimeFormId(grenade)) && inventory.Item(grenade)!.Count == 3,
            "Weapon wheel did not select exactly once without consumption.");
        wheel.Free();
        var consumed = 0;
        var stimpak = records.RuntimeFormKey(0x15169);
        inventory.Add(records, stimpak, 2, 1, true);
        var vitals = new FalloutPlayerVitals(records, records.RuntimeFormKey(7), saved.Special,
            saved.Vitals! with { HitPoints = 1, HitPointFraction = 0 });
        var body = FalloutBodyPartData.Read(records.GetEffective(records.RuntimeFormKey(0x1d)));
        var ingestibles = new FalloutPlayerIngestibles(records, inventory, vitals, body, _ => 50, _ => false, () => false);
        wheel = new(records, inventory, true, false, form => { ingestibles.Prepare(form).Commit(); consumed++; }, () => closes++);
        AddChild(wheel); wheel.SelectDirection(Vector2.Up); wheel.SelectDirection(Vector2.Zero); wheel.Finish(true);
        Require(consumed == 0 && inventory.Item(stimpak)!.Count == 2, "Wheel center did not cancel."); wheel.Free();
        wheel = new(records, inventory, true, true, form => { ingestibles.Prepare(form).Commit(); consumed++; }, () => closes++);
        AddChild(wheel); wheel.SelectDirection(Vector2.Up); wheel.Finish(true); wheel.Finish(true);
        Require(consumed == 1 && inventory.Item(stimpak)!.Count == 1 && vitals.State.HitPoints > 1,
            "Consumable wheel did not apply the shared Aid transaction once."); wheel.Free();
        GD.Print("OPENNV_COMBAT_WHEEL_PASS equip=true cancel=true consumeOnce=true sharedAid=true");

        var config = RuntimeConfiguration.Load();
        OpenNV.Runtime.InputSystem.DesktopInputMap.Configure(config.Player.DesktopInput);
        var floor = new StaticBody3D { CollisionLayer = config.Player.CollisionMask, CollisionMask = 0, Position = new(0, -.1f, 0) };
        var floorShape = new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, .2f, 200) } };
        floorShape.SetMeta("opennv_havok_material", 0u); // Diagnostic stone floor, not a retail placement.
        floor.AddChild(floorShape); AddChild(floor);
        var player = new RuntimeNativePlayer(); player.Configure(config, Transform3D.Identity); AddChild(player);
        try
        {
            var playerForm = records.RuntimeFormKey(7);
            var appearance = FalloutNpcAppearanceResolver.Resolve(records, playerForm,
                equippedArmor: saved.EquippedRuntimeFormIds.Select(records.RuntimeFormKey).Where(key => records.GetEffective(key).Signature == "ARMO").ToArray(),
                appearanceState: FalloutNativeCharacterCreation.ActorState(records, playerForm, saved.Character));
            player.ConfigurePresentation(records, inventory, () => appearance, () => Colors.White);
            player.ConfigureLocomotion(records, () => vitals.State);
            player.ConfigureCombat(records, FalloutGlobalState.Read(records), () => 1, value => value < 32 ? 5 : 100, () => [], _ => 0, (_, _) => { });
            player.ConfigureExplosionExposure(() => saved.ActiveCell, amount => vitals.Publish(vitals.State with { RadiationRads = vitals.State.RadiationRads + amount }), new());
            for (var frame = 0; frame < 5; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            using var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            using var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false };
            player._UnhandledInput(press);
            for (var frame = 0; frame < 80; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Require(inventory.Item(grenade)!.Count == 3, "Held grenade released before the trigger was let go.");
            player._UnhandledInput(release);
            for (var frame = 0; frame < 240; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var firing = JsonSerializer.SerializeToElement(player.FiringState);
            GD.Print("OPENNV_GRENADE_CONTROL_STATE " + JsonSerializer.Serialize(new { effects = firing.GetProperty("effects"), error = firing.GetProperty("error"), effectErrors = firing.GetProperty("effectErrors") }));
            Require(inventory.Item(grenade)!.Count == 2 && firing.GetProperty("shots").GetInt64() == 1 &&
                firing.GetProperty("lastExplosion").ValueKind == JsonValueKind.Object,
                "Source grenade did not release, consume one, fly and detonate through ordinary weapon input.");
            Require(firing.GetProperty("error").ValueKind == JsonValueKind.Null && firing.GetProperty("damageError").ValueKind == JsonValueKind.Null,
                "Grenade input retained a runtime error.");
            Require(!firing.GetProperty("effectErrors").EnumerateObject().Any(), "Grenade retained a presentation failure.");
            var particles = firing.GetProperty("effects").GetProperty("particles").EnumerateArray().ToArray();
            Require(particles.Length > 0 && particles.All(particle => particle.GetProperty("BirthCount").GetInt64() > 0),
                "Grenade emission clock skipped a source burst.");
            await AuditNuclearDestruction(records, content, saved, player, inventory, vitals);
            await AuditAutomaticControls(records, player, inventory);
            await AuditBeamAndDynamiteControls(records, player, inventory);
            GD.Print("OPENNV_GRENADE_CONTROL_PASS hold=true release=true flight=true fuse=true explosion=true consumption=true fixture=true");
        }
        finally { player.Free(); floor.Free(); }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
