namespace OpenNV.Runtime.Content;

// Source display preparation only. Native input and persistent outcomes have
// separate admission owners; no replacement answer or world effect is supplied.
internal sealed class FalloutHackingLayout
{
    private readonly byte[] _display;
    private readonly Func<uint, uint> _random;
    internal FalloutHackingCandidates Candidates { get; }
    internal IReadOnlyList<int> Positions { get; }
    internal byte[] DisplayBytes() => _display.ToArray();

    private FalloutHackingLayout(FalloutHackingCandidates candidates, byte[] display,
        int[] positions, Func<uint, uint> random)
    {
        Candidates = candidates; _display = display.ToArray();
        Positions = Array.AsReadOnly(positions.ToArray()); _random = random;
    }

    internal FalloutHackingTransaction CreateTransaction(Func<byte, bool> alphabetic) =>
        new(Candidates, Positions, _display, _random, alphabetic);

    internal static FalloutHackingLayout CreateFromProcess(FalloutHackingCandidates candidates,
        CancellationToken cancellation = default) =>
        Create(candidates, FalloutSharedUnsignedRandom.Process.NextBounded, cancellation);

    internal static FalloutHackingLayout Create(FalloutHackingCandidates candidates,
        Func<uint, uint> random, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(random);
        var length = candidates.Answer.Length;
        if (length is < 4 or > 12 || candidates.Words.Any(word => word.Length != length))
            throw new InvalidDataException("Hacking placement requires the selected common source word length.");
        ReadOnlySpan<byte> symbols = [33, 64, 35, 36, 37, 94, 42, 40, 41, 95, 43, 61, 45, 96, 91,
            93, 123, 125, 124, 59, 39, 58, 44, 46, 47, 60, 62, 63, 92, 34];
        var retained = candidates;
        var failedGrids = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var display = new byte[408];
            for (var index = 0; index < display.Length; ++index)
            {
                cancellation.ThrowIfCancellationRequested();
                display[index] = symbols[(int)FalloutHackingRules.Draw(random, (uint)symbols.Length)];
            }
            var positions = new int[retained.Words.Count];
            var placed = 0;
            var misses = 0;
            while (placed < retained.Words.Count && misses < 100)
            {
                cancellation.ThrowIfCancellationRequested();
                var position = (int)FalloutHackingRules.Draw(random, (uint)(407 - length));
                var clear = true;
                for (var offset = -2; clear && offset < length + 2; ++offset)
                    if (position + offset >= 0 && display[position + offset] is >= (byte)'A' and <= (byte)'Z')
                        clear = false;
                if (!clear) { ++misses; continue; }
                retained.Words[placed].Bytes.CopyTo(display.AsSpan(position, length));
                positions[placed++] = position;
                misses = 0;
            }
            if (placed == retained.Words.Count) return new(retained, display, positions, random);
            // Four full retries. Subsequent exhaustion retires the unplaced
            // suffix, without resetting this counter or recalculating allowance.
            if (++failedGrids > 4) retained = retained.RetainPlacedPrefix(placed);
        }
    }
}
