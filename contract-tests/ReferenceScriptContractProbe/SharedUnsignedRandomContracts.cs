using OpenNV.Runtime.Content;

internal static class SharedUnsignedRandomContracts
{
    internal static void Run()
    {
        var clockCalls = 0;
        var clock = 1u;
        var owner = new FalloutSharedUnsignedRandom(() => { ++clockCalls; return clock; });
        Require(owner.Observation == new FalloutUnsignedRandomObservation(null, 625, 0) &&
            owner.NextBounded(0) == 0 && clockCalls == 0 && owner.Observation.Seed is null,
            "Zero bound seeded or consumed the unsigned stream.");
        uint[] first = [4285570180, 1294850450, 4001963888, 2925490134, 796127836, 1719259434, 497642776, 733914910];
        // Golden vectors are derived from the qualified neutral equations,
        // not a runtime retail oracle or a standard-library RNG.
        var checkpoints = new Dictionary<int, uint>
        {
            [622] = 1734336846,
            [623] = 227326209,
            [624] = 3964708090,
            [625] = 789262563,
            [1247] = 2048569573,
            [1248] = 1664563043,
            [1249] = 222198950
        };
        for (var draw = 0; draw < 1250; ++draw)
        {
            var value = owner.NextBounded(uint.MaxValue);
            if (draw < first.Length) Require(value == first[draw], "Unsigned seed recurrence or raw output changed.");
            if (checkpoints.TryGetValue(draw, out var expected))
                Require(value == expected, "Unsigned block boundary changed.");
            clock = 999; // Later clock changes must not reseed exhausted blocks.
        }
        Require(clockCalls == 1 && owner.Observation == new FalloutUnsignedRandomObservation(1, 2, 1250),
            "Unsigned continuation reseeded or lost its draw/index state.");
        foreach (var bound in new uint[] { 1, 2, 100, 0x7fff, 0x80000001, uint.MaxValue })
        {
            var reduced = new FalloutSharedUnsignedRandom(() => 1);
            var expected = bound is uint.MaxValue or 0x7fff ? first[0] & bound : first[0] % bound;
            Require(reduced.NextBounded(bound) == expected && reduced.Observation.Draws == 1,
                "Unsigned bound masking/modulo added rejection draws or changed reduction.");
        }
        var zero = new FalloutSharedUnsignedRandom(() => 0);
        Require(zero.NextBounded(uint.MaxValue) == 0 && zero.NextBounded(17) == 0 && zero.Observation.Seed == 0,
            "Source zero seed was replaced by a nonzero fallback.");
        var failures = 0;
        var fault = new FalloutSharedUnsignedRandom(() => { ++failures; throw new IOException("Clock unavailable."); });
        try { fault.NextBounded(100); }
        catch (IOException) { }
        Require(failures == 1 && fault.Observation == new FalloutUnsignedRandomObservation(null, 625, 0),
            "Clock failure partially seeded or advanced the stream.");

        var legacy = new FalloutScriptValueStore();
        legacy.Restore(new(0, [], 0x1234));
        var legacyBefore = legacy.Capture();
        _ = owner.NextBounded(100);
        Require(legacy.Capture().RandomState == legacyBefore.RandomState &&
            legacy.RandomPercent() == new FalloutSoundRandomState(0x1234).NextBounded(100),
            "Unsigned adoption converted or consumed the existing saved script stream.");

        var before = FalloutSharedUnsignedRandom.Process.Observation;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var a = FalloutHackingGenerationOwners.FromProcess();
        var b = FalloutHackingGenerationOwners.FromProcess(cancellation.Token);
        Require(ReferenceEquals(a.Random.Target, b.Random.Target) &&
            ReferenceEquals(a.Random.Target, FalloutSharedUnsignedRandom.Process) &&
            a.Clock.Method == b.Clock.Method && a.RetryWindowTicks == 100 && b.RetryWindowTicks == 100,
            "Generation factories created menu-local owners or changed the source clock window.");
        var cancelled = false;
        try
        {
            FalloutHackingCandidates.GenerateFromProcess(FalloutHackingDictionary.ReadSpaceDelimited("AAAA AAAB"u8),
                new(0, 0, 0, 0, 2, 2), value => value, cancellation.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled && FalloutSharedUnsignedRandom.Process.Observation == before,
            "Cancelled generation consumed the shared process stream.");
        if (OperatingSystem.IsWindows()) _ = FalloutSourceMilliseconds.Read();
        else
        {
            var refused = false;
            try { FalloutSourceMilliseconds.Read(); }
            catch (PlatformNotSupportedException) { refused = true; }
            Require(refused, "An unqualified platform clock was silently substituted.");
        }
        Console.WriteLine("OPENNV_SHARED_UNSIGNED_RANDOM_PASS lazySeed=true zeroNoDraw=true " +
            "rawBlocks=true exactBounds=true processIdentity=true cancelNoDraw=true sourceWindow100ms=true " +
            "savedStreamsUnchanged=true nativeHacking=unverified");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
