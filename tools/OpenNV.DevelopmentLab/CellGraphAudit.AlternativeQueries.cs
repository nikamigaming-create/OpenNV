using OpenNV.Runtime.Content;

internal static partial class CellGraphAudit
{
    internal sealed record AlternativeQueryReuse(string StackId, string SaveCompatibilityId,
        int PersistentCellQueries, int PersistentCellEntries, int ResourceDirectoryQueries, int ResourceDirectoryEntries);

    // One serial audit over one immutable selection. Lazy retains the original
    // owner result or refusal; each declaring WRLD/LAND still emits its own row.
    internal sealed class AlternativeSourceQueries
    {
        private readonly RuntimeLiveContentSource _source;
        private readonly FalloutExteriorGrid _grid;
        private readonly Dictionary<FalloutFormKey, Lazy<FalloutFormKey>> _persistent = new(FalloutFormKeyComparer.Instance);
        private readonly Dictionary<string, Lazy<IReadOnlyList<string>>> _directories = new(StringComparer.OrdinalIgnoreCase);
        private int _persistentQueries;
        private int _directoryQueries;

        internal AlternativeSourceQueries(RuntimeLiveContentSource source, FalloutPluginStack records)
        {
            if (!ReferenceEquals(records.OwnedSource, source))
                throw new InvalidDataException("Alternative query reuse requires records from the exact owned source instance.");
            _source = source;
            _grid = new(records);
        }

        internal FalloutFormKey PersistentCell(FalloutFormKey world)
        {
            if (!_persistent.TryGetValue(world, out var result))
            {
                result = new(() =>
                {
                    _persistentQueries++;
                    return _grid.PersistentCell(world);
                });
                _persistent.Add(world, result);
            }
            return result.Value;
        }

        internal IReadOnlyList<string> ResourcePathsUnder(string logicalDirectory)
        {
            var canonical = FalloutBsaArchive.CanonicalPath(logicalDirectory).TrimEnd('\\');
            if (!_directories.TryGetValue(canonical, out var result))
            {
                result = new(() =>
                {
                    _directoryQueries++;
                    return Array.AsReadOnly(_source.ResourcePathsUnder(canonical).ToArray());
                });
                _directories.Add(canonical, result);
            }
            return result.Value;
        }

        internal AlternativeQueryReuse Reuse => new(_source.StackId, _source.SaveCompatibilityId,
            _persistentQueries, _persistent.Count, _directoryQueries, _directories.Count);
    }
}
