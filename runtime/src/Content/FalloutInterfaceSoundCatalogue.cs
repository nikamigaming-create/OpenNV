using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal enum FalloutInterfaceSoundDisposition { NamedSound, SourceSilent }
internal sealed record FalloutInterfaceSoundEntry(int Index, FalloutInterfaceSoundDisposition Disposition,
    string? EditorId, uint RequestFlags);

// These are original caller indices and their source-declared aliases, not
// sound IDs inferred from menu labels or a list of selected successful cues.
internal sealed record FalloutInterfaceSoundCatalogue(string EngineSha256, uint MaximumBiasedIndex,
    IReadOnlyList<FalloutInterfaceSoundEntry> Entries, FalloutInterfaceSoundDisposition OutsideRange)
{
    internal string Identity => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)))).ToLowerInvariant();

    internal FalloutInterfaceSoundEntry Resolve(int callerIndex)
    {
        Validate();
        // The original adds one in its UInt32 domain before an unsigned range
        // test. Negative and overflowing Int32 callers retain that operation.
        var biased = unchecked((uint)callerIndex + 1);
        return biased <= MaximumBiasedIndex ? Entries[checked((int)biased)] :
            new(callerIndex, OutsideRange, null, 0);
    }

    internal void Validate()
    {
        if (!FalloutAdvancementRuntimeReceipt.Digest(EngineSha256) || MaximumBiasedIndex >= int.MaxValue ||
            Entries is null || Entries.Count != checked((int)MaximumBiasedIndex + 1) ||
            OutsideRange != FalloutInterfaceSoundDisposition.SourceSilent)
            throw new InvalidDataException("Interface sound catalogue has no complete original index/default extent.");
        for (var ordinal = 0; ordinal < Entries.Count; ++ordinal)
        {
            var row = Entries[ordinal];
            if (row is null || row.Index != ordinal - 1 || !Enum.IsDefined(row.Disposition) ||
                row.Disposition == FalloutInterfaceSoundDisposition.NamedSound &&
                    (string.IsNullOrWhiteSpace(row.EditorId) || row.RequestFlags == 0) ||
                row.Disposition == FalloutInterfaceSoundDisposition.SourceSilent && (row.EditorId is not null || row.RequestFlags != 0))
                throw new InvalidDataException("Interface sound catalogue lost an original named/silent branch.");
        }
    }
}
