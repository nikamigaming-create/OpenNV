using System.Numerics;
using OpenNV.Runtime.Formats.FaceGen;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed class FalloutNpcFaceMorph(FalloutNpcAppearance appearance, FalloutTriFile tri,
    FalloutEgmFile? egm, string? selectedShape)
{
    private IReadOnlyDictionary<string, Vector3[]>? _deltas;

    internal static FalloutNpcFaceMorph? Resolve(RuntimeLiveContentSource content, FalloutNpcAppearance appearance,
        FalloutNpcAppearancePart part, FalloutEgmFile? egm, string? selectedShape)
    {
        var model = part.ModelPath ?? throw new InvalidDataException("FaceGen TRI requires a source model.");
        var path = selectedShape is null ? Path.ChangeExtension(model, ".tri") : model[..^4] + selectedShape.ToLowerInvariant() + ".tri";
        return content.TryRead(path, null, out var bytes, out _) ? new(appearance, FalloutTriFile.Read(bytes), egm, selectedShape) : null;
    }

    internal IReadOnlyDictionary<string, Vector3[]> Build(FalloutNifFile source, FalloutNifGeometry geometry, FalloutNifMeshData mesh)
    {
        if (_deltas is not null) return _deltas;
        var geometries = source.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips")
            .Where(block => selectedShape is null || source.ReadGeometry(block.Index).Name.Equals(selectedShape, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (geometries.Length != 1 || geometries[0].Index != geometry.Block.Index || tri.VertexCount != mesh.Vertices.Length)
            throw new NotSupportedException("FaceGen TRI requires a unique source geometry and matching vertex order.");
        // NIF base vertices and TRI's statistical suffix use the same EGM basis.
        // Transform both before subtracting a statistical target from its base.
        var combined = tri.Vertices.ToArray();
        var original = source.ReadMeshData(geometry.Data);
        for (var index = 0; index < tri.VertexCount; index++)
            combined[index] = new(original.Vertices[index].X, original.Vertices[index].Y, original.Vertices[index].Z);
        var shaped = egm is null ? combined : egm.EvaluatePositions(combined,
            FalloutFaceGenCoefficients.AddSourceGeometry(appearance.FaceGen.SymmetricGeometry, appearance.RaceFaceGen.SymmetricGeometry, egm.SymmetricModes.Count),
            FalloutFaceGenCoefficients.AddSourceGeometry(appearance.FaceGen.AsymmetricGeometry, appearance.RaceFaceGen.AsymmetricGeometry, egm.AsymmetricModes.Count));
        _deltas = tri.BuildDeltas(shaped);
        return _deltas;
    }
}
