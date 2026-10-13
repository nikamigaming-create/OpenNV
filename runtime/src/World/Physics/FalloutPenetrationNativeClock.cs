using System.Runtime.InteropServices;

namespace OpenNV.Runtime.World.Physics;

internal interface IFalloutPenetrationNativeClockApi
{
    bool QueryPerformanceFrequency(out long frequency);
    bool QueryPerformanceCounter(out long ticks);
}

internal sealed record FalloutPenetrationClockSnapshot(string EngineSha256, bool Initialized,
    long? Frequency, uint? TicksPerMillisecondBits, string Phase, FalloutPenetrationFailure? Failure);

// Actor model replacement borrows this retained source clock; it does not
// initialize another frequency or change the scale of an existing utility.
internal sealed class FalloutNativePenetrationClock : IFalloutPenetrationClock, IDisposable
{
    private readonly FalloutPenetrationClockSource _source;
    private readonly IFalloutPenetrationNativeClockApi _api;
    private long? _frequency;
    private uint? _ticksPerMillisecondBits;
    private bool _busy, _disposed;
    private string _phase = "constructed";
    private FalloutPenetrationFailure? _failure;

    internal FalloutNativePenetrationClock(FalloutPenetrationClockSource source) : this(source, new WindowsClockApi()) { }

    internal FalloutNativePenetrationClock(FalloutPenetrationClockSource source, IFalloutPenetrationNativeClockApi api)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(api);
        _source = source; _api = api;
    }

    internal void Initialize()
    {
        Enter("frequency-initialization", requireInitialized: false);
        try
        {
            if (_frequency is not null || _ticksPerMillisecondBits is not null)
                throw new InvalidOperationException("Source performance-counter scale is already initialized.");
            _phase = "native-frequency-read";
            // The original frequency destination is uninitialized on API
            // failure. Refuse that arm rather than supplying zero as authority.
            if (!_api.QueryPerformanceFrequency(out var frequency))
                throw new NotSupportedException("source-penetration-performance-frequency-unwritten");
            _frequency = frequency;
            _phase = "frequency-scale-conversion";
            _ticksPerMillisecondBits = _source.TicksPerMillisecondBits(frequency);
            _phase = "initialized";
        }
        catch (Exception error) { Fail("initialization", error); throw; }
        finally { _busy = false; }
    }

    public long ReadCounterTicks()
    {
        Enter("counter-read");
        try
        {
            // The wrapper initializes this output to zero and ignores BOOL.
            // Preserve the actual API output even when BOOL is false.
            _ = _api.QueryPerformanceCounter(out var ticks);
            _phase = "counter-read-completed";
            return ticks;
        }
        catch (Exception error) { Fail("counter-read", error); throw; }
        finally { _busy = false; }
    }

    public uint ReadTicksPerMillisecondBits()
    {
        RequireCurrent();
        return _ticksPerMillisecondBits ?? throw new InvalidOperationException("Source performance-counter scale is not initialized.");
    }

    public long ConvertDurationTicks(uint scaledFloat32Bits)
    {
        Enter("duration-conversion");
        try
        {
            var ticks = _source.ConvertDurationTicks(scaledFloat32Bits);
            _phase = "duration-conversion-completed";
            return ticks;
        }
        catch (Exception error) { Fail("duration-conversion", error); throw; }
        finally { _busy = false; }
    }

    internal FalloutPenetrationClockSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Source clock snapshot cannot interrupt a current native callback.");
        return new(_source.EngineSha256, _ticksPerMillisecondBits is not null,
            _frequency, _ticksPerMillisecondBits, _phase, _failure);
    }

    private void Enter(string phase, bool requireInitialized = true)
    {
        RequireCurrent();
        if (requireInitialized && _ticksPerMillisecondBits is null)
            throw new InvalidOperationException("Source performance-counter clock has not initialized its retained scale.");
        _busy = true; _phase = phase;
    }

    private void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy) throw new InvalidOperationException("Source performance-counter clock cannot reenter a current callback.");
        if (_failure is not null) throw new InvalidOperationException("Source performance-counter clock retains a failed " + _failure.Phase + " prefix.");
    }

    private void Fail(string operation, Exception error) => _failure ??=
        new(operation, _phase, error.GetType().FullName ?? error.GetType().Name, error.Message);

    public void Dispose()
    {
        if (_disposed) return;
        if (_busy) throw new InvalidOperationException("Source clock retirement cannot interrupt a current native callback.");
        _disposed = true;
    }

    private sealed class WindowsClockApi : IFalloutPenetrationNativeClockApi
    {
        public bool QueryPerformanceFrequency(out long frequency)
        {
            RequireWindows();
            return QueryPerformanceFrequencyNative(out frequency);
        }

        public bool QueryPerformanceCounter(out long ticks)
        {
            RequireWindows(); ticks = 0;
            return QueryPerformanceCounterNative(ref ticks);
        }

        private static void RequireWindows()
        {
            if (!OperatingSystem.IsWindows())
                throw new NotSupportedException("The selected source clock requires the genuine Windows performance-counter API.");
        }

        [DllImport("kernel32.dll", EntryPoint = "QueryPerformanceFrequency", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceFrequencyNative(out long frequency);

        [DllImport("kernel32.dll", EntryPoint = "QueryPerformanceCounter", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryPerformanceCounterNative(ref long ticks);
    }
}
