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
        Reject(() => world.Capture());
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
        Console.WriteLine("OPENNV_UNLOADED_PACKAGE_CONTRACT_PASS sourceSelection=true liveQuest=true sourceEvents=true noArrival=true nativeHandoff=true earlyNativeCompleted=true coldAssignment=true sourceDriftRefused=true missingResidentOwnerRefused=true fixture=assignment-native-lifecycle-no-body");
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
