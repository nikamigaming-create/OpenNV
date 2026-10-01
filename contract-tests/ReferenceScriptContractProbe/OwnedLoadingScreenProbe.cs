using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static class OwnedLoadingScreenProbe
{
    internal static void Run(string mod, string root, string baseRoot, string markerId, string[] dependencies)
    {
        var setup = new FalloutModStackSelection([new(mod, root, dependencies)]).Resolve(baseRoot);
        using var content = setup.OpenSource();
        using var records = FalloutPluginStack.Load(content.PluginSources);
        using var world = new FalloutReferenceWorld(records);
        var marker = FalloutDialogueTopic.Find(records, "REFR", markerId);
        var cell = FalloutCellSceneReader.ReadDefinition(records, world.Placement(marker.FormKey).Cell);
        var screens = FalloutLoadingScreenCatalog.InGame(records, cell, true);
        if (screens.Count == 0) throw new InvalidDataException("Selected source CELL has no location-specific loading screens.");
        foreach (var screen in screens)
        {
            if (!content.TryRead(screen.TexturePath, null, out _, out _)) throw new FileNotFoundException("Eligible loading screen is missing owned pixels.", screen.TexturePath);
            if (screen.Type is { } type && FalloutLoadingScreenType.Tip(records.GetEffective(type)) is null)
                throw new InvalidDataException("Selected source loading screen has no tip layout.");
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema = "opennv-owned-loading-screen-audit/v1", marker = marker.FormKey, cell = cell.FormKey,
            eligible = screens.Select(screen => new { screen.Identity, screen.Type, descriptionLength = screen.Description.Length }).ToArray(),
            sourceTextureReads = true, sourceTipLayouts = true, recording = false,
            boundary = "owned-component-fixture; native-loading-menu-and-campaign-parity-unverified"
        }));
    }
}
