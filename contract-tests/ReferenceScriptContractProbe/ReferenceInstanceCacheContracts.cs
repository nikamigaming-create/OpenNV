using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Content;

internal static class ReferenceInstanceCacheContracts
{
    internal static void Run()
    {
        using var fixture = new ScriptSaveFixture("", includeSecondReference: true);
        using var other = new FalloutReferenceWorld(fixture.Records);
        var first = fixture.World.Get(fixture.Caller);
        var independent = other.Get(fixture.Caller);
        Require(!ReferenceEquals(first, independent), "Reference state escaped its world owner.");
        first.Enabled = false;
        first.Write(1, 23);
        var sibling = fixture.World.Get(new("Saves.esm", 0x92));
        Require(ReferenceEquals(first.Script, sibling.Script) && sibling.Read(1) == 0 && sibling.Enabled,
            "References sharing an immutable source definition shared mutable state.");
        Require(independent.Enabled && independent.Read(1) == 0 &&
            ReferenceEquals(first, fixture.World.Get(fixture.Caller)) && fixture.World.Get(fixture.Caller).Read(1) == 23,
            "Cached lookups lost independent live enable or script state.");
        for (var index = 0; index < 256; index++) _ = fixture.World.Get(fixture.Caller);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 16384; index++)
            if (!ReferenceEquals(first, fixture.World.Get(fixture.Caller)))
                throw new InvalidDataException("A repeated lookup replaced live reference identity.");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, $"Cached live reference lookups allocated {allocated} bytes.");
        var snapshot = fixture.World.ResidentSnapshot();
        Require(snapshot.Count == 3 && ReferenceEquals(snapshot, fixture.World.ResidentSnapshot()) &&
            ReferenceEquals(snapshot.Single(value => value.Reference == fixture.Caller), first),
            "Resident snapshot did not reuse the actual live reference instances.");
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 16384; index++)
            if (!ReferenceEquals(snapshot, fixture.World.ResidentSnapshot()))
                throw new InvalidDataException("Unchanged residency rebuilt its snapshot.");
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0, $"Cached resident snapshots allocated {allocated} bytes.");
        var scene = FalloutCellSceneReader.Read(fixture.Records, fixture.Cell);
        fixture.World.ReplaceResidentCell(scene with { References = scene.References.Where(value => value.FormKey != fixture.Caller).ToArray() });
        var replaced = fixture.World.ResidentSnapshot();
        Require(replaced.Count == 2 && !ReferenceEquals(snapshot, replaced) && snapshot.Count == 3 &&
            replaced.All(value => value.Reference != fixture.Caller),
            "Residency replacement changed a retained snapshot or left a stale current member.");
        fixture.World.UnloadCell(fixture.Cell);
        Require(fixture.World.ResidentSnapshot().Count == 0, "Unloading retained stale sandbox candidates.");
        fixture.World.LoadCell(scene);
        Require(fixture.World.ResidentSnapshot().Count == 3 && ReferenceEquals(first, fixture.World.Get(fixture.Caller)),
            "Reloading did not restore actual live references to the current snapshot.");
        other.Dispose();
        try { _ = other.Get(fixture.Caller); throw new InvalidDataException("Disposed world returned cached live state."); }
        catch (ObjectDisposedException) { }
        Console.WriteLine("OPENNV_REFERENCE_INSTANCE_CACHE_PASS liveIdentity=true independentWorlds=true liveState=true warmedAllocations=0 disposedHitsRefused=true");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
