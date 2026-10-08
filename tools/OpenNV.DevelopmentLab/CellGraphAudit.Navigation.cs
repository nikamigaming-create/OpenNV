using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class CellGraphAudit
{
    // Both declaration inventory and current-state projection feed the same
    // actual structural owner. This does not activate any source NAVM.
    private static void ValidateNavigationDeclarations(IReadOnlyList<FalloutNavigationMesh> meshes,
        IReadOnlySet<string> acceptedCells)
    {
        var sourceGraph = JsonSerializer.SerializeToElement(new
        {
            schema = "opennv-owned-cell-navigation/v1",
            navmeshes = meshes.Select(mesh => new
            {
                formId = mesh.Form.ToString(), cellFormId = mesh.Cell.ToString(), version = mesh.Version,
                verticesGameUnits = mesh.Vertices.Select(value => new[] { value.X, value.Y, value.Z }),
                triangles = mesh.Triangles.Select(value => new { vertexIndices = value.Vertices, adjacentTriangles = value.Edges, flags = value.Flags }),
                externalConnections = mesh.Edges.Select(value => new { navmeshFormId = value.Mesh.ToString(), triangleIndex = value.Triangle })
            })
        }, Json);
        _ = CellNavigationGraph.Load(sourceGraph, acceptedCells);
    }
}
