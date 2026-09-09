using System.Text.Json;
using System.Text.RegularExpressions;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Content;

/// <summary>Private art/PRO/MAP inventory through the same source readers as the game.</summary>
internal static class ClassicAssetInventory
{
    internal sealed class Asset(string campaign, string key, string category)
    {
        public string Campaign { get; } = campaign;
        public string Key { get; } = key;
        public string Category { get; } = category;
        public SortedSet<string> Files { get; } = [];
        public SortedSet<string> Prototypes { get; } = [];
        public SortedSet<int> ObjectTypes { get; } = [];
        public SortedSet<int> Subtypes { get; } = [];
        public Dictionary<string, int> Maps { get; } = [];
        public int WorldPlacements { get; set; }
        public int VisiblePlacements { get; set; }
        public int InventoryEntries { get; set; }
        public long InventoryQuantity { get; set; }
        public int FloorTiles { get; set; }
        public int RoofTiles { get; set; }
        public int SourceAnimationFiles => Files.Count;
        public int Width { get; set; }
        public int Height { get; set; }
        public int FramesPerDirection { get; set; }
        public bool HasVisibleReference { get; set; }
        public string Reference { get; set; } = "";
        public string Route { get; set; } = "missing-3d";
        public string Model { get; set; } = "";
        public string RequiredWork { get; set; } = "Model, materials, orientation, anchors and in-game acceptance.";
        public List<string> Errors { get; } = [];
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static string Canonical(string path) => path.Replace('\\', '/').ToLowerInvariant();
    private static T Recipe<T>(string runtime, string name) => JsonSerializer.Deserialize<T>(
        File.ReadAllText(Path.Combine(runtime, "config", name)), Json) ?? throw new InvalidDataException(name);

    internal static int Run(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("classic-assets requires both classic roots, both donor Data roots, runtime and private output directories.");
        var runtime = Path.GetFullPath(args[4]); var output = Path.GetFullPath(args[5]);
        foreach (var input in args[..4].Append(runtime))
            if (output.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(input)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The private inventory output must be outside all source installations and runtime inputs.");
        Directory.CreateDirectory(output); Directory.CreateDirectory(Path.Combine(output, "references"));
        var authored = Recipe<ClassicAuthoredSceneryRecipe>(runtime, "classic-authored-scenery-v1.json");
        var owned = Recipe<ClassicWorldAssetRecipe>(runtime, "classic-world-assets-v1.json");
        var creatures = Recipe<ClassicCreatureRecipe>(runtime, "classic-creatures-v1.json");
        using var nv = RuntimeLiveContentSource.Open(args[2], RuntimeLiveContentSource.FalloutNewVegasGame);
        using var fo3 = RuntimeLiveContentSource.Open(args[3], RuntimeLiveContentSource.Fallout3Game);
        var donors = new[] { nv, fo3 };
        var donorCatalog = donors.Select(source => new { game = source.Game, stack = source.StackId,
            meshesAndAnimations = source.ResourcePathsUnder("meshes").Select(Canonical).Order().ToArray(),
            textures = source.ResourcePathsUnder("textures").Select(Canonical).Order().ToArray() }).ToArray();
        File.WriteAllText(Path.Combine(output, "donor-catalog.json"), JsonSerializer.Serialize(donorCatalog, Json));
        var all = new List<Asset>(); var campaigns = new List<object>(); var failures = new List<object>();
        using var placements = new StreamWriter(Path.Combine(output, "placements.jsonl"), false);
        foreach (var (campaign, root) in new[] { ("fallout-1", args[0]), ("fallout-2", args[1]) })
        {
            IFalloutClassicOwnedSource source = campaign == "fallout-1" ? Fallout1OwnedContentSource.LoadInstall(root) : Fo2NativeOwnedSource.LoadInstall(root);
            using var catalog = new ClassicMapCatalog(source, campaign);
            var art = new ClassicArtCache(path => catalog.Read(path, out _));
            var assets = new Dictionary<string, Asset>();
            var palette = catalog.Read("color.pal", out _);
            Asset Row(string path)
            {
                path = Canonical(path); var parts = path.Split('/');
                var category = parts.Length > 2 ? parts[1] : "unknown";
                var filename = Path.GetFileNameWithoutExtension(path);
                var key = category == "critters" && filename.Length > 2 ? "art/critters/" + filename[..^2] : path;
                if (!assets.TryGetValue(key, out var row)) assets.Add(key, row = new(campaign, key, category));
                row.Files.Add(path); return row;
            }
            string Art(uint fid, int frame = 0) => ((fid >> 24) & 15) == 1
                ? art.Critter(fid, frame).Path : Fallout1NativePrototypeReader.ResolveArt(catalog, fid);
            var artPaths = Enumerable.Range(0, 6).SelectMany(direction => source.EffectiveLogicalPaths("art/", ".fr" + direction))
                .Concat(source.EffectiveLogicalPaths("art/", ".frm")).Select(Canonical).Distinct().Order().ToArray();
            var processed = 0;
            foreach (var path in artPaths)
            {
                var row = Row(path);
                if (row.Reference.Length == 0)
                    try
                    {
                        var extension = Path.GetExtension(path);
                        var direction = extension[^1] is >= '0' and <= '5' ? extension[^1] - '0' : 0;
                        var frame = Fallout1NativeFrmReader.ReadFirstFrame(catalog.Read(path, out _), direction);
                        row.Width = frame.Width; row.Height = frame.Height; row.FramesPerDirection = frame.FramesPerDirection;
                        row.HasVisibleReference = frame.PaletteIndexes.Any(value => value != 0);
                        var reference = "references/" + campaign + "-" + Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(row.Key))).ToLowerInvariant() + ".png";
                        ClassicInventoryPng.Write(Path.Combine(output, reference), frame, palette); row.Reference = reference;
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException)
                    { row.Errors.Add(path + ": " + error.Message); }
                if (++processed % 500 == 0) Console.WriteLine($"{campaign}: indexed {processed}/{artPaths.Length} art files");
            }
            var prototypeCount = 0;
            var protoDirectories = new[] { "items", "critters", "scenery", "walls", "tiles", "misc" };
            for (var type = 0; type < protoDirectories.Length; type++)
            {
                var directory = protoDirectories[type];
                IReadOnlyList<string> names;
                try { names = Fallout1NativeLists.Read(catalog.Read($"proto/{directory}/{directory}.lst", out _)); }
                catch (FileNotFoundException) { continue; }
                for (var index = 1; index <= names.Count; index++)
                    try
                    {
                        var prototype = Fallout1NativeObjectGraphReader.ResolvePrototype(catalog, type << 24 | index);
                        prototypeCount++;
                        if (prototype.Fid is not { } fid) continue;
                        var row = Row(Art(fid)); row.Prototypes.Add(prototype.Pid.ToString("x8")); row.ObjectTypes.Add(type);
                        if (prototype.Subtype is { } subtype) row.Subtypes.Add(subtype);
                    }
                    catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException)
                    { failures.Add(new { campaign, source = $"proto/{directory}/{names[index - 1]}", error = error.Message }); }
            }
            var decodedMaps = 0; var elevations = 0; var objects = 0; var nested = 0;
            var visibleFrames = new Dictionary<(string Path, int Direction, int Frame), bool>();
            foreach (var mapPath in catalog.Maps)
            {
                try
                {
                    var level = catalog.Load(mapPath); decodedMaps++; elevations += level.Map.Elevations.Count;
                    void Visit(Fallout1NativeMapObject placed, int? parent)
                    {
                        if (parent is null) objects++; else nested++;
                        try
                        {
                            var path = Art(placed.Fid, placed.Frame); var row = Row(path);
                            row.Prototypes.Add(placed.Pid.ToString("x8")); row.ObjectTypes.Add(placed.Prototype.ObjectType);
                            if (placed.Prototype.Subtype is { } subtype) row.Subtypes.Add(subtype);
                            var location = mapPath + " / " + catalog.ElevationName(mapPath, placed.Elevation);
                            row.Maps[location] = row.Maps.GetValueOrDefault(location) + 1;
                            if (parent is null)
                            {
                                row.WorldPlacements++;
                                if (placed.Tile >= 0 && (placed.Flags & 1) == 0 && placed.Prototype.ObjectType != 5)
                                {
                                    var frameIndex = placed.Prototype.ObjectType == 1 ? art.Critter(placed.Fid, placed.Frame).Frame : placed.Frame;
                                    var key = (Canonical(path), placed.Rotation, frameIndex);
                                    if (!visibleFrames.TryGetValue(key, out var visible))
                                    {
                                        var framePath = key.Item1;
                                        var extension = Path.GetExtension(framePath);
                                        if (extension[^1] is >= '0' and <= '5') framePath = framePath[..^1] + placed.Rotation;
                                        visible = Fallout1NativeFrmReader.ReadFrame(catalog.Read(framePath, out _), placed.Rotation, frameIndex)
                                            .PaletteIndexes.Any(value => value != 0);
                                        visibleFrames.Add(key, visible);
                                    }
                                    if (visible) row.VisiblePlacements++;
                                }
                            }
                            else { row.InventoryEntries++; row.InventoryQuantity += placed.Quantity; }
                            placements.WriteLine(JsonSerializer.Serialize(new { campaign, map = mapPath, asset = row.Key,
                                placed.Serial, parent, placed.Pid, placed.Fid, placed.Tile, placed.Elevation, placed.Rotation,
                                placed.PixelX, placed.PixelY, placed.Frame, placed.Flags, placed.ScriptId, placed.Quantity }, Json));
                        }
                        catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException)
                        { failures.Add(new { campaign, source = mapPath + " object " + placed.Serial, error = error.Message }); }
                        foreach (var child in placed.Inventory) Visit(child, placed.Serial);
                    }
                    foreach (var placed in level.Objects.TopLevelObjects) Visit(placed, null);
                    var tileNames = Fallout1NativeLists.Read(catalog.Read("art/tiles/tiles.lst", out _));
                    foreach (var (elevation, tiles) in level.Map.Elevations)
                        foreach (var roof in new[] { false, true })
                            foreach (var group in Enumerable.Range(0, tiles.Length).GroupBy(index => (int)((tiles[index] >> (roof ? 16 : 0)) & 0xfff)))
                            {
                                if (group.Key == 1) continue;
                                if (group.Key >= tileNames.Count) throw new InvalidDataException("MAP tile index exceeds the source list.");
                                var row = Row("art/tiles/" + tileNames[group.Key]); var indices = group.ToArray();
                                if (roof) row.RoofTiles += indices.Length; else row.FloorTiles += indices.Length;
                                row.Maps[mapPath + " / " + catalog.ElevationName(mapPath, elevation)] =
                                    row.Maps.GetValueOrDefault(mapPath + " / " + catalog.ElevationName(mapPath, elevation)) + indices.Length;
                                placements.WriteLine(JsonSerializer.Serialize(new { campaign, map = mapPath, elevation,
                                    asset = row.Key, layer = roof ? "roof" : "floor", indices }, Json));
                            }
                }
                catch (Exception error) when (error is IOException or InvalidDataException or NotSupportedException or ArgumentException)
                { failures.Add(new { campaign, source = mapPath, error = error.Message }); }
                if ((decodedMaps % 20) == 0) Console.WriteLine($"{campaign}: read {decodedMaps}/{catalog.Maps.Count} maps");
            }
            foreach (var row in assets.Values)
            {
                bool Match(string pattern) => Regex.IsMatch(Path.GetFileName(row.Key), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var firstParty = authored.Bindings.FirstOrDefault(binding => binding.Art.Equals(Path.GetFileName(row.Key), StringComparison.OrdinalIgnoreCase));
                var creature = creatures.Bindings.FirstOrDefault(binding => row.Key == "art/critters/" + binding.ArtBase);
                var prop = owned.Props?.FirstOrDefault(binding => Match(binding.ArtPattern));
                if (row.Category == "intrface" || row.Category == "skilldex" || row.Category == "inven")
                { row.Route = "original-ui-art"; row.RequiredWork = "Keep intentional classic interface/portrait art; this is not world geometry."; }
                else if (row.Category == "tiles")
                { row.Route = "source-surface"; row.RequiredWork = "Source floor/roof surface; relief, roof geometry and visual acceptance remain open."; }
                else if (!row.HasVisibleReference && row.FramesPerDirection == 1 && row.VisiblePlacements == 0)
                { row.Route = "nonvisual-reference"; row.RequiredWork = "Retain source state/collision; no visible first-frame surface."; }
                else if (firstParty is not null && (row.Category == "scenery" || row.ObjectTypes.Contains(2)))
                {
                    row.Route = "authored-candidate"; row.Model = "assets/classic/" + firstParty.Model;
                    if (!File.Exists(Path.Combine(runtime, row.Model))) row.Errors.Add("Declared first-party model is absent.");
                }
                else if (creature is not null)
                {
                    row.Route = "skinned-candidate"; row.Model = creature.Model;
                    row.RequiredWork = "Source identity, body/outfit, all action states and animation transitions; idle evidence is not combat acceptance.";
                    foreach (var resource in creature.Animations.Values.Append(creature.Skeleton).Append(creature.Model))
                        if (!donors.Any(source => source.Game == creature.SourceGame && source.TryResolve(resource, null, out _)))
                            row.Errors.Add("Owned resource absent: " + resource);
                }
                else if ((row.Category == "scenery" || row.ObjectTypes.Contains(2)) &&
                    (prop is not null || Match(owned.RockPattern) || Match(owned.StalagmitePattern)))
                {
                    row.Route = "owned-candidate"; row.Model = prop?.Model ?? (Match(owned.RockPattern) ? owned.RockModel : owned.StalagmiteModel);
                    if (!donors.Any(source => (prop?.SourceGame is null || source.Game == prop.SourceGame) && source.TryResolve(row.Model, null, out _)))
                        row.Errors.Add("Declared donor model is absent from the selected libraries.");
                }
                else if (row.Category == "walls" && (Match(owned.CaveWallPattern) || Match(owned.VaultWallPattern) || Match(owned.VaultFramePattern)))
                { row.Route = "procedural-candidate"; row.RequiredWork = "Source contour/door silhouette acceptance; a matching recipe is not successful geometry."; }
                else if (row.Category == "scenery" && row.Subtypes.Contains(0) && Path.GetFileName(row.Key).StartsWith('v'))
                { row.Route = "procedural-candidate"; row.RequiredWork = "Door contour, thickness, opening and animation; complex gear doors still fall back."; }
            }
            all.AddRange(assets.Values);
            var summary = new { campaign, source.ProfileId, sourceMaps = catalog.Maps.Count, decodedMaps, elevations,
                artFiles = artPaths.Length, assetFamilies = assets.Count, prototypeCount, worldObjects = objects, nestedInventoryEntries = nested,
                routes = assets.Values.GroupBy(row => row.Route).ToDictionary(group => group.Key, group => new {
                    families = group.Count(), visiblePlacements = group.Sum(row => row.VisiblePlacements) }) };
            campaigns.Add(summary); Console.WriteLine(JsonSerializer.Serialize(summary, Json));
        }
        placements.Flush();
        var data = new { schema = "opennv-classic-asset-inventory/v1", generatedUtc = DateTime.UtcNow,
            scope = "Effective disk art, indexed PRO identities, all stored MAP elevations and nested inventory. No script-spawn or visual-parity completion claim.",
            campaigns, failures, assets = all.OrderByDescending(row => row.VisiblePlacements).ThenBy(row => row.Campaign).ThenBy(row => row.Key),
            donors = donorCatalog.Select(row => new { row.game, modelsAndAnimations = row.meshesAndAnimations.Length, textures = row.textures.Length }) };
        var serialized = JsonSerializer.Serialize(data, Json);
        File.WriteAllText(Path.Combine(output, "inventory.json"), serialized);
        File.WriteAllText(Path.Combine(output, "index.html"), ClassicInventoryPage.Render(serialized));
        Console.WriteLine($"Private asset inventory: {Path.Combine(output, "index.html")}; unresolved source rows: {failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }
}
