namespace OpenNV.Runtime.Content;

internal sealed record FalloutModDependency(string Name, string LogicalPath, string? SourcePath);

/// <summary>
/// Resolves a selected mod folder against an owned New Vegas installation.
/// File presence is a setup check, never evidence that extension behavior works.
/// </summary>
internal sealed record FalloutModInstallation(
    string Id,
    string SelectedRoot,
    string ContentRoot,
    NativeGameInstallation BaseInstallation,
    IReadOnlyList<FalloutModDependency> Dependencies,
    IReadOnlyList<string> ContentRoots,
    IReadOnlyList<string> ActivePlugins,
    string? EntryPlugin)
{
    internal static bool IsMod(string id) => FalloutModCatalog.All.Any(value => value.Id == id);

    internal static FalloutModInstallation Detect(string id, string selectedRoot, string? baseRoot,
        IReadOnlyList<string>? dependencyRoots = null)
    {
        var contentRoot = ResolveContentRoot(id, selectedRoot);
        var installationRoot = baseRoot ?? (Find(contentRoot, "FalloutNV.esm") is not null ? selectedRoot : null);
        if (string.IsNullOrWhiteSpace(installationRoot))
            throw new InvalidDataException("Choose your New Vegas game folder first.");
        var stack = FalloutModStackInstallation.Detect(installationRoot, [new(id, selectedRoot, dependencyRoots ?? [])]);
        var entry = FalloutModCatalog.Get(id).PluginChoices.FirstOrDefault(name => stack.ActivePlugins.Contains(name, StringComparer.OrdinalIgnoreCase));
        return new(id, Path.GetFullPath(selectedRoot), contentRoot, stack.BaseInstallation, stack.Dependencies,
            stack.ContentRoots, stack.ActivePlugins, entry);
    }

    internal static string ResolveContentRoot(string id, string root)
    {
        var definition = FalloutModCatalog.Get(id);
        return FalloutModFolder.Resolve(Path.GetFullPath(root), folder => definition.PluginChoices.Count == 0
            ? folder.ResourcePathsUnder("textures").Any(path => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            : definition.PluginChoices.Any(name => folder.ResolveFile(name) is not null));
    }

    internal RuntimeLiveContentSource OpenSource() => RuntimeLiveContentSource.Open(BaseInstallation.InstallRoot,
        RuntimeLiveContentSource.FalloutNewVegasGame, ContentRoots.Skip(1).ToArray(),
        ActivePlugins);

    internal IReadOnlyList<FalloutModDependency> MissingDependencies =>
        Dependencies.Where(dependency => dependency.SourcePath is null).ToArray();

    internal string SetupStatus => MissingDependencies.Count == 0
        ? "Required package files found. Gameplay support is still under development."
        : "Missing package files: " + string.Join(", ", MissingDependencies.Select(dependency => dependency.LogicalPath));

    internal static IEnumerable<(string Name, string Path)> ExtensionFiles(string id)
    {
        if (id is "jam" or "ttw" or "jsawyer" or "living-desert" or "nevada-skies")
            yield return ("JIP LN", "NVSE/Plugins/jip_nvse.dll");
        if (id is "jam" or "ttw" or "living-desert")
            yield return ("JohnnyGuitar", "NVSE/Plugins/johnnyguitar.dll");
        if (id == "jam")
        {
            yield return ("kNVSE", "NVSE/Plugins/kNVSE.dll");
            yield return ("Stewie Tweaks", "NVSE/Plugins/nvse_stewie_tweaks.dll");
            yield return ("Stewie Tweaks settings", "NVSE/Plugins/nvse_stewie_tweaks.ini");
            yield return ("UI Organizer", "NVSE/Plugins/ui_organizer.dll");
        }
        else if (id == "ttw")
        {
            yield return ("TTW NVSE", "NVSE/Plugins/TTW_nvse.dll");
            yield return ("Stewie Tweaks", "NVSE/Plugins/nvse_stewie_tweaks.dll");
        }
    }

    internal static string? ResolveFile(string root, string logicalPath)
    {
        var segments = logicalPath.Replace('\\', '/').Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
            segment.Contains(':')) || Path.IsPathRooted(logicalPath))
            throw new InvalidDataException("A mod resource must be a relative path within its selected folder.");
        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            var next = Find(current, segments[index], directory: index < segments.Length - 1);
            if (next is null) return null;
            current = next;
        }
        return current;
    }

    private static string? Find(string root, string name, bool directory = false)
    {
        var entries = directory ? Directory.EnumerateDirectories(root) : Directory.EnumerateFiles(root);
        var matches = entries.Where(path => Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw new InvalidDataException($"Ambiguous source path: {Path.Combine(root, name)}");
        return matches.SingleOrDefault();
    }
}
