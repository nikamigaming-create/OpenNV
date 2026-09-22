using OpenNV.Runtime.Content;

internal static class McmUiContracts
{
    internal static void Owned(string id, string selected, string game, string[] dependencies)
    {
        var setup = FalloutModInstallation.Detect(id, selected, game, dependencies);
        RuntimeLiveContentSource.Configure(setup.BaseInstallation.InstallRoot, RuntimeLiveContentSource.FalloutNewVegasGame,
            setup.ContentRoots.Skip(1).ToArray(), setup.ActivePlugins);
        try
        {
            var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned MCM source was not configured.");
            var expanded = FalloutMenuXml.Expand(FalloutMenuXml.Read("menus/options/start_menu.xml"));
            var menu = expanded.Elements("menu").Single();
            var mcm = menu.Descendants().SingleOrDefault(tile =>
                string.Equals((string?)tile.Attribute("name"), "MCM", StringComparison.OrdinalIgnoreCase));
            Require(mcm is not null, "Selected UIO/MCM source did not inject the MCM root tile.");
            Require(menu.Descendants().Any(tile => string.Equals((string?)tile.Attribute("name"), "MCM_ModList", StringComparison.OrdinalIgnoreCase)) &&
                menu.Descendants().Any(tile => string.Equals((string?)tile.Attribute("name"), "MCM_Options", StringComparison.OrdinalIgnoreCase)),
                "MCM source includes did not resolve its list and options fragments.");
            var mcmResolved = source.TryResolve("menus/prefabs/MCM/MCM.xml", null, out var mcmSource);
            var uioResolved = source.TryResolve("uio/supported.txt", null, out var uioSource);
            Require(mcmResolved && uioResolved,
                "Selected MCM/UIO resources were not resolved through the live source graph.");
            Console.WriteLine($"OPENNV_MCM_UI_SOURCE_PASS menu=MCM includes=list-options uio=true sourceMcm={mcmSource} sourceUio={uioSource}");
        }
        finally { RuntimeLiveContentSource.Clear(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
