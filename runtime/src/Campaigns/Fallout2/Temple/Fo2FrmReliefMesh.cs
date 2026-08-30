using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2FrmReliefArtifact(
    string Mode,
    string SourcePngSha256,
    string NormalPngPath,
    string NormalPngSha256,
    string SolidMaskPngPath,
    string SolidMaskPngSha256,
    string DepthPngPath,
    string DepthPngSha256,
    int SourceOpaquePixels,
    int SolidOpaquePixels,
    Color AverageOpaqueColor,
    int IslandCount,
    int MaximumInteriorDistancePixels,
    float LumaWeight,
    float BackDepthFraction)
{
    private const string Schema = "opennv-fo2-frm-alpha-relief/v3";
    private const string ExpectedMode = "exact-frm-alpha-island-molded-relief-v2";
    private const string DepthMode = "owned-alpha-distance-and-luma-normalized-v1";
    private const string DepthAuthority =
        "owned FRM alpha/luma define exact molded cells and normalized depth; " +
        "versioned role cap supplies only otherwise unknowable thickness";
    private const float ByteColorScale = 255.0f;
    private const int ByteColorMaximum = 255;
    private const float MaximumBackDepthFraction = 0.5f;

    internal static Fo2FrmReliefArtifact Load(
        JsonElement row,
        string cacheRoot,
        string expectedSourcePngSha256,
        string label)
    {
        if (Fo2TemplePresentationCatalog.RequiredString(row, "schema") != Schema ||
            Fo2TemplePresentationCatalog.RequiredString(row, "mode") != ExpectedMode ||
            Fo2TemplePresentationCatalog.RequiredHash(row, "sourcePngSha256") !=
                expectedSourcePngSha256 ||
            Fo2TemplePresentationCatalog.RequiredString(row, "depthAuthority") !=
                DepthAuthority)
            throw new InvalidOperationException($"{label} relief identity drifted.");
        var normalPath = VerifyDerivedImage(row, cacheRoot, "normalPng", label);
        var solidMaskPath = VerifyDerivedImage(row, cacheRoot, "solidMaskPng", label);
        var depthPath = VerifyDerivedImage(row, cacheRoot, "depthPng", label);
        var average = row.GetProperty("averageOpaqueRgb").EnumerateArray()
            .Select(value => value.GetInt32()).ToArray();
        var sourceOpaquePixels = row.GetProperty("sourceOpaquePixels").GetInt32();
        var solidOpaquePixels = row.GetProperty("solidOpaquePixels").GetInt32();
        var islands = row.GetProperty("islands").EnumerateArray().ToArray();
        var islandPixels = islands.Sum(island =>
            island.GetProperty("opaquePixels").GetInt32());
        var depthField = row.GetProperty("depthField");
        var silhouetteWeight = depthField.GetProperty("silhouetteWeight").GetSingle();
        var lumaWeight = depthField.GetProperty("lumaWeight").GetSingle();
        var backDepthFraction = depthField.GetProperty("backDepthFraction").GetSingle();
        if (average.Length != 3 ||
            average.Any(value => value is < 0 or > ByteColorMaximum) ||
            sourceOpaquePixels <= 0 || solidOpaquePixels <= 0 ||
            sourceOpaquePixels != solidOpaquePixels ||
            islands.Length == 0 || islands.Any(island =>
                island.GetProperty("opaquePixels").GetInt32() <= 0 ||
                island.GetProperty("boundsPixels").GetArrayLength() != 4) ||
            row.GetProperty("islandCount").GetInt32() != islands.Length ||
            islandPixels != solidOpaquePixels ||
            Fo2TemplePresentationCatalog.RequiredString(depthField, "mode") != DepthMode ||
            depthField.GetProperty("maximumInteriorDistancePixels").GetInt32() <= 0 ||
            !Mathf.IsEqualApprox(silhouetteWeight + lumaWeight, 1.0f) ||
            silhouetteWeight <= 0.0f || lumaWeight <= 0.0f ||
            backDepthFraction is <= 0.0f or >= MaximumBackDepthFraction)
            throw new InvalidOperationException($"{label} relief coverage drifted.");
        return new Fo2FrmReliefArtifact(
            ExpectedMode,
            expectedSourcePngSha256,
            normalPath.Path,
            normalPath.Sha256,
            solidMaskPath.Path,
            solidMaskPath.Sha256,
            depthPath.Path,
            depthPath.Sha256,
            sourceOpaquePixels,
            solidOpaquePixels,
            new Color(
                average[0] / ByteColorScale,
                average[1] / ByteColorScale,
                average[2] / ByteColorScale),
            islands.Length,
            depthField.GetProperty("maximumInteriorDistancePixels").GetInt32(),
            lumaWeight,
            backDepthFraction);
    }

    private static (string Path, string Sha256) VerifyDerivedImage(
        JsonElement row,
        string cacheRoot,
        string property,
        string label)
    {
        var path = Fo2TemplePresentationCatalog.ResolvePath(
            Fo2TemplePresentationCatalog.RequiredString(row, property),
            cacheRoot);
        var sha256 = Fo2TemplePresentationCatalog.RequiredHash(row, property + "Sha256");
        Fo2TemplePresentationCatalog.VerifyFile(
            path,
            sha256,
            row.GetProperty(property + "Bytes").GetInt64(),
            $"{label} relief {property}");
        return (path, sha256);
    }
}

internal sealed record Fo2FrmReliefMeshSet(
    ArrayMesh Faces,
    ArrayMesh? Sides,
    StandardMaterial3D FaceMaterial,
    StandardMaterial3D? SideMaterial,
    Vector3 LocalOffsetMeters,
    bool SourcePixelsOnly,
    int FaceTriangles,
    int SideTriangles);

internal static class Fo2FrmReliefMesh
{
    private const float Half = 0.5f;
    private const float SolidThreshold = 0.5f;
    private const float MinimumFrontDepthFraction = 0.14f;

    internal static Fo2FrmReliefMeshSet Build(
        string sourcePngPath,
        int width,
        int height,
        Vector2I sourcePixelOffset,
        float sourcePixelsPerMeter,
        float depthMeters,
        float sideRoughness,
        Fo2FrmReliefArtifact relief,
        bool sourcePixelsOnly)
    {
        if (width <= 0 || height <= 0 || sourcePixelsPerMeter <= 0.0f ||
            depthMeters <= 0.0f || sideRoughness is < 0.0f or > 1.0f)
            throw new InvalidOperationException("Fallout 2 FRM relief dimensions are invalid.");
        var sourceImage = Image.LoadFromFile(sourcePngPath);
        var normalImage = Image.LoadFromFile(relief.NormalPngPath);
        var solidMask = Image.LoadFromFile(relief.SolidMaskPngPath);
        var depthImage = Image.LoadFromFile(relief.DepthPngPath);
        if (sourceImage.IsEmpty() || normalImage.IsEmpty() || solidMask.IsEmpty() ||
            depthImage.IsEmpty() ||
            sourceImage.GetWidth() != width || sourceImage.GetHeight() != height ||
            normalImage.GetWidth() != width || normalImage.GetHeight() != height ||
            solidMask.GetWidth() != width || solidMask.GetHeight() != height ||
            depthImage.GetWidth() != width || depthImage.GetHeight() != height ||
            CountSolidPixels(solidMask, width, height) != relief.SolidOpaquePixels)
            throw new InvalidOperationException("Fallout 2 FRM relief images drifted.");
        var faceMaterial = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(sourceImage),
            AlbedoColor = Colors.White,
            Roughness = sideRoughness,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = sourcePixelsOnly
                ? BaseMaterial3D.ShadingModeEnum.Unshaded
                : BaseMaterial3D.ShadingModeEnum.PerPixel,
        };
        if (!sourcePixelsOnly)
        {
            faceMaterial.NormalEnabled = true;
            faceMaterial.NormalTexture = ImageTexture.CreateFromImage(normalImage);
            faceMaterial.NormalScale = 1.0f;
        }
        StandardMaterial3D? sideMaterial = sourcePixelsOnly ? null : new StandardMaterial3D
        {
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            Roughness = sideRoughness,
            Metallic = 0.0f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled,
        };
        var faces = sourcePixelsOnly
            ? BuildSourceAlphaFaces(sourceImage, width, height, sourcePixelsPerMeter)
            : BuildFaces(
                solidMask,
                depthImage,
                width,
                height,
                sourcePixelsPerMeter,
                depthMeters,
                relief.BackDepthFraction);
        var sides = sourcePixelsOnly ? null : BuildSides(
            sourceImage,
            solidMask,
            depthImage,
            width,
            height,
            sourcePixelsPerMeter,
            depthMeters,
            relief.BackDepthFraction);
        faces.SurfaceSetMaterial(0, faceMaterial);
        if (sides is not null && sideMaterial is not null)
            sides.SurfaceSetMaterial(0, sideMaterial);
        var localOffset = new Vector3(
            sourcePixelOffset.X / sourcePixelsPerMeter,
            (-sourcePixelOffset.Y + height * Half) / sourcePixelsPerMeter,
            0.0f);
        return new Fo2FrmReliefMeshSet(
            faces,
            sides,
            faceMaterial,
            sideMaterial,
            localOffset,
            sourcePixelsOnly,
            faces.GetFaces().Length / 3,
            sides?.GetFaces().Length / 3 ?? 0);
    }

    private static ArrayMesh BuildSourceAlphaFaces(
        Image sourceImage,
        int width,
        int height,
        float pixelsPerMeter)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var pixels = 0;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (sourceImage.GetPixel(x, y).A <= 0.0f)
                    continue;
                pixels++;
                var topLeft = new Vector3(
                    (x - width * Half) / pixelsPerMeter,
                    (height * Half - y) / pixelsPerMeter,
                    0.0f);
                var topRight = topLeft + Vector3.Right / pixelsPerMeter;
                var bottomLeft = topLeft + Vector3.Down / pixelsPerMeter;
                var bottomRight = bottomLeft + Vector3.Right / pixelsPerMeter;
                var uvTopLeft = Uv(x, y, width, height);
                var uvTopRight = Uv(x + 1, y, width, height);
                var uvBottomLeft = Uv(x, y + 1, width, height);
                var uvBottomRight = Uv(x + 1, y + 1, width, height);
                AddTexturedTriangle(
                    tool,
                    topLeft,
                    topRight,
                    bottomRight,
                    uvTopLeft,
                    uvTopRight,
                    uvBottomRight,
                    Vector3.Back);
                AddTexturedTriangle(
                    tool,
                    topLeft,
                    bottomRight,
                    bottomLeft,
                    uvTopLeft,
                    uvBottomRight,
                    uvBottomLeft,
                    Vector3.Back);
            }
        if (pixels == 0)
            throw new InvalidOperationException("Fallout 2 source FRM alpha is empty.");
        tool.Index();
        return tool.Commit() ?? throw new InvalidOperationException(
            "Fallout 2 source FRM alpha mesh is empty.");
    }

    internal static Node3D Instantiate(string name, Fo2FrmReliefMeshSet meshSet)
    {
        var root = new Node3D { Name = name };
        var visual = new Node3D
        {
            Name = "SOURCE_FRM_ALPHA_ISLAND_MOLDED_VISUAL",
            Position = meshSet.LocalOffsetMeters,
        };
        root.AddChild(visual);
        visual.AddChild(new MeshInstance3D
        {
            Name = "SOURCE_FRM_OPAQUE_CELL_FRONT_BACK",
            Mesh = meshSet.Faces,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        });
        if (meshSet.Sides is not null)
            visual.AddChild(new MeshInstance3D
            {
                Name = "SOURCE_FRM_ALPHA_PERIMETER_DEPTH",
                Mesh = meshSet.Sides,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            });
        return root;
    }

    private static ArrayMesh BuildFaces(
        Image solidMask,
        Image depthImage,
        int width,
        int height,
        float pixelsPerMeter,
        float depth,
        float backDepthFraction)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var back = -depth * backDepthFraction;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (!IsSolid(solidMask, x, y, width, height))
                    continue;
                var topLeft = MoldedPoint(
                    solidMask, depthImage, x, y, width, height, pixelsPerMeter, depth);
                var topRight = MoldedPoint(
                    solidMask, depthImage, x + 1, y, width, height, pixelsPerMeter, depth);
                var bottomRight = MoldedPoint(
                    solidMask, depthImage, x + 1, y + 1, width, height,
                    pixelsPerMeter, depth);
                var bottomLeft = MoldedPoint(
                    solidMask, depthImage, x, y + 1, width, height,
                    pixelsPerMeter, depth);
                var uvTopLeft = Uv(x, y, width, height);
                var uvTopRight = Uv(x + 1, y, width, height);
                var uvBottomRight = Uv(x + 1, y + 1, width, height);
                var uvBottomLeft = Uv(x, y + 1, width, height);
                AddTexturedTriangle(
                    tool,
                    topLeft,
                    topRight,
                    bottomRight,
                    uvTopLeft,
                    uvTopRight,
                    uvBottomRight,
                    Vector3.Back);
                AddTexturedTriangle(
                    tool,
                    topLeft,
                    bottomRight,
                    bottomLeft,
                    uvTopLeft,
                    uvBottomRight,
                    uvBottomLeft,
                    Vector3.Back);
                var backTopLeft = topLeft with { Z = back };
                var backTopRight = topRight with { Z = back };
                var backBottomRight = bottomRight with { Z = back };
                var backBottomLeft = bottomLeft with { Z = back };
                AddTexturedTriangle(
                    tool,
                    backTopLeft,
                    backBottomRight,
                    backTopRight,
                    uvTopLeft,
                    uvBottomRight,
                    uvTopRight,
                    Vector3.Forward);
                AddTexturedTriangle(
                    tool,
                    backTopLeft,
                    backBottomLeft,
                    backBottomRight,
                    uvTopLeft,
                    uvBottomLeft,
                    uvBottomRight,
                    Vector3.Forward);
            }
        tool.Index();
        tool.GenerateTangents();
        return tool.Commit() ?? throw new InvalidOperationException(
            "Fallout 2 FRM molded face mesh is empty.");
    }

    private static ArrayMesh BuildSides(
        Image sourceImage,
        Image solidMask,
        Image depthImage,
        int width,
        int height,
        float pixelsPerMeter,
        float depth,
        float backDepthFraction)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        var back = -depth * backDepthFraction;
        var directions = new[]
        {
            (NeighborX: -1, NeighborY: 0, StartX: 0, StartY: 0, EndX: 0, EndY: 1),
            (NeighborX: 1, NeighborY: 0, StartX: 1, StartY: 1, EndX: 1, EndY: 0),
            (NeighborX: 0, NeighborY: -1, StartX: 1, StartY: 0, EndX: 0, EndY: 0),
            (NeighborX: 0, NeighborY: 1, StartX: 0, StartY: 1, EndX: 1, EndY: 1),
        };
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (!IsSolid(solidMask, x, y, width, height))
                    continue;
                var sourceColor = sourceImage.GetPixel(x, y);
                sourceColor.A = 1.0f;
                foreach (var direction in directions)
                {
                    if (IsSolid(
                            solidMask,
                            x + direction.NeighborX,
                            y + direction.NeighborY,
                            width,
                            height))
                        continue;
                    var frontStart = MoldedPoint(
                        solidMask,
                        depthImage,
                        x + direction.StartX,
                        y + direction.StartY,
                        width,
                        height,
                        pixelsPerMeter,
                        depth);
                    var frontEnd = MoldedPoint(
                        solidMask,
                        depthImage,
                        x + direction.EndX,
                        y + direction.EndY,
                        width,
                        height,
                        pixelsPerMeter,
                        depth);
                    var backEnd = frontEnd with { Z = back };
                    var backStart = frontStart with { Z = back };
                    AddColoredTriangle(
                        tool, frontStart, frontEnd, backEnd, sourceColor);
                    AddColoredTriangle(
                        tool, frontStart, backEnd, backStart, sourceColor);
                }
            }
        tool.Index();
        return tool.Commit() ?? throw new InvalidOperationException(
            "Fallout 2 FRM alpha-perimeter mesh is empty.");
    }

    private static Vector3 MoldedPoint(
        Image solidMask,
        Image depthImage,
        int gridX,
        int gridY,
        int width,
        int height,
        float pixelsPerMeter,
        float depth)
    {
        var sum = 0.0f;
        var count = 0;
        for (var offsetY = -1; offsetY <= 0; offsetY++)
            for (var offsetX = -1; offsetX <= 0; offsetX++)
            {
                var pixelX = gridX + offsetX;
                var pixelY = gridY + offsetY;
                if (!IsSolid(solidMask, pixelX, pixelY, width, height))
                    continue;
                sum += depthImage.GetPixel(pixelX, pixelY).R;
                count++;
            }
        if (count == 0)
            throw new InvalidOperationException(
                "Fallout 2 FRM molded vertex has no source-alpha owner.");
        var normalized = sum / count;
        var frontDepth = depth *
            (MinimumFrontDepthFraction + (1.0f - MinimumFrontDepthFraction) * normalized);
        return new Vector3(
            (gridX - width * Half) / pixelsPerMeter,
            (height * Half - gridY) / pixelsPerMeter,
            frontDepth);
    }

    private static void AddTexturedTriangle(
        SurfaceTool tool,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector2 firstUv,
        Vector2 secondUv,
        Vector2 thirdUv,
        Vector3 expectedNormal)
    {
        var normal = (second - first).Cross(third - first).Normalized();
        if (normal.Dot(expectedNormal) < 0.0f)
            normal = -normal;
        foreach (var vertex in new[]
            {
                (Point: first, Uv: firstUv),
                (Point: second, Uv: secondUv),
                (Point: third, Uv: thirdUv),
            })
        {
            tool.SetNormal(normal);
            tool.SetUV(vertex.Uv);
            tool.AddVertex(vertex.Point);
        }
    }

    private static void AddColoredTriangle(
        SurfaceTool tool,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Color color)
    {
        var normal = (second - first).Cross(third - first).Normalized();
        foreach (var point in new[] { first, second, third })
        {
            tool.SetNormal(normal);
            tool.SetColor(color);
            tool.AddVertex(point);
        }
    }

    private static int CountSolidPixels(Image image, int width, int height)
    {
        var count = 0;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (IsSolid(image, x, y, width, height))
                    count++;
        return count;
    }

    private static bool IsSolid(Image image, int x, int y, int width, int height) =>
        x >= 0 && x < width && y >= 0 && y < height &&
        image.GetPixel(x, y).R > SolidThreshold;

    private static Vector2 Uv(int x, int y, int width, int height) => new(
        (float)x / width,
        (float)y / height);
}
