using System.Text;

namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeLiveContentSource
{
    private bool CanReuse(string root, string campaign, string identity, FalloutContentLayers layers,
        IReadOnlyList<FalloutPluginSource> plugins, IReadOnlyList<string> archives,
        IReadOnlyList<FalloutInstallationSetting>? settings)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        if (_retired || ArchiveWarmup.IsFaulted || ArchiveWarmup.IsCanceled ||
            Campaign != campaign || StackId != identity || !comparer.Equals(ContentRoot, root) ||
            !ContentRoots.SequenceEqual(layers.Roots, comparer) || !ArchivePaths.SequenceEqual(archives, comparer) ||
            !PluginSources.Select(source => source.AbsolutePath).SequenceEqual(plugins.Select(source => source.AbsolutePath), comparer) ||
            !Settings.SequenceEqual(settings ?? []) || !_layers.IsCurrent ||
            _reuseSettingsIdentity != ReuseSettingsIdentity(root, Game)) return false;
        foreach (var (path, retained) in _loosePayloads)
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != retained.Bytes ||
                file.LastWriteTimeUtc.Ticks != retained.MtimeTicks) return false;
        }
        return true;
    }

    // Settings/executable caches share this source identity too. A warm session
    // must not keep an old effective INI merely because ESM/BSA files match.
    private static string ReuseSettingsIdentity(string contentRoot, string game)
    {
        var root = Path.GetDirectoryName(contentRoot)!;
        var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games",
            game == FalloutNewVegasGame ? "FalloutNV" : "Fallout3");
        var identity = new StringBuilder();
        foreach (var path in new[] { Path.Combine(root, game == FalloutNewVegasGame ? "FalloutNV.exe" : "Fallout3.exe"),
            Path.Combine(root, "Fallout_default.ini"), Path.Combine(user, "Fallout.ini"), Path.Combine(user, "FalloutPrefs.ini"),
            Path.Combine(user, "FalloutCustom.ini"), Path.Combine(user, "RendererInfo.txt") })
        {
            var file = new FileInfo(path);
            identity.Append(path).Append('\0').Append(file.Exists ? file.Length : -1).Append('\0')
                .Append(file.Exists ? file.LastWriteTimeUtc.Ticks : -1).Append('\n');
        }
        return identity.ToString();
    }
}
