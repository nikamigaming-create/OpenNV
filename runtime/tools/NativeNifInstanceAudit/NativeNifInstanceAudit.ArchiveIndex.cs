using Godot;
using OpenNV.Runtime.Content;

public partial class NativeNifInstanceAudit
{
    private static void ExerciseOwnedArchiveIndex(string game, string[] paths)
    {
        if (paths.Length == 0) throw new ArgumentException("Archive index audit needs source paths.");
        using var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
            activePlugins: RuntimeLiveContentSource.ResolveVanillaPlugins(game), includeImplicitPlugins: false);
        source.ArchiveWarmup.GetAwaiter().GetResult();
        var archives = source.ArchivePaths.Select(path => (Path: path, Archive: new FalloutBsaArchive(path))).ToArray();
        try
        {
            foreach (var path in paths)
            {
                var winner = archives.LastOrDefault(value => value.Archive.Contains(path)).Path;
                var found = source.TryResolve(path, null, out var actual);
                if (found && !actual.Contains("::", StringComparison.Ordinal))
                {
                    if (!File.Exists(actual)) throw new InvalidDataException("Loose winner is absent.");
                }
                else if (found != (winner is not null) || found &&
                    !actual.Equals(winner + "::" + FalloutBsaArchive.CanonicalPath(path), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Indexed archive lookup differs from the selected stack: {path}.");
                GD.Print($"OPENNV_OWNED_ARCHIVE_INDEX_PATH source={path} resolved={found} archiveWinner={winner is not null}");
            }
            GD.Print($"OPENNV_OWNED_ARCHIVE_INDEX_PASS paths={paths.Length} archives={archives.Length} sourceReadonly=true runtimeParity=unverified");
        }
        finally { foreach (var (_, archive) in archives) archive.Dispose(); }
    }
}
