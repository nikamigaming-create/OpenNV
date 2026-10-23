using System.Runtime.InteropServices;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutUnsignedRandomObservation(uint? Seed, int Index, ulong Draws);

// The qualified unsigned path is process-owned. It is not the saved script,
// sound or actor stream, and supplies no conversion or save restoration API.
internal sealed class FalloutSharedUnsignedRandom(Func<uint> milliseconds)
{
    internal static FalloutSharedUnsignedRandom Process { get; } = new(FalloutSourceMilliseconds.Read);
    private readonly Func<uint> _milliseconds = milliseconds ?? throw new ArgumentNullException(nameof(milliseconds));
    private readonly object _gate = new();
    private readonly uint[] _state = new uint[624];
    private int _index = 625;
    private uint? _seed;
    private ulong _draws;

    internal FalloutUnsignedRandomObservation Observation
    {
        get { lock (_gate) return new(_seed, _index, _draws); }
    }

    internal uint NextBounded(uint bound)
    {
        lock (_gate)
        {
            if (bound == 0) return 0;
            if (_index == 625) Seed(_milliseconds());
            if (_index >= 624) Twist();
            var value = _state[_index++];
            _draws = unchecked(_draws + 1);
            // This source unsigned method returns the raw state word, without
            // the tempering used by standard MT19937 implementations.
            return bound is uint.MaxValue or 0x7fff ? value & bound : value % bound;
        }
    }

    private void Seed(uint seed)
    {
        _state[0] = seed;
        for (var index = 1; index < _state.Length; ++index)
            _state[index] = unchecked(69069u * _state[index - 1]);
        _index = 624;
        _seed = seed;
    }

    private void Twist()
    {
        // In-place order matters: the wrapped references use words already
        // written in this block, including word zero in the final pair.
        for (var index = 0; index < _state.Length; ++index)
        {
            var pair = (_state[index] & 0x80000000u) | (_state[(index + 1) % 624] & 0x7fffffffu);
            _state[index] = _state[(index + 397) % 624] ^ (pair >> 1) ^
                ((pair & 1) == 0 ? 0u : 0x9908b0dfu);
        }
        _index = 0;
    }
}

internal static partial class FalloutSourceMilliseconds
{
    internal static uint Read()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The source unsigned clock requires its qualified Win32 GetTickCount boundary.");
        return NativeGetTickCount();
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetTickCount")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint NativeGetTickCount();
}
