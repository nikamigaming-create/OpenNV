using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Ui;

internal static class ModStackContracts
{
    internal static void Run(string root, string game)
    {
        var jam = Path.Combine(root, "StackJAM");
        var ttw = Path.Combine(root, "StackTTW");
        var nmc = Path.Combine(root, "StackNMC");
        var dependencies = Path.Combine(root, "SharedDependencies");
        foreach (var directory in new[] { jam, ttw, nmc, dependencies }) Directory.CreateDirectory(directory);
        ModInstallationContracts.WritePlugin(jam, "JustAssortedMods.esp", "TaleOfTwoWastelands.esm");
        ModInstallationContracts.WritePlugin(ttw, "Fallout3.esm", "FalloutNV.esm");
        ModInstallationContracts.WritePlugin(ttw, "TaleOfTwoWastelands.esm", "Fallout3.esm");
        foreach (var (name, path) in FalloutModInstallation.ExtensionFiles("jam").Concat(FalloutModInstallation.ExtensionFiles("ttw")))
        {
            var file = Path.Combine(dependencies, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, name + " synthetic file-presence fixture");
        }
        File.WriteAllText(Path.Combine(dependencies, "nvse_1_4.dll"), "synthetic presence only");
        foreach (var folder in new[] { jam, nmc })
        {
            Directory.CreateDirectory(Path.Combine(folder, "Textures"));
            File.WriteAllText(Path.Combine(folder, "Textures", "shared.dds"), folder);
        }
        var selections = new FalloutModSelection[] { new("jam", jam, [dependencies]), new("ttw", ttw, [dependencies]), new("nmc", nmc, []) };
        var selection = new FalloutModStackSelection(selections);
        var setup = selection.Resolve(game);
        Require(setup.ActivePlugins.Contains("JustAssortedMods.esp") && setup.ActivePlugins.Contains("TaleOfTwoWastelands.esm"), "Enabling TTW removed JAM.");
        Require(setup.ActivePlugins.ToList().IndexOf("TaleOfTwoWastelands.esm") < setup.ActivePlugins.ToList().IndexOf("JustAssortedMods.esp"), "Cross-mod masters did not precede their dependants.");
        Require(setup.ContentRoots.Count(path => path == dependencies) == 1, "Shared dependencies were duplicated.");
        var archiveIni = Path.Combine(root, "stack-archives.ini");
        ModContentContracts.WriteArchive(Path.Combine(game, "Data", "Stack.bsa"), "base");
        File.WriteAllText(archiveIni, "[Archive]\nsArchiveList=Stack.bsa\n");
        using (var source = RuntimeLiveContentSource.Open(game, RuntimeLiveContentSource.FalloutNewVegasGame,
                   setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins, archiveIni))
        {
            Require(source.TryRead("textures/shared.dds", null, out var bytes, out _) && System.Text.Encoding.UTF8.GetString(bytes) == nmc,
                "Combined mod source did not apply the final loose-file winner.");
        }
        var options = new Dictionary<string, string>();
        var reverseClicks = new FalloutModStackSelection(selections.Reverse().ToArray()).Resolve(game);
        Require(reverseClicks.ContentRoots.SequenceEqual(setup.ContentRoots) && reverseClicks.ActivePlugins.SequenceEqual(setup.ActivePlugins),
            "Automatic order depended on the order of enabling mods.");
        selection.WriteOptions(options);
        var restored = FalloutModStackSelection.ReadOptions(options)!.Resolve(game);
        Require(restored.ContentRoots.SequenceEqual(setup.ContentRoots) && restored.ActivePlugins.SequenceEqual(setup.ActivePlugins), "Cold restart lost the combined stack.");
        var storePath = Path.Combine(root, "stack-profiles.json");
        var store = GodotLauncherProfileStore.Open(storePath, Path.Combine(root, "stack-saves"));
        store.Save("newvegas", game);
        foreach (var mod in selections) store.Save(mod.Id, mod.Root, game, mod.AdditionalRoots);
        store.SetEnabledMods("newvegas", selections.Select(mod => mod.Id).ToArray());
        var combinedSave = store.LaunchSavePath("newvegas");
        store.Save("newvegas", game);
        var reopened = GodotLauncherProfileStore.Open(storePath, Path.Combine(root, "stack-saves"));
        Require(reopened.EnabledMods("newvegas").SequenceEqual(["jam", "ttw", "nmc"]) && reopened.LaunchSavePath("newvegas") == combinedSave,
            "Game folder change or cold restart reset enabled mods or their save.");
        reopened.SetEnabledMods("newvegas", ["nmc", "ttw", "jam"]);
        Require(reopened.LaunchSavePath("newvegas") == combinedSave, "Automatic ordering created a different save for the same enabled mods.");
        reopened.SetAutomaticModOrder("newvegas", false);
        reopened.SetEnabledMods("newvegas", ["nmc", "ttw", "jam"]);
        var reordered = reopened.ModStack("newvegas")!.Resolve(game);
        Require(new FalloutContentLayers(reordered.ContentRoots).ResolveFile("textures/shared.dds") == Path.Combine(jam, "Textures", "shared.dds"), "Changing mod order did not change the file winner.");
        Require(reopened.LaunchSavePath("newvegas") != combinedSave, "Reordered content reused an incompatible save namespace.");
        var manualRestart = GodotLauncherProfileStore.Open(storePath, Path.Combine(root, "stack-saves"));
        Require(!manualRestart.AutomaticModOrder("newvegas") && manualRestart.EnabledMods("newvegas").SequenceEqual(["nmc", "ttw", "jam"]),
            "The explicit manual override was lost after restart.");
        reopened.SetEnabledMods("newvegas", ["jam", "ttw"]);
        Require(reopened.ModStack("newvegas")!.Mods.Select(mod => mod.Id).SequenceEqual(["jam", "ttw"]), "Disabling NMC disabled other mods.");
        reopened.SetEnabledMods("newvegas", []);
        Require(reopened.ModStack("newvegas") is null && reopened.LaunchSavePath("newvegas") != combinedSave, "The vanilla save was mixed with modded saves.");
        Require(FalloutModLoadOrder.CompatibilityIssues(["YUPTTW.esm", "YUP - Base Game + All DLC.esm"]).Count == 1,
            "Automatic sorting claimed to resolve an incompatible standalone YUP / TTW patch combination.");
        Console.WriteLine("OPENNV_MOD_STACK_CONTRACT_PASS additive=JAM,TTW,NMC automatic=deterministic manual=optional dependencySharing=true precedence=true restart=true saves=isolated");
    }

    internal static void Owned(string game, string file)
    {
        var mods = JsonSerializer.Deserialize<FalloutModSelection[]>(File.ReadAllText(file)) ?? throw new InvalidDataException("Missing mod selections.");
        var setup = new FalloutModStackSelection(mods).Resolve(game);
        using var source = setup.OpenSource();
        source.ArchiveWarmup.GetAwaiter().GetResult();
        using var plugins = FalloutPluginStack.Load(source.PluginSources);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            enabledMods = mods.Select(mod => mod.Id), setup.ContentRoots, setup.ActivePlugins, setup.Dependencies,
            plugins = plugins.Plugins.Select(row => row.Plugin.Name), source.ArchivePaths, source.SaveCompatibilityId,
            sourceLoading = "verified", gameplay = "unverified", parity = "unverified",
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }
}
