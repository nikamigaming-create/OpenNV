using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record RuntimeNativeLivingBodyShape(Shape3D Shape, Transform3D Transform);

internal static partial class NativeNifCollisionBuilder
{
    // Reuse the complete source shape factory without its static/animated body
    // policy. The temporary container never enters a scene or physics world;
    // the caller owns the returned real shape resources and constructs the
    // independent living body in PhysicsServer3D.
    internal static IReadOnlyList<RuntimeNativeLivingBodyShape> BuildLivingBodyShapes(
        FalloutNifFile source, FalloutNifRigidBody body, float unitsToMetres, float physicalToSourceUnits)
    {
        var container = new StaticBody3D();
        var shapes = new List<CollisionShape3D>();
        var triangles = 0;
        try
        {
            BuildShape(source, body.Shape, unitsToMetres, body.Mass, Transform3D.Identity,
                container, shapes, ref triangles, [], physicalToSourceUnits: physicalToSourceUnits);
            if (shapes.Count == 0) throw new InvalidDataException("Living body has no actual source shapes.");
            return shapes.Select(shape => new RuntimeNativeLivingBodyShape(shape.Shape ??
                throw new InvalidDataException("Living shape factory lost a physical resource."), shape.Transform)).ToArray();
        }
        catch
        {
            foreach (var shape in shapes) shape.Shape?.Dispose();
            throw;
        }
        finally { container.Free(); }
    }
}
