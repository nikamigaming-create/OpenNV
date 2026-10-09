namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutNativePluginLoadedFiles
{
    internal const string SourceStateSchema = "opennv-native-loaded-source-coordinates/v1";

    internal FalloutNativeSourceCollectionState CaptureSourceState()
    {
        RequireCurrent();
        if (_readers.Values.Any(reader => reader.Calls != 0))
            throw new InvalidOperationException("A source invocation still owns the loaded contributor continuation.");
        return new(SourceStateSchema, RuntimeSha256, _records.OwnedSource?.StackId, SourceSelection(),
            _records.Plugins.Where(_readers.ContainsKey).Select(context =>
            {
                var reader = _readers[context]; RequireReader(reader);
                return new FalloutNativeSourceContributor(context.Plugin.Name, context.Sha256, reader.File.CaptureSourceState());
            }).ToArray());
    }

    internal void RestoreSourceState(FalloutNativeSourceCollectionState saved)
    {
        RequireCurrent(); ArgumentNullException.ThrowIfNull(saved);
        if (saved.Schema != SourceStateSchema || !StringComparer.OrdinalIgnoreCase.Equals(saved.RuntimeSha256, RuntimeSha256) ||
            !StringComparer.OrdinalIgnoreCase.Equals(saved.StackId, _records.OwnedSource?.StackId) ||
            saved.Selection is null || saved.Contributors is null)
            throw new InvalidDataException("Loaded source continuation changed its exact selected source/runtime owner.");
        var selection = SourceSelection();
        if (selection.Count != saved.Selection.Count || selection.Where((input, ordinal) =>
                saved.Selection[ordinal] is not { } expected || input.LoadOrder != expected.LoadOrder || input.Bytes != expected.Bytes ||
                !StringComparer.OrdinalIgnoreCase.Equals(input.Plugin, expected.Plugin) ||
                !StringComparer.OrdinalIgnoreCase.Equals(input.Sha256, expected.Sha256) || expected.Masters is null ||
                !input.Masters.SequenceEqual(expected.Masters, StringComparer.OrdinalIgnoreCase)).Any())
            throw new InvalidDataException("Loaded source continuation changed source order, bytes or master identities.");
        var readers = _records.Plugins.Where(_readers.ContainsKey).Select(context => _readers[context]).ToArray();
        if (readers.Length != saved.Contributors.Count || saved.Contributors.Any(row => row is null || row.State is null) ||
            saved.Contributors.Select(row => row.Plugin).Distinct(StringComparer.OrdinalIgnoreCase).Count() != readers.Length ||
            readers.Where((reader, ordinal) => !StringComparer.OrdinalIgnoreCase.Equals(reader.Context.Plugin.Name, saved.Contributors[ordinal].Plugin) ||
                !StringComparer.OrdinalIgnoreCase.Equals(reader.Context.Sha256, saved.Contributors[ordinal].Sha256)).Any())
            throw new InvalidDataException("Loaded source continuation differs from the actual retained contributor topology.");
        var recreated = new FalloutNativeLoadedContributorSnapshot[readers.Length];
        for (var ordinal = 0; ordinal < readers.Length; ++ordinal)
        {
            var reader = readers[ordinal]; RequireReader(reader);
            if (reader.Calls != 0) throw new InvalidOperationException("Loaded source restore overlaps an actual contributor call.");
            recreated[ordinal] = new(reader.Context.Plugin.Name, reader.Context.Sha256,
                reader.File.RecreateSourceState(saved.Contributors[ordinal].State));
        }
        // Existing owner preflight/rollback performs the actual parser, buffer
        // and OS metadata publication. These reconstructed bytes never come
        // from a saved retail buffer or a substitute winning record.
        Restore(new(SnapshotSchema, saved.RuntimeSha256, recreated));
    }

    private IReadOnlyList<FalloutNativeSourceInput> SourceSelection()
        => _records.Plugins.Select(context => new FalloutNativeSourceInput(context.Plugin.Name, context.Sha256,
            context.LoadOrderIndex, context.Bytes, context.Plugin.Masters.ToArray())).ToArray();
}
