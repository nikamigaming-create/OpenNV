using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal sealed class OpeningPlayerFaceGenPreviewHost
{
    private const float PreviewMinimumDimensionPixels = 160.0f;
    private const float PreviewFrameMargin = 1.15f;
    private const float PreviewNearPlaneMeters = 0.01f;
    private const float PreviewFarPlaneMeters = 100.0f;

    private readonly IReadOnlyDictionary<string, IReadOnlyList<MorphBinding>> _bindings;

    private OpeningPlayerFaceGenPreviewHost(
        SubViewportContainer control,
        IReadOnlyDictionary<string, IReadOnlyList<MorphBinding>> bindings)
    {
        Control = control;
        _bindings = bindings;
    }

    internal Control Control { get; }
    internal int BoundControlCount => _bindings.Count;
    internal int BoundSurfaceCount => _bindings.Values
        .Select(value => value.Count)
        .Distinct()
        .Single();

    internal static OpeningPlayerFaceGenPreviewHost Load(
        OpeningPlayerFaceGenPreview source,
        IReadOnlyList<OpeningNativeFaceGenGeometryControl> controls,
        OpeningFaceGenPreviewControl policy,
        Control parent,
        RuntimeConfiguration configuration,
        Vector2 availableSize)
    {
        VerifyHash(source.GltfPath, source.GltfSha256);
        VerifyHash(source.SidecarPath, source.SidecarSha256);
        if (controls.Count == 0 ||
            controls.Select(value => value.SettingEntity).Distinct(StringComparer.Ordinal).Count() !=
                controls.Count)
            throw new InvalidOperationException(
                "Player FaceGen preview control selection is invalid.");
        if (!parent.IsInsideTree())
            throw new InvalidOperationException(
                "Player FaceGen preview owner is outside the SceneTree.");
        var width = Mathf.Max(PreviewMinimumDimensionPixels, availableSize.X);
        var height = Mathf.Max(PreviewMinimumDimensionPixels, availableSize.Y);
        var viewportContainer = new SubViewportContainer
        {
            Name = "OwnedPlayerFaceGenPreview",
            CustomMinimumSize = new Vector2(width, height),
            SizeFlagsHorizontal = Godot.Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Godot.Control.SizeFlags.ExpandFill,
            Stretch = true,
        };
        parent.AddChild(viewportContainer);
        if (!viewportContainer.IsInsideTree())
            throw new InvalidOperationException(
                "Player FaceGen preview viewport container did not enter the SceneTree.");
        var viewport = new SubViewport
        {
            Name = "OwnedPlayerFaceGenPreviewViewport",
            Size = new Vector2I(Mathf.RoundToInt(width), Mathf.RoundToInt(height)),
            TransparentBg = true,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        viewportContainer.AddChild(viewport);
        var scene = new Node3D { Name = "OwnedPlayerFaceGenPreviewScene" };
        viewport.AddChild(scene);
        if (!scene.IsInsideTree())
            throw new InvalidOperationException(
                "Player FaceGen preview scene did not enter the SceneTree.");
        var actor = ActorModelSlice.Load(
            source.GltfPath,
            source.SidecarPath,
            scene,
            configuration,
            scaleToMeters: true,
            ActorModelSlice.BoundsContract.AnyActor);
        var bounds = ActorModelSlice.PosedWorldBounds(actor);
        var target = bounds.GetCenter();
        var camera = new Camera3D
        {
            Name = "OwnedPlayerFaceGenPreviewCamera",
            Near = PreviewNearPlaneMeters,
            Far = PreviewFarPlaneMeters,
            Current = true,
        };
        scene.AddChild(camera);
        var verticalHalfRadians = Mathf.DegToRad(camera.Fov) *
            policy.Presentation.VerticalFovHalfAngleFactor;
        var framedDimension = Mathf.Max(bounds.Size.X, bounds.Size.Y);
        var distance = framedDimension * PreviewFrameMargin /
            (2.0f * MathF.Tan(verticalHalfRadians));
        camera.Position = target + Vector3.Forward *
            (distance + bounds.Size.Z * policy.Presentation.DepthExtentFraction);
        camera.LookAt(target, Vector3.Up);
        var environment = new WorldEnvironment
        {
            Name = "OwnedPlayerFaceGenPreviewEnvironment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Transparent,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 1.0f,
            },
        };
        scene.AddChild(environment);

        var bindings = controls.ToDictionary(
            control => control.SettingEntity,
            control => (IReadOnlyList<MorphBinding>)actor.Surfaces.Select(surface =>
            {
                var matches = surface.FaceGenMorphTargets
                    .Select((name, index) => (name, index))
                    .Where(value => value.name == control.SettingEntity)
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException(
                        "Owned player preview surface has no unique CTL/EGM target: " +
                        $"{surface.Role}/{surface.Shape}/{control.SettingEntity}.");
                return new MorphBinding(surface.Mesh, matches[0].index);
            }).ToArray(),
            StringComparer.Ordinal);
        if (bindings.Values.Any(value => value.Count != actor.Surfaces.Count))
            throw new InvalidOperationException(
                "Owned player preview CTL/EGM binding coverage is incomplete.");
        return new OpeningPlayerFaceGenPreviewHost(viewportContainer, bindings);
    }

    internal void Apply(string settingEntity, float value)
    {
        if (!float.IsFinite(value) || !_bindings.TryGetValue(settingEntity, out var bindings))
            throw new ArgumentOutOfRangeException(nameof(value));
        foreach (var binding in bindings)
            binding.Mesh.SetBlendShapeValue(binding.Index, value);
    }

    private static void VerifyHash(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Player FaceGen preview artifact hash differs: {path}");
    }

    private readonly record struct MorphBinding(MeshInstance3D Mesh, int Index);
}
