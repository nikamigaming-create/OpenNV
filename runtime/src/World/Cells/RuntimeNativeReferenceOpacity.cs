using Godot;
using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.World.Cells;

// Explicit reference/BSFadeNode opacity is independent of shader decal LOD.
// In particular shader NoFade does not exempt a geometry from this owner.
internal static class RuntimeNativeReferenceOpacity
{
    internal static GeometryInstance3D[] Collect(Node3D root) => NodeTraversal.SelfAndDescendants<Node3D>(root)
        .Where(child => child.GetMeta("opennv_nif_fade_node", false).AsBool())
        .SelectMany(NodeTraversal.SelfAndDescendants<GeometryInstance3D>).Distinct().ToArray();

    internal static void Publish(IEnumerable<GeometryInstance3D> geometry, float opacity)
    {
        if (!float.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new InvalidDataException("Reference opacity must be finite and normalized.");
        foreach (var instance in geometry) instance.Transparency = 1 - opacity;
    }
}
