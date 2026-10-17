using Godot;
using OpenNV.Runtime.Content;
using System.Security.Cryptography;
using System.Text;

namespace OpenNV.Runtime.World.Cells;

internal sealed record NavigationDoorAssociation(FalloutFormKey Mesh, FalloutNavigationDoor Source, int TriangleCount)
{
    internal bool Resolvable => Source.Triangle < TriangleCount;
    internal string? Error => Resolvable ? null :
        $"NAVM {Mesh} door {Source.Door} triangle {Source.Triangle} exceeds its {TriangleCount} triangles.";
}

internal sealed partial class CellNavigationGraph
{
    internal IReadOnlyList<NavigationDoorAssociation> DoorAssociations { get; private set; } = [];
    internal IReadOnlyList<string> DoorAssociationErrors { get; private set; } = [];

    internal void RequireDoorAssociations(FalloutFormKey door)
    {
        foreach (var association in DoorAssociations.Where(value => value.Source.Door == door))
            if (association.Error is { } error) throw new InvalidDataException(error);
    }

    internal static CellNavigationGraph LoadOwned(FalloutPluginStack stack, FalloutFormKey cell)
        => LoadOwned(stack, new HashSet<FalloutFormKey> { cell });

    internal static CellNavigationGraph LoadOwned(FalloutPluginStack stack, IReadOnlySet<FalloutFormKey> cells,
        Action<FalloutFormKey, Exception>? unavailable = null)
    {
        var sources = FalloutNavigationMesh.ReadCells(stack, cells, unavailable);
        var meshes = sources.Select(source => new NavigationMeshRecord(source.Form.ToString(), source.Cell.ToString(), source.Version,
            source.Vertices.Select(value => new Vector3(value.X, value.Y, value.Z)).ToArray(),
            source.Triangles.Select(value => new NavigationTriangle(value.Vertices.Select(index => (int)index).ToArray(),
                value.Edges.Select(index => (int)index).ToArray(), value.Flags)).ToArray(),
            source.Edges.Select(value => new NavigationExternalConnection(value.Mesh.ToString(), value.Triangle)).ToArray())).ToArray();
        foreach (var mesh in meshes) mesh.ValidateAdjacency();
        var graph = new CellNavigationGraph(meshes);
        graph.DoorAssociations = sources.SelectMany(source => source.Doors.Select(door =>
            new NavigationDoorAssociation(source.Form, door, source.Triangles.Length))).ToArray();
        graph.DoorAssociationErrors = graph.DoorAssociations.Where(value => !value.Resolvable)
            .Select(value => value.Error!).ToArray();
        var sourceIdentity = string.Join("\n", sources.OrderBy(value => value.Form.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(source => source.Form + ":" + Convert.ToHexString(SHA256.HashData(stack.GetEffective(source.Form).ReadData()))));
        graph.SourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceIdentity)));
        return graph;
    }
}
