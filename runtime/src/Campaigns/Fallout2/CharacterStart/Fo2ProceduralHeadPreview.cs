using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

internal sealed partial class Fo2ProceduralHeadPreview : SubViewportContainer
{
    private const int RadialSegments = 24;
    private const int Rings = 12;
    private const float CameraZ = 4.0f;
    private const float CameraSize = 2.8f;
    private const float EyeRadius = 0.075f;
    private const float EyeX = 0.22f;
    private const float EyeY = 0.10f;
    private const float EyeZ = 0.64f;
    private const float NoseRadius = 0.09f;
    private const float NoseY = -0.10f;
    private const float NoseZ = 0.69f;
    private const float HairCapRadiusScale = 1.04f;
    private const float HairCapHeight = 0.62f;
    private const float HairCapY = 0.60f;
    private const float SideHairRadius = 0.15f;
    private const float SideHairX = 0.66f;
    private const float SideHairYFactor = 0.25f;
    private const float MinimumCapsuleHeight = 0.32f;
    private const float AmbientEnergy = 0.72f;
    private const float LightPitchDegrees = -24.0f;
    private const float LightYawDegrees = -28.0f;
    private const float HeadLightEnergy = 1.1f;
    private const float MaterialRoughness = 0.88f;
    private readonly Fo2ProceduralAppearanceCatalog _catalog;
    private readonly Node3D _identityRoot;
    private readonly MeshInstance3D _head;
    private readonly MeshInstance3D _hairCap;
    private readonly MeshInstance3D _leftHair;
    private readonly MeshInstance3D _rightHair;
    private readonly StandardMaterial3D _skinMaterial;
    private readonly StandardMaterial3D _hairMaterial;
    private double _elapsed;

    internal Fo2ProceduralHeadPreview()
    {
        _catalog = Fo2ProceduralAppearanceCatalog.Load();
        Name = "FO2_LOCAL_PROCEDURAL_LIVE_3D_HEAD";
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var viewport = new SubViewport
        {
            Name = "FO2_LOCAL_PROCEDURAL_HEAD_VIEWPORT",
            Size = _catalog.LiveHead.Viewport,
            TransparentBg = false,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        AddChild(viewport);
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = _catalog.Background,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = AmbientEnergy,
            },
        });
        viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(LightPitchDegrees, LightYawDegrees, 0.0f),
            LightEnergy = HeadLightEnergy,
            ShadowEnabled = false,
        });
        _identityRoot = new Node3D { Name = "SavedProceduralIdentity" };
        viewport.AddChild(_identityRoot);
        _skinMaterial = BuildMaterial(Colors.White);
        _hairMaterial = BuildMaterial(_catalog.LiveHead.HairAlbedo);
        _head = Part(
            "FaceShape",
            new SphereMesh
            {
                Radius = _catalog.LiveHead.HeadRadius,
                Height = _catalog.LiveHead.HeadHeight,
                RadialSegments = RadialSegments,
                Rings = Rings,
            },
            _skinMaterial);
        _hairCap = Part(
            "HairCap",
            new SphereMesh
            {
                Radius = _catalog.LiveHead.HeadRadius * HairCapRadiusScale,
                Height = HairCapHeight,
                RadialSegments = RadialSegments,
                Rings = Rings,
            },
            _hairMaterial);
        _hairCap.Position = new Vector3(0.0f, HairCapY, 0.0f);
        _leftHair = Part("LeftHair", SideMesh(MinimumCapsuleHeight), _hairMaterial);
        _rightHair = Part("RightHair", SideMesh(MinimumCapsuleHeight), _hairMaterial);
        AddFeature("LeftEye", new Vector3(-EyeX, EyeY, EyeZ), EyeRadius);
        AddFeature("RightEye", new Vector3(EyeX, EyeY, EyeZ), EyeRadius);
        AddFeature("Nose", new Vector3(0.0f, NoseY, NoseZ), NoseRadius, _skinMaterial);
        viewport.AddChild(new Camera3D
        {
            Name = "FO2_LOCAL_PROCEDURAL_HEAD_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = CameraSize,
            Position = new Vector3(0.0f, 0.0f, CameraZ),
            Current = true,
        });
    }

    internal string FaceShapeId => GetMeta("face_shape_id").AsString();
    internal string HairStyleId => GetMeta("hair_style_id").AsString();
    internal string SkinToneId => GetMeta("skin_tone_id").AsString();
    internal string RecipeSha256 => GetMeta("appearance_recipe_sha256").AsString();
    internal int VisibleGeometryParts =>
        new[] { _head, _hairCap, _leftHair, _rightHair }
            .Count(part => part.Visible);

    internal void SetIdentity(
        string sex,
        string faceShapeId,
        string hairStyleId,
        string skinToneId)
    {
        _ = Fo2ProceduralPortrait.Render(sex, faceShapeId, hairStyleId, skinToneId);
        var face = _catalog.Face(faceShapeId);
        var hair = _catalog.HairStyle(hairStyleId);
        var skin = _catalog.SkinTone(skinToneId);
        _head.Scale = face.HeadScale;
        _hairCap.Scale = new Vector3(face.HeadScale.X, face.HeadScale.Y, face.HeadScale.Z);
        _skinMaterial.AlbedoColor = skin.HeadAlbedo;
        var sideHeight = MathF.Max(hair.SideLength, MinimumCapsuleHeight);
        _leftHair.Mesh = SideMesh(sideHeight);
        _rightHair.Mesh = SideMesh(sideHeight);
        _leftHair.Position = new Vector3(
            -SideHairX * face.HeadScale.X,
            -sideHeight * SideHairYFactor,
            0.0f);
        _rightHair.Position = new Vector3(
            SideHairX * face.HeadScale.X,
            -sideHeight * SideHairYFactor,
            0.0f);
        _leftHair.Visible = hair.SideMode == Fo2ProceduralAppearanceCatalog.BothSideHair;
        _rightHair.Visible = hair.SideMode is Fo2ProceduralAppearanceCatalog.RightSideHair or
            Fo2ProceduralAppearanceCatalog.BothSideHair;
        SetMeta("sex", sex);
        SetMeta("face_shape_id", faceShapeId);
        SetMeta("hair_style_id", hairStyleId);
        SetMeta("skin_tone_id", skinToneId);
        SetMeta("appearance_recipe_sha256", _catalog.Sha256);
        SetMeta("boundary", "local-procedural-preview-not-retail-head-geometry");
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        _identityRoot.Rotation = new Vector3(
            0.0f,
            MathF.Sin((float)(_elapsed * Math.Tau *
                _catalog.LiveHead.YawCyclesPerSecond)) *
                _catalog.LiveHead.YawAmplitudeRadians,
            0.0f);
    }

    private MeshInstance3D Part(string name, Mesh mesh, Material material)
    {
        var part = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _identityRoot.AddChild(part);
        return part;
    }

    private void AddFeature(
        string name,
        Vector3 position,
        float radius,
        Material? material = null)
    {
        var feature = Part(
            name,
            new SphereMesh
            {
                Radius = radius,
                Height = radius * 2.0f,
                RadialSegments = RadialSegments,
                Rings = Rings,
            },
            material ?? BuildMaterial(_catalog.LiveHead.EyeAlbedo));
        feature.Position = position;
    }

    private static CapsuleMesh SideMesh(float height) => new()
    {
        Radius = SideHairRadius,
        Height = height,
        RadialSegments = RadialSegments,
        Rings = Rings,
    };

    private static StandardMaterial3D BuildMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Roughness = MaterialRoughness,
        Metallic = 0.0f,
    };
}
