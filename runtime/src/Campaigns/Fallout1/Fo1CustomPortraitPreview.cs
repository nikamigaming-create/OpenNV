using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.Temple;
using OpenNV.Runtime.Presentation.Actors;
using OpenNV.Runtime.Presentation.CharacterCreation;


using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.Fallout1;

/// <summary>
/// Renders the selected owned-data custom donor as a real head-and-shoulders
/// portrait, then applies a crisp green wireframe projection in a second viewport.
/// The projection is presentation-only and never replaces authored premade art.
/// </summary>
internal sealed partial class Fo1CustomPortraitPreview : TextureRect
{
    private const int RenderSize = 384;
    private const float CameraDepthMeters = 4.0f;
    private const float PortraitHeightMeters = 0.58f;
    private readonly string _sex;
    private readonly Fo1HexSceneLoader.PlayerPresentationSource _source;
    private readonly SubViewport _renderViewport;
    private readonly SubViewport _projectionViewport;
    private readonly Node3D _donorRoot;
    private readonly Camera3D _camera;
    private ActorModelSlice.LoadedActor? _donor;
    private Skeleton3D? _skeleton;
    private Fo1CustomAppearanceSelection? _selection;
    private bool _faceFraming = true;
    private bool _greenProjection;

    internal Fo1CustomPortraitPreview(
        string sex,
        Fo1HexSceneLoader.PlayerPresentationSource source)
    {
        if (sex is not "Male" and not "Female" ||
            source.SourceActorFemale != sex.Equals("Female", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Fallout 1 custom portrait donor does not match the selected sex.");
        _sex = sex;
        _source = source;
        Name = "FO1_CUSTOM_OWNED_DONOR_GREEN_FACE_PORTRAIT";
        ExpandMode = ExpandModeEnum.IgnoreSize;
        StretchMode = StretchModeEnum.KeepAspectCentered;
        MouseFilter = MouseFilterEnum.Ignore;

        _renderViewport = new SubViewport
        {
            Name = "FO1_CUSTOM_FACE_SOURCE_3D_VIEWPORT",
            Size = new Vector2I(RenderSize, RenderSize),
            TransparentBg = false,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        AddChild(_renderViewport);
        _renderViewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("07100b"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("fff1dc"),
                AmbientLightEnergy = 0.86f,
            },
        });
        _renderViewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-24.0f, -30.0f, 0.0f),
            LightColor = new Color("ffe3bd"),
            LightEnergy = 1.28f,
            ShadowEnabled = false,
        });
        _donorRoot = new Node3D { Name = "FO1_CUSTOM_FACE_OWNED_DONOR_ROOT" };
        _renderViewport.AddChild(_donorRoot);
        _camera = new Camera3D
        {
            Name = "FO1_CUSTOM_FACE_PORTRAIT_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = PortraitHeightMeters,
            Near = 0.05f,
            Far = 20.0f,
            Current = true,
        };
        _renderViewport.AddChild(_camera);

        _projectionViewport = new SubViewport
        {
            Name = "FO1_CUSTOM_FACE_GREEN_PROJECTION_VIEWPORT",
            Size = new Vector2I(RenderSize, RenderSize),
            TransparentBg = false,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        AddChild(_projectionViewport);
        _projectionViewport.AddChild(new TextureRect
        {
            Name = "FO1_CUSTOM_FACE_GREEN_PROJECTION_SURFACE",
            Texture = _renderViewport.GetTexture(),
            Size = new Vector2(RenderSize, RenderSize),
            ExpandMode = ExpandModeEnum.IgnoreSize,
            StretchMode = StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
            Material = GreenWireframeMaterial(),
        });
        Texture = _renderViewport.GetTexture();
    }

    internal bool ReadyForCapture => _donor is not null && _skeleton is not null;
    internal string SourceActorFormId => _source.SourceActorBaseFormId;

    public override void _Ready()
    {
        var donor = ActorModelSlice.Load(_source.Model, _source.Sidecar, _donorRoot);
        if (donor.FormId != _source.SourceActorBaseFormId ||
            donor.AuthoredSurfaces != _source.Surfaces ||
            donor.AuthoredTextures != _source.Textures ||
            donor.Animations != _source.Animations)
            throw new InvalidOperationException(
                "Fallout 1 custom portrait donor coverage differs from its contract.");
        if (_source.BodyProfile is { } profile)
        {
            var rig = donor.Root.FindChildren("*", "Skeleton3D", true, false)
                .OfType<Skeleton3D>()
                .Single();
            CharacterBodyRig.Apply(
                donor.Root,
                rig,
                profile,
                this,
                $"fallout1-custom-portrait-{_sex.ToLowerInvariant()}");
        }
        foreach (var player in donor.LoadedAnimations
                     .Select(row => row.Player)
                     .Distinct())
            player.Stop();
        _skeleton = donor.Root.FindChildren("*", "Skeleton3D", true, false)
            .OfType<Skeleton3D>()
            .Single();
        if (_skeleton.FindBone("Bip01 Head") < 0)
            throw new InvalidOperationException(
                "Fallout 1 custom portrait donor has no head bone.");
        _donorRoot.Rotation = Vector3.Zero;
        var configuration = RuntimeConfiguration.Load();
        var litMaterials = RuntimeMaterialLoader.ApplyRetailActorLighting(
            donor.Root,
            new Color(0.64f, 0.56f, 0.46f, 1.0f),
            new Color(0.008f, 0.012f, 0.008f, 1.0f),
            0.0f,
            100000.0f,
            1.0f,
            configuration.World.GameUnitsToMeters);
        if (litMaterials <= 0)
            throw new InvalidOperationException(
                "Fallout 1 custom portrait donor has no source-lit materials.");
        _donor = donor;
        FrameHead();
        if (_selection is not null)
            ApplySelection(_selection);
        SetMeta("source_actor_form_id", _source.SourceActorBaseFormId);
        SetMeta("source_model_sha256", _source.ModelSha256);
        SetMeta("source_sidecar_sha256", _source.SidecarSha256);
        SetMeta("presentation", "owned-donor-close-green-wireframe-projection-non-parity");
        SetMeta("camera_alignment", "front-centered");
    }

    internal void SetSelection(Fo1CustomAppearanceSelection selection)
    {
        _selection = selection;
        if (_donor is not null)
            ApplySelection(selection);
    }

    internal void SetPreviewState(bool faceFraming, bool greenProjection)
    {
        _faceFraming = faceFraming;
        _greenProjection = greenProjection;
        Texture = greenProjection
            ? _projectionViewport.GetTexture()
            : _renderViewport.GetTexture();
        if (_donor is not null)
        {
            if (faceFraming)
                FrameHead();
            else
                FrameBody();
        }
        SetMeta(
            "preview_mode",
            $"{(greenProjection ? "green" : "normal")}-" +
            $"{(faceFraming ? "face" : "body")}");
    }

    internal Image CapturePortrait()
    {
        if (!ReadyForCapture)
            throw new InvalidOperationException(
                "Fallout 1 custom green portrait has not rendered its owned donor.");
        RenderingServer.ForceSync();
        var image = _projectionViewport.GetTexture().GetImage();
        if (image.IsEmpty() || image.GetWidth() != RenderSize || image.GetHeight() != RenderSize)
            throw new InvalidOperationException(
                "Fallout 1 custom green portrait capture is empty.");
        image.Resize(
            Fo1ProceduralPortrait.Width,
            Fo1ProceduralPortrait.Height,
            Image.Interpolation.Lanczos);
        return image;
    }

    private void ApplySelection(Fo1CustomAppearanceSelection selection)
    {
        var donor = _donor ?? throw new InvalidOperationException(
            "Fallout 1 custom portrait has no donor.");
        var skeleton = _skeleton ?? throw new InvalidOperationException(
            "Fallout 1 custom portrait has no skeleton.");
        var catalog = Fo1ProceduralAppearanceCatalog.Load();
        var headBone = skeleton.FindBone("Bip01 Head");
        skeleton.SetBonePoseScale(headBone, catalog.Face(selection.FaceShapeId).HeadScale);

        var skin = catalog.Skin(selection.SkinToneId).HeadAlbedo;
        var hair = catalog.HairColor(selection.HairColorId).HeadAlbedo;
        var headMaterials = 0;
        var hairMaterials = 0;
        foreach (var surface in donor.Surfaces)
        {
            for (var index = 0;
                 index < (surface.Mesh.Mesh?.GetSurfaceCount() ?? 0);
                 index++)
            {
                if (surface.Mesh.GetSurfaceOverrideMaterial(index) is not ShaderMaterial material)
                    continue;
                var shader = material.Shader?.Code ?? "";
                if (surface.Role == "head" &&
                    shader.Contains("uniform bool use_complexion_target;", StringComparison.Ordinal))
                {
                    var source = ActorComplexionMath.AverageFaceGenEncodedSkinColor(material);
                    material.SetShaderParameter("use_complexion_target", true);
                    material.SetShaderParameter(
                        "complexion_target",
                        new Vector3(skin.R, skin.G, skin.B));
                    material.SetShaderParameter(
                        "complexion_source_mean",
                        ActorComplexionMath.Mean(source));
                    headMaterials++;
                }
                if (surface.Role == "hair" &&
                    shader.Contains("uniform vec4 base_color_factor;", StringComparison.Ordinal))
                {
                    var original = material.GetShaderParameter("base_color_factor").AsColor();
                    material.SetShaderParameter(
                        "base_color_factor",
                        new Color(hair.R, hair.G, hair.B, original.A));
                    hairMaterials++;
                }
            }
        }
        if (headMaterials == 0 || hairMaterials == 0)
            throw new InvalidOperationException(
                "Fallout 1 custom portrait could not bind its head and hair materials.");
        FrameHead();
        SetMeta("face_shape_id", selection.FaceShapeId);
        SetMeta("hair_style_id", selection.HairStyleId);
        SetMeta("skin_tone_id", selection.SkinToneId);
        SetMeta("hair_color_id", selection.HairColorId);
        SetMeta("eye_color_id", selection.EyeColorId);
        SetMeta(
            "hair_style_disposition",
            "source-sex-default-geometry-until-owned-style-set-exists");
        SetMeta(
            "eye_color_disposition",
            "source-default-until-iris-only-mask-is-bound");
    }

    private void FrameHead()
    {
        var skeleton = _skeleton;
        if (skeleton is null)
            return;
        var head = skeleton.FindBone("Bip01 Head");
        var headWorld = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(head);
        var target = headWorld.Origin + Vector3.Down * 0.03f;
        _camera.Size = PortraitHeightMeters;
        _camera.Position = new Vector3(
            target.X,
            target.Y,
            target.Z - CameraDepthMeters);
        _camera.LookAt(target, Vector3.Up);
    }

    private void FrameBody()
    {
        if (_donor is not { } donor)
            return;
        var bounds = donor.Bounds;
        var target = bounds.GetCenter();
        _camera.Size = MathF.Max(bounds.Size.Y, bounds.Size.X) * 1.12f;
        _camera.Position = new Vector3(
            target.X,
            target.Y,
            target.Z - CameraDepthMeters);
        _camera.LookAt(target, Vector3.Up);
    }

    private static ShaderMaterial GreenWireframeMaterial() =>
        ClassicGreenWireframeShader.Create(
            "OpenNV_Fallout1CustomGreenWireframeFacePortrait");
}
