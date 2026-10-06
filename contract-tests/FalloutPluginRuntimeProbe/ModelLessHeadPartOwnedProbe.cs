using System.Globalization;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;

internal static class ModelLessHeadPartOwnedProbe
{
    internal static void Run(string mod, string root, string game, string headPlugin, string headHex,
        string actorHex, string[] dependencies)
    {
        using var content = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(game).OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var key = new FalloutFormKey(headPlugin, uint.Parse(headHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
        var source = records.GetEffective(key);
        if (source.Signature != "HDPT" || FalloutNpcAppearanceResolver.PathField(source, "MODL", "meshes", false) is not null)
            throw new InvalidDataException("Selected owned head part is not an authored model-less HDPT.");
        var before = SHA256.HashData(source.ReadData());
        var actor = records.RuntimeFormKey(uint.Parse(actorHex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
        var sourceAppearance = FalloutNpcAppearanceResolver.Resolve(records, actor, equippedArmor: []);
        var selection = new FalloutActorAppearanceState(sourceAppearance.Female, sourceAppearance.Race,
            sourceAppearance.Hair, sourceAppearance.Eyes, HeadParts: [key]);
        var appearance = FalloutNpcAppearanceResolver.Resolve(records, actor, equippedArmor: [], appearanceState: selection);
        if (!appearance.CanConstruct || appearance.Models.Any(part => part.Source == key) ||
            selection.HeadParts?.Single() != key || !before.SequenceEqual(SHA256.HashData(source.ReadData())))
            throw new InvalidDataException("Original model-less selection changed identity/source or invented a drawable addon.");
        Console.WriteLine($"OPENNV_OWNED_MODEL_LESS_HEAD_PART_PASS source={key} winner={source.Plugin.Name} " +
            $"sha256={Convert.ToHexString(before)} appearanceModels={appearance.Models.Count} " +
            "selectionRetained=true noProxyGeometry=true sourceUnchanged=true nativeMenuAndPixels=unverified");
    }
}
