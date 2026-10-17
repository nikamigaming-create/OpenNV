namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeLiveContentSource
{
    // Vanilla is an explicit installed official-content selection, independent
    // of another application's plugins.txt and custom NAM activation markers.
    // File flags and timestamps still determine the engine's real load order.
    internal static IReadOnlyList<string> ResolveVanillaPlugins(string selectedRoot)
    {
        var installation = NativeGameInstallation.Detect(selectedRoot);
        string[] names = installation.Game switch
        {
            NativeGame.FalloutNewVegas => ["FalloutNV.esm", "DeadMoney.esm", "HonestHearts.esm", "OldWorldBlues.esm",
                "LonesomeRoad.esm", "GunRunnersArsenal.esm", "CaravanPack.esm", "ClassicPack.esm", "MercenaryPack.esm", "TribalPack.esm"],
            NativeGame.Fallout3 => ["Fallout3.esm", "Anchorage.esm", "ThePitt.esm", "BrokenSteel.esm", "PointLookout.esm", "Zeta.esm"],
            _ => throw new NotSupportedException("Vanilla ESM selection requires Fallout 3 or New Vegas."),
        };
        var available = new FalloutContentLayers([installation.ContentRoot]).TopLevelFiles();
        var master = names[0];
        var selected = names.Where(available.ContainsKey)
            .Select(name => new { Name = name, Path = available[name] })
            .OrderBy(plugin => plugin.Name == master ? 0 : FalloutPlugin.ReadMasterFlag(plugin.Path) ? 1 : 2)
            .ThenBy(plugin => File.GetLastWriteTimeUtc(plugin.Path))
            .ThenByDescending(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .Select(plugin => Path.GetFileName(plugin.Path)).ToArray();
        var resolved = ResolveSelectedPluginOrder(new FalloutContentLayers([installation.ContentRoot]), installation.Game,
            selected, includeImplicitPlugins: false);
        if (resolved.Any(path => !names.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("An official plugin requires a non-vanilla master. Select a clean game installation.");
        return resolved.Select(path => Path.GetFileName(path)).ToArray();
    }
}
