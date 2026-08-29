using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

/// <summary>
/// Presents the exact owned premade portrait on a live curved 3D surface.
/// This is an honest Hex-picker presentation adapter, not reconstructed head
/// geometry. Normal/FPS character creation continues to show the source
/// portrait without this control.
/// </summary>
internal sealed partial class Fo1OwnedPortraitRelief : SubViewportContainer
{
    private const int HorizontalSegments = 24;
    private const int VerticalSegments = 12;
    private const float MeshWidth = 2.0f;
    private const float CurvatureMeters = 0.12f;
    private const float CameraMargin = 1.12f;
    private const float Half = 0.5f;
    private const float YawAmplitudeRadians = 0.09f;
    private const float YawCyclesPerSecond = 0.08f;
    private readonly StandardMaterial3D _material;
    private readonly MeshInstance3D _surface;
    private double _elapsed;

    internal Fo1OwnedPortraitRelief(Fo1PremadeCharacter character)
    {
        Name = "FO1_HEX_OWNED_PORTRAIT_LIVE_3D_RELIEF";
        Stretch = true;
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        var viewport = new SubViewport
        {
            Name = "FO1_HEX_OWNED_PORTRAIT_RELIEF_VIEWPORT",
            Size = new Vector2I(character.Portrait.Width, character.Portrait.Height),
            TransparentBg = false,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        AddChild(viewport);
        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _surface = new MeshInstance3D
        {
            Name = "FO1_HEX_DISTINCT_OWNED_PORTRAIT_CURVED_SURFACE",
            Mesh = BuildMesh(character.Portrait.Width, character.Portrait.Height),
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        viewport.AddChild(_surface);
        var meshHeight = MeshWidth * character.Portrait.Height / character.Portrait.Width;
        viewport.AddChild(new Camera3D
        {
            Name = "FO1_HEX_OWNED_PORTRAIT_RELIEF_CAMERA",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = meshHeight * CameraMargin,
            Position = new Vector3(0.0f, 0.0f, 3.0f),
            Current = true,
        });
        SetCharacter(character);
    }

    internal string CharacterId => GetMeta("source_character_id").AsString();
    internal string SourcePortraitSha256 => GetMeta("source_portrait_sha256").AsString();
    internal string LocalPortraitPngSha256 =>
        GetMeta("local_portrait_png_sha256").AsString();
    internal int SurfaceCount => _surface.Mesh?.GetSurfaceCount() ?? 0;

    internal void SetCharacter(Fo1PremadeCharacter character)
    {
        _material.AlbedoTexture = character.Portrait.Load();
        SetMeta("source_character_id", character.Id);
        SetMeta("source_portrait_frm_sha256", character.Portrait.SourceFrmSha256);
        SetMeta("source_portrait_sha256", character.Portrait.SourceFrmSha256);
        SetMeta("local_portrait_png_sha256", character.Portrait.Sha256);
        SetMeta("presentation_mode", "owned-portrait-live-3d-relief");
        SetMeta("head_geometry_status", "not-rebuilt");
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        _surface.Rotation = new Vector3(
            0.0f,
            MathF.Sin((float)(_elapsed * Math.Tau * YawCyclesPerSecond)) *
                YawAmplitudeRadians,
            0.0f);
    }

    private static ArrayMesh BuildMesh(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException(
                "Fallout 1 owned portrait relief requires positive source dimensions.");
        var meshHeight = MeshWidth * height / width;
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (var row = 0; row < VerticalSegments; row++)
            for (var column = 0; column < HorizontalSegments; column++)
            {
                var u0 = (float)column / HorizontalSegments;
                var u1 = (float)(column + 1) / HorizontalSegments;
                var v0 = (float)row / VerticalSegments;
                var v1 = (float)(row + 1) / VerticalSegments;
                var first = Point(u0, v0, meshHeight);
                var second = Point(u1, v0, meshHeight);
                var third = Point(u1, v1, meshHeight);
                var fourth = Point(u0, v1, meshHeight);
                AddTriangle(tool, first, second, third, new Vector2(u0, v0),
                    new Vector2(u1, v0), new Vector2(u1, v1));
                AddTriangle(tool, first, third, fourth, new Vector2(u0, v0),
                    new Vector2(u1, v1), new Vector2(u0, v1));
            }
        tool.Index();
        tool.GenerateNormals();
        tool.GenerateTangents();
        return tool.Commit() ?? throw new InvalidOperationException(
            "Could not build Fallout 1 owned portrait relief mesh.");
    }

    private static Vector3 Point(float u, float v, float meshHeight)
    {
        var normalizedX = u * 2.0f - 1.0f;
        return new Vector3(
            normalizedX * MeshWidth / 2.0f,
            (Half - v) * meshHeight,
            CurvatureMeters * (1.0f - normalizedX * normalizedX));
    }

    private static void AddTriangle(
        SurfaceTool tool,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector2 firstUv,
        Vector2 secondUv,
        Vector2 thirdUv)
    {
        tool.SetUV(firstUv);
        tool.AddVertex(first);
        tool.SetUV(secondUv);
        tool.AddVertex(second);
        tool.SetUV(thirdUv);
        tool.AddVertex(third);
    }
}
