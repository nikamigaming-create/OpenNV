using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

// Retained by the actual mesh factory. Native node metadata is unnecessary for
// associating a renderer target with its original geometry/property identity.
internal sealed record RuntimeNativeNifGeometryMaterial(string SourceSha256, int GeometryBlock, Material Material);

internal static partial class RuntimeNativeNifMeshBuilder
{
    private sealed partial class BuildState
    {
        private readonly List<RuntimeNativeNifGeometryMaterial> _geometryMaterials = [];
        private Material RetainGeometryMaterial(FalloutNifGeometry geometry, Material material)
        {
            _geometryMaterials.Add(new(_source.Sha256, geometry.Block.Index, material));
            return material;
        }
        internal IReadOnlyList<RuntimeNativeNifGeometryMaterial> GeometryMaterials => Array.AsReadOnly(_geometryMaterials.ToArray());
    }
}
