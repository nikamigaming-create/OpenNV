using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// A living source rectangle, without editor art or physical thickness. Its
// actual consumer is the shared C# CELL graph publishing native render RIDs.
internal sealed partial class RuntimeNativeSourceOcclusionPlane : Node3D
{
    private FalloutDynamicPrimitive _source = null!;
    private FalloutReferenceWorld _world = null!;

    internal static RuntimeNativeSourceOcclusionPlane Create(FalloutDynamicPrimitive source,
        FalloutReferenceWorld world, Transform3D transform, float unitsToMetres)
    {
        if (source.Role != FalloutDynamicPrimitiveRole.OcclusionPlane || source.Primitive.Type != 3 ||
            !float.IsFinite(unitsToMetres) || unitsToMetres <= 0 || !transform.Origin.IsFinite() ||
            !transform.Basis.X.IsFinite() || !transform.Basis.Y.IsFinite() || !transform.Basis.Z.IsFinite() ||
            transform.Basis.Determinant() <= 0 || world.Get(source.Reference).Base != source.Base ||
            world.Placement(source.Reference).Cell != source.Cell)
            throw new InvalidDataException("Native occlusion plane has no valid selected source/placement.");
        if (source.SerializedPlane is { } serialized) serialized.Validate();
        else if (!float.IsFinite(source.Primitive.X) || !float.IsFinite(source.Primitive.Z) ||
            source.Primitive.X <= 0 || source.Primitive.Z <= 0)
            throw new InvalidDataException("Native source plane rectangle cannot be represented.");
        RuntimeNativeSourceOcclusionPlane? result = null;
        try
        {
            result = new RuntimeNativeSourceOcclusionPlane
            {
                Name = $"Reference_{source.Reference}",
                Transform = transform,
                _source = source,
                _world = world,
                _units = unitsToMetres,
                _birthReferenceTransform = transform
            };
            result.SetMeta("opennv_reference_form_key", source.Reference.ToString());
            result.SetMeta("opennv_source_primitive_role", "occlusion-plane");
            result.SetMeta("opennv_source_primitive_sha256", source.SourceSha256);
            return result;
        }
        catch (Exception original)
        {
            try { result?.Free(); }
            catch (Exception error) { throw new AggregateException("Source plane construction and retirement both failed.", original, error); }
            throw;
        }
    }

    public override void _ExitTree() => _graph = null;

    internal void RequirePublished()
    {
        RequireNative();
        (_graph ?? throw new NotSupportedException("Source occlusion rectangle has no actual CELL camera consumer."))
            .RequirePublished(this);
    }
}
