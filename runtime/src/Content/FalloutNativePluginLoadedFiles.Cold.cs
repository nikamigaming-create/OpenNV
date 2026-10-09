namespace OpenNV.Runtime.Content;

internal sealed record FalloutNativeLoadedContributorSnapshot(string Plugin, string SourceSha256, FalloutNativeLoadedFileSnapshot File);
internal sealed record FalloutNativeLoadedFilesSnapshot(string Schema, string RuntimeSha256,
    IReadOnlyList<FalloutNativeLoadedContributorSnapshot> Readers);

internal sealed partial class FalloutNativePluginLoadedFiles
{
    internal const string SnapshotSchema = "opennv-native-loaded-contributors/v1";

    internal FalloutNativeLoadedFilesSnapshot Capture()
    {
        RequireCurrent();
        if (_readers.Values.Any(reader => reader.Calls != 0))
            throw new NotSupportedException("Saving loaded contributors during a source invocation needs its actual continuation owner.");
        return new(SnapshotSchema, RuntimeSha256, _records.Plugins.Where(_readers.ContainsKey).Select(context =>
        {
            var reader = _readers[context]; RequireReader(reader);
            return new FalloutNativeLoadedContributorSnapshot(context.Plugin.Name, context.Sha256, reader.File.Capture());
        }).ToArray());
    }

    // Restore the exact already-retained selected topology. Recreating native
    // pointers or choosing missing contributor roots is the execution domain's
    // distinct responsibility; cold state never invents those capabilities.
    internal void Restore(FalloutNativeLoadedFilesSnapshot saved)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.Schema != SnapshotSchema || !StringComparer.OrdinalIgnoreCase.Equals(saved.RuntimeSha256, RuntimeSha256) ||
            saved.Readers is null || saved.Readers.Any(row => row is null || string.IsNullOrWhiteSpace(row.Plugin) || row.File is null) ||
            saved.Readers.Select(row => row.Plugin).Distinct(StringComparer.OrdinalIgnoreCase).Count() != saved.Readers.Count)
            throw new InvalidDataException("Cold loaded contributor collection changed its complete source topology/runtime identity.");
        var ordered = _records.Plugins.Where(_readers.ContainsKey).Select(context => _readers[context]).ToArray();
        if (ordered.Length != saved.Readers.Count || ordered.Where((reader, ordinal) =>
                !StringComparer.OrdinalIgnoreCase.Equals(reader.Context.Plugin.Name, saved.Readers[ordinal].Plugin) ||
                !StringComparer.OrdinalIgnoreCase.Equals(reader.Context.Sha256, saved.Readers[ordinal].SourceSha256)).Any())
            throw new InvalidDataException("Cold loaded contributor order/source differs from actual retained selected readers.");
        foreach (var reader in ordered)
        {
            RequireReader(reader);
            if (reader.Calls != 0) throw new InvalidOperationException("Cold loaded contributor collection still owns an actual source call.");
            reader.File.RequireIdle();
        }
        var before = ordered.Select(reader => reader.File.Capture()).ToArray();
        var restored = 0;
        try
        {
            for (; restored < ordered.Length; ++restored) ordered[restored].File.Restore(saved.Readers[restored].File);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            // Each individual owner preflights and rolls itself back. Earlier
            // successful siblings still need their independent rollback.
            for (var ordinal = restored - 1; ordinal >= 0; --ordinal)
                try { ordered[ordinal].File.Restore(before[ordinal]); }
                catch (Exception rollback) { failures.Add(rollback); }
            if (failures.Count != 1)
            {
                var retained = new AggregateException("Loaded contributor collection restore/rollback failed.", failures);
                _retirementFailures.Add(retained); throw retained;
            }
            throw;
        }
    }
}
