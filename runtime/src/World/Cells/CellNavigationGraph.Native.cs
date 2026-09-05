using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class CellNavigationGraph
{
    internal static CellNavigationGraph LoadOwned(FalloutPluginStack stack, FalloutFormKey cell)
    {
        var sources = FalloutNavigationMesh.ReadCell(stack, cell);
        var meshes = sources.Select(source => new NavigationMeshRecord(source.Form.ToString(), source.Cell.ToString(), source.Version,
            source.Vertices.Select(value => new Vector3(value.X, value.Y, value.Z)).ToArray(),
            source.Triangles.Select(value => new NavigationTriangle(value.Vertices.Select(index => (int)index).ToArray(),
                value.Edges.Select(index => (int)index).ToArray(), value.Flags)).ToArray(),
            source.Edges.Select(value => new NavigationExternalConnection(value.Mesh.ToString(), value.Triangle)).ToArray())).ToArray();
        foreach (var mesh in meshes) mesh.ValidateAdjacency();
        return new(meshes);
    }
}
