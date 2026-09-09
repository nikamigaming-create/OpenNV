using System.Text.Json;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Content;

internal static class ClassicSceneryInventory
{
    internal static int Run(string root, string recipePath)
    {
        var source = Fallout1OwnedContentSource.LoadInstall(root);
        var catalog = new ClassicMapCatalog(source, "fallout-1");
        var recipe = JsonSerializer.Deserialize<ClassicAuthoredSceneryRecipe>(File.ReadAllText(recipePath))
            ?? throw new InvalidDataException("Authored scenery recipe is absent.");
        var bindings = recipe.Bindings.ToDictionary(row => row.Art, row => row.Model, StringComparer.OrdinalIgnoreCase);
        foreach (var path in catalog.Maps)
        {
            var level = catalog.Load(path);
            var matches = level.Objects.TopLevelObjects.Where(row => row.Prototype.ObjectType == 2 && row.Tile >= 0 && (row.Flags & 1) == 0)
                .Select(row => (Object: row, Art: Path.GetFileName(Fallout1NativePrototypeReader.ResolveArt(catalog, row.Fid).Replace('\\', '/'))))
                .Where(row => bindings.ContainsKey(row.Art)).ToArray();
            foreach (var group in matches.GroupBy(row => (row.Object.Elevation, Model: bindings[row.Art])))
                Console.WriteLine(JsonSerializer.Serialize(new { map = path, name = catalog.DisplayName(path),
                    elevation = group.Key.Elevation, model = group.Key.Model, placements = group.Select(row => new
                    { row.Object.Serial, row.Object.Tile, row.Object.Rotation, row.Art }).ToArray() }));
        }
        return 0;
    }
}
