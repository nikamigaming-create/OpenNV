using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class UnloadedActorPackageContracts
{
    internal static void Run(FalloutPluginStack records)
    {
        FalloutFormKey Key(uint id) => new("Actors.esm", id);
        var actor = Key(0x90b);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var packages = new FalloutUnloadedActorPackages(records, world, quests, null, null,
            (program, _) => program.RequireEmptyScript(), () => 1);
        world.UnloadedPackages = packages;
        Check(world.CurrentPackage(actor) == Key(0x8f5), "Unloaded actor did not select its actual source package.");
        var firstReceipt = world.PackageEvents.SnapshotPending(actor);
        Check(firstReceipt.Events.Single().Packages!.Single() == Key(0x8f5), "Source assignment lost its package-start receipt.");
        world.PackageEvents.Consume(firstReceipt);
        Check(world.CurrentPackage(actor) == Key(0x8f5) && world.PendingPackageEventCount == 0,
            "Repeated query replayed package-start effects.");
        var deferred = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        var deferredActor = deferred.Single(snapshot => snapshot.Reference == actor);
        Check(deferredActor.DeferredPackageContinuation is { Revision: 1 } &&
            deferredActor.PackageAssignment is { Done: false } && deferredActor.Animation is null && world.PendingProcedureCaptureCount == 0,
            "Deferred source Start lost its owned boundary or invented native progress.");
        CheckDeferredCold(records, quests, actor, deferred);
        CheckRetainedNative(records, actor);
        CheckRetainedMarker();
        quests.EnterStage(Key(0x871), 1);
        Check(world.CurrentPackage(actor) == Key(0x8f6), "Unloaded package query retained stale quest eligibility.");
        var secondReceipt = world.PackageEvents.SnapshotPending(actor);
        Check(secondReceipt.Events.Count == 2 && secondReceipt.Events.All(value => value.Name != "OnPackageDone"),
            "Selection change invented arrival or lost its source events.");
        world.PackageEvents.Consume(secondReceipt);
        var native = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Handoff replayed a source event."));
        packages.BindNative(actor, native);
        native.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x8f6))));
        Check(native.Active?.Form == Key(0x8f6) && !native.Done && world.PendingPackageEventCount == 0,
            "Native handoff invented completion or repeated start.");
        Check(native.Revision == 3 && native.LastEvent == "POBA" && world.Get(actor).DeferredPackageContinuation is null,
            "Native handoff lost consumed transition history or retained the deferred owner.");
        var state = world.Get(actor);
        state.CapturePackageAssignment = () => FalloutActorPackageAssignment.Capture(records, native);
        var snapshots = world.Capture();
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(snapshots))!);
        cold.UnloadedPackages = new(records, cold, quests, null, null,
            (program, _) => program.RequireEmptyScript(), () => 1);
        Check(cold.CurrentPackage(actor) == Key(0x8f6) && cold.PendingPackageEventCount == 0,
            "Cold assignment lost source identity or replayed its prefix.");
        using var directCold = new FalloutReferenceWorld(records);
        directCold.Restore(snapshots);
        directCold.UnloadedPackages = new(records, directCold, quests, null, null,
            (program, _) => program.RequireEmptyScript(), () => 1);
        var directNative = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Direct cold admission replayed a source event."));
        directCold.UnloadedPackages.BindNative(actor, directNative);
        directNative.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x8f6))));
        Check(directNative.Active?.Form == Key(0x8f6) && directCold.PendingPackageEventCount == 0,
            "Cold native admission required an intervening unloaded query or replayed effects.");
        var completedEvents = new FalloutPackageEvents((_, _) => { });
        completedEvents.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x8f6))));
        completedEvents.Complete();
        var completedAssignment = FalloutActorPackageAssignment.Capture(records, completedEvents)!;
        var earlyNative = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Early native binding replayed source events."));
        completedAssignment.Bind(records, earlyNative);
        earlyNative.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x8f6))));
        earlyNative.Complete();
        Check(earlyNative.Active?.Form == Key(0x8f6) && earlyNative.Done && earlyNative.Revision == 0,
            "Native assembly before scheduler construction lost its saved completion or replayed results.");
        var rejectedEarly = new FalloutPackageEvents((_, _) => { });
        Reject(() => (completedAssignment with { Sha256 = new string('0', 64) }).Bind(records, rejectedEarly));
        Check(rejectedEarly.Active is null, "Source drift partially bound an early native lifecycle.");
        using var invalid = new FalloutReferenceWorld(records);
        Reject(() => invalid.Restore(snapshots.Select(snapshot => snapshot with
        {
            PackageAssignment = snapshot.PackageAssignment is { } assignment ? assignment with { Sha256 = new string('0', 64) } : null
        }).ToArray()));
        world.LoadCell(FalloutCellSceneReader.Read(records, Key(0x880)));
        Reject(() => world.CurrentPackage(actor));
        state.QueryCurrentPackage = () => native.Active?.Form;
        Check(world.CurrentPackage(actor) == Key(0x8f6), "Resident package query did not use the actual native owner.");
        Reject(() => world.CurrentPackage(Key(0x903)));
        Console.WriteLine("OPENNV_UNLOADED_PACKAGE_CONTRACT_PASS sourceSelection=true liveQuest=true sourceEvents=true noArrival=true deferredCold=true deferredHistory=true residentBoundary=true nativeHandoff=true earlyNativeCompleted=true coldAssignment=true sourceDriftRefused=true missingResidentOwnerRefused=true fixture=assignment-native-lifecycle-no-body");
    }

    private static void CheckDeferredCold(FalloutPluginStack records, FalloutQuestState quests, FalloutFormKey actor,
        FalloutReferenceSnapshot[] snapshots)
    {
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(snapshots);
        cold.UnloadedPackages = new(records, cold, quests, null, null,
            (_, _) => throw new InvalidDataException("Cold deferred Start replayed source effects."), () => 1);
        var retained = snapshots.Single(snapshot => snapshot.Reference == actor).DeferredPackageContinuation!;
        Check(cold.CurrentPackage(actor) == retained.Assignment.Package && cold.PendingPackageEventCount == 0 &&
            JsonSerializer.Serialize(cold.Capture()) == JsonSerializer.Serialize(snapshots),
            "Cold deferred assignment changed its revision, source state or consumed effects.");
        var state = cold.Get(actor);
        state.DeferredPackageContinuation = null;
        Check(cold.PendingProcedureCaptureCount == 1, "Missing deferred ownership became saveable.");
        Reject(() => cold.Capture());
        state.DeferredPackageContinuation = retained;
        cold.LoadCell(FalloutCellSceneReader.Read(records, new("Actors.esm", 0x880)));
        Check(cold.PendingProcedureCaptureCount == 1, "Resident deferred actor was accepted without its native owner.");
        Reject(() => cold.Capture());
        var native = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Cold native handoff replayed source effects."));
        cold.UnloadedPackages.BindNative(actor, native);
        Check(native.Active?.Form == retained.Assignment.Package && !native.Done && native.Revision == retained.Revision &&
            native.LastEvent == "POBA" && cold.PendingPackageEventCount == 0 && state.DeferredPackageContinuation is null,
            "Cold native handoff lost its Start boundary or invented completion.");
        foreach (var invalid in new[]
        {
            retained with { Reference = new("Actors.esm", 0x903) }, retained with { Revision = 0 },
            retained with { Assignment = retained.Assignment with { Done = true } },
            retained with { Assignment = retained.Assignment with { Sha256 = new('0', 64) } }
        })
        {
            using var rejected = new FalloutReferenceWorld(records);
            Reject(() => rejected.Restore(snapshots.Select(value => value.Reference == actor ? value with
            { DeferredPackageContinuation = invalid, PackageAssignment = invalid.Assignment } : value).ToArray()));
            Check(rejected.InstanceCount == 0, "Invalid deferred boundary partially mutated the world.");
        }
        using var mixed = new FalloutReferenceWorld(records);
        Reject(() => mixed.Restore(snapshots.Select(value => value.Reference == actor ? value with
        { Animation = new("meshes/fixture/idle.kf", new('a', 64), 0, true) } : value).ToArray()));
        Check(mixed.InstanceCount == 0, "Deferred ownership accepted an already-started native clock.");
        using var mixedObjects = new FalloutReferenceWorld(records);
        Reject(() => mixedObjects.Restore(snapshots.Select(value => value.Reference == actor ? value with
        { ObjectAnimations = [] } : value).ToArray()));
        Check(mixedObjects.InstanceCount == 0, "Deferred ownership accepted an initialized native object-animation owner.");
        using var priorNative = new FalloutReferenceWorld(records);
        priorNative.Get(actor).Animation.Change("meshes/fixture/idle.kf", new('a', 64));
        priorNative.UnloadedPackages = new(records, priorNative, quests, null, null,
            (program, _) => program.RequireEmptyScript(), () => 1);
        _ = priorNative.CurrentPackage(actor);
        Check(priorNative.Get(actor).DeferredPackageContinuation is null && priorNative.PendingProcedureCaptureCount == 1,
            "Prior native state was replaced with a deferred-initialization receipt.");
        Reject(() => priorNative.Capture());
    }

    private static void CheckRetainedNative(FalloutPluginStack records, FalloutFormKey actor)
    {
        FalloutFormKey Key(uint id) => new("Actors.esm", id);
        using var world = new FalloutReferenceWorld(records);
        var quests = new FalloutQuestState(records);
        var packages = new FalloutUnloadedActorPackages(records, world, quests, null, null,
            (program, _) => program.RequireEmptyScript(), () => 1);
        world.UnloadedPackages = packages;
        var native = new FalloutPackageEvents((_, _) => { });
        native.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x8f5))));
        var state = world.Get(actor);
        var previous = FalloutActorPackageAssignment.Capture(records, native)!;
        state.PackageAssignment = previous;
        state.Animation.Change("meshes/fixture/idle.kf", new('a', 64));
        state.PackageMotion = new(previous.Package, previous.Sha256, "meshes/fixture/walk.kf", new('b', 64),
            12.5, false, [1, 2, 3], [0, 0, 0, 1]);
        var pose = JsonSerializer.Serialize(state.PackageMotion);
        var clock = JsonSerializer.Serialize(state.Animation.Capture());
        _ = world.InitializeActorTemplates(actor, 1, null);
        var random = state.SoundRandom.State;
        packages.Retain(actor, native);
        Check(world.CurrentPackage(actor) == previous.Package && state.DeferredPackageContinuation is null,
            "Unchanged unloaded native assignment invented a transition.");
        quests.EnterStage(Key(0x871), 1);
        Check(world.CurrentPackage(actor) == Key(0x8f6), "Prior native motion suppressed new source eligibility.");
        var boundary = state.DeferredPackageContinuation ?? throw new InvalidDataException("Retired native body lost deferred ownership.");
        Check(boundary is { Revision: 3 } && boundary.RetainedAssignment == previous &&
            boundary.Assignment.Package == Key(0x8f6) && world.PendingProcedureCaptureCount == 0,
            "Changed unloaded assignment lost its actual native predecessor or lifecycle.");
        Check(JsonSerializer.Serialize(state.PackageMotion) == pose &&
            JsonSerializer.Serialize(state.Animation.Capture()) == clock && state.SoundRandom.State == random,
            "Unloaded selection advanced or replaced the physical predecessor.");
        world.PackageEvents.Consume(world.PackageEvents.SnapshotPending(actor));
        var saved = JsonSerializer.Deserialize<FalloutReferenceSnapshot[]>(JsonSerializer.Serialize(world.Capture()))!;
        using var cold = new FalloutReferenceWorld(records);
        cold.Restore(saved);
        cold.UnloadedPackages = new(records, cold, quests, null, null,
            (_, _) => throw new InvalidDataException("Cold retained selection replayed source effects."), () => 1);
        Check(cold.CurrentPackage(actor) == Key(0x8f6) && cold.PendingPackageEventCount == 0 &&
            JsonSerializer.Serialize(cold.Get(actor).PackageMotion) == pose &&
            cold.Get(actor).DeferredPackageContinuation == boundary,
            "Cold unloaded selection changed the retained physical phase or consumed history.");
        cold.LoadCell(FalloutCellSceneReader.Read(records, Key(0x880)));
        var incoming = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Native retained handoff replayed source effects."));
        cold.UnloadedPackages.BindNative(actor, incoming);
        incoming.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x8f6))));
        Check(incoming.Revision == 3 && incoming.LastEvent == "POBA" &&
            cold.Get(actor).DeferredPackageContinuation == boundary && cold.PendingProcedureCaptureCount == 1,
            "Native binding consumed the physical handoff before an actual incoming observation.");
        Reject(() => cold.Capture());
        foreach (var (invalid, invalidMotion) in new[]
        {
            (boundary with { RetainedAssignment = previous with { Sha256 = new('0', 64) } },
                state.PackageMotion! with { PackageSha256 = new('0', 64) }),
            (boundary with { RetainedAssignment = boundary.Assignment }, state.PackageMotion!)
        })
        {
            using var rejected = new FalloutReferenceWorld(records);
            Reject(() => rejected.Restore(saved.Select(value => value.Reference == actor ? value with
            { DeferredPackageContinuation = invalid, PackageMotion = invalidMotion } : value).ToArray()));
            Check(rejected.InstanceCount == 0, "Invalid predecessor partially restored a reference world.");
        }
        using var repeated = new FalloutReferenceWorld(records);
        repeated.Restore(saved.Select(value => value.Reference == actor ? value with
        {
            PackageAssignment = previous,
            DeferredPackageContinuation = new(actor, previous, 5, previous)
        } : value).ToArray());
        repeated.UnloadedPackages = new(records, repeated, new(records), null, null,
            (_, _) => throw new InvalidDataException("Same-ID native handoff replayed source effects."), () => 1);
        repeated.LoadCell(FalloutCellSceneReader.Read(records, Key(0x880)));
        var sameId = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Same-ID lifecycle replayed source effects."));
        repeated.UnloadedPackages.BindNative(actor, sameId);
        sameId.Change(FalloutScriptPackage.Read(records.GetEffective(previous.Package)));
        Check(sameId.Revision == 5 && repeated.Get(actor).DeferredPackageContinuation?.RetainedAssignment == previous &&
            repeated.Get(actor).PackageMotion?.Seconds == 12.5 && repeated.PendingProcedureCaptureCount == 1,
            "A-to-B-to-A handoff resumed its old physical clock or collapsed the new lifecycle.");
        Console.WriteLine("OPENNV_UNLOADED_RETAINED_NATIVE_PASS unchangedPose=true unchangedClock=true noRandomDraw=true cold=true noEventReplay=true nativePublicationRequired=true sourceDriftRefused=true fixture=no-native-body");
    }

    private static void CheckRetainedMarker()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-unloaded-marker-history-").FullName;
        try
        {
            byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
            byte[] Field(string name, byte[] data)
            {
                var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length));
                data.CopyTo(bytes, 6); return bytes;
            }
            byte[] Record(string name, uint id, params byte[][] fields)
            {
                var data = Join(fields); var bytes = new byte[24 + data.Length];
                Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), id); data.CopyTo(bytes, 24); return bytes;
            }
            var references = Join(Record("ACHR", 0x900, Field("NAME", BitConverter.GetBytes(0x700u)), Field("DATA", new byte[24])),
                Record("REFR", 0x901, Field("NAME", BitConverter.GetBytes(0x710u)), Field("DATA", new byte[24])));
            var group = new byte[24 + references.Length]; Encoding.ASCII.GetBytes("GRUP").CopyTo(group, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x800);
            BinaryPrimitives.WriteInt32LittleEndian(group.AsSpan(12), 6); references.CopyTo(group, 24);
            var packageData = new byte[12]; packageData[4] = 12;
            var location = new byte[12]; location[0] = 3;
            File.WriteAllBytes(Path.Combine(directory, "MarkerHistory.esm"), Join(
                Record("TES4", 0, Field("HEDR", new byte[12])), Record("NPC_", 0x700),
                Record("PACK", 0x720, Field("EDID", Encoding.ASCII.GetBytes("MarkerHistorySandbox\0")),
                    Field("PKDT", packageData), Field("PLDT", location), Field("IDLC", [0]), Field("POBA", [])),
                Record("IDLM", 0x710, Field("IDLF", [0]), Field("IDLC", [0]), Field("IDLT", BitConverter.GetBytes(0f))),
                Record("CELL", 0x800, Field("DATA", [1])), group));
            using var records = FalloutPluginStack.Load(directory, ["MarkerHistory.esm"]);
            using var world = new FalloutReferenceWorld(records);
            FalloutFormKey Key(uint id) => new("MarkerHistory.esm", id);
            string Hash(uint id) => FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(Key(id)));
            var actor = Key(0x900); var marker = Key(0x901); var state = world.Get(actor);
            var packages = new FalloutUnloadedActorPackages(records, world, new(records), null, null,
                (_, _) => throw new InvalidDataException("Marker handoff replayed source effects."), () => 1);
            world.UnloadedPackages = packages;
            var events = new FalloutPackageEvents((_, _) => { });
            events.Change(FalloutScriptPackage.Read(records.GetEffective(Key(0x720))));
            var assignment = FalloutActorPackageAssignment.Capture(records, events)!;
            state.PackageAssignment = assignment;
            state.Animation.Change("meshes/fixture/idle.kf", new('a', 64)); state.Animation.Advance(.5);
            state.PackageMotion = new(assignment.Package, assignment.Sha256, "meshes/fixture/idle.kf", new('a', 64),
                .5, false, [0, 0, 0], [0, 0, 0, 1], Sandbox: new(FalloutSandboxActivity.IdleMarker, 9, 1,
                    new FalloutSandboxPreferences().Capture(), marker));
            var retained = new FalloutActorSandboxMarkerContinuation(assignment, events.Revision, events.LastEvent,
                events.LastPackage, Hash(0x720), 123, .75, null, assignment.Package,
                new(false, false, false, false, false, false, 0), null, marker, Key(0x710), Hash(0x710),
                FalloutSandboxMarkerPhase.Approach, [0, 0, 0]);
            state.SandboxMarker = retained;
            retained.Validate(records, world, state);
            Check(world.ReserveIdleMarker(marker, 0, actor, world.IdleMarkerOccupied), "Marker history lost its source reservation.");
            var random = state.SoundRandom.State;
            var before = JsonSerializer.Serialize(world.Capture());
            for (var handoff = 0; handoff < 2; ++handoff)
            {
                packages.Retain(actor, events);
                var incoming = new FalloutPackageEvents((_, _) => throw new InvalidDataException("Marker restoration replayed a source event."));
                packages.BindNative(actor, incoming);
                Check(incoming.Active is null && incoming.Revision == 0 && incoming.LastEvent is null && incoming.LastPackage is null,
                    "Unloaded handoff consumed the marker owner's package history.");
                retained.Assignment.Bind(records, incoming);
                incoming.RestoreHistory(retained.EventRevision, retained.LastEvent, retained.LastPackage);
                Check(incoming.Active?.Form == retained.Package && !incoming.Done && incoming.Revision == 1 &&
                    incoming.LastEvent == "POBA" && incoming.LastPackage == retained.Package &&
                    world.PendingPackageEventCount == 0 && state.SoundRandom.State == random &&
                    world.OwnsIdleMarker(marker, 0, actor) && JsonSerializer.Serialize(world.Capture()) == before,
                    "Warm marker restoration changed its consumed lifecycle, reservation, motion or randomness.");
                events = incoming;
            }
            Console.WriteLine("OPENNV_UNLOADED_MARKER_HISTORY_PASS warmRebind=true repeated=true markerHistoryOwner=true noEventReplay=true unchangedContinuation=true fixture=no-native-body");
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static byte[] StageCondition(uint quest)
    {
        var data = new byte[28]; data[0] = 128;
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8), 58);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), quest);
        return data;
    }
    private static void Check(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException) { return; }
        throw new InvalidDataException("Unowned package continuation or invalid source was admitted.");
    }
}
