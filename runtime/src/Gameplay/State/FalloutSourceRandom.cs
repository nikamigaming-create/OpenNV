using System.Runtime.InteropServices;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutSourceRandomSeed(uint TickCount, RuntimeSaveProcessIdentity Process);
internal sealed record FalloutSourceRandomSnapshot(string SourceSha256, bool Constructed,
    FalloutSourceRandomSeed? Seed, long Draws, int Cursor, IReadOnlyList<uint> Words, string StateSha256);

// This owner is shared by the source directory consumers. It does not replace
// the unrelated per-owner SplitMix streams or claim their original call order.
internal sealed class FalloutSourceRandom
{
    private readonly string _source;
    private readonly Func<uint> _ticks;
    private readonly uint[] _words = new uint[624];
    private bool _constructed;
    private FalloutSourceRandomSeed? _seed;
    private int _cursor = 625;
    internal long Draws { get; private set; }

    internal FalloutSourceRandom(FalloutMenuSoundSelectionSource source,
        FalloutSourceRandomSnapshot? restore = null)
        : this(source, ReadTickCount, restore) { }

    // The authored seam supplies a real observable producer; production always
    // uses the source's Win32 import, never a fixed seed or zero-ready flag.
    internal FalloutSourceRandom(FalloutMenuSoundSelectionSource source, Func<uint> ticks,
        FalloutSourceRandomSnapshot? restore = null)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(ticks); _source = source.Identity; _ticks = ticks;
        if (restore is null) return;
        RequireSnapshot(source, restore);
        _constructed = restore.Constructed; _seed = restore.Seed; Draws = restore.Draws; _cursor = restore.Cursor;
        if (_seed is not null) for (var index = 0; index < _words.Length; ++index) _words[index] = restore.Words[index];
    }

    internal uint Next(uint bound)
    {
        _constructed = true;
        if (bound == 0) return 0;
        var nextOrdinal = checked(Draws + 1);
        if (_cursor == 625)
        {
            var seed = new FalloutSourceRandomSeed(_ticks(), RuntimeSaveProcessIdentity.Current); seed.Process.Validate();
            _seed = seed; _words[0] = seed.TickCount;
            for (var index = 1; index < _words.Length; ++index) _words[index] = unchecked(_words[index - 1] * 69069U);
            _cursor = 624;
        }
        if (_cursor == 624) Twist();
        var raw = _words[_cursor++]; Draws = nextOrdinal;
        return bound is uint.MaxValue or 32767 ? raw & bound : raw % bound;
    }

    private void Twist()
    {
        for (var index = 0; index < 227; ++index) _words[index] = _words[index + 397] ^ TwistPair(_words[index], _words[index + 1]);
        for (var index = 227; index < 623; ++index) _words[index] = _words[index - 227] ^ TwistPair(_words[index], _words[index + 1]);
        _words[623] = _words[396] ^ TwistPair(_words[623], _words[0]); _cursor = 0;
    }
    private static uint TwistPair(uint first, uint second)
    {
        var pair = (first & 0x80000000U) | (second & 0x7fffffffU);
        return (pair >> 1) ^ ((pair & 1) == 0 ? 0U : 0x9908b0dfU);
    }

    internal FalloutSourceRandomSnapshot Capture()
    {
        var words = _seed is null ? Array.Empty<uint>() : _words.ToArray();
        return new(_source, _constructed, _seed, Draws, _cursor, words, Digest(_constructed, _seed, Draws, _cursor, words));
    }

    internal static void RequireSnapshot(FalloutMenuSoundSelectionSource source, FalloutSourceRandomSnapshot saved)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.SourceSha256 != source.Identity || saved.Draws < 0 || saved.Words is null ||
            saved.Seed is null && (saved.Draws != 0 || saved.Cursor != 625 || saved.Words.Count != 0) ||
            saved.Seed is not null && (!saved.Constructed || saved.Draws <= 0 || saved.Words.Count != 624 ||
                saved.Cursor != (int)((saved.Draws - 1) % 624) + 1) ||
            !saved.Constructed && saved.Seed is not null ||
            saved.StateSha256 != Digest(saved.Constructed, saved.Seed, saved.Draws, saved.Cursor, saved.Words))
            throw new InvalidDataException("Shared sound RNG lost its actual seed/state/draw prefix.");
        saved.Seed?.Process.Validate();
    }

    private static string Digest(bool constructed, FalloutSourceRandomSeed? seed, long draws, int cursor, IReadOnlyList<uint> words)
    {
        using var data = new MemoryStream(); using var output = new BinaryWriter(data);
        output.Write(constructed); output.Write(seed is not null);
        if (seed is not null)
        {
            output.Write(seed.TickCount); output.Write(seed.Process.Boot.ToByteArray());
            output.Write(seed.Process.ProcessId); output.Write(seed.Process.StartedUtcTicks);
        }
        output.Write(draws); output.Write(cursor); output.Write(words.Count);
        foreach (var word in words) output.Write(word);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data.ToArray())).ToLowerInvariant();
    }

    private static uint ReadTickCount()
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("Original shared sound seed requires the actual Win32 tick producer.");
        return GetTickCount();
    }
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern uint GetTickCount();
}
