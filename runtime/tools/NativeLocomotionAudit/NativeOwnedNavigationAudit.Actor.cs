using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

internal static partial class NativeOwnedNavigationAudit
{
    private sealed record OwnedActorRoute(RuntimeNativeNpc Body, float Radius, CellNavigationGraph Navigation, Action RequireUnchanged);

    private static OwnedActorRoute PrepareOwnedActor(Node3D fixture, FalloutPluginStack records, FalloutReferenceWorld world,
        RuntimeLiveContentSource content, RuntimeConfiguration config, Vector3 start, string identity, string checkpoint,
        FalloutFormKey navigationCell)
    {
        var checkpointBytes = File.ReadAllBytes(checkpoint);
        var controls = FalloutOpeningPlayerControlResolver.Resolve(records, ["VCG00", "VCG01"]);
        var opening = FalloutCellSceneReader.Read(records, new("FalloutNV.esm", 0x103df9));
        var saved = FalloutNativeCampaignSave.Read(checkpoint, content.SaveCompatibilityId, records,
            FalloutNativeVigorResolver.Resolve(records, opening), FalloutNativeTagSkillResolver.Resolve(records, controls),
            FalloutOpeningInventoryGrantResolver.Resolve(records, controls, "VCG01"),
            FalloutNativeTraitFarewellResolver.Resolve(records, controls, opening)).State;
        var parts = identity.Split(':');
        if (parts.Length != 2) throw new ArgumentException("Owned actor route needs plugin:hex-object-id.");
        var caller = new FalloutFormKey(parts[0], Convert.ToUInt32(parts[1], 16));
        var record = records.GetEffective(caller);
        if (record.Signature != "ACHR" || record.IsDeleted) throw new InvalidDataException("Actor route needs its actual winning ACHR.");
        world.RestoreEncounterZones(saved.EncounterZones); world.Restore(saved.References!);
        world.RestoreActorOverrides(saved.ActorOverrides); world.RestoreFactionRelations(saved.FactionRelations);
        var state = world.Get(caller);
        if (!state.Enabled || state.Injury?.Dead == true || records.GetEffective(state.Base).Signature != "NPC_")
            throw new InvalidDataException("Actor route requires its actual enabled living source NPC.");
        var globals = FalloutGlobalState.Read(records); globals.Restore(saved.Globals!);
        var vitals = saved.Vitals ?? throw new InvalidDataException("Actor route checkpoint has no player vitals.");
        var cell = FalloutCellSceneReader.Read(records, world.Placement(caller).Cell); world.LoadCell(cell);
        var placed = cell.References.Single(value => value.FormKey == caller);
        var npc = RuntimeNativeNpc.Create(records, content, placed, config.World.GameUnitsToMeters,
            (_, _, _, _) => new StandardMaterial3D(), world.EquippedArmor(caller, vitals.Level, globals),
            world.InitializeActorTemplates(caller, vitals.Level, globals), world.ActorAppearanceOverride(caller));
        try
        {
            npc.Transform = new(GamebryoCoordinate.ConvertReferenceEuler(
                new(placed.RotationRadians[0], placed.RotationRadians[1], placed.RotationRadians[2]), placed.Scale), start);
            var context = new NativeActorCombatContext(() => null, () => vitals,
                (_, _) => throw new InvalidDataException("Actor corridor attempted gameplay damage."),
                (_, _) => throw new InvalidDataException("Actor corridor attempted autonomous combat planning."),
                _ => true, () => vitals.Level, globals, config.Player.StepHeightMeters,
                config.Simulation.GravityMetersPerSecondSquared,
                MaximumWalkableSlopeDegrees: config.Player.MaximumWalkableSlopeDegrees);
            npc.Combat = RuntimeNativeActorCombat.Attach(npc, npc.Skeleton, npc.Appearance.SkeletonPath,
                world, state, records, content, 2, config.Player.CollisionMask, context);
            fixture.AddChild(npc); npc.SetProcess(false); npc.SetPhysicsProcess(false);
            npc.Combat.SetProcess(false); npc.Combat.SetPhysicsProcess(false);
            // Diagnostic controller start is the caller's explicit source-floor
            // endpoint. It never rewrites the reference world or input save.
            npc.GlobalPosition = start;
            var radius = npc.Combat.PreparePortalArrival();
            var envelope = npc.GetNode<CollisionShape3D>("SourceActorMovementEnvelope");
            var recordHash = SHA256.HashData(record.ReadData()); var baseRecord = records.GetEffective(state.Base);
            var baseHash = SHA256.HashData(baseRecord.ReadData()); var skeleton = npc.Appearance.SkeletonPath;
            if (!content.TryRead(skeleton, null, out var source, out var resource)) throw new FileNotFoundException(skeleton);
            var skeletonHash = SHA256.HashData(source);
            var navigation = CellNavigationGraph.LoadOwned(records, navigationCell);
            float[] Point(Vector3 point) => [point.X, point.Y, point.Z];
            GD.Print(JsonSerializer.Serialize(new
            {
                kind = "owned-native-actor-route-owner",
                runtimeBuild = typeof(RuntimeNativeActorCombat).Module.ModuleVersionId,
                content.SaveCompatibilityId,
                actor = caller.ToString(),
                referenceWinner = record.Plugin.Name,
                referenceSha256 = Convert.ToHexString(recordHash),
                baseWinner = baseRecord.Plugin.Name,
                baseSha256 = Convert.ToHexString(baseHash),
                checkpointSha256 = Convert.ToHexString(SHA256.HashData(checkpointBytes)),
                skeleton,
                resource,
                skeletonSha256 = Convert.ToHexString(skeletonHash),
                shape = envelope.Shape.GetClass(),
                envelope = Point(envelope.Position),
                radius,
                height = envelope.Shape switch { CapsuleShape3D capsule => capsule.Height, CylinderShape3D cylinder => cylinder.Height, _ => (float?)null },
                scale = Point(npc.Scale),
                npc.CollisionLayer,
                npc.CollisionMask,
                npc.FloorSnapLength,
                npc.FloorMaxAngle,
                navigation.SourceSha256,
                navigation.NavMeshes,
                navigation.Triangles,
                campaign = false,
                parity = false,
                recording = false,
                boundary = "source-actor-BBX-and-native-controller;selected-STAT-collision;combat-KF-root-distance-and-whole-cell-support-unverified"
            }));
            return new(npc, radius, navigation, () =>
            {
                if (!recordHash.AsSpan().SequenceEqual(SHA256.HashData(record.ReadData())) ||
                    !baseHash.AsSpan().SequenceEqual(SHA256.HashData(baseRecord.ReadData())) ||
                    !content.TryRead(skeleton, null, out var after, out _) || !skeletonHash.AsSpan().SequenceEqual(SHA256.HashData(after)) ||
                    !checkpointBytes.SequenceEqual(File.ReadAllBytes(checkpoint)))
                    throw new InvalidDataException("Owned actor route changed its source or checkpoint input.");
            });
        }
        catch { npc.Free(); throw; }
    }
}
