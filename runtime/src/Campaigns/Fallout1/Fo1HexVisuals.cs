using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1HexVisualsNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float GeometryFloat0Point82f = 0.82f;
    internal const float GeometryFloat0Point96f = 0.96f;
}

internal static class Fo1HexVisuals
{
    internal static ArrayMesh BuildRingMesh(float innerScale = Fo1HexVisualsNumericContracts.GeometryFloat0Point82f, float outerScale = Fo1HexVisualsNumericContracts.GeometryFloat0Point96f)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (var index = 0; index < Fo1HexMath.DirectionCount; index++)
        {
            var next = (index + 1) % Fo1HexMath.DirectionCount;
            var outerFirst = Fo1HexMath.CornerOffset(index, outerScale);
            var outerSecond = Fo1HexMath.CornerOffset(next, outerScale);
            var innerFirst = Fo1HexMath.CornerOffset(index, innerScale);
            var innerSecond = Fo1HexMath.CornerOffset(next, innerScale);
            Add(tool, outerFirst);
            Add(tool, outerSecond);
            Add(tool, innerSecond);
            Add(tool, outerFirst);
            Add(tool, innerSecond);
            Add(tool, innerFirst);
        }
        return tool.Commit() ?? throw new InvalidOperationException("Could not build Fallout hex ring mesh.");
    }

    internal static StandardMaterial3D Material(Color color, bool transparent = false) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = transparent
            ? BaseMaterial3D.TransparencyEnum.Alpha
            : BaseMaterial3D.TransparencyEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = false,
    };

    private static void Add(SurfaceTool tool, Vector3 vertex)
    {
        tool.SetNormal(Vector3.Up);
        tool.AddVertex(vertex);
    }
}
