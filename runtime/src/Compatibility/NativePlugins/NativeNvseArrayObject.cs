using System.Collections.Immutable;

namespace OpenNV.Runtime.Compatibility.NativePlugins;

// Internal ArrayVar objects are different from the public opaque ArrayID ABI.
// These descriptions come from the existing store; no native pointer is supplied.
internal sealed record NativeNvseArrayCreationOwner(uint ScriptForm, string Plugin,
    string SourceOwner, string SourceSha256, string StackId);
internal sealed record NativeNvseArrayObjectSnapshot(uint Id, int Kind, byte OwningMod,
    string SourceOwner, ImmutableArray<byte> ReferringMods, IReadOnlyList<NativeNvseArrayEntry> Entries);
internal sealed record NativeNvseArrayObjectReceipt(ulong Generation, ulong Callback, ulong Parent,
    ulong Caller, uint Array, ulong Lifetime, uint Address, uint Count, string SourceOwner);
internal sealed class NativeNvseArrayObjectLease(ulong caller, NativeNvseArrayObjectSnapshot snapshot)
{
    internal ulong Caller { get; } = caller;
    internal NativeNvseArrayObjectSnapshot Snapshot { get; } = snapshot;
    internal uint EntriesSent { get; set; }
    internal uint ReferencesSent { get; set; }
}

internal static class NativeNvseArrayObjectLayout
{
    internal const uint ObjectBytes = 44, ContainerBytes = 16, ElementBytes = 24;
    internal static void Validate(NativeNvseArrayObjectSnapshot snapshot)
    {
        if (snapshot.Id is 0 or uint.MaxValue || snapshot.Kind is < 0 or > 2 ||
            string.IsNullOrWhiteSpace(snapshot.SourceOwner) || snapshot.Entries.Count > 1_000_000 ||
            snapshot.ReferringMods.IsDefault || snapshot.ReferringMods.Length > 1_000_000)
            throw new InvalidDataException("Internal array description has an incomplete source identity/extent.");
        for (var index = 0; index < snapshot.Entries.Count; ++index)
        {
            var entry = snapshot.Entries[index];
            if (entry is null || entry.Key is null || entry.Value is null || entry.Value.Type == NativeNvseElementType.Invalid ||
                snapshot.Kind == 2 && entry.Key.Type != NativeNvseElementType.String ||
                snapshot.Kind != 2 && entry.Key.Type != NativeNvseElementType.Number ||
                snapshot.Kind == 0 && entry.Key.Number != index)
                throw new InvalidDataException("Internal array container is not the complete typed store extent.");
            // The existing store uses Unicode comparison. The native map uses
            // the selected game's byte case table. Until that table producer is
            // bound, non-ASCII map keys cannot certify its ordered container.
            if (snapshot.Kind == 2 && entry.Key.Text.Any(value => value >= 128))
                throw new NotSupportedException("Internal string-map ordering needs the selected source byte case table.");
            if (index == 0) continue;
            var previous = snapshot.Entries[index - 1].Key;
            if (snapshot.Kind == 1 && previous.Number >= entry.Key.Number ||
                snapshot.Kind == 2 && CompareAscii(previous.Text.AsSpan(), entry.Key.Text.AsSpan()) >= 0)
                throw new InvalidDataException("Internal map keys are not in the consumed native comparison order.");
        }
    }
    private static int CompareAscii(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        for (var index = 0; index < Math.Min(first.Length, second.Length); ++index)
        {
            var left = Fold(first[index]); var right = Fold(second[index]);
            if (left != right) return left.CompareTo(right);
        }
        return first.Length.CompareTo(second.Length);
        static byte Fold(byte value) => value is >= (byte)'a' and <= (byte)'z' ? (byte)(value - 32) : value;
    }
}
