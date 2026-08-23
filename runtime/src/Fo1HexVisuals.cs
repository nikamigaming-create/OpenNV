using Godot;

namespace OpenNV.Runtime;

internal static class Fo1HexVisuals
{
    internal static ArrayMesh BuildRingMesh(float innerScale = 0.82f, float outerScale = 0.96f)
    {
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (var index = 0; index < 6; index++)
        {
            var next = (index + 1) % 6;
            var outerFirst = Corner(index, outerScale);
            var outerSecond = Corner(next, outerScale);
            var innerFirst = Corner(index, innerScale);
            var innerSecond = Corner(next, innerScale);
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

    private static Vector3 Corner(int index, float scale)
    {
        var angle = Mathf.DegToRad(60.0f * index - 30.0f);
        var radius = Fo1HexMath.CircumradiusMeters * scale;
        return new Vector3(MathF.Cos(angle) * radius, 0.0f, MathF.Sin(angle) * radius);
    }

    private static void Add(SurfaceTool tool, Vector3 vertex)
    {
        tool.SetNormal(Vector3.Up);
        tool.AddVertex(vertex);
    }
}
