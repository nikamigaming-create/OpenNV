using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

public partial class NativeActorPerformanceAudit
{
    private const BindingFlags CorpseFixtureFlags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static FieldInfo CorpseField(string name) => typeof(RuntimeNativeActorCombat).GetField(name, CorpseFixtureFlags) ??
        throw new MissingFieldException(name);

    private sealed class CorpsePursuitLease(Action atDeath) : IEnumerator<IReadOnlyList<Vector3>?>
    {
        internal int Advances, Disposals;
        public IReadOnlyList<Vector3>? Current => null;
        object? System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() { Advances++; return true; }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { Disposals++; atDeath(); }
    }

    private sealed class CorpseEquipmentProof
    {
        internal string? InventoryAtDeath, HandlingAtDeath;
        internal string SourceHistory = "";
        internal FalloutCorpseWeaponAttachment[] Attachments = [];
        internal Func<string> DoorClock = null!;
        internal string OriginalDoorClock = "";
        internal CorpsePursuitLease Search = null!;
        internal int EndEvents;
        internal long[] SoundGenerations = [];
        internal bool MuzzleStarted;
    }

    private async Task<CorpseEquipmentProof> PrepareOwnedCorpseEquipment(Node3D fixture, RuntimeNativeNpc actor,
        FalloutReferenceWorld world, FalloutPluginStack records, RuntimeLiveContentSource content,
        FalloutCellScene cell, FalloutFormKey attacker, NativeActorCombatContext context)
    {
        var combat = actor.Combat!; var state = world.Get(actor.Appearance.Reference!.Value);
        var proof = new CorpseEquipmentProof();
        var before = state.Capture();
        var binding = before.PackageBindingFailure ??
            throw new InvalidDataException("Equipped corpse fixture has no admitted current stopped-package binding.");
        proof.SourceHistory = JsonSerializer.Serialize(new
        { before.PackageBindingFailure, before.SelectionFailure, ProcedureCaptureBlocker = binding.Error });
        // These are the existing source presentation entry points. Fixture
        // preparation is not ordinary combat or a retail drop proof.
        typeof(RuntimeNativeActorCombat).GetMethod("PreparePackageWeapon", CorpseFixtureFlags)!
            .Invoke(combat, [records.GetEffective(binding.Package), true]);
        state.Engagement = new(attacker);
        typeof(RuntimeNativeActorCombat).GetMethod("PrepareCombatPresentation", CorpseFixtureFlags)!.Invoke(combat, null);
        combat.StopPackageMotion();
        var attachment = CorpseField("_enemyObject").GetValue(combat) as NativeActorWeaponAttachment ??
            throw new InvalidDataException("Equipped corpse fixture has no actual source inventory model.");
        var weapon = (FalloutWeaponPresentation)CorpseField("_enemyWeapon").GetValue(combat)!;
        var handling = (FalloutWeaponHandling)CorpseField("_enemyWeaponHandling").GetValue(combat)!;
        var sounds = (NativeOwnedAnimationSoundPlayer)CorpseField("_enemySounds").GetValue(combat)!;
        CorpseField("_context").SetValue(combat, context with
        {
            DispatchEvent = (reference, name) =>
            {
                if (reference != state.Reference || name != "OnCombatEnd")
                    throw new InvalidDataException("Unexpected corpse lifecycle event.");
                proof.EndEvents++;
            }
        });
        await BindOwnedCorpseDoor(fixture, actor, world, records, content, cell, proof);
        proof.Attachments = new[] { attachment.CapturePersistence("combat", records) }
            .Concat(CorpseField("_packageWeapon").GetValue(combat) is NativeActorWeaponAttachment package
                ? new[] { package.CapturePersistence("package", records) } : []).ToArray();
        proof.Search = new(() =>
        {
            // BeginActorDeath has already performed its authored death grant.
            // Compare the exact inventory/handling at native death admission,
            // not a guessed inventory before that independent source effect.
            proof.InventoryAtDeath = JsonSerializer.Serialize(state.Inventory!.Capture());
            proof.HandlingAtDeath = JsonSerializer.Serialize(handling.Capture());
        });
        CorpseField("_routeSearch").SetValue(combat, proof.Search);
        CorpseField("_routeError").SetValue(combat, "retained-corpse-route-component-refusal");
        CorpseField("_routeFailures").SetValue(combat, 3);
        if (weapon.Sounds.TryGetValue("shoot", out var sound))
        {
            var generation = state.AnimationSoundEvents.Events.Select(entry => entry.Generation).DefaultIfEmpty(0).Max();
            sounds.DispatchSound(sound);
            proof.SoundGenerations = state.AnimationSoundEvents.Events.Where(entry => entry.Generation > generation)
                .Select(entry => entry.Generation).ToArray();
            if (proof.SoundGenerations.Length == 0 || !sounds.CanAwaitFiniteCompletion || !sounds.ActiveNativeVoices.Any())
                throw new InvalidDataException("Original equipped-actor shot sound lacks a proven actual finite native binding.");
        }
        if (!weapon.IsMeleeWeapon)
        {
            var shot = FalloutWeaponShot.Read(records, weapon.Form, handling.Ammunition(weapon), weapon.HasAmmunitionSource);
            if (shot.Projectile.HasMuzzleFlash)
            {
                var socket = (Node3D)typeof(RuntimeNativeActorCombat).GetMethod("MuzzleNode", CorpseFixtureFlags)!.Invoke(combat, null)!;
                var muzzle = new NativeActorMuzzle(records, content, socket, actor.Skeleton.UnitsToMetres);
                muzzle.Prepare(shot.Projectile, encoded: false); muzzle.Flash();
                if (muzzle.CaptureReady) throw new InvalidDataException("Original source muzzle effect did not acquire its actual finite owner.");
                CorpseField("_enemyMuzzle").SetValue(combat, muzzle); proof.MuzzleStarted = true;
            }
        }
        return proof;
    }

    private async Task BindOwnedCorpseDoor(Node3D fixture, RuntimeNativeNpc actor, FalloutReferenceWorld world,
        FalloutPluginStack records, RuntimeLiveContentSource content, FalloutCellScene cell, CorpseEquipmentProof proof)
    {
        var combat = actor.Combat!;
        var door = cell.References.FirstOrDefault(reference => reference.Teleport is null &&
            cell.BaseObjects.TryGetValue(reference.Base, out var definition) && definition.Signature == "DOOR" &&
            world.IsEnabled(reference.FormKey) && world.GetLocked(reference.FormKey) == 0) ??
            throw new InvalidDataException("Corpse route-retirement fixture has no enabled unlocked resident source door.");
        var path = cell.BaseObjects[door.Base].ModelPath ?? throw new InvalidDataException("Source route door has no model.");
        if (!content.TryRead(path, null, out var bytes, out _)) throw new FileNotFoundException(path);
        var prototype = new RuntimeNativeNifPrototype(bytes, actor.Skeleton.UnitsToMetres);
        Node3D model;
        try
        {
            model = prototype.InstantiatePlaced(new(GamebryoCoordinate.ConvertReferenceEuler(
                new(door.RotationRadians[0], door.RotationRadians[1], door.RotationRadians[2]), door.Scale),
                GamebryoCoordinate.ConvertVector(new(door.Position[0], door.Position[1], door.Position[2])) * actor.Skeleton.UnitsToMetres));
        }
        finally { prototype.Scene.Root.Free(); }
        fixture.AddChild(model);
        var controllers = model.FindChildren("*", "", true, false).OfType<RuntimeNifControllerPlayer>().ToArray();
        var state = world.Get(door.FormKey);
        var motion = RuntimeNativeDoorMotion.Attach(model, state, controllers, () => { }); motion.SetProcess(false);
        foreach (var controller in controllers) controller.SetProcess(false);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var radius = combat.PreparePortalArrival();
        var envelope = (CollisionShape3D)CorpseField("_movementEnvelope").GetValue(combat)!;
        NativeNavigationContact? contact = null;
        foreach (var shape in model.FindChildren("*", "", true, false).OfType<CollisionShape3D>())
        {
            var to = shape.GlobalPosition - Vector3.Up * envelope.Position.Y * actor.Scale.Y;
            var from = to + shape.GlobalBasis.Z.Normalized() * radius * 4;
            var candidate = NativeCapsuleNavigation.FirstCorridorContact(actor, from, [to]);
            if (candidate is not null && GodotObject.InstanceFromId(candidate.Collider) is Node collider && model.IsAncestorOf(collider))
            { contact = candidate with { Reference = door.FormKey }; break; }
        }
        if (contact is null) throw new InvalidDataException("Original route-door model produced no actual complete-capsule source contact.");
        motion.SetOpen(!state.DoorOpen);
        foreach (var controller in controllers) controller.SetProcess(false);
        state.ObjectAnimations = controllers.Select(controller => controller.CaptureScriptState())
            .OfType<FalloutObjectAnimationSnapshot>().ToArray();
        if (state.DoorMotion?.Moving != true) throw new InvalidDataException("Original route door did not retain its actual pending source motion.");
        state.DoorMotion.Validate(state.DoorOpen, state.ObjectAnimations);
        var type = typeof(RuntimeNativeActorCombat).GetNestedType("RouteDoor", BindingFlags.NonPublic)!;
        var procedure = Activator.CreateInstance(type, CorpseFixtureFlags | BindingFlags.Public, null, [contact, door.FormKey], null)!;
        type.GetField("Requested", CorpseFixtureFlags | BindingFlags.Public)!.SetValue(procedure, true);
        type.GetField("Seconds", CorpseFixtureFlags | BindingFlags.Public)!.SetValue(procedure, .125d);
        type.GetField("Error", CorpseFixtureFlags | BindingFlags.Public)!.SetValue(procedure, "retained-corpse-door-component-refusal");
        CorpseField("_routeDoor").SetValue(combat, procedure);
        proof.DoorClock = () => JsonSerializer.Serialize(new
        { state.DoorOpen, state.DoorMotion, Controllers = controllers.Select(controller => controller.CaptureScriptState()).ToArray() });
        proof.OriginalDoorClock = proof.DoorClock();
    }

    private async Task RequireOwnedCorpseEquipmentAdmission(RuntimeNativeNpc actor, FalloutReferenceWorld world,
        FalloutPluginStack records, CorpseEquipmentProof proof)
    {
        var combat = actor.Combat!; var state = world.Get(actor.Appearance.Reference!.Value);
        if (proof.Search.Disposals != 1 || proof.Search.Advances != 0 || proof.EndEvents != 0 ||
            CorpseField("_routeSearch").GetValue(combat) is not null || CorpseField("_routeDoor").GetValue(combat) is not null ||
            !Equals(CorpseField("_routeError").GetValue(combat), "retained-corpse-route-component-refusal") ||
            proof.DoorClock() != proof.OriginalDoorClock)
            throw new InvalidDataException("Death failed exact-once query retirement or changed an original door/fault/event owner.");
        if (proof.InventoryAtDeath != JsonSerializer.Serialize(state.Inventory!.Capture()) ||
            proof.HandlingAtDeath != JsonSerializer.Serialize(((FalloutWeaponHandling)CorpseField("_enemyWeaponHandling").GetValue(combat)!).Capture()))
            throw new InvalidDataException("Native death changed actual items, ammunition, condition or handling randomness.");
        var active = proof.SoundGenerations.Any(generation => state.AnimationSoundEvents.Events.Single(entry => entry.Generation == generation)
            .End == FalloutAnimationSoundEnd.Active) || CorpseField("_enemyMuzzle").GetValue(combat) is NativeActorMuzzle { CaptureReady: false };
        if (active && (combat.StoppedAiPoseCaptureReady || state.CorpseEquipmentCaptureReady))
            throw new InvalidDataException("Active actual finite sound or muzzle admitted corpse capture.");
        CorpseField("_pendingHitscanImpacts").SetValue(combat, 1);
        if (state.CorpseEquipmentCaptureReady || combat.StoppedAiPoseFiniteCandidateReady)
            throw new InvalidDataException("Unowned pending actor hit effects admitted corpse capture.");
        var rejected = false;
        try { state.Capture(); } catch (NotSupportedException) { rejected = true; }
        if (!rejected) throw new InvalidDataException("Pending native hit continuation was admitted.");
        CorpseField("_pendingHitscanImpacts").SetValue(combat, 0);
        var timer = Stopwatch.StartNew(); var previous = timer.Elapsed.TotalSeconds;
        while (!state.CorpseEquipmentCaptureReady && timer.Elapsed.TotalSeconds < 10)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var next = timer.Elapsed.TotalSeconds;
            combat._PhysicsProcess(next - previous); previous = next;
        }
        if (!state.CorpseEquipmentCaptureReady || !combat.StoppedAiPoseCaptureReady ||
            proof.SoundGenerations.Any(generation => state.AnimationSoundEvents.Events.Single(entry => entry.Generation == generation)
                .End != FalloutAnimationSoundEnd.NativeFinished))
            throw new InvalidDataException("Original corpse audio/effects did not genuinely settle: " + state.CorpseEquipmentCaptureBlocker);
        var snapshot = state.Capture();
        RequireCorpseEquipmentConservation(snapshot, proof);
        var current = snapshot.CorpseEquipment!;
        var missingItem = snapshot with
        {
            Inventory = snapshot.Inventory! with
            {
                Contents = snapshot.Inventory!.Contents with
                {
                    Inventory = new(snapshot.Inventory.Contents.Inventory.Items
                        .Where(item => item.FormKey != current.HandlingWeapon!.Weapon).ToArray(), null),
                    EquippedRuntimeFormIds = snapshot.Inventory.Contents.EquippedRuntimeFormIds
                        .Where(id => id != records.RuntimeFormId(current.HandlingWeapon!.Weapon)).ToArray()
                }
            }
        };
        var invalidInventory = new List<FalloutReferenceSnapshot> { missingItem };
        foreach (var magazine in current.WeaponHandling!.Magazines)
        {
            var weapon = FalloutWeaponPresentation.Read(records, magazine.Weapon, false);
            foreach (var changed in new[] { magazine with { Loaded = weapon.ClipSize + 1 },
                magazine with { UsesInventoryAmmo = !magazine.UsesInventoryAmmo } })
            {
                var handling = current.WeaponHandling with
                { Magazines = current.WeaponHandling.Magazines.Select(entry => entry.Weapon == magazine.Weapon ? changed : entry).ToArray() };
                invalidInventory.Add(snapshot with
                {
                    CorpseEquipment = current with { WeaponHandling = handling },
                    Engagement = snapshot.Engagement is { } engagement ? engagement with { WeaponHandling = handling } : null
                });
            }
        }
        foreach (var invalid in invalidInventory)
        {
            using var rejectedWorld = new FalloutReferenceWorld(records);
            var refused = false;
            try { rejectedWorld.Restore([invalid]); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException) { refused = true; }
            if (!refused || rejectedWorld.InstanceCount != 0 || proof.InventoryAtDeath != JsonSerializer.Serialize(state.Inventory!.Capture()) ||
                proof.HandlingAtDeath != JsonSerializer.Serialize(((FalloutWeaponHandling)CorpseField("_enemyWeaponHandling").GetValue(combat)!).Capture()))
                throw new InvalidDataException("Corpse source item/magazine corruption changed a current owner or published a partial world.");
        }
        foreach (var attachment in current.Attachments)
        {
            var native = (NativeActorWeaponAttachment)CorpseField(attachment.Owner == "combat" ? "_enemyObject" : "_packageWeapon").GetValue(combat)!;
            var before = JsonSerializer.Serialize(native.CapturePersistence(attachment.Owner, records));
            foreach (var drift in new[]
            {
                attachment with { ModelSha256 = new string('0', 64) },
                attachment with { Nodes = attachment.Nodes.Take(attachment.Nodes.Count - 1).ToArray() },
                attachment with { Nodes = attachment.Nodes.Select(node => node.Index == 0 ? node with { BoneParent = "WrongWeaponBone" } : node).ToArray() }
            })
            {
                var refused = false;
                try { native.RestorePersistence(drift, records); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException) { refused = true; }
                if (!refused || before != JsonSerializer.Serialize(native.CapturePersistence(attachment.Owner, records)))
                    throw new InvalidDataException("Corrupt native source binding published part of a saved weapon pose.");
            }
        }
    }

    private static void RequireCorpseEquipmentConservation(FalloutReferenceSnapshot snapshot, CorpseEquipmentProof proof)
    {
        if (snapshot.CorpseEquipment is not { } equipment || proof.InventoryAtDeath != JsonSerializer.Serialize(snapshot.Inventory) ||
            proof.HandlingAtDeath != JsonSerializer.Serialize(equipment.WeaponHandling) ||
            JsonSerializer.Serialize(equipment.Attachments) != JsonSerializer.Serialize(proof.Attachments) ||
            equipment.RouteRetirement is not
            {
                SearchRetired: true, DoorRequested: true, Failures: 3,
                RouteError: "retained-corpse-route-component-refusal", DoorError: "retained-corpse-door-component-refusal"
            } ||
            proof.SourceHistory != JsonSerializer.Serialize(new
            { snapshot.PackageBindingFailure, snapshot.SelectionFailure, ProcedureCaptureBlocker = snapshot.PackageBindingFailure?.Error }))
            throw new InvalidDataException("Corpse capture/cold restoration changed actual inventory, equipment, random state or original source faults: " +
                JsonSerializer.Serialize(new
                {
                    equipmentMissing = snapshot.CorpseEquipment is null,
                    inventoryUnchanged = proof.InventoryAtDeath == JsonSerializer.Serialize(snapshot.Inventory),
                    handlingUnchanged = proof.HandlingAtDeath == JsonSerializer.Serialize(snapshot.CorpseEquipment?.WeaponHandling),
                    attachmentsUnchanged = JsonSerializer.Serialize(snapshot.CorpseEquipment?.Attachments) == JsonSerializer.Serialize(proof.Attachments),
                    route = snapshot.CorpseEquipment?.RouteRetirement,
                    sourceUnchanged = proof.SourceHistory == JsonSerializer.Serialize(new
                    { snapshot.PackageBindingFailure, snapshot.SelectionFailure, ProcedureCaptureBlocker = snapshot.PackageBindingFailure?.Error })
                }));
    }
}
