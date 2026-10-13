using OpenNV.Runtime.World.Physics;

internal static class PenetrationClockContracts
{
    internal static void Run()
    {
        var source = FalloutPenetrationClockSource.ForExecutable(FalloutPhysicalConstructorInputs.ReviewedEngineSha256);
        Reject<NotSupportedException>(() => FalloutPenetrationClockSource.ForExecutable(new string('0', 64)));
        FrequencyStores(source);
        DurationConversion(source);
        NativeApiPrefixes(source);
        NativeClock(source);
        Console.WriteLine("OPENNV_PENETRATION_CLOCK_OK units=counterTicks scale=retainedFrequencyFloat32 " +
            "duration=truncateSigned64 invalidConversion=refused canonicalRegisters=unverified floatingFlags=unverified gameplayParity=unverified");
    }

    private static void FrequencyStores(FalloutPenetrationClockSource source)
    {
        foreach (var (frequency, scale) in new (long, uint)[]
        {
            (10_000_000, 0x461c4000), (16_777_219, 0x46831271),
            (1_099_528_404_995, 0x4e8312f2), (-1, 0xba83126f), (long.MaxValue, 0x5a03126f),
        })
            Require(source.TicksPerMillisecondBits(frequency) == scale,
                "Source frequency changed signed64 conversion, Float32 store or Float32 division order.");
        Require(source.TicksPerMillisecondBits(16_777_219) != BitConverter.SingleToUInt32Bits((float)(16_777_219d / 1000d)),
            "Source frequency collapsed its distinct intermediate Float32 store into Double division.");
    }

    private static void DurationConversion(FalloutPenetrationClockSource source)
    {
        foreach (var (bits, ticks) in new (uint, long)[]
        {
            (0, 0), (0x80000000, 0), (1, 0), (0x80000001, 0),
            (0x3f7fffff, 0), (0xbf7fffff, 0), (0x3fc00000, 1), (0xbfc00000, -1),
            (0x4f000000, 2_147_483_648), (0xcf000000, -2_147_483_648),
            (0x4f800000, 4_294_967_296), (0x5effffff, 9_223_371_487_098_961_920), (0xdf000000, long.MinValue),
        })
            Require(source.ConvertDurationTicks(bits) == ticks,
                "Source finite duration changed truncation, signed magnitude or a full-width boundary.");
        foreach (var bits in new uint[] { 0x5f000000, 0xdf000001, 0x7fc12345, 0x7f800000, 0xff800000 })
            Reject<NotSupportedException>(() => source.ConvertDurationTicks(bits));
    }

    private static void NativeApiPrefixes(FalloutPenetrationClockSource source)
    {
        var api = new Api(); using var clock = new FalloutNativePenetrationClock(source, api);
        Require(clock.Capture() is { Initialized: false, Frequency: null, TicksPerMillisecondBits: null },
            "Constructed clock supplied a scale without real source frequency initialization.");
        Reject<InvalidOperationException>(() => clock.ReadCounterTicks());
        clock.Initialize();
        var initialized = clock.Capture();
        Require(initialized is { Initialized: true, Frequency: 10_000_000, TicksPerMillisecondBits: 0x461c4000 } && api.FrequencyReads == 1,
            "Source clock did not retain the actual frequency-derived scale.");
        api.Counters.Enqueue((false, 0)); api.Counters.Enqueue((false, 1L << 45));
        Require(clock.ReadCounterTicks() == 0 && clock.ReadCounterTicks() == 1L << 45,
            "Counter wrapper reinterpreted ignored BOOL, invented fallback time or truncated actual output.");
        Require(clock.ReadTicksPerMillisecondBits() == initialized.TicksPerMillisecondBits && api.FrequencyReads == 1,
            "Counter sampling or model borrowing recomputed the retained source scale.");
        Require(clock.ConvertDurationTicks(BitConverter.SingleToUInt32Bits(50_000_000f)) == 50_000_000,
            "Ordinary finite source duration did not reach the full signed64 owner.");
        Reject<NotSupportedException>(() => clock.ConvertDurationTicks(0x7fc12345));
        Require(clock.Capture().Failure is { Phase: "duration-conversion" },
            "Unknown exceptional conversion lost its original failed callback prefix.");
        Reject<InvalidOperationException>(() => clock.ReadCounterTicks());
        clock.Dispose(); Reject<ObjectDisposedException>(() => clock.ReadTicksPerMillisecondBits());

        var failedApi = new Api { FrequencySucceeds = false, Frequency = 73 };
        using var failed = new FalloutNativePenetrationClock(source, failedApi);
        Reject<NotSupportedException>(failed.Initialize);
        Require(failed.Capture() is
        {
            Initialized: false, Frequency: null, TicksPerMillisecondBits: null,
            Failure.Phase: "native-frequency-read"
        } && failedApi.FrequencyReads == 1,
            "Failed native frequency became initialized zero/input output or lost the source failure phase.");
        Reject<InvalidOperationException>(failed.Initialize);
        Require(failedApi.FrequencyReads == 1, "Failed source frequency replayed a native startup callback.");
    }

    private static void NativeClock(FalloutPenetrationClockSource source)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("OPENNV_PENETRATION_NATIVE_CLOCK_UNEXECUTED platform=nonWindows"); return;
        }
        using var clock = new FalloutNativePenetrationClock(source);
        clock.Initialize();
        var initialized = clock.Capture();
        Require(initialized is { Initialized: true, Frequency: > 0, TicksPerMillisecondBits: not null },
            "Genuine Windows frequency did not publish a current positive scale.");
        var first = clock.ReadCounterTicks(); var previous = first;
        for (var index = 0; index < 16; index++)
        {
            var current = clock.ReadCounterTicks();
            Require(current >= previous, "Genuine Windows counter observations moved backwards within one clock lifetime.");
            previous = current;
        }
        Require(clock.Capture().Frequency == initialized.Frequency &&
            clock.Capture().TicksPerMillisecondBits == initialized.TicksPerMillisecondBits,
            "Genuine counter sampling changed its initialized source scale.");
        var milliseconds = FalloutPhysicalFloat32.Multiply(5f, FalloutPenetrationClockSource.MillisecondsPerSecond);
        var duration = clock.ConvertDurationTicks(BitConverter.SingleToUInt32Bits(FalloutPhysicalFloat32.Multiply(milliseconds,
            BitConverter.UInt32BitsToSingle(clock.ReadTicksPerMillisecondBits()))));
        Require(duration > 0, "Genuine native frequency produced no ordinary positive source duration.");
        Console.WriteLine($"OPENNV_PENETRATION_NATIVE_CLOCK_OK frequency={initialized.Frequency} " +
            $"scaleBits={initialized.TicksPerMillisecondBits:x8} firstCounterTicks={first} lastCounterTicks={previous} " +
            $"fiveSecondDurationTicks={duration} provider=WindowsKernel32 graphics=none");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class Api : IFalloutPenetrationNativeClockApi
    {
        internal long Frequency { get; set; } = 10_000_000;
        internal bool FrequencySucceeds { get; set; } = true;
        internal int FrequencyReads { get; private set; }
        internal Queue<(bool Succeeds, long Ticks)> Counters { get; } = [];
        public bool QueryPerformanceFrequency(out long frequency)
        { FrequencyReads++; frequency = Frequency; return FrequencySucceeds; }
        public bool QueryPerformanceCounter(out long ticks)
        { var next = Counters.Dequeue(); ticks = next.Ticks; return next.Succeeds; }
    }
}
