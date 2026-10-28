using OpenNV.Runtime.Presentation.Rendering;

internal static class PrototypeRetirementProbe
{
    internal static void Run()
    {
        var hot = new object(); var cold = new object(); var replacement = new object();
        var cache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase) { ["hot"] = hot, ["cold"] = cold };
        var retirement = new NativePrototypeRetirement<object>();
        retirement.EnqueueCold(cache, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hot" });
        if (cache.Count != 1 || cache["hot"] != hot || retirement.Count != 1)
            throw new InvalidOperationException("Prototype retirement did not detach only the cold cache entry.");
        cache["cold"] = replacement;
        try { retirement.RetireNext(_ => throw new InvalidOperationException("simulated native free failure")); }
        catch (InvalidOperationException error) when (error.Message == "simulated native free failure") { }
        if (retirement.Count != 1 || cache["cold"] != replacement)
            throw new InvalidOperationException("Failed retirement lost its owner or changed a replacement cache entry.");
        var released = new List<object>();
        retirement.Drain(released.Add);
        if (retirement.Count != 0 || !released.SequenceEqual([cold]) || cache["cold"] != replacement || cache["hot"] != hot)
            throw new InvalidOperationException("Retirement released the wrong owner or left a pending entry.");
        Console.WriteLine("OPENNV_PROTOTYPE_RETIREMENT_PASS coldDetached=true failedReleaseRetained=true replacementPreserved=true");
    }
}
