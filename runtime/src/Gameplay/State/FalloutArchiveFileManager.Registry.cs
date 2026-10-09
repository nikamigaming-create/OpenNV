using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutArchiveFileManager
{
    private readonly FalloutSourceArchiveRegistry? _registry;

    internal FalloutArchiveStartupModes RequirePhysicalSoundModes()
    {
        lock (_sync) { RequireAvailable(); RequireArchiveSoundVisibility(); return _modes; }
    }

    internal FalloutArchiveSoundWinner? ResolveArchiveSound(string logicalPath)
    {
        lock (_sync)
        {
            RequireAvailable();
            var path = FalloutBsaArchive.CanonicalPath(logicalPath);
            if (!path.StartsWith("sound\\", StringComparison.Ordinal))
                throw new NotSupportedException("Indexed/menu byte reads require their actual sound namespace.");
            RequireArchiveSoundVisibility();
            return _modes.UseArchives == 0 ? null :
                (_registry ?? throw new NotSupportedException("Sound read has no actual source startup registry.")).Resolve(path);
        }
    }

    private void RequireArchiveSoundVisibility()
    {
        if (_modes.InvalidateOlderFiles == 0)
            throw new NotSupportedException("Audio selected its independent direct archive provider; ordinary source directory/read cannot replace it.");
        if (_modes.UseArchives == 0) return;
        if (_invalidation?.UnownedReason is { } unowned)
            throw new NotSupportedException("Archive sound visibility has no actual startup root/invalidation owner: " + unowned);
        if (_invalidation is { Present: true, Bytes: > 0 })
            throw new NotSupportedException("Nonempty source invalidation lists require actual hash/parser/visibility mutations.");
        if (_registry is null) throw new NotSupportedException("Archive sound visibility has no actual registered source lifetime.");
    }

    private FalloutMenuSoundDirectory ReadRegisteredSoundDirectory(string raw, string extension, string folder)
    {
        RequireArchiveSoundVisibility();
        var registry = _registry ?? throw new NotSupportedException("Archive directory lost its actual startup registry.");
        var paths = registry.ReadDirectory(folder, extension);
        var producer = FalloutAdvancementRuntimeReceipt.Hash(_source.Identity + "\0" + _modes.Identity +
            "\0archive-registry\0" + System.Text.Json.JsonSerializer.Serialize(_invalidation) + "\0" + registry.Identity);
        return new(raw, extension, paths, FalloutMenuSoundSelection.OrderDigest(paths), producer);
    }
}
