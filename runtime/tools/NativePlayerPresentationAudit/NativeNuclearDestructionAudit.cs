using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativePlayerPresentationAudit
{
    private async Task AuditNuclearDestruction(FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutNativeCampaignState saved, RuntimeNativePlayer player, FalloutPlayerInventory inventory, FalloutPlayerVitals vitals)
    {
        var source = records.RuntimeFormKey(0x14e78); // Owned Car01 declaration, not a campaign placement.
        var reference = records.EffectiveRecords("REFR").First(record =>
            record.ReadSubrecords().Any(field => field.Signature == "NAME" && field.Data.Length == 4 &&
                record.Plugin.AdjustOptionalFormId(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(field.Data.Span)) == source));
        using var world = new FalloutReferenceWorld(records);
        var state = world.Get(reference.FormKey);
        var modelPath = "meshes/" + FalloutDialogueTopic.Text(records.GetEffective(source).ReadSubrecords().Single(field => field.Signature == "MODL").Data.Span).Replace('\\', '/');
        if (!content.TryRead(modelPath, null, out var bytes, out _)) throw new FileNotFoundException(modelPath);
        var prototype = new RuntimeNativeNifPrototype(bytes, .0142875f);
        var car = prototype.InstantiatePlaced(new(Basis.Identity, new(0, 1, -15)));
        prototype.Scene.Root.Free(); AddChild(car);
        try
        {
            var context = new NativeActorCombatContext(() => player, () => vitals.State, (_, _) => { }, (_, _) => [], _ => true,
                () => 1, FalloutGlobalState.Read(records), .3f, 9.8f);
            var destructible = RuntimeNativeDestructible.Attach(car, state, records, content, .0142875f, uint.MaxValue, context)!;
            CheckDestructionOwner(car, destructible);
            if (RuntimeNativeDestructible.Find(player) is not null)
                throw new InvalidOperationException("A neighboring source object claimed the player's collision.");
            var fatman = FalloutWeaponPresentation.Read(records, records.RuntimeFormKey(0x432c));
            inventory.Add(records, fatman.Form, 1, 1, true);
            inventory.Add(records, fatman.Ammunition[0], 3, 1, true);
            inventory.Equip(records, fatman.Form);
            for (var frame = 0; frame < 15; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            for (var frame = 0; frame < 180; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            using var reloadPress = new InputEventKey { PhysicalKeycode = Key.R, Pressed = true };
            using var reloadRelease = new InputEventKey { PhysicalKeycode = Key.R, Pressed = false };
            player._UnhandledInput(reloadPress); player._UnhandledInput(reloadRelease);
            for (var frame = 0; frame < 240; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var beforeAmmo = inventory.Item(fatman.Ammunition[0])!.Count;
            using var firePress = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true };
            using var fireRelease = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false };
            player._UnhandledInput(firePress); player._UnhandledInput(fireRelease);
            for (var frame = 0; frame < 480; frame++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            var firing = JsonSerializer.SerializeToElement(player.FiringState);
            GD.Print("OPENNV_NUCLEAR_CONTROL_STATE " + JsonSerializer.Serialize(new { effects = firing.GetProperty("effects"), error = firing.GetProperty("error"), effectErrors = firing.GetProperty("effectErrors") }));
            GD.Print("OPENNV_CAR_CONTROL_STATE " + JsonSerializer.Serialize(destructible.Observation));
            if (inventory.Item(fatman.Ammunition[0])!.Count != beforeAmmo - fatman.AmmoUse ||
                firing.GetProperty("lastExplosion").GetProperty("explosion").GetString() != "FalloutNV.esm:014c06" ||
                firing.GetProperty("error").ValueKind != JsonValueKind.Null ||
                firing.GetProperty("damageError").ValueKind != JsonValueKind.Null ||
                firing.GetProperty("effectErrors").EnumerateObject().Any())
                throw new InvalidOperationException("Fat Man shot did not complete source reload, flight, nuclear explosion, effects and ammo consumption.");
            if (!state.Destroyed || state.Destruction is not { Stage: >= 4, Error: null })
                throw new InvalidOperationException("Nuclear blast did not damage, explode and replace the source car.");
            CheckDestructionOwner(car, destructible);
            var snapshot = world.Capture();
            using var cold = new FalloutReferenceWorld(records); cold.Restore(snapshot);
            if (cold.Get(reference.FormKey).Destruction != state.Destruction)
                throw new InvalidOperationException("Cold world lost destruction health, stage or source identity.");
            GD.Print("OPENNV_NUCLEAR_DESTRUCTION_PASS fatman=true ammo=true carBlast=true wreckage=true coldState=true fixture=true");
        }
        finally { car.Free(); }
    }

    private static void CheckDestructionOwner(Node3D car, RuntimeNativeDestructible owner)
    {
        var contacts = car.FindChildren("*", "", true, false).OfType<CollisionObject3D>().ToArray();
        if (contacts.Length == 0 || contacts.Any(contact => RuntimeNativeDestructible.Find(contact) != owner))
            throw new InvalidOperationException("Source car or replacement collision lost its destruction owner.");
    }
}
