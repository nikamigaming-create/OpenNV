using System.Text.Json;

namespace OpenNV.Runtime.Content;

/// <summary>An ordered, additive selection. Inspecting a mod does not change this selection.</summary>
internal sealed record FalloutModStackSelection(IReadOnlyList<FalloutModSelection> Mods, bool AutomaticOrder = true)
{
    internal void WriteOptions(IDictionary<string, string> options)
    {
        options["mod-stack"] = JsonSerializer.Serialize(Mods);
        options["mod-order"] = AutomaticOrder ? "automatic" : "manual";
    }

    internal static FalloutModStackSelection? ReadOptions(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("mod-stack", out var value))
        {
            var legacy = FalloutModSelection.ReadOptions(options);
            return legacy is null ? null : new([legacy]);
        }
        if (options.ContainsKey("mod-id") || options.ContainsKey("mod-root") || options.ContainsKey("mod-additional-roots"))
            throw new ArgumentException("Select one mod stack instead of combining it with a legacy single-mod selection.");
        var mods = JsonSerializer.Deserialize<FalloutModSelection[]>(value)
            ?? throw new InvalidDataException("The selected mods must be a list.");
        var order = options.GetValueOrDefault("mod-order", "automatic");
        if (order is not ("automatic" or "manual")) throw new InvalidDataException("Unknown mod load-order mode.");
        return new(mods, order == "automatic");
    }

    internal FalloutModStackInstallation Resolve(string baseRoot)
    {
        var setup = FalloutModStackInstallation.Detect(baseRoot, Mods, AutomaticOrder);
        if (setup.MissingDependencies.Count != 0) throw new InvalidDataException(setup.SetupStatus);
        var issues = FalloutModLoadOrder.CompatibilityIssues(setup.ActivePlugins);
        if (issues.Count != 0) throw new InvalidDataException(string.Join(" ", issues));
        return setup;
    }
}

internal sealed record FalloutModStackInstallation(
    NativeGameInstallation BaseInstallation,
    IReadOnlyList<FalloutModSelection> Mods,
    IReadOnlyList<string> ContentRoots,
    IReadOnlyList<string> ActivePlugins,
    IReadOnlyList<FalloutModDependency> Dependencies)
{
    internal IReadOnlyList<FalloutModDependency> MissingDependencies => Dependencies.Where(row => row.SourcePath is null).ToArray();
    internal string SetupStatus => MissingDependencies.Count == 0
        ? "Required package files found. Gameplay support is still under development."
        : "Missing package files: " + string.Join(", ", MissingDependencies.Select(row => row.LogicalPath));

    internal RuntimeLiveContentSource OpenSource() => RuntimeLiveContentSource.Open(BaseInstallation.InstallRoot,
        RuntimeLiveContentSource.FalloutNewVegasGame, ContentRoots.Skip(1).ToArray(), ActivePlugins);

    internal static FalloutModStackInstallation Detect(string baseRoot, IReadOnlyList<FalloutModSelection> mods, bool automaticOrder = true)
    {
        if (mods.Count == 0) throw new InvalidDataException("Select at least one mod for a mod stack.");
        if (mods.Any(mod => mod is null || string.IsNullOrWhiteSpace(mod.Id) || string.IsNullOrWhiteSpace(mod.Root) ||
            mod.AdditionalRoots is null || mod.AdditionalRoots.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("Every selected mod needs an identity, folder and additional-folder list.");
        if (mods.Select(mod => mod.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != mods.Count)
            throw new InvalidDataException("A mod can only appear once in a stack.");
        if (automaticOrder) mods = FalloutModLoadOrder.OrderMods(mods);
        var installation = NativeGameInstallation.Detect(baseRoot);
        if (installation.Game != NativeGame.FalloutNewVegas)
            throw new InvalidDataException("These mods require a Fallout: New Vegas installation.");
        var packages = mods.Select(mod => new
        {
            Selection = mod,
            Definition = FalloutModCatalog.Get(mod.Id),
            Content = FalloutModInstallation.ResolveContentRoot(mod.Id, mod.Root),
            Additional = mod.AdditionalRoots.Select(path => FalloutModFolder.Resolve(path)).ToArray(),
        }).ToArray();
        var layers = new FalloutContentLayers(new[] { installation.ContentRoot }.Concat(packages
            .SelectMany(package => new[] { package.Content }.Concat(package.Additional))
            .Where(path => !Path.TrimEndingDirectorySeparator(path).Equals(Path.TrimEndingDirectorySeparator(installation.ContentRoot),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))));
        string? Resolve(string path) => layers.ResolveFile(path);
        var dependencies = new Dictionary<string, FalloutModDependency>(StringComparer.OrdinalIgnoreCase);
        void Require(string name, string path, string? source) => dependencies[path] = new(name, path, source);
        Require("New Vegas master", "FalloutNV.esm", Resolve("FalloutNV.esm"));
        var active = new List<string>();
        void Activate(string name) { if (!active.Contains(name, StringComparer.OrdinalIgnoreCase)) active.Add(name); }
        Activate("FalloutNV.esm");
        // NAM activation is part of the same plan, including TTW's bundled
        // patch. Compatibility checks must see the actual effective set.
        var available = layers.TopLevelFiles();
        foreach (var marker in available.Keys.Where(name => Path.GetExtension(name).Equals(".nam", StringComparison.OrdinalIgnoreCase)))
            foreach (var extension in new[] { ".esm", ".esp" })
            {
                var name = Path.ChangeExtension(marker, extension);
                if (available.ContainsKey(name)) Activate(name);
            }
        var rawRoots = mods.SelectMany(mod => new[] { mod.Root }.Concat(mod.AdditionalRoots)).ToArray();
        foreach (var package in packages)
        {
            foreach (var additional in package.Additional)
            {
                var plugins = new FalloutContentLayers([additional]).TopLevelFiles().Keys.Where(IsPlugin).ToArray();
                if (plugins.Length > 1 && FalloutModInstallation.ResolveFile(additional, "fomod/ModuleConfig.xml") is not null)
                    throw new InvalidDataException($"The additional package contains alternative plugins. Choose its installed Data folder: {additional}");
                foreach (var name in plugins) Activate(name);
            }
            var choices = package.Definition.PluginChoices;
            var entry = choices.FirstOrDefault(name => FalloutModInstallation.ResolveFile(package.Content, name) is { } path &&
                FalloutPlugin.ReadMasterNames(path).All(master => Resolve(master) is not null)) ??
                choices.FirstOrDefault(name => FalloutModInstallation.ResolveFile(package.Content, name) is not null);
            if (entry is not null) Activate(entry);
            foreach (var (name, path) in FalloutModInstallation.ExtensionFiles(package.Selection.Id)) Require(name, path, Resolve(path));
            if (FalloutModInstallation.ExtensionFiles(package.Selection.Id).Any())
            {
                var gameRoot = Path.GetFileName(installation.ContentRoot).Equals("Data", StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(installation.ContentRoot)! : installation.InstallRoot;
                Require("xNVSE", "nvse_1_4.dll", Resolve("nvse_1_4.dll") ?? rawRoots.Reverse()
                    .Select(root => FalloutModInstallation.ResolveFile(root, "nvse_1_4.dll")).FirstOrDefault(path => path is not null) ??
                    FalloutModInstallation.ResolveFile(gameRoot, "nvse_1_4.dll"));
            }
        }
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        void Visit(string name)
        {
            if (visited.Contains(name)) return;
            if (!visiting.Add(name)) throw new InvalidDataException($"Plugin masters contain a cycle through {name}.");
            var path = Resolve(name);
            Require(name, name, path);
            if (path is not null) foreach (var master in FalloutPlugin.ReadMasterNames(path)) Visit(master);
            visiting.Remove(name);
            visited.Add(name);
            ordered.Add(name);
        }
        foreach (var plugin in active) Visit(plugin);
        return new(installation, mods, layers.Roots,
            automaticOrder ? FalloutModLoadOrder.OrderPlugins(layers, ordered) : ordered, dependencies.Values.ToArray());
    }

    private static bool IsPlugin(string path) => Path.GetExtension(path).Equals(".esm", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".esp", StringComparison.OrdinalIgnoreCase);
}
