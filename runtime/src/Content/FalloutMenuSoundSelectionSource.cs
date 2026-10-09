using System.Text;

namespace OpenNV.Runtime.Content;

internal enum FalloutMenuSoundHistoryPolicy { None, SignedTenSlotRemainder }

// A source declaration selects algorithms, not a catalogue of successful cues.
// The retained source scopes include constructor, actual directory callback,
// seed import, integer draw and the independent path/history consumers.
internal sealed record FalloutMenuSoundSelectionSource(string EngineSha256, string RuntimeSha256,
    FalloutMenuSoundHistoryPolicy History, IReadOnlyList<string> Extensions, string ContractSha256)
{
    private const string Contract = "shared-lazy-624-word-state;tick32-seed;seed-multiplier69069;" +
        "in-place227+396+last-twist;no-temper;zero-bound-no-draw;uintmax-and32767-mask-other-modulo;" +
        "raw-trailing-separator-directory;case-sensitive-fx-or-song-prefix-data-sound;" +
        "direct-child-list-prepend;first-full-draw-modulo-count;" +
        "signed-path-bytes-after-first-polynomial65599;source-selected-no-history-or-ten-signed-slots;" +
        "remember-low-byte;cursor-remainder-without-increment;exact-file-no-random-getter";
    internal static string CurrentContractSha256 => FalloutAdvancementRuntimeReceipt.Hash(Contract);
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(EngineSha256 + "\0" + RuntimeSha256 + "\0" +
        ContractSha256 + "\0" + History + "\0" + string.Join(';', Extensions));

    internal static FalloutMenuSoundSelectionSource Read(FalloutAdvancementRuntimeSource runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime); runtime.Receipt.Validate();
        return Read(runtime.Receipt.EngineSha256, runtime.Receipt.SourceSha256);
    }

    internal static FalloutMenuSoundSelectionSource Read(string engineSha256, string runtimeSha256)
    {
        var result = engineSha256 switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" =>
                new FalloutMenuSoundSelectionSource(engineSha256, runtimeSha256,
                    FalloutMenuSoundHistoryPolicy.SignedTenSlotRemainder, [".ogg", ".wav"], CurrentContractSha256),
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" =>
                new FalloutMenuSoundSelectionSource(engineSha256, runtimeSha256,
                    FalloutMenuSoundHistoryPolicy.None, [".wav"], CurrentContractSha256),
            _ => throw new NotSupportedException("Selected executable has no reviewed sound directory/random consumer."),
        };
        result.Validate(); return result;
    }

    internal void Validate()
    {
        var expectedHistory = EngineSha256 switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => FalloutMenuSoundHistoryPolicy.SignedTenSlotRemainder,
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => FalloutMenuSoundHistoryPolicy.None,
            _ => throw new NotSupportedException("Sound selection has another original executable consumer."),
        };
        var expectedExtensions = expectedHistory == FalloutMenuSoundHistoryPolicy.None ? new[] { ".wav" } : [".ogg", ".wav"];
        if (History != expectedHistory || Extensions is null || !Extensions.SequenceEqual(expectedExtensions) ||
            ContractSha256 != CurrentContractSha256 || !FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256))
            throw new InvalidDataException("Sound selection changed its source algorithm/extension declaration.");
    }

    internal static byte[] PathBytes(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length == 0 || raw.Contains('\0')) throw new InvalidDataException("Menu sound has no bounded source path.");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(raw);
        if (bytes.Length >= 260) throw new NotSupportedException("Sound path exceeds the original prepared-path capacity.");
        return bytes;
    }

    internal static string PreparedPath(string raw)
    {
        _ = PathBytes(raw);
        // The original file component stores FNAM unchanged. Its audio manager
        // prepends this format namespace only for these case-sensitive prefixes.
        // Hashing receives that prepared buffer before folder replacement.
        var prepared = raw.StartsWith("fx", StringComparison.Ordinal) || raw.StartsWith("song", StringComparison.Ordinal)
            ? "data\\sound\\" + raw : raw;
        _ = PathBytes(prepared); return prepared;
    }

    internal static string LogicalPath(string raw)
    {
        var prepared = PreparedPath(raw);
        if (prepared.StartsWith("data\\", StringComparison.OrdinalIgnoreCase)) prepared = prepared[5..];
        var logical = FalloutBsaArchive.CanonicalPath(prepared);
        if (!logical.StartsWith("sound\\", StringComparison.Ordinal))
            throw new NotSupportedException("Menu filename requires its original working-directory/virtual-path consumer outside the admitted sound namespace.");
        return logical;
    }

    internal static uint DirectoryHash(string raw)
    {
        var bytes = PathBytes(PreparedPath(raw)); uint hash = 0;
        for (var index = 1; index < bytes.Length; ++index)
            hash = unchecked(hash * 65599U + (uint)(sbyte)bytes[index]);
        return hash;
    }
}
