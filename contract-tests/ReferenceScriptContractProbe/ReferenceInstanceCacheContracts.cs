using OpenNV.Runtime.World.Cells;

internal static class ReferenceInstanceCacheContracts
{
    internal static void Run()
    {
        using var fixture = new ScriptSaveFixture("");
        using var other = new FalloutReferenceWorld(fixture.Records);
        var first = fixture.World.Get(fixture.Caller);
        var independent = other.Get(fixture.Caller);
        Require(!ReferenceEquals(first, independent), "Reference state escaped its world owner.");
        first.Enabled = false;
        first.Write(1, 23);
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
        other.Dispose();
        try { _ = other.Get(fixture.Caller); throw new InvalidDataException("Disposed world returned cached live state."); }
        catch (ObjectDisposedException) { }
        Console.WriteLine("OPENNV_REFERENCE_INSTANCE_CACHE_PASS liveIdentity=true independentWorlds=true liveState=true warmedAllocations=0 disposedHitsRefused=true");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }
}
