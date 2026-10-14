using OpenNV.Runtime.Content;

internal static partial class ModContentContracts
{
    private static void VerifyReadRetirement(Func<RuntimeLiveContentSource> open, string loose)
    {
        using var source = open();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var callbacks = 0; var nestedReturned = false;
        source.ResourceReadObserver = (_, _, _) =>
        {
            if (Interlocked.Increment(ref callbacks) != 1) return;
            try { source.Dispose(); throw new Exception("Resource observer retired its entered provider."); }
            catch (InvalidOperationException) { }
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Entered source read was not released.");
            nestedReturned = source.TryRead("textures/extra.dds", null, out _, out _);
        };
        var reader = Task.Run(() => Read(source, "loose patch", "SHARED.DDS"));
        Task? retirement = null;
        try
        {
            Require(entered.Wait(TimeSpan.FromSeconds(5)), "Actual source read never entered its observer.");
            retirement = Task.Run(source.Dispose);
            Require(SpinWait.SpinUntil(() => source.ResourceReadsClosed, TimeSpan.FromSeconds(5)),
                "Actual provider never closed admission for retirement.");
            Require(!retirement.IsCompleted, "Provider returned retirement while its read still held resources.");
            RejectRetired(() => source.TryRead("textures/shared.dds", null, out _, out _));
        }
        finally
        {
            release.Set();
            Require(reader.Wait(TimeSpan.FromSeconds(5)), "Entered owned read failed to drain.");
            if (retirement is not null) Require(retirement.Wait(TimeSpan.FromSeconds(5)), "Owned source failed to retire after its readers returned.");
        }
        Require(nestedReturned, "Already-entered read lost its actual nested resource dependency.");
        RejectRetired(() => source.TryResolve("textures/shared.dds", null, out _));
        RejectRetired(() => source.ResourcePathsUnder("textures"));
        RejectRetired(() => source.CachedResources().ToArray());
        RejectRetired(() => source.ResourceExtent(loose));
        RejectRetired(() => source.TryReadSourceMenuAudio("sound/test.wav", 1, out _, out _));
        source.Dispose();
        using var cold = open();
        Read(cold, "loose patch", "SHARED.DDS");
        Console.WriteLine("OPENNV_CONTENT_SOURCE_LIFETIME_PASS looseReadAfterDisposeRefused=true activeReadDrained=true nestedDependencyReturned=true observerRetirementRefused=true coldProviderFresh=true");
    }

    private static void RejectRetired(Action read)
    {
        try { read(); throw new Exception("Retired owned resource provider admitted a new read."); }
        catch (ObjectDisposedException) { }
    }
}
