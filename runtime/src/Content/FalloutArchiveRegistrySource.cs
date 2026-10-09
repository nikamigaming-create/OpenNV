using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal enum FalloutArchiveRegistrationOrder { Append, SourceSubstringInsertion }

internal sealed record FalloutArchiveRegistrySource(string EngineSha256, string RuntimeSha256,
    FalloutArchiveRegistrationOrder Order, IReadOnlyList<string> Categories, string DeclarationSha256,
    string ContractSha256)
{
    private const string Contract = "configured-token-order-no-dedup;ordinary-unmarked-registration;" +
        "append-or-source-case-sensitive-substring-insertion;first-incoming-category;" +
        "existing-ordered-tests-thresholds-minus1-minus1-1-2-3-3;category0-append;unmatched-insert-head;" +
        "registry-head-first-byte-lookup;fixed-initial-loose-hash-exclusion;" +
        "archive-file-table-prepend-with-cross-archive-duplicates;borrowed-reader-lifetime;" +
        "cold-source-validation-without-directory-caller-rng-or-cue-replay";
    internal static string CurrentContractSha256 => FalloutAdvancementRuntimeReceipt.Hash(Contract);
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(JsonSerializer.Serialize(this));

    internal static FalloutArchiveRegistrySource Read(FalloutMenuSoundSelectionSource selection,
        string executable)
    {
        selection.Validate();
        var declaration = FalloutExecutableStringTable.ReadArchiveRegistryDeclaration(executable,
            selection.EngineSha256);
        var result = new FalloutArchiveRegistrySource(selection.EngineSha256, selection.RuntimeSha256,
            declaration.Order, declaration.Categories, declaration.DeclarationSha256, CurrentContractSha256);
        result.Validate(); return result;
    }

    internal void Validate()
    {
        var expected = EngineSha256 switch
        {
            "518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" => FalloutArchiveRegistrationOrder.SourceSubstringInsertion,
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e" => FalloutArchiveRegistrationOrder.Append,
            _ => throw new NotSupportedException("Selected image has no reviewed ordinary archive registry consumer."),
        };
        if (Order != expected || Categories is null || Categories.Count != (Order == FalloutArchiveRegistrationOrder.Append ? 0 : 6) ||
            Categories.Any(value => string.IsNullOrEmpty(value) || value.Length > 64 || value.Any(character => character is < ' ' or > '~')) ||
            Categories.Distinct(StringComparer.Ordinal).Count() != Categories.Count ||
            !FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) || !FalloutAdvancementRuntimeReceipt.Digest(DeclarationSha256) ||
            ContractSha256 != CurrentContractSha256)
            throw new InvalidDataException("Archive registry lost its actual source order/category declaration.");
    }

    internal int Category(string filename)
    {
        Validate(); FalloutArchiveNameHash.RequireAscii(filename);
        for (var index = 0; index < Categories.Count; index++)
            if (filename.Contains(Categories[index], StringComparison.Ordinal)) return index;
        return Categories.Count;
    }

    internal int InsertionIndex(string filename, IReadOnlyList<string> current)
    {
        Validate();
        if (Order == FalloutArchiveRegistrationOrder.Append) return current.Count;
        var category = Category(filename);
        if (category == 0) return current.Count;
        ReadOnlySpan<int> thresholds = [-1, -1, 1, 2, 3, 3];
        for (var at = 0; at < current.Count; at++)
            // The source checks each existing substring in order. It does not
            // classify that filename once and discard later matching tests.
            for (var test = 0; test < Categories.Count; test++)
                if (category > thresholds[test] && current[at].Contains(Categories[test], StringComparison.Ordinal)) return at;
        return 0;
    }
}

internal sealed record FalloutArchiveRegistryDeclaration(FalloutArchiveRegistrationOrder Order,
    IReadOnlyList<string> Categories, string DeclarationSha256);
