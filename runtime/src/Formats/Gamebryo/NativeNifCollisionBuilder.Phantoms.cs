using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal static partial class NativeNifCollisionBuilder
{
    internal static RuntimeNativeNifCollision BuildPhantom(FalloutNifFile source,
        FalloutNifCollisionObject attachment, float unitsToMetres)
    {
        var contract = FalloutNifPhantomContract.Read(source, attachment);
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
        var phantom = (FalloutNifSimpleShapePhantom)source.ReadObject(contract.Body);
        var transform = MatrixTransform(phantom.MatrixRowMajor, unitsToMetres, "Havok shaped-phantom matrix");
        Area3D? result = null;
        try
        {
            // Construction can happen off-tree in a shared prototype. The real
            // placed-reference/contact owner admits filtering before publication.
            result = new Area3D
            {
                Name = $"NifShapePhantom{contract.Body}",
                Transform = transform,
                CollisionLayer = 0,
                CollisionMask = 0,
                Monitoring = false,
                Monitorable = false,
            };
            result.SetMeta("opennv_nif_shape_phantom", true);
            result.SetMeta("opennv_nif_phantom_source", contract.SourceIdentity);
            result.SetMeta("opennv_nif_source_sha256", contract.NifSha256);
            result.SetMeta("opennv_nif_collision_object", contract.Attachment);
            result.SetMeta("opennv_nif_collision_body", contract.Body);
            result.SetMeta("opennv_nif_phantom_target", contract.Target);
            result.SetMeta("opennv_nif_phantom_shape", contract.Shape);
            result.SetMeta("opennv_nif_collision_flags", contract.AttachmentFlags);
            result.SetMeta("opennv_collision_havok_filter", 12U);
            result.SetMeta("opennv_nif_phantom_broad_phase", contract.BroadPhase);
            result.SetMeta("opennv_nif_phantom_properties_capacity", phantom.Property.CapacityAndFlags);
            var shapes = new List<CollisionShape3D>(); var triangles = 0;
            BuildShape(source, phantom.Shape, unitsToMetres, 0, Transform3D.Identity,
                result, shapes, ref triangles, [], convexOnly: true);
            if (shapes.Count == 0 || triangles != 0)
                throw new InvalidDataException("Shaped phantom did not produce its source convex query volume.");
            return new(result, shapes.Count, 0);
        }
        catch
        {
            result?.Free();
            throw;
        }
    }

    internal static IReadOnlyList<Area3D> PlacedPhantoms(Node3D model, uint collisionMask)
    {
        if (!GodotObject.IsInstanceValid(model) || model.IsQueuedForDeletion() || collisionMask == 0)
            throw new InvalidDataException("Shaped phantom has no living placed-model/filter owner.");
        var children = model.FindChildren("*", "", true, false);
        using var childrenOwner = (Godot.Collections.Array)children;
        var result = children.OfType<Area3D>()
            .Where(area => area.HasMeta("opennv_nif_shape_phantom")).ToArray();
        foreach (var area in result)
        {
            RequirePhantom(area, model);
            GamebryoReferenceEnableRuntime.SetCollisionFilter(area, 0, collisionMask);
            area.Monitoring = true;
        }
        return result;
    }

    internal static void RequirePhantom(Area3D area, Node3D model)
    {
        if (!GodotObject.IsInstanceValid(area) || area.IsQueuedForDeletion() || !model.IsAncestorOf(area) ||
            !area.GetMeta("opennv_nif_shape_phantom", false).AsBool() ||
            area.GetMeta("opennv_collision_havok_filter", 0U).AsUInt32() != 12 ||
            area.GetMeta("opennv_nif_phantom_broad_phase", 0).AsInt32() != 2 ||
            !area.HasMeta("opennv_nif_phantom_source") || !area.HasMeta("opennv_nif_source_sha256") ||
            area.GetMeta("opennv_nif_source_sha256").AsString().Length != 64)
            throw new InvalidDataException("Placed shaped-phantom owner lost its source geometry/filter/lifetime.");
        var children = area.GetChildren();
        using var childrenOwner = (Godot.Collections.Array)children;
        var shapes = children.OfType<CollisionShape3D>().ToArray();
        if (shapes.Length == 0)
            throw new InvalidDataException("Placed shaped phantom lost its actual query shapes.");
        foreach (var child in shapes)
        {
            using var shape = child.Shape;
            if (shape is null || child.Disabled)
                throw new InvalidDataException("Placed shaped phantom lost its active query-shape resource.");
        }
        if (area.CollisionLayer != 0 || area.Monitorable)
            throw new InvalidDataException("Source shaped phantom was substituted by a physical body/contact target.");
    }
}
