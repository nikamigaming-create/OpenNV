using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private sealed class PendingPursuitLease : IEnumerator<IReadOnlyList<Vector3>?>
    {
        internal int Advances, Disposals;
        public IReadOnlyList<Vector3>? Current => null;
        object? System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() { Advances++; return true; }
        public void Dispose() { Disposals++; }
        public void Reset() => throw new NotSupportedException();
    }

    // Query lifetime is isolated from gameplay execution. The actual owned
    // actor, source failure receipts, door collision and source clocks remain
    // present; this component proof does not admit a moving campaign save.
    private async Task OwnedRouteLifecycle(Node3D fixture, RuntimeNativeNpc actor, FalloutReferenceWorld world,
        FalloutPluginStack records, RuntimeLiveContentSource content, FalloutNativeCampaignState saved,
        FalloutCellScene cell, FalloutGlobalState globals, string checkpoint, byte[] savedBytes)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(RuntimeNativeActorCombat);
        FieldInfo Field(string name) => type.GetField(name, flags) ?? throw new MissingFieldException(name);
        var combat = actor.Combat!; var state = world.Get(actor.Appearance.Reference!.Value);
        var config = RuntimeConfiguration.Load(); var units = config.World.GameUnitsToMeters;
        var vitals = saved.Vitals ?? throw new InvalidDataException("Route checkpoint has no player vitals.");
        var player = new RuntimeNativePlayer();
        player.Configure(config, Transform3D.Identity);
        fixture.AddChild(player); player.SetProcess(false); player.SetPhysicsProcess(false);
        player.RestoreTransform(saved.PlayerPosition!, saved.PlayerRotation!, FalloutNativeCampaignSave.RestorePlayerViewPitch(saved));
        var ended = 0;
        Field("_context").SetValue(combat, new NativeActorCombatContext(() => player, () => vitals,
            (_, _) => throw new InvalidDataException("Route lifecycle attempted gameplay damage."),
            (_, _) => throw new InvalidDataException("Pending-query lifecycle attempted a new source route."),
            _ => true, () => vitals.Level, globals, config.Player.StepHeightMeters, config.Simulation.GravityMetersPerSecondSquared,
            DispatchEvent: (reference, name) =>
            {
                if (reference != state.Reference || name != "OnCombatEnd") throw new InvalidDataException("Unexpected lifecycle event.");
                ended++;
            }));
        var door = cell.References.Where(reference => reference.Teleport is null &&
                cell.BaseObjects.TryGetValue(reference.Base, out var definition) && definition.Signature == "DOOR" &&
                world.IsEnabled(reference.FormKey) && world.GetLocked(reference.FormKey) == 0)
            .OrderBy(reference => GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2]))
                .DistanceSquaredTo(actor.GlobalPosition / units)).FirstOrDefault() ??
            throw new InvalidDataException("Selected owned cell has no enabled unlocked ordinary source door.");
        var path = cell.BaseObjects[door.Base].ModelPath ?? throw new InvalidDataException("Owned door has no model.");
        if (!content.TryRead(path, null, out var bytes, out var resource)) throw new FileNotFoundException(path);
        var prototype = new RuntimeNativeNifPrototype(bytes, units);
        Node3D instance;
        try
        {
            instance = prototype.InstantiatePlaced(new(GamebryoCoordinate.ConvertReferenceEuler(
                new(door.RotationRadians[0], door.RotationRadians[1], door.RotationRadians[2]), door.Scale),
                GamebryoCoordinate.ConvertVector(new(door.Position[0], door.Position[1], door.Position[2])) * units));
        }
        finally { prototype.Scene.Root.Free(); }
        fixture.AddChild(instance);
        var controllers = instance.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
        var doorState = world.Get(door.FormKey);
        var motion = new RuntimeNativeDoorMotion(doorState, controllers, () => { }); instance.AddChild(motion); motion.SetProcess(false);
        foreach (var controller in controllers) controller.SetProcess(false);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var radius = combat.PreparePortalArrival();
        var envelope = (CollisionShape3D)Field("_movementEnvelope").GetValue(combat)!;
        NativeNavigationContact? contact = null;
        foreach (var shape in instance.FindChildren("*", "", true, false).OfType<CollisionShape3D>())
        {
            var to = shape.GlobalPosition - Vector3.Up * envelope.Position.Y * actor.Scale.Y;
            var from = to + shape.GlobalBasis.Z.Normalized() * radius * 4;
            var candidate = NativeCapsuleNavigation.FirstCorridorContact(actor, from, [to]);
            if (candidate is not null && GodotObject.InstanceFromId(candidate.Collider) is Node collider && instance.IsAncestorOf(collider))
            { contact = candidate with { Reference = door.FormKey }; break; }
        }
        if (contact is null) throw new InvalidDataException("Owned door produced no complete-capsule source collision contact.");
        motion.SetOpen(!doorState.DoorOpen);
        foreach (var controller in controllers) controller.SetProcess(false);
        if (doorState.DoorMotion?.Moving != true) throw new InvalidDataException("Owned door has no pending source motion.");
        var procedureType = type.GetNestedType("RouteDoor", BindingFlags.NonPublic)!;
        var procedure = Activator.CreateInstance(procedureType, flags | BindingFlags.Public, null, [contact, door.FormKey], null)!;
        procedureType.GetField("Requested", flags | BindingFlags.Public)!.SetValue(procedure, true);
        Field("_routeDoor").SetValue(combat, procedure);
        Field("_routeError").SetValue(combat, "retained-native-query-refusal");
        Field("_routeFailures").SetValue(combat, 3); Field("_routeClock").SetValue(combat, 1.25d);
        string History() => JsonSerializer.Serialize(new { state.SelectionFailure, state.PackageBindingFailure, state.ProcedureCaptureBlocker });
        string DoorClock() => JsonSerializer.Serialize(new
        {
            doorState.DoorOpen,
            doorState.DoorMotion,
            clocks = controllers.Select(controller => controller.CaptureScriptState()).ToArray()
        });
        var history = History(); var doorClock = DoorClock();
        void Retained()
        {
            if (History() != history || DoorClock() != doorClock || !ReferenceEquals(Field("_routeDoor").GetValue(combat), procedure) ||
                !Equals(Field("_routeError").GetValue(combat), "retained-native-query-refusal") ||
                !Equals(Field("_routeFailures").GetValue(combat), 3) || !Equals(Field("_routeClock").GetValue(combat), 1.25d))
                throw new InvalidDataException("Search retirement changed a source fault, query refusal, retry or real door obligation.");
            if (combat.StoppedAiPoseCaptureReady) throw new InvalidDataException("A pending real door obligation admitted stopped capture.");
        }
        var paths = (IReadOnlyList<string>)Field("_attackPaths").GetValue(combat)!;
        var clips = (Dictionary<string, NativeActorCombatAnimation>)Field("_combatClips").GetValue(combat)!;
        var selected = FalloutAttackAnimationSelection.Bind(state.Engagement!.Transition("attack"), paths, source => clips[source].Hash);
        var active = selected with { Seconds = clips[selected.Animation!].Duration / 2, StartPending = false };
        state.AttackRandom.Restore(active.AttackRandomState!.Value); state.Engagement = active;
        var advance = type.GetMethod("AdvanceEngagement", flags)!;
        var unused = new PendingPursuitLease(); Field("_routeSearch").SetValue(combat, unused);
        advance.Invoke(combat, [player, 0d]); advance.Invoke(combat, [player, 0d]);
        if (unused.Disposals != 1 || unused.Advances != 0 || Field("_routeSearch").GetValue(combat) is not null ||
            state.Engagement!.Animation != active.Animation || state.Engagement.AnimationHash != active.AnimationHash ||
            state.Engagement.Seconds != active.Seconds || state.AttackRandom.State != active.AttackRandomState)
            throw new InvalidDataException("Normal attack failed exact-once unused-query retirement or changed its source clock/draw.");
        Retained();
        state.Engagement = active.Transition("pursue");
        var required = new PendingPursuitLease(); Field("_routeSearch").SetValue(combat, required);
        var horizontal = player.GlobalPosition - actor.GlobalPosition; horizontal.Y = 0;
        if (actor.IsOnFloor() || horizontal.Length() <= (float)Field("_attackRange").GetValue(combat)! + radius + player.CombatRadius)
            throw new InvalidDataException("Selected lifecycle checkpoint lacks the unsupported-start pursuit proof domain.");
        advance.Invoke(combat, [player, 0d]);
        if (required.Disposals != 0 || required.Advances != 0 || !ReferenceEquals(Field("_routeSearch").GetValue(combat), required) ||
            state.Engagement!.Action != "idle") throw new InvalidDataException("Requested pending pursuit was retired as unused idle.");
        Retained();
        vitals = vitals.Damage(vitals.ExactHitPoints);
        combat._PhysicsProcess(0);
        if (required.Disposals != 1 || required.Advances != 0 || Field("_routeSearch").GetValue(combat) is not null ||
            state.Engagement is not null || ended != 1 || combat.EngagementError is not null)
            throw new InvalidDataException("Normal target completion did not retire its pending query and emit exactly one end event.");
        Retained();
        if (!savedBytes.SequenceEqual(File.ReadAllBytes(checkpoint)) || !content.TryRead(path, null, out var current, out _) ||
            !bytes.SequenceEqual(current)) throw new InvalidDataException("Route lifecycle changed an owned input.");
        GD.Print("OPENNV_NATIVE_OWNED_ROUTE_LIFECYCLE " + JsonSerializer.Serialize(new
        {
            runtimeBuild = type.Assembly.ManifestModule.ModuleVersionId,
            content.SaveCompatibilityId,
            actor = state.Reference.ToString(),
            checkpointSha256 = Convert.ToHexString(SHA256.HashData(savedBytes)),
            unused = new { unused.Advances, unused.Disposals },
            required = new { required.Advances, required.Disposals },
            normalEndEvents = ended,
            retired = Field("_retiredPursuitSearches").GetValue(combat),
            door = new
            {
                reference = door.FormKey.ToString(),
                path,
                resource,
                sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                contact,
                motion = doorState.DoorMotion
            },
            sourceFaultsUnchanged = true,
            sourceDoorClockUnchanged = true,
            sourceUnchanged = true,
            campaign = false,
            framesRecorded = false,
            boundary = "independent-query-lifetime;owned-actor-and-moving-source-door;ordinary-activation-and-native-support-gap-unverified;whole-moving-or-pending-door-save-refused"
        }));
        GD.Print("OPENNV_NATIVE_OWNED_ROUTE_LIFECYCLE_PASS normalAction=true requiredPending=true normalEnd=true sourceFaultsUnchanged=true realDoorRetained=true campaign=false framesRecorded=false");
    }
}
