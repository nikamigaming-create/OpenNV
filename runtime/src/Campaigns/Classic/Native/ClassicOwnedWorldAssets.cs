using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicSurfaceProfile(string Albedo, string Normal, string Tint, float RepeatMeters, float Roughness, float NormalScale, string? SourceGame = null);
internal sealed record ClassicWallShape(int SmoothPasses, float SmoothStrength, int Layers, float BulgeMeters, float HeightNoiseMeters, float NoiseWavelength);
internal sealed record ClassicPropBinding(string ArtPattern, string Model, int Count = 1, float YawDegrees = 0, string? SourceGame = null,
    float PitchDegrees = 0, float RollDegrees = 0, ClassicPropPalette[]? Palette = null, int[]? Pids = null, bool FitSourceArt = true,
    string[]? Campaigns = null);
internal sealed record ClassicWorldAssetRecipe(string Schema, string CaveWallPattern, string VaultWallPattern, string RockPattern, string StalagmitePattern,
    string RockModel, string StalagmiteModel, float GroundSinkFraction, ClassicSurfaceProfile CaveWall, ClassicSurfaceProfile VaultWall,
    ClassicSurfaceProfile CaveFloor, ClassicWallShape WallShape, ClassicPropBinding[]? Props = null,
    string VaultFramePattern = "^vd[0-9]+\\.frm$", ClassicGroundBinding[]? GroundLayers = null);

/// <summary>The older cave's materials and prop donors, decoded directly from owned NIF/DDS resources.</summary>
internal sealed class ClassicOwnedWorldAssets : IDisposable
{
    internal ClassicWorldAssetRecipe Recipe { get; }
    private readonly List<RuntimeLiveContentSource> _sources = [];
    private readonly Dictionary<ClassicSurfaceProfile, StandardMaterial3D> _materials = [];
    private readonly Dictionary<string, (Node3D Root, Aabb Bounds, bool Live)> _models = [];
    private readonly ClassicOwnedCreatures _creatures = new();
    private ClassicOwnedHumanoids? _humanoids;
    internal bool Prefer3D { get; set; } = true;
    internal ClassicPlayerBody? Humanoid(string path, ClassicCharacterDraft? choice = null, Fallout1NativeMapObject? placed = null, ClassicInventoryEntry? equipped = null)
    {
        var source = _sources.FirstOrDefault(row => row.Game == RuntimeLiveContentSource.FalloutNewVegasGame);
        return source is null ? null : (_humanoids ??= new ClassicOwnedHumanoids(source)).Create(path, choice, placed, equipped);
    }

    internal ClassicOwnedWorldAssets(string? newVegasRoot, string? fallout3Root = null)
    {
        Recipe = JsonSerializer.Deserialize<ClassicWorldAssetRecipe>(Godot.FileAccess.GetFileAsString("res://config/classic-world-assets-v1.json"))
            ?? throw new InvalidDataException("Classic world material recipe is absent.");
        if (Recipe.Schema != "opennv-classic-owned-world/v1") throw new InvalidDataException("Classic world material recipe is unsupported.");
        try
        {
            if (!string.IsNullOrWhiteSpace(newVegasRoot))
                _sources.Add(RuntimeLiveContentSource.Open(newVegasRoot, RuntimeLiveContentSource.FalloutNewVegasGame));
            if (!string.IsNullOrWhiteSpace(fallout3Root))
                _sources.Add(RuntimeLiveContentSource.Open(fallout3Root, RuntimeLiveContentSource.Fallout3Game));
        }
        catch { Dispose(); throw; }
    }

    private RuntimeLiveContentSource Source(string logicalPath, string? game = null, string? companion = null) =>
        _sources.FirstOrDefault(source => (game is null || source.Game == game) &&
            source.TryResolve(logicalPath, null, out _) && (companion is null || source.TryResolve(companion, null, out _)))
        ?? throw new FileNotFoundException($"Owned scenery resource absent from selected donor libraries ({game ?? "FNV / FO3"}): {logicalPath}");

    private static string Filename(string path) => Path.GetFileName(path.Replace('\\', '/'));
    internal bool HasGround(string path) => Recipe.GroundLayers?.Any(row => Regex.IsMatch(Filename(path), row.ArtPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) == true;
    internal Material? Ground(string path, Texture2D original)
    {
        var binding = Recipe.GroundLayers?.SingleOrDefault(row => Regex.IsMatch(Filename(path), row.ArtPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        return binding is null ? null : ClassicGroundLayers.Build(original, Material(binding.Surface), binding);
    }
    internal bool IsCaveWall(string path) => Regex.IsMatch(Filename(path), Recipe.CaveWallPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal bool IsVaultWall(string path) => Regex.IsMatch(Filename(path), Recipe.VaultWallPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal bool IsVaultFrame(string path) => Regex.IsMatch(Filename(path), Recipe.VaultFramePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal ClassicCreatureBody? Creature(string artPath, ClassicArtCache art, Fallout1NativeMapObject placed, ClassicBlockoutPolicy policy) =>
        _creatures.Create(artPath, art, placed, policy, (path, game) => Source(path, game));

    internal StandardMaterial3D Material(ClassicSurfaceProfile profile)
    {
        if (_materials.TryGetValue(profile, out var cached)) return cached;
        var source = Source(profile.Albedo, profile.SourceGame, profile.Normal);
        Texture2D Texture(string path)
        {
            if (!source.TryRead(path, null, out var bytes, out var identity)) throw new FileNotFoundException(path);
            using var image = new Image();
            if (image.LoadDdsFromBuffer(bytes) != Error.Ok || image.IsEmpty()) throw new InvalidDataException("Owned cave texture failed to decode: " + path);
            var texture = ImageTexture.CreateFromImage(image); texture.SetMeta("owned_source", identity); return texture;
        }
        var material = new StandardMaterial3D
        {
            ResourceName = "Classic owned stone " + profile.Albedo,
            AlbedoTexture = Texture(profile.Albedo),
            NormalTexture = Texture(profile.Normal),
            AlbedoColor = new Color(profile.Tint),
            Roughness = profile.Roughness,
            NormalEnabled = true,
            NormalScale = profile.NormalScale,
            Uv1Triplanar = true,
            Uv1WorldTriplanar = true,
            Uv1TriplanarSharpness = 3.5f,
            Uv1Scale = Vector3.One / profile.RepeatMeters,
            TextureRepeat = true,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            CullMode = BaseMaterial3D.CullModeEnum.Back
        };
        material.SetMeta("owned_game", source.Game);
        _materials.Add(profile, material); return material;
    }

    internal Node3D? Prop(string artPath, Fallout1NativeFrmFrame frame, float pixelsPerMeter, int sourceRotation = 0, int? sourcePid = null, string? campaign = null)
    {
        var name = Filename(artPath);
        var binding = Recipe.Props?.FirstOrDefault(row => (row.Campaigns is null || campaign is not null && row.Campaigns.Contains(campaign, StringComparer.Ordinal)) &&
            (row.Pids is null || sourcePid is { } pid && row.Pids.Contains(pid)) &&
            Regex.IsMatch(name, row.ArtPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        var model = binding?.Model ?? (Regex.IsMatch(name, Recipe.StalagmitePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? Recipe.StalagmiteModel :
            Regex.IsMatch(name, Recipe.RockPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? Recipe.RockModel : null);
        if (model is null) return null;
        var library = Source(model, binding?.SourceGame);
        var key = library.Game + ":" + model + ":" + JsonSerializer.Serialize(new { binding?.PitchDegrees, binding?.RollDegrees, binding?.Palette });
        (Node3D Root, Aabb Bounds, bool Live) BuildPrototype()
        {
            if (!library.TryRead(model, null, out var bytes, out _)) throw new FileNotFoundException(model);
            var source = FalloutNifFile.Read(bytes);
            var built = RuntimeNativeNifMeshBuilder.Build(source, RuntimeConfiguration.Load().World.GameUnitsToMeters, contentSource: library);
            var root = new Node3D { RotationDegrees = new(binding?.PitchDegrees ?? 0, 0, binding?.RollDegrees ?? 0) };
            root.AddChild(built.Root);
            try
            {
                ClassicDonorPresentation.Prepare(root);
                ClassicPropMaterials.Apply(root, source, binding?.Palette);
                var bounds = new Aabb(); var first = true;
                void Visit(Node3D node, Transform3D transform)
                {
                    if (!node.Visible) return;
                    transform *= node.Transform;
                    if (node is MeshInstance3D mesh && mesh.Mesh is not null)
                    {
                        var box = transform * ClassicSceneryPlacement.MeshBounds(mesh); bounds = first ? box : bounds.Merge(box); first = false;
                        mesh.SetInstanceShaderParameter("source_ambient", new Vector3(0.22f, 0.23f, 0.20f));
                    }
                    foreach (var child in node.GetChildren().OfType<Node3D>()) Visit(child, transform);
                }
                Visit(root, Transform3D.Identity);
                if (first || bounds.Size.Y <= 0 || Math.Max(bounds.Size.X, bounds.Size.Z) <= 0)
                    throw new InvalidDataException("Owned cave prop has no complete 3D bounds: " + model);
                return (root, bounds, root.FindChildren("*", "", true, false).Any(node => node is RuntimeNifControllerPlayer or RuntimeNifParticleSystem));
            }
            catch { root.Free(); throw; }
        }
        if (!_models.TryGetValue(key, out var prototype)) { prototype = BuildPrototype(); _models.Add(key, prototype); }
        var result = new Node3D();
        var assembly = new Node3D { RotationDegrees = new(0, binding?.YawDegrees ?? 0, 0) }; result.AddChild(assembly);
        var size = prototype.Bounds.Size;
        var count = binding?.Count ?? 1;
        if (count is < 1 or > 16) throw new InvalidDataException("Source scenery assembly count is invalid.");
        // A FRM's height includes the ground footprint in the original view.
        // Treating it all as vertical height inflated rocks and shifted their
        // art anchor into adjacent hexes. Fit the complete rotated silhouette,
        // preserving the donor's proportions for geology and furniture alike.
        var stackBounds = new Aabb(-new Vector3(size.X, 0, size.Z) / 2, new(size.X, size.Y * count, size.Z));
        var orientation = new Transform3D(new Basis(Vector3.Up, -sourceRotation * Mathf.Pi / 3), Vector3.Zero);
        var projected = ClassicSceneryPlacement.Project(stackBounds, pixelsPerMeter, orientation * assembly.Transform);
        // Inventory donors retain the same physical units in the hand and on
        // the floor; a large source pickup icon must not inflate the item mesh.
        var scale = binding is { FitSourceArt: false } ? 1 : Math.Min(frame.Width / projected.Size.X, frame.Height / projected.Size.Y);
        if (!float.IsFinite(scale) || scale <= 0) throw new InvalidDataException("Owned scenery scale is invalid: " + model);
        var center = prototype.Bounds.GetCenter();
        for (var index = 0; index < count; index++)
        {
            // Godot duplicates node properties, not a C# controller's bound
            // delegates. Animated appearances need their own live assembly.
            var meshRoot = prototype.Live ? BuildPrototype().Root : (Node3D)prototype.Root.Duplicate(); assembly.AddChild(meshRoot);
            meshRoot.Scale = Vector3.One * scale;
            meshRoot.Position = new(-center.X * scale,
                (-prototype.Bounds.Position.Y + size.Y * index) * scale -
                (binding is null ? frame.Height / pixelsPerMeter * Recipe.GroundSinkFraction : 0), -center.Z * scale);
        }
        result.SetMeta("owned_model", model); result.SetMeta("source_art", artPath);
        result.SetMeta("owned_game", library.Game); result.SetMeta("owned_stack", library.StackId);
        result.SetMeta("source_assembly_parts", count);
        result.SetMeta("live_item_model", prototype.Live);
        result.SetMeta("physical_item_anchor", binding is { FitSourceArt: false });
        return result;
    }

    public void Dispose()
    {
        _humanoids?.Dispose();
        foreach (var model in _models.Values) model.Root.Free();
        _models.Clear(); _materials.Clear();
        foreach (var source in _sources) source.Dispose();
        _sources.Clear();
    }
}
