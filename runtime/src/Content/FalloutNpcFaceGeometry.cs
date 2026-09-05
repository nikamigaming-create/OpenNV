using System.Numerics;
using OpenNV.Runtime.Formats.FaceGen;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed class FalloutNpcFaceGeometry
{
    private readonly FalloutEgmFile _egm;
    private readonly float[] _symmetric;
    private readonly float[] _asymmetric;
    private readonly string? _selectedShape;

    // The resource owner supplies the resolved EGM. Missing companion resources
    // and the eligibility of hair/equipment are not guessed here.
    internal FalloutNpcFaceGeometry(FalloutNpcAppearance appearance, FalloutNpcAppearancePart part,
        ReadOnlyMemory<byte> ownedEgm)
        : this(appearance, part, FalloutEgmFile.Read(ownedEgm))
    {
    }

    internal FalloutNpcFaceGeometry(FalloutNpcAppearance appearance, FalloutNpcAppearancePart part,
        FalloutEgmFile ownedEgm, string? selectedShape = null)
    {
        SourcePart = part;
        _egm = ownedEgm;
        _selectedShape = selectedShape;
        _symmetric = FalloutFaceGenCoefficients.AddSourceGeometry(appearance.FaceGen.SymmetricGeometry,
            appearance.RaceFaceGen.SymmetricGeometry, _egm.SymmetricModes.Count);
        _asymmetric = FalloutFaceGenCoefficients.AddSourceGeometry(appearance.FaceGen.AsymmetricGeometry,
            appearance.RaceFaceGen.AsymmetricGeometry, _egm.AsymmetricModes.Count);
    }

    internal FalloutNpcAppearancePart SourcePart { get; }
    internal uint GeometryBasisVersion => _egm.BasisVersion;

    internal FalloutNifMeshData Apply(FalloutNifFile source, FalloutNifGeometry geometry, FalloutNifMeshData mesh)
    {
        var geometries = source.Blocks.Where(block => block.TypeName is "NiTriShape" or "NiTriStrips")
            .Where(block => _selectedShape is null || source.ReadGeometry(block.Index).Name.Equals(_selectedShape, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (geometries.Length != 1 || geometries[0].Index != geometry.Block.Index || geometry.Data != mesh.Block.Index)
            throw new NotSupportedException("FaceGen EGM mapping across multiple source geometry blocks requires a source vertex-order owner.");
        var positions = _egm.EvaluateSourcePrefixPositions(
            mesh.Vertices.Select(value => new Vector3(value.X, value.Y, value.Z)).ToArray(), _symmetric, _asymmetric);
        // The observed native base FaceGen pass retains source normal/tangent
        // bytes. Expressions and runtime morph controllers remain separate.
        return mesh with { Vertices = positions.Select(value => new FalloutNifVector3(value.X, value.Y, value.Z)).ToArray() };
    }
}
