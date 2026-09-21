namespace OpenNV.Runtime.Content;

/// <summary>
/// Deterministic automatic order for the supported collection. Authored masters
/// are hard constraints; reviewed metadata supplies priorities and explicit edges.
/// This is not the LOOT sorting engine or its complete compatibility database.
/// </summary>
internal static class FalloutModLoadOrder
{
    internal const string RulesRevision = "loot-falloutnv-79b2bb6-supported-collection-v1";

    internal static IReadOnlyList<FalloutModSelection> OrderMods(IReadOnlyList<FalloutModSelection> mods)
    {
        var byId = mods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var providers = mods.SelectMany(mod => FalloutModCatalog.Get(mod.Id).PluginChoices.Select(plugin => (plugin, mod.Id)))
            .ToDictionary(pair => pair.plugin, pair => pair.Id, StringComparer.OrdinalIgnoreCase);
        var requirements = mods.ToDictionary(mod => mod.Id, mod =>
        {
            var content = FalloutModInstallation.ResolveContentRoot(mod.Id, mod.Root);
            return FalloutModCatalog.Get(mod.Id).PluginChoices.Select(name => FalloutModInstallation.ResolveFile(content, name))
                .Where(path => path is not null).SelectMany(path => FalloutPlugin.ReadMasterNames(path!))
                .Where(providers.ContainsKey).Select(name => providers[name]).Where(id => id != mod.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }, StringComparer.OrdinalIgnoreCase);
        return Sort(byId.Keys, id => requirements[id], id => ModPriority(id)).Select(id => byId[id]).ToArray();
    }

    internal static int ModPriority(string id) => id switch
    {
        "ttw" => 10,
        "yup" => 20,
        "nevada-skies" => 190,
        "nmc" => 200,
        _ => 80,
    };

    internal static IReadOnlyList<string> OrderPlugins(FalloutContentLayers layers, IReadOnlyList<string> plugins)
    {
        var available = layers.TopLevelFiles();
        var selected = new HashSet<string>(plugins, StringComparer.OrdinalIgnoreCase);
        var requirements = plugins.ToDictionary(name => name, name =>
        {
            var path = available.GetValueOrDefault(name);
            return (path is null ? [] : FalloutPlugin.ReadMasterNames(path)).Concat(After(name))
                .Where(selected.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }, StringComparer.OrdinalIgnoreCase);
        return Sort(plugins, name => requirements[name], name =>
            (available.TryGetValue(name, out var path) && FalloutPlugin.ReadMasterFlag(path) ? 0 : 1000) + PluginPriority(name));
    }

    private static int PluginPriority(string name) => name.ToLowerInvariant() switch
    {
        "falloutnv.esm" => -100,
        "deadmoney.esm" => 0,
        "honesthearts.esm" => 1,
        "oldworldblues.esm" => 2,
        "lonesomeroad.esm" => 3,
        "gunrunnersarsenal.esm" => 4,
        "classicpack.esm" => 5,
        "mercenarypack.esm" => 6,
        "tribalpack.esm" => 7,
        "caravanpack.esm" => 8,
        "fallout3.esm" => 9,
        "anchorage.esm" => 10,
        "thepitt.esm" => 11,
        "brokensteel.esm" => 12,
        "pointlookout.esm" => 13,
        "zeta.esm" => 14,
        "taleoftwowastelands.esm" => 15,
        "yup - base game + all dlc.esm" or "yup - base game.esm" or "yupttw.esm" => 20,
        "someguyseries.esm" => 40,
        "nevadaskies.esp" => 190,
        _ => 80,
    };

    private static IEnumerable<string> After(string name) => name.ToLowerInvariant() switch
    {
        "taleoftwowastelands.esm" => ["CaravanPack.esm", "ClassicPack.esm", "MercenaryPack.esm", "TribalPack.esm"],
        "eve fnv - all dlc.esp" => ["Improved Sound FX.esp"],
        "jsawyer - eve.esp" => ["EVE FNV - ALL DLC.esp"],
        _ => [],
    };

    private static IReadOnlyList<string> Sort(IEnumerable<string> values, Func<string, IReadOnlyList<string>> requirements, Func<string, int> priority)
    {
        var pending = values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        while (pending.Count != 0)
        {
            var next = pending.Where(name => requirements(name).All(dependency => !pending.Contains(dependency)))
                .OrderBy(priority).ThenBy(name => name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (next is null) throw new InvalidDataException("Load-order requirements contain a cycle: " + string.Join(", ", pending.Order(StringComparer.OrdinalIgnoreCase)));
            result.Add(next);
            pending.Remove(next);
        }
        return result;
    }

    internal static IReadOnlyList<string> CompatibilityIssues(IReadOnlyList<string> plugins)
    {
        var selected = plugins.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();
        if (selected.Contains("YUPTTW.esm") && selected.Contains("YUP - Base Game + All DLC.esm"))
            issues.Add("TTW already includes YUPTTW. Disable the standalone YUP mod for this combination.");
        if (selected.Contains("TaleOfTwoWastelands.esm") && selected.Contains("EVE FNV - ALL DLC.esp"))
            issues.Add("EVE's New Vegas plugin is not verified for this TTW combination. Choose a compatible TTW version before playing.");
        return issues;
    }
}
