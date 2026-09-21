using System.Text.Json;

namespace OpenNV.Runtime.Content;

/// <summary>The same folder selection is retained through launcher entry and cold session restart.</summary>
internal sealed record FalloutModSelection(string Id, string Root, IReadOnlyList<string> AdditionalRoots)
{
    internal void WriteOptions(IDictionary<string, string> options)
    {
        options["mod-id"] = Id;
        options["mod-root"] = Root;
        options["mod-additional-roots"] = JsonSerializer.Serialize(AdditionalRoots);
    }

    internal static FalloutModSelection? ReadOptions(IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("mod-id", out var id))
        {
            if (options.ContainsKey("mod-root") || options.ContainsKey("mod-additional-roots"))
                throw new ArgumentException("Mod folders require a selected mod identity.");
            return null;
        }
        _ = FalloutModCatalog.Get(id);
        if (!options.TryGetValue("mod-root", out var root) || string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("The selected mod folder is missing.");
        var additional = JsonSerializer.Deserialize<string[]>(options.GetValueOrDefault("mod-additional-roots", "[]"))
            ?? throw new InvalidDataException("Additional mod folders must be a list.");
        if (additional.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("An additional mod folder is empty.");
        return new(id, root, additional);
    }

    internal FalloutModInstallation Resolve(string baseRoot)
    {
        var setup = FalloutModInstallation.Detect(Id, Root, baseRoot, AdditionalRoots);
        if (setup.MissingDependencies.Count != 0) throw new InvalidDataException(setup.SetupStatus);
        return setup;
    }
}
