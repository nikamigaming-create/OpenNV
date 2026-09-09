using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicAuthoredSceneryBinding(string Art, string Model, float YawDegrees = 0, bool UseSourcePalette = true,
    string? OwnedSurface = null);
internal sealed record ClassicAuthoredSceneryRecipe(string Schema, ClassicAuthoredSceneryBinding[] Bindings,
    ClassicAuthoredMaterialRecipe Materials);

/// <summary>First-party geometry with source paint and physically scaled surface detail resolved live.</summary>
internal sealed class ClassicAuthoredScenery : IDisposable
{
    private readonly ClassicAuthoredSceneryRecipe _recipe;
    private readonly ClassicAuthoredMaterials _materials;
    private readonly Dictionary<string, (Node3D Root, Aabb Bounds)> _models = [];

    internal ClassicAuthoredScenery()
    {
        _recipe = JsonSerializer.Deserialize<ClassicAuthoredSceneryRecipe>(
            Godot.FileAccess.GetFileAsString("res://config/classic-authored-scenery-v1.json"))
            ?? throw new InvalidDataException("Classic authored scenery bindings are absent.");
        if (_recipe.Schema != "opennv-classic-authored-scenery/v1" || _recipe.Materials is not { } material)
            throw new InvalidDataException("Classic authored scenery bindings are unsupported.");
        var lengths = new[] { material.DepthTolerancePixels, material.GrimeLargeMeters, material.GrimeSmallMeters,
            material.DetailMeters, material.WoodAcrossMeters, material.WoodAlongMeters, material.ClothWeaveMeters, material.DetailFadeMeters };
        var responses = new[] { material.GrimeMinimum, material.GrimeMaximum, material.DetailMinimum,
            material.DetailMaximum, material.RoughnessVariation, material.Specular };
        if (lengths.Any(value => !float.IsFinite(value) || value <= 0) || responses.Any(value => !float.IsFinite(value) || value < 0) ||
            material.GrimeMaximum < material.GrimeMinimum || material.DetailMaximum < material.DetailMinimum ||
            material.RoughnessVariation > 1 || material.Specular > 1 ||
            !float.IsFinite(material.PaintStrength) || material.PaintStrength is < 0 or > 1 ||
            !float.IsFinite(material.PaintFacingMinimum) || !float.IsFinite(material.PaintFacingFull) ||
            material.PaintFacingMinimum < 0 || material.PaintFacingFull > 1 || material.PaintFacingFull <= material.PaintFacingMinimum)
            throw new InvalidDataException("Classic authored material dimensions or paint projection are invalid.");
        _materials = new(material);
    }

    internal Node3D? Prop(string artPath, ImageTexture sourceTexture, Fallout1NativeFrmFrame frame, float pixelsPerMeter,
        ClassicOwnedWorldAssets? ownedAssets = null, int sourceRotation = 0)
    {
        var name = Path.GetFileName(artPath.Replace('\\', '/'));
        var binding = _recipe.Bindings.FirstOrDefault(row => row.Art.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (binding is null) return null;
        if (binding.OwnedSurface is not null && ownedAssets is null) return null;
        if (!_models.TryGetValue(binding.Model, out var prototype))
        {
            using var document = new GltfDocument();
            using var state = new GltfState();
            // Godot's resource namespace works in both development and a PCK.
            var resourcePath = "res://assets/classic/" + binding.Model;
            var bytes = Godot.FileAccess.GetFileAsBytes(resourcePath);
            if (bytes.Length == 0) throw new FileNotFoundException("Classic authored mesh is absent: " + resourcePath);
            var error = document.AppendFromBuffer(bytes, resourcePath.GetBaseDir(), state);
            if (error != Error.Ok) throw new InvalidDataException($"Classic authored mesh could not load: {binding.Model} ({error}).");
            var model = document.GenerateScene(state) as Node3D ?? throw new InvalidDataException("Classic authored mesh has no scene.");
            try { prototype = (model, ClassicSceneryPlacement.Bounds(model)); }
            catch { model.Free(); throw; }
            _models.Add(binding.Model, prototype);
        }
        var result = new Node3D();
        try
        {
            var assembly = new Node3D { RotationDegrees = new(0, binding.YawDegrees, 0) }; result.AddChild(assembly);
            var instance = (Node3D)prototype.Root.Duplicate(); assembly.AddChild(instance);
            var bounds = prototype.Bounds;
            var orientation = new Transform3D(new Basis(Vector3.Up, -sourceRotation * Mathf.Pi / 3), Vector3.Zero);
            var projected = ClassicSceneryPlacement.Project(prototype.Root, pixelsPerMeter, orientation * assembly.Transform);
            if (projected.Size.X <= 0 || projected.Size.Y <= 0) throw new InvalidDataException("Classic authored mesh has no projected extent.");
            var scale = Math.Min(frame.Width / projected.Size.X, frame.Height / projected.Size.Y);
            if (!float.IsFinite(scale) || scale <= 0) throw new InvalidDataException("Classic authored mesh scale is invalid.");
            instance.Scale = Vector3.One * scale;
            instance.Position = new(-bounds.GetCenter().X * scale, -bounds.Position.Y * scale, -bounds.GetCenter().Z * scale);
            if (binding.UseSourcePalette)
                _materials.Apply(instance, orientation * assembly.Transform, binding, sourceTexture, frame, pixelsPerMeter);
            if (binding.OwnedSurface is not null)
            {
                var material = binding.OwnedSurface switch
                {
                    "cave" => ownedAssets!.Material(ownedAssets.Recipe.CaveWall),
                    _ => throw new InvalidDataException("Unknown authored scenery surface: " + binding.OwnedSurface),
                };
                void Apply(Node3D node)
                {
                    if (node is MeshInstance3D mesh) mesh.MaterialOverride = material;
                    foreach (var child in node.GetChildren().OfType<Node3D>()) Apply(child);
                }
                Apply(instance);
            }
            result.SetMeta("source_art", artPath); result.SetMeta("authored_model", binding.Model);
            result.SetMeta("presentation", binding.UseSourcePalette ? "first-party-geometry-live-source-paint" : "first-party-geometry-owned-surface");
            return result;
        }
        catch { result.Free(); throw; }
    }

    public void Dispose()
    {
        foreach (var model in _models.Values) model.Root.Free();
        _models.Clear(); _materials.Clear();
    }
}
