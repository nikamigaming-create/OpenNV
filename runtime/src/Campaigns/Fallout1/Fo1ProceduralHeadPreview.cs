using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal sealed partial class Fo1ProceduralHeadPreview : SubViewportContainer
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
    private readonly Fo1ProceduralAppearanceCatalog _catalog;
    private readonly Node3D _identityRoot;
    private readonly MeshInstance3D _head;
    private readonly MeshInstance3D _hairCap;
    private readonly MeshInstance3D _leftHair;
    private readonly MeshInstance3D _rightHair;
    private readonly StandardMaterial3D _skinMaterial;
    private readonly StandardMaterial3D _hairMaterial;
    private readonly StandardMaterial3D _eyeMaterial;
    private double _elapsed;

    internal Fo1ProceduralHeadPreview()
    {
        _catalog = Fo1ProceduralAppearanceCatalog.Load();
        Name = "FO1_HEX_LOCAL_PROCEDURAL_LIVE_3D_HEAD";
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var viewport = new SubViewport
        {
            Name = "FO1_HEX_LOCAL_PROCEDURAL_HEAD_VIEWPORT",
            Size = _catalog.LiveViewport,
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
                AmbientLightEnergy = 0.72f,
            },
        });
        viewport.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-24.0f, -28.0f, 0.0f),
            LightEnergy = 1.1f,
            ShadowEnabled = false,
        });
        _identityRoot = new Node3D { Name = "SavedProceduralIdentity" };
        viewport.AddChild(_identityRoot);
        _skinMaterial = BuildMaterial(Colors.White);
        _hairMaterial = BuildMaterial(Colors.Black);
        _eyeMaterial = BuildMaterial(Colors.Green);
        _head = Part(
            "FaceShape",
            new SphereMesh
            {
                Radius = _catalog.LiveHeadRadius,
                Height = _catalog.LiveHeadHeight,
                RadialSegments = RadialSegments,
                Rings = Rings,
            },
            _skinMaterial);
        _hairCap = Part(
            "HairCap",
            new SphereMesh
            {
                Radius = _catalog.LiveHeadRadius * HairCapRadiusScale,
                Height = HairCapHeight,
                RadialSegments = RadialSegments,
                Rings = Rings,
            },
            _hairMaterial);
        _hairCap.Position = new Vector3(0.0f, HairCapY, 0.0f);
        _leftHair = Part("LeftHair", SideMesh(MinimumCapsuleHeight), _hairMaterial);
        _rightHair = Part("RightHair", SideMesh(MinimumCapsuleHeight), _hairMaterial);
        Feature("LeftEye", new Vector3(-EyeX, EyeY, EyeZ), EyeRadius, _eyeMaterial);
        Feature("RightEye", new Vector3(EyeX, EyeY, EyeZ), EyeRadius, _eyeMaterial);
        Feature("Nose", new Vector3(0.0f, NoseY, NoseZ), NoseRadius, _skinMaterial);
        viewport.AddChild(new Camera3D
        {
            Name = "FO1_HEX_LOCAL_PROCEDURAL_HEAD_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = CameraSize,
            Position = new Vector3(0.0f, 0.0f, CameraZ),
            Current = true,
        });
    }

    internal string FaceShapeId => GetMeta("face_shape_id").AsString();
    internal string HairStyleId => GetMeta("hair_style_id").AsString();
    internal string SkinToneId => GetMeta("skin_tone_id").AsString();
    internal string HairColorId => GetMeta("hair_color_id").AsString();
    internal string EyeColorId => GetMeta("eye_color_id").AsString();
    internal string RecipeSha256 => GetMeta("appearance_recipe_sha256").AsString();

    internal void SetIdentity(
        string sex,
        string faceShapeId,
        string hairStyleId,
        string skinToneId,
        string hairColorId,
        string eyeColorId)
    {
        _ = Fo1ProceduralPortrait.Render(
            sex, faceShapeId, hairStyleId, skinToneId, hairColorId, eyeColorId);
        var face = _catalog.Face(faceShapeId);
        var hair = _catalog.Hair(hairStyleId);
        _head.Scale = face.HeadScale;
        _hairCap.Scale = face.HeadScale;
        _skinMaterial.AlbedoColor = _catalog.Skin(skinToneId).HeadAlbedo;
        _hairMaterial.AlbedoColor = _catalog.HairColor(hairColorId).HeadAlbedo;
        _eyeMaterial.AlbedoColor = _catalog.EyeColor(eyeColorId).HeadAlbedo;
        var sideHeight = MathF.Max(hair.SideLength, MinimumCapsuleHeight);
        _leftHair.Mesh = SideMesh(sideHeight);
        _rightHair.Mesh = SideMesh(sideHeight);
        _leftHair.Position = new Vector3(
            -SideHairX * face.HeadScale.X, -sideHeight * SideHairYFactor, 0.0f);
        _rightHair.Position = new Vector3(
            SideHairX * face.HeadScale.X, -sideHeight * SideHairYFactor, 0.0f);
        _leftHair.Visible = hair.SideMode == Fo1ProceduralAppearanceCatalog.BothSideHair;
        _rightHair.Visible = hair.SideMode is Fo1ProceduralAppearanceCatalog.RightSideHair or
            Fo1ProceduralAppearanceCatalog.BothSideHair;
        SetMeta("sex", sex);
        SetMeta("face_shape_id", faceShapeId);
        SetMeta("hair_style_id", hairStyleId);
        SetMeta("skin_tone_id", skinToneId);
        SetMeta("hair_color_id", hairColorId);
        SetMeta("eye_color_id", eyeColorId);
        SetMeta("appearance_recipe_sha256", _catalog.Sha256);
        SetMeta("boundary", "local-procedural-preview-not-retail-head-geometry");
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        _identityRoot.Rotation = new Vector3(
            0.0f,
            MathF.Sin((float)(_elapsed * Math.Tau * _catalog.LiveYawCycles)) *
                _catalog.LiveYawAmplitude,
            0.0f);
    }

    private MeshInstance3D Part(string name, Mesh mesh, Material material)
    {
        var result = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _identityRoot.AddChild(result);
        return result;
    }

    private void Feature(string name, Vector3 position, float radius, Material material)
    {
        var result = Part(
            name,
            new SphereMesh
            {
                Radius = radius,
                Height = radius * 2.0f,
                RadialSegments = RadialSegments,
                Rings = Rings,
            },
            material);
        result.Position = position;
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
        Roughness = 0.88f,
        Metallic = 0.0f,
    };
}
