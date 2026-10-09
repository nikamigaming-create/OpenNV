using System.Buffers.Binary;
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
        Check(priorNative.Get(actor) is { DeferredPackageContinuation: null, PackageAssignment: null,
            PendingPackageChoice.Package: not null } prior && prior.Animation.Resource == "meshes/fixture/idle.kf" &&
            priorNative.PendingPackageEventCount == 0,
            "Prior native state was replaced, or a pending choice entered deferred Start effects.");
        var priorState = priorNative.Capture();
        using var priorCold = new FalloutReferenceWorld(records);
        priorCold.Restore(priorState);
        Check(priorCold.Get(actor).PendingPackageChoice == priorNative.Get(actor).PendingPackageChoice &&
            priorCold.Get(actor).Animation.Capture() == priorNative.Get(actor).Animation.Capture() &&
            priorCold.PendingPackageEventCount == 0,
            "Cold pending choice changed the retained native clock or replayed Start.");
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
