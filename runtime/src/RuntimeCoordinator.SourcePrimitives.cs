using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private bool TryPlaceNativeSourcePrimitive(Node3D root, FalloutPlacedReference reference)
    {
        var world = _nativeReferences ?? throw new InvalidOperationException("Source primitive lost its campaign reference lifetime.");
        var records = _nativePluginStack ?? throw new InvalidOperationException("Source primitive lost its selected record lifetime.");
        var engine = world.CampaignPlayerRuntimeSource.Receipt.EngineSha256;
        if (!FalloutDynamicPrimitiveSource.Supports(engine)) return false;
        var source = FalloutDynamicPrimitiveSource.Read(engine);
        if (source.Role(records, reference.Base) == FalloutDynamicPrimitiveRole.None) return false;
        var primitive = FalloutDynamicPrimitive.Read(source, records, reference);
        Node3D node = primitive.Role switch
        {
            FalloutDynamicPrimitiveRole.OcclusionPlane => RuntimeNativeSourceOcclusionPlane.Create(primitive, world,
                ReferenceTransform(reference), _configuration.World.GameUnitsToMeters),
            FalloutDynamicPrimitiveRole.Room or FalloutDynamicPrimitiveRole.Portal => RuntimeNativeSourcePortalPrimitive.Create(
                primitive, world, ReferenceTransform(reference), _configuration.World.GameUnitsToMeters),
            _ => throw new NotSupportedException("Source primitive has no actual native factory consumer.")
        };
        try
        {
            root.AddChild(node);
            if (node.GetParent() != root)
                throw new InvalidDataException("Actual source primitive did not join the owning CELL root.");
        }
        catch { node.Free(); throw; }
        return true;
    }
}
