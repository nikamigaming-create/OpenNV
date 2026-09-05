using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedActorPreview : Node
{
    internal SubViewport View { get; }
    internal RuntimeNativeNpc Actor { get; }
    private readonly Camera3D _camera;
    private readonly Node3D _root, _lights;
    private readonly FalloutNifFile _lightSource;
    private readonly RuntimeLiveContentSource _content;
    private readonly FalloutInstallationSettings _settings;
    private readonly float _zoom;

    internal NativeOwnedActorPreview(FalloutPluginStack records, FalloutNpcAppearance appearance,
        FalloutInstallationSettings settings, int pixels)
    {
        Name = "OwnedActorPreview"; ProcessMode = ProcessModeEnum.Always;
        _content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned preview source is absent.");
        _settings = settings;
        _zoom = settings.Number("Interface", "fRSMStartingZoom");
        View = new SubViewport
        {
            Name = "PortraitView",
            OwnWorld3D = true,
            Size = new(pixels, pixels),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(View);
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.125f, 0.161f, 0.141f),
            AmbientLightSource = Godot.Environment.AmbientSource.Disabled,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
        };
        View.AddChild(new WorldEnvironment { Environment = environment });
        _camera = new Camera3D { Name = "PortraitCamera", Current = true, Near = settings.Number("Display", "fNearDistance"), Far = 5000 };
        View.AddChild(_camera); // Native +Y / +Z corresponds to Godot -Z / +Y.
        _root = new Node3D { Name = "PortraitRoot" };
        View.AddChild(_root);
        Actor = RuntimeNativeNpc.Create(appearance, _content, 1,
            (npc, part, nif, geometry) => NativeNpcMaterial.Resolve(npc, part, nif, geometry, records, Colors.Black));
        Actor.ConfigureFaceAnimation(records);
        _root.AddChild(Actor);
        Actor.Position = GamebryoCoordinate.ConvertVector(new(0, -5, 0));
        const string lightPath = "meshes/terminals/PlayerFaceLights01.NIF";
        if (!_content.TryRead(lightPath, null, out var lightBytes, out var identity)) throw new FileNotFoundException(lightPath);
        _lightSource = FalloutNifFile.Read(lightBytes);
        _lights = RuntimeNativeNifMeshBuilder.Build(_lightSource, 1).Root;
        _lights.SetMeta("opennv_source_model", lightPath);
        _root.AddChild(_lights);
        Actor.PosePublished += UpdateProjection;
        SetMeta("opennv_preview_light_source", identity);
        SetMeta("opennv_preview_unbound", "animation-selection,render-target-sampling,hierarchical-bound-merging,FaceGen-quantization");
    }

    public override void _Ready() => UpdateProjection();

    internal void UpdateProjection()
    {
        var projection = FalloutCharacterPreviewProjection.Read(_settings, Actor.SourceHeight, _zoom, MathF.PI - MathF.PI / 6);
        var position = projection.Translation;
        _root.Position = GamebryoCoordinate.ConvertVector(new(position.X, position.Y, position.Z));
        // NiMatrix3's axis-angle constructor uses the opposite signed-angle
        // convention to Godot's Basis constructor.
        _root.Basis = new Basis(Vector3.Up, -projection.Rotation);
        _camera.Fov = 2 * MathF.Atan(projection.Slope) * 180 / MathF.PI;
        var radius = Actor.CurrentWorldBound(_content).Radius * 3;
        var cameraInverse = _camera.GlobalTransform.AffineInverse();
        var nodes = _lights.FindChildren("*", "", true, false).OfType<Node3D>().ToArray();
        var lights = _lightSource.Blocks.Where(block => block.TypeName == "NiPointLight")
            .Select(block => (FalloutNifPointLight)_lightSource.ReadObject(block.Index)).Where(light => light.Light.SwitchState)
            .Select(light =>
            {
                var node = nodes.Single(node => node.HasMeta("opennv_nif_block") && node.GetMeta("opennv_nif_block").AsInt32() == light.Block.Index);
                var rgb = light.Light.Diffuse;
                return new NativeNifPointLight(cameraInverse * node.GlobalPosition, new Vector3(rgb.R, rgb.G, rgb.B) * light.Light.Dimmer, radius);
            }).ToArray();
        foreach (var mesh in Actor.FindChildren("*", "", true, false).OfType<MeshInstance3D>().Where(mesh => mesh.Mesh is not null))
            for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
                if (mesh.GetActiveMaterial(surface) is ShaderMaterial material &&
                    material.ResourceName is NativeNifLightingMaterial.ResourceIdentity or "Owned NIF FaceGen skin")
                    NativeNifPointLighting.Bind(material, lights, 1, storeEncoded: true);
        SetMeta("opennv_preview_actor_height", Actor.SourceHeight);
        SetMeta("opennv_preview_light_radius", radius);
        SetMeta("opennv_preview_translation", _root.Position);
    }
}
