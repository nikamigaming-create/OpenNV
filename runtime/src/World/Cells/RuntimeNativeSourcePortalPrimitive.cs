using Godot;
using OpenNV.Runtime.Content;
using V3 = System.Numerics.Vector3;

namespace OpenNV.Runtime.World.Cells;

// This is the living source room-bound or portal rectangle. It has no editor
// art or physical substitute; its required consumer is the actual graph draw.
internal sealed partial class RuntimeNativeSourcePortalPrimitive : Node3D
{
    internal FalloutDynamicPrimitive Source { get; private set; } = null!;
    private FalloutReferenceWorld _world = null!;
    private float _units;
    private RuntimeNativeRoomPortalGraph? _graph;

    internal static RuntimeNativeSourcePortalPrimitive Create(FalloutDynamicPrimitive source,
        FalloutReferenceWorld world, Transform3D transform, float units)
    {
        if (source.Role is not (FalloutDynamicPrimitiveRole.Room or FalloutDynamicPrimitiveRole.Portal) ||
            source.Role == FalloutDynamicPrimitiveRole.Portal && source.Primitive.Type != 3 ||
            source.Role == FalloutDynamicPrimitiveRole.Room && source.Primitive.Type is not (1 or 2) ||
            !float.IsFinite(units) || units <= 0 || !transform.Origin.IsFinite() || transform.Basis.Determinant() <= 0 ||
            !transform.Basis.X.IsFinite() || !transform.Basis.Y.IsFinite() || !transform.Basis.Z.IsFinite() ||
            world.Get(source.Reference).Base != source.Base)
            throw new InvalidDataException("Source room/portal native factory lacks its actual winning primitive/placement.");
        var result = new RuntimeNativeSourcePortalPrimitive
        { Name = $"Reference_{source.Reference}", Source = source, Transform = transform, _world = world, _units = units };
        result.SetMeta("opennv_reference_form_key", source.Reference.ToString());
        result.SetMeta("opennv_source_primitive_role", source.Role.ToString());
        result.SetMeta("opennv_source_primitive_sha256", source.SourceSha256);
        return result;
    }
    internal void Bind(RuntimeNativeRoomPortalGraph graph)
    {
        if (_graph is not null || GetParent() != graph.GetParent())
            throw new InvalidOperationException("Source room/portal already belongs to another native CELL graph.");
        _graph = graph;
    }
    internal void RequirePublished()
    {
        RequireNative();
        (_graph ?? throw new NotSupportedException("Source room/portal has no actual current camera consumer.")).RequirePublished(this);
    }
    internal FalloutRoomGeometry Room()
    {
        RequireNative();
        if (Source.Role != FalloutDynamicPrimitiveRole.Room) throw new InvalidOperationException("Portal is not a room bound.");
        var transform = GlobalTransform; var scale = transform.Basis.Scale;
        var half = new V3(Source.Primitive.X * scale.X, Source.Primitive.Z * scale.Y, Source.Primitive.Y * scale.Z) * _units;
        var bound = new FalloutVisibilityBound(N(transform.Origin), N(transform.Basis.X.Normalized()),
            N(transform.Basis.Y.Normalized()), N(transform.Basis.Z.Normalized()), half, Source.Primitive.Type == 2);
        bound.Validate(); return new(Source.Reference, _world.IsEnabled(Source.Reference), bound);
    }
    internal FalloutPortalGeometry Portal()
    {
        RequireNative();
        if (Source.Role != FalloutDynamicPrimitiveRole.Portal) throw new InvalidOperationException("Room is not a portal rectangle.");
        var transform = GlobalTransform;
        var x = Source.Primitive.X * _units; var z = Source.Primitive.Z * _units;
        V3[] corners = [N(transform * new Vector3(-x, -z, 0)), N(transform * new Vector3(x, -z, 0)),
            N(transform * new Vector3(x, z, 0)), N(transform * new Vector3(-x, z, 0))];
        return new(Source.Reference, _world.IsEnabled(Source.Reference), Array.AsReadOnly(corners));
    }
    internal void RequireNative()
    {
        if (!GodotObject.IsInstanceValid(this) || IsQueuedForDeletion() || !IsInsideTree() || GetParent() is not Node3D ||
            _world.Get(Source.Reference).Base != Source.Base || _world.Placement(Source.Reference).Cell != Source.Cell ||
            Visible != _world.IsEnabled(Source.Reference))
            throw new NotSupportedException("Source room/portal lost its actual reference/CELL/native lifetime.");
    }
    private static V3 N(Vector3 value) => new(value.X, value.Y, value.Z);
}
