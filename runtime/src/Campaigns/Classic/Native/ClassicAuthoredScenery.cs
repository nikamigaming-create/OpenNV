using System.Text.Json;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed record ClassicAuthoredSceneryBinding(string Art, string Model, float YawDegrees = 0, bool UseSourcePalette = true,
    string? OwnedSurface = null, ClassicAuthoredVisualPlacement? VisualPlacement = null);
internal sealed record ClassicAuthoredReferenceCanvas(float X, float Y, float Width, float Height);
internal sealed record ClassicAuthoredSourceFrame(int Width, int Height, int DirectionX, int DirectionY,
    int FrameX, int FrameY, int FramesPerDirection, int Rotation);
internal sealed record ClassicAuthoredVisualPlacement(string Schema, ClassicAuthoredReferenceCanvas Canvas,
    float AuthoredElevationMeters, ClassicAuthoredSourceFrame SourceFrame, int SourceRotation,
    float PixelsPerMeter, float YawDegrees = 0);
internal sealed record ClassicAuthoredSceneryRecipe(string Schema, ClassicAuthoredSceneryBinding[] Bindings,
    ClassicAuthoredMaterialRecipe Materials);

/// <summary>First-party geometry with source paint and physically scaled surface detail resolved live.</summary>
internal sealed class ClassicAuthoredScenery : IDisposable
{
    private readonly ClassicAuthoredSceneryRecipe _recipe;
    private readonly ClassicAuthoredMaterials _materials;
    private readonly string _modelRoot;
    private readonly Dictionary<string, (Node3D Root, Aabb Bounds)> _models = [];
    private readonly Dictionary<ulong, ShaderMaterial> _linearVertexMaterials = [];

    internal ClassicAuthoredScenery(ClassicAuthoredSceneryRecipe? recipe = null,
        string modelRoot = "res://assets/classic")
    {
        _modelRoot = modelRoot;
        _recipe = recipe ?? JsonSerializer.Deserialize<ClassicAuthoredSceneryRecipe>(
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
        if (binding.VisualPlacement is { } qualification)
            QualifyVisualPlacement(binding, qualification, frame, pixelsPerMeter, sourceRotation);
        if (!_models.TryGetValue(binding.Model, out var prototype))
        {
            using var document = new GltfDocument();
            using var state = new GltfState();
            // Godot's resource namespace works in both development and a PCK.
            var resourcePath = _modelRoot.PathJoin(binding.Model);
            var bytes = Godot.FileAccess.GetFileAsBytes(resourcePath);
            if (bytes.Length == 0) throw new FileNotFoundException("Classic authored mesh is absent: " + resourcePath);
            var error = document.AppendFromBuffer(bytes, resourcePath.GetBaseDir(), state);
            if (error != Error.Ok) throw new InvalidDataException($"Classic authored mesh could not load: {binding.Model} ({error}).");
            var model = document.GenerateScene(state) as Node3D ?? throw new InvalidDataException("Classic authored mesh has no scene.");
            try
            {
                prototype = (model, ClassicSceneryPlacement.Bounds(model));
            }
            catch { model.Free(); throw; }
            _models.Add(binding.Model, prototype);
        }
        var result = new Node3D();
        try
        {
            var assembly = new Node3D { RotationDegrees = new(0, binding.YawDegrees, 0) }; result.AddChild(assembly);
            var instance = ClassicDonorPresentation.CanShareStatic(prototype.Root)
                ? ClassicDonorPresentation.CloneStatic(prototype.Root) : (Node3D)prototype.Root.Duplicate();
            assembly.AddChild(instance);
            var bounds = prototype.Bounds;
            var orientation = new Transform3D(new Basis(Vector3.Up, -sourceRotation * Mathf.Pi / 3), Vector3.Zero);
            var visual = binding.VisualPlacement;
            var projected = visual is null
                ? ClassicSceneryPlacement.Project(prototype.Root, pixelsPerMeter, orientation * assembly.Transform)
                : new Rect2(visual.Canvas.X, visual.Canvas.Y, visual.Canvas.Width, visual.Canvas.Height);
            if (projected.Size.X <= 0 || projected.Size.Y <= 0) throw new InvalidDataException("Classic authored mesh has no projected extent.");
            var scale = Math.Min(frame.Width / projected.Size.X, frame.Height / projected.Size.Y);
            if (!float.IsFinite(scale) || scale <= 0) throw new InvalidDataException("Classic authored mesh scale is invalid.");
            instance.Scale = Vector3.One * scale;
            instance.Position = new(-bounds.GetCenter().X * scale, -bounds.Position.Y * scale, -bounds.GetCenter().Z * scale);
            if (visual is not null)
            {
                if (!prototype.Root.Transform.IsEqualApprox(Transform3D.Identity) ||
                    MathF.Abs(bounds.Position.Y - visual.AuthoredElevationMeters) > .0001f)
                    throw new InvalidDataException("Authored reference-canvas elevation or model origin differs from its geometry.");
                // Restore the genuine minimum authored height, not an invented
                // world placement or hidden below-ground geometry. The model's
                // shared origin survives the existing horizontal centering.
                instance.Position += Vector3.Up * (visual.AuthoredElevationMeters * scale);
                var translation = (orientation * assembly.Transform).Basis * instance.Position;
                var referenceCenter = projected.GetCenter() * scale + new Vector2(
                    ClassicSceneryPlacement.ScreenX.Dot(translation),
                    ClassicSceneryPlacement.ScreenY(pixelsPerMeter).Dot(translation));
                if (!referenceCenter.IsFinite()) throw new InvalidDataException("Authored reference-canvas center is nonfinite.");
                result.SetMeta("authored_reference_center_px", referenceCenter);
            }
            if (binding.UseSourcePalette)
                _materials.Apply(instance, orientation * assembly.Transform, binding, sourceTexture, frame, pixelsPerMeter);
            else if (binding.OwnedSurface is null) BindLinearVertexMaterials(instance);
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

    private void BindLinearVertexMaterials(Node3D root)
    {
        Shader? shader = null;
        foreach (var mesh in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                // Textured, transparent and additional material features retain
                // their existing importer owner. This adapter only binds the
                // plain opaque double-sided vertex-painted glTF contract.
                if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D source ||
                    !source.VertexColorUseAsAlbedo || source.VertexColorIsSrgb || source.AlbedoColor != Colors.White ||
                    source.AlbedoTexture is not null || source.NormalEnabled || source.EmissionEnabled ||
                    source.MetallicTexture is not null || source.RoughnessTexture is not null ||
                    Enum.GetValues<BaseMaterial3D.Feature>().Any(feature => feature != BaseMaterial3D.Feature.Max && source.GetFeature(feature)) ||
                    source.Transparency != BaseMaterial3D.TransparencyEnum.Disabled ||
                    source.ShadingMode != BaseMaterial3D.ShadingModeEnum.PerPixel ||
                    source.CullMode != BaseMaterial3D.CullModeEnum.Disabled) continue;
                if (!_linearVertexMaterials.TryGetValue(source.GetInstanceId(), out var material))
                {
                    shader ??= GD.Load<Shader>("res://assets/classic/materials/linear-vertex-color.gdshader")
                        ?? throw new InvalidDataException("Linear vertex-color presentation shader is absent.");
                    material = new ShaderMaterial { Shader = shader, ResourceName = source.ResourceName + " / linear vertex color" };
                    material.SetShaderParameter("surface_roughness", source.Roughness);
                    material.SetShaderParameter("surface_metallic", source.Metallic);
                    material.SetShaderParameter("surface_specular", source.MetallicSpecular);
                    _linearVertexMaterials.Add(source.GetInstanceId(), material);
                }
                mesh.SetSurfaceOverrideMaterial(surface, material);
            }
    }

    private static void QualifyVisualPlacement(ClassicAuthoredSceneryBinding binding, ClassicAuthoredVisualPlacement visual,
        Fallout1NativeFrmFrame frame, float pixelsPerMeter, int sourceRotation)
    {
        if (visual.Schema != "opennv-classic-reference-canvas/v1" || visual.Canvas is not { } canvas ||
            visual.SourceFrame is not { } source)
            throw new InvalidDataException("Authored visual placement contract is absent or unsupported.");
        var values = new[] { canvas.X, canvas.Y, canvas.Width, canvas.Height, visual.AuthoredElevationMeters,
            visual.PixelsPerMeter, visual.YawDegrees, binding.YawDegrees, pixelsPerMeter };
        if (values.Any(value => !float.IsFinite(value)) || canvas.Width <= 0 || canvas.Height <= 0 ||
            visual.AuthoredElevationMeters < 0 || visual.PixelsPerMeter <= 0 || pixelsPerMeter <= 0 ||
            visual.SourceRotation is < 0 or > 5 || source.Rotation is < 0 or > 5)
            throw new InvalidDataException("Authored visual placement dimensions, projection or rotation are invalid.");
        if (source.Width != frame.Width || source.Height != frame.Height ||
            source.DirectionX != frame.DirectionX || source.DirectionY != frame.DirectionY ||
            source.FrameX != frame.FrameX || source.FrameY != frame.FrameY ||
            source.FramesPerDirection != frame.FramesPerDirection || source.Rotation != frame.Rotation ||
            sourceRotation != visual.SourceRotation || MathF.Abs(binding.YawDegrees - visual.YawDegrees) > .0001f ||
            MathF.Abs(pixelsPerMeter - visual.PixelsPerMeter) > .0001f)
            throw new NotSupportedException("Authored reference canvas does not qualify this source frame, rotation or projection; original FRM required.");
        // Source-paint projection still uses actual visible mesh bounds. Do not
        // silently apply that different projection to a reference-canvas model.
        if (binding.UseSourcePalette)
            throw new NotSupportedException("Reference-canvas placement requires original model materials; live source-paint projection is unbound.");
    }

    public void Dispose()
    {
        foreach (var model in _models.Values) model.Root.Free();
        _linearVertexMaterials.Clear();
        _models.Clear(); _materials.Clear();
    }
}
