using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerPresentationAudit
{
    private async Task AuditBeamAndDynamiteControls(FalloutPluginStack records, RuntimeNativePlayer player, FalloutPlayerInventory inventory)
    {
        var laser = FalloutWeaponPresentation.Read(records, records.RuntimeFormKey(0x4336));
        inventory.Add(records, laser.Form, 1, 1, true);
        inventory.Add(records, laser.Ammunition[0], 30, 1, true);
        inventory.Equip(records, laser.Form);
        await Frames(180);
        using var reload = new InputEventKey { PhysicalKeycode = Key.R, Pressed = true };
        using var reloadRelease = new InputEventKey { PhysicalKeycode = Key.R, Pressed = false };
        player._UnhandledInput(reload); player._UnhandledInput(reloadRelease);
        await Frames(240);
        using var fire = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
        using var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false };
        player._UnhandledInput(fire); await Frames(2); player._UnhandledInput(release);
        await Frames(90);
        var firing = JsonSerializer.SerializeToElement(player.FiringState);
        var beam = firing.GetProperty("effects").GetProperty("lastBeam");
        Require(firing.GetProperty("last").GetProperty("weapon").GetString() == laser.Form.ToString() &&
            beam.ValueKind == JsonValueKind.Object && beam.GetProperty("surfaces").GetInt32() > 0,
            "Ordinary laser input did not instantiate its source beam model.");
        var endpoint = beam.GetProperty("endpoint").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        var rendered = beam.GetProperty("renderedEndpoint").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        Require(endpoint.Zip(rendered).All(pair => Math.Abs(pair.First - pair.Second) < .001f),
            "Beam source endpoint did not reach the authoritative ray endpoint.");
        Require(!firing.GetProperty("effectErrors").EnumerateObject().Any(), "Laser retained an effect error: " + firing);
        GD.Print("OPENNV_LASER_CONTROL_PASS " + beam);

        foreach (var id in new uint[] { 0xba0f3, 0x109a0c })
        {
            var dynamite = records.RuntimeFormKey(id);
            inventory.Add(records, dynamite, 3, 1, true);
            var before = inventory.Item(dynamite)!.Count;
            inventory.Equip(records, dynamite); await Frames(180);
            player._UnhandledInput(fire); await Frames(150);
            Require(inventory.Item(dynamite)!.Count == before, "Held dynamite released before trigger release.");
            player._UnhandledInput(release);
            var released = false;
            for (var frame = 0; frame < 1800; frame++)
            {
                await Frames(1);
                if (inventory.Item(dynamite)!.Count == before) continue;
                released = true;
                firing = JsonSerializer.SerializeToElement(player.FiringState);
                if (firing.GetProperty("effects").GetProperty("projectileFlights").GetArrayLength() == 0) break;
            }
            Require(released && inventory.Item(dynamite)!.Count == before - 1, "Dynamite did not release exactly one inventory object.");
            Require(firing.GetProperty("error").ValueKind == JsonValueKind.Null &&
                !firing.GetProperty("effectErrors").EnumerateObject().Any(), "Dynamite retained a runtime/presentation error: " + firing);
            var projectile = firing.GetProperty("effects").GetProperty("lastProjectile");
            Require(projectile.GetProperty("detonations").GetInt32() == 1 &&
                projectile.GetProperty("error").ValueKind == JsonValueKind.Null,
                "Dynamite did not finish its physical flight with one timed detonation: " + projectile);
            GD.Print($"OPENNV_DYNAMITE_CONTROL_PASS weapon={dynamite} hold=true release=true consumed=1 flight={projectile}");
        }

        async Task Frames(int count)
        {
            for (var frame = 0; frame < count; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
