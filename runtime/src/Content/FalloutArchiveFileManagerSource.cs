using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutArchiveStartupModes(byte UseArchives, byte InvalidateOlderFiles,
    string ArchivesOrigin, string InvalidationOrigin, FalloutSourceIniString ArchiveList,
    FalloutSourceIniString InvalidationFile, FalloutSourceIniString LocalMasterPath, string SettingsSha256)
{
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(JsonSerializer.Serialize(this));
    internal void Validate()
    {
        if (UseArchives > 1 || InvalidateOlderFiles > 1 || string.IsNullOrWhiteSpace(ArchivesOrigin) ||
            string.IsNullOrWhiteSpace(InvalidationOrigin) || !FalloutAdvancementRuntimeReceipt.Digest(SettingsSha256) ||
            ArchiveList is null || InvalidationFile is null || LocalMasterPath is null ||
            ArchiveList.Name != "sArchiveList:Archive" || InvalidationFile.Name != "sInvalidationFile:Archive" ||
            LocalMasterPath.Name != "sLocalMasterPath:General")
            throw new InvalidDataException("Archive startup lost its independent actual setting values/owners.");
        foreach (var value in new[] { ArchiveList, InvalidationFile, LocalMasterPath })
        {
            // Archive list storage is independent of the audio prepared-path
            // buffer. Individual path consumers validate their own grammar.
            if (value.Value is null || value.Value.Contains('\0') || string.IsNullOrWhiteSpace(value.Origin))
                throw new InvalidDataException("Archive string has no selected value/provenance.");
        }
    }
}

internal sealed record FalloutArchiveFileManagerSource(string EngineSha256, string RuntimeSha256, string ContractSha256)
{
    private const string Contract = "independent-loader-bytes1,1;startup-typed-main-useArchives-and-invalidateOlderFiles;" +
        "useArchives0-skips-startup-registry;invalidate0-audio-alternate-provider;both1-skips-native-wildcards;" +
        "original-file-type8;ordinary-runtime-mark0-independent-header;offset-topbit-equalsmark-low31-nonzero;" +
        "ascii-crt-lower-namehash;leading-star-encoded-extension-class;folderhash-exact-owner;" +
        "startup-empty-root-list-then-data-prefix;pre-registration-direct-then-prefix;" +
        "original-file-table-prepend;configured-single-contributing-archive;missing-configured-reader-refused;nonempty-invalidation-unowned;" +
        "multi-registry-and-runtime-setting-writes-unowned;winning-shared-resource-agrees-before-admission";
    internal static string CurrentContractSha256 => FalloutAdvancementRuntimeReceipt.Hash(Contract);
    internal string Identity => FalloutAdvancementRuntimeReceipt.Hash(EngineSha256 + "\0" + RuntimeSha256 + "\0" + ContractSha256);

    internal static FalloutArchiveFileManagerSource Read(FalloutMenuSoundSelectionSource source)
    {
        source.Validate(); var result = new FalloutArchiveFileManagerSource(source.EngineSha256, source.RuntimeSha256, CurrentContractSha256);
        result.Validate(); return result;
    }
    internal void Validate()
    {
        if (EngineSha256 is not ("518c87f58a6c4d9826e9ef8fbb7f4213882fa70822675610d45aea2464502a57" or
            "c3f97c2255fa041a851c17cf372d69aaadd8694e2dc4230ba556001bbfbd2f3e") ||
            !FalloutAdvancementRuntimeReceipt.Digest(RuntimeSha256) || ContractSha256 != CurrentContractSha256)
            throw new NotSupportedException("Selected image has no reviewed archive/file-manager startup consumer.");
    }

    internal FalloutArchiveStartupModes ReadStartup(RuntimeLiveContentSource source)
    {
        Validate();
        var bytes = File.ReadAllBytes(source.FalloutExecutablePath);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (hash != EngineSha256) throw new InvalidDataException("File-manager startup differs from the retained original runtime image.");
        var settings = FalloutInstallationSettings.Read(source);
        return ReadStartup(settings, FalloutExecutableStringTable.ReadIniStringDefaults(bytes,
            ["sArchiveList:Archive", "sInvalidationFile:Archive", "sLocalMasterPath:General"]));
    }

    internal FalloutArchiveStartupModes ReadStartup(FalloutInstallationSettings settings,
        IReadOnlyList<FalloutIniStringDefault> strings)
    {
        Validate();
        var use = Boolean("bUseArchives:Archive"); var invalidate = Boolean("bInvalidateOlderFiles:Archive");
        var archiveList = settings.SourceString(FalloutIniCollection.Main, "sArchiveList:Archive", strings);
        var invalidationFile = settings.SourceString(FalloutIniCollection.Main, "sInvalidationFile:Archive", strings);
        var root = settings.SourceString(FalloutIniCollection.Main, "sLocalMasterPath:General", strings);
        var identity = FalloutAdvancementRuntimeReceipt.Hash(settings.NumericIni.Source + "\0" +
            JsonSerializer.Serialize(new { use = new { use.Value, use.Origin },
                invalidate = new { invalidate.Value, invalidate.Origin }, archiveList, invalidationFile, root }));
        var result = new FalloutArchiveStartupModes(use.Value, invalidate.Value, use.Origin, invalidate.Origin,
            archiveList, invalidationFile, root, identity);
        result.Validate(); return result;

        (byte Value, string Origin) Boolean(string name)
        {
            var found = settings.NumericIni.Find(FalloutIniCollection.Main, name);
            if (found is not { Declaration.Kind: 'b', Declaration.Payload: 1, Number: { } number } ||
                number is not (0 or 1) || string.IsNullOrWhiteSpace(found.Origin))
                throw new NotSupportedException("Independent Archive mode has no actual Boolean startup declaration/override.");
            return ((byte)number, found.Origin);
        }
    }

    internal static IReadOnlyList<string> ConfiguredArchiveNames(FalloutArchiveStartupModes modes)
    {
        modes.Validate();
        // The source consumes commas and removes leading SP/TAB/LF. This is
        // not filename sorting or a inferred plugin/archive registration list.
        var names = new List<string>();
        foreach (var part in modes.ArchiveList.Value.Split(','))
        {
            var name = part.TrimStart(' ', '\t', '\n');
            if (name.Length == 0) continue;
            FalloutArchiveNameHash.RequireAscii(name);
            if (Path.GetFileName(name) != name)
                throw new NotSupportedException("Archive list contains an unowned source path/registration grammar.");
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase)) names.Add(name);
        }
        return Array.AsReadOnly(names.ToArray());
    }
}
