using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

internal sealed record RuntimeNativeNifCollision(Node3D Body, int Shapes, int Triangles);

internal static class NativeNifCollisionBuilder
{
    // Equipped geometry follows its animation attachment. Its dropped-object
    // Havok declaration remains inspectable, but cannot publish a world pose.
    internal static void BindAnimatedAttachment(Node3D root)
    {
        foreach (var body in root.FindChildren("*", "", true, false).OfType<CollisionObject3D>())
        {
            body.CollisionLayer = body.CollisionMask = 0;
            if (body is RigidBody3D rigid) rigid.Freeze = true;
            body.SetMeta("opennv_collision_owner", "animated-equipment;world-simulation-disabled");
        }
    }

    private const float HavokToGameUnits = 7.0f;
    private const float QuaternionTolerance = 0.0001f;
    private const float MidpointFactor = 0.5f;
    private const float ParallelAxisThreshold = 0.99f;
    private const uint DefaultWorldLayer = 1;
    private const int MatrixM11 = 0;
    private const int MatrixM12 = 1;
    private const int MatrixM13 = 2;
    private const int MatrixM21 = 4;
    private const int MatrixM22 = 5;
    private const int MatrixM23 = 6;
    private const int MatrixM31 = 8;
    private const int MatrixM32 = 9;
    private const int MatrixM33 = 10;
    private const int MatrixM41 = 12;
    private const int MatrixM42 = 13;
    private const int MatrixM43 = 14;

    internal static RuntimeNativeNifCollision Build(
        FalloutNifFile source,
        FalloutNifCollisionObject attachment,
        float unitsToMetres,
        uint collisionLayer = DefaultWorldLayer)
    {
        if (source.ReadObject(attachment.Body) is not FalloutNifRigidBody body)
            throw new NotSupportedException(
                $"NIF collision object {attachment.Block.Index} does not reference a decoded rigid body.");
        if (body.Shape == -1)
            throw new InvalidDataException($"NIF rigid body {body.Block.Index} has no shape.");
        if (body.Mass < 0.0f)
            throw new InvalidDataException($"NIF rigid body {body.Block.Index} has negative mass.");

        var pinToWorld = attachment.IsBlend || body.Constraints.Length != 0;
        PhysicsBody3D result;
        if (body.Mass == 0.0f || pinToWorld || body.MotionSystem is 6 or 7)
            result = new StaticBody3D();
        else
            result = body.MotionSystem is 1 or 2 or 3 or 4 or 5 or 8 ? new RuntimeNifRigidBody { Mass = body.Mass } :
                throw new NotSupportedException($"NIF dynamic motion system {body.MotionSystem} has no physics owner.");
        result.Name = $"NifCollisionBody{body.Block.Index}";
        result.CollisionLayer = collisionLayer;
        result.CollisionMask = collisionLayer;
        result.Transform = BodyTransform(body, unitsToMetres);
        result.SetMeta("opennv_nif_collision_object", attachment.Block.Index);
        result.SetMeta("opennv_nif_collision_body", body.Block.Index);
        result.SetMeta("opennv_nif_collision_mass", body.Mass);
        result.SetMeta("opennv_nif_collision_motion_system", body.MotionSystem);
        result.SetMeta("opennv_nif_collision_constraints", body.Constraints.Length);
        result.SetMeta("opennv_nif_collision_pinned", pinToWorld);
        if (result is RigidBody3D dynamic)
        {
            dynamic.PhysicsMaterialOverride = new PhysicsMaterial { Friction = body.Friction, Bounce = body.Restitution };
            dynamic.LinearDampMode = RigidBody3D.DampMode.Replace; dynamic.LinearDamp = body.LinearDamping;
            dynamic.AngularDampMode = RigidBody3D.DampMode.Replace; dynamic.AngularDamp = body.AngularDamping;
        }

        var shapes = new List<CollisionShape3D>();
        var triangles = 0;
        BuildShape(source, body.Shape, unitsToMetres, result is StaticBody3D ? 0.0f : body.Mass, Transform3D.Identity,
            shapes, ref triangles, []);
        if (shapes.Count == 0)
            throw new InvalidDataException($"NIF rigid body {body.Block.Index} produced no collision shapes.");
        foreach (var shape in shapes)
            result.AddChild(shape);
        return new RuntimeNativeNifCollision(result, shapes.Count, triangles);
    }

    private static void BuildShape(
        FalloutNifFile source,
        int reference,
        float unitsToMetres,
        float mass,
        Transform3D localTransform,
        List<CollisionShape3D> output,
        ref int triangles,
        HashSet<int> active)
    {
        if (!active.Add(reference))
            throw new InvalidDataException($"NIF collision shape graph contains a cycle at block {reference}.");
        try
        {
            switch (source.ReadObject(reference))
            {
                case FalloutNifMoppShape mopp:
                    BuildShape(source, mopp.Child, unitsToMetres, mass, localTransform,
                        output, ref triangles, active);
                    break;
                case FalloutNifPackedShape packed:
                    if (mass != 0.0f)
                        throw new NotSupportedException(
                            $"NIF packed triangle shape {packed.Block.Index} is non-static; concave dynamics fail closed.");
                    if (source.ReadObject(packed.Data) is not FalloutNifPackedData data)
                        throw new InvalidDataException(
                            $"NIF packed triangle shape {packed.Block.Index} has invalid data.");
                    if (data.SubShapes.Length != 0 &&
                        data.SubShapes.Sum(value => checked((long)value.VertexCount)) != data.Vertices.Length)
                        throw new InvalidDataException(
                            $"NIF packed data {data.Block.Index} sub-shape vertices do not cover its vertex table.");
                    var mesh = new ArrayMesh();
                    var arrays = new Godot.Collections.Array();
                    arrays.Resize((int)Mesh.ArrayType.Max);
                    arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices.Select(vertex =>
                        ConvertScaled(vertex, packed.Scale, unitsToMetres)).ToArray();
                    arrays[(int)Mesh.ArrayType.Index] = data.Triangles.SelectMany(triangle =>
                        new[] { (int)triangle.A, (int)triangle.B, (int)triangle.C }).ToArray();
                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                    var concave = mesh.CreateTrimeshShape() as ConcavePolygonShape3D ??
                        throw new InvalidOperationException(
                            $"Godot could not create packed collision shape {packed.Block.Index}.");
                    concave.BackfaceCollision = true;
                    var materials = (data.SubShapes.Length != 0 ? data.SubShapes : packed.SubShapes).Select(value => value.Material).Distinct().ToArray();
                    var packedNode = Add(output, concave, localTransform, packed.Block.Index, materials.Length == 1 ? materials[0] : null);
                    if (materials.Length > 1)
                        packedNode.SetMeta("opennv_havok_face_materials", PackedMaterials(data, data.SubShapes.Length != 0 ? data.SubShapes : packed.SubShapes));
                    triangles += data.Triangles.Length;
                    break;
                case FalloutNifConvexVerticesShape convex:
                    if (convex.Vertices.Length < 4 || convex.Vertices.Any(vertex => vertex.W != 0.0f))
                        throw new InvalidDataException(
                            $"NIF convex shape {convex.Block.Index} has invalid homogeneous vertices.");
                    Add(output, new ConvexPolygonShape3D
                    {
                        Points = convex.Vertices.Select(vertex => Convert(vertex, unitsToMetres)).ToArray(),
                        Margin = convex.Radius * HavokToGameUnits * unitsToMetres,
                    }, localTransform, convex.Block.Index, convex.Material);
                    break;
                case FalloutNifBoxShape box:
                    RequirePositive(box.Dimensions, box.Block.Index, "box dimensions");
                    Add(output, new BoxShape3D
                    {
                        Size = ConvertAbsolute(box.Dimensions) * (2.0f * HavokToGameUnits * unitsToMetres),
                        Margin = box.Radius * HavokToGameUnits * unitsToMetres,
                    }, localTransform, box.Block.Index, box.Material);
                    break;
                case FalloutNifSphereShape sphere:
                    RequirePositive(sphere.Radius, sphere.Block.Index, "sphere radius");
                    Add(output, new SphereShape3D
                    {
                        Radius = sphere.Radius * HavokToGameUnits * unitsToMetres,
                    }, localTransform, sphere.Block.Index, sphere.Material);
                    break;
                case FalloutNifCapsuleShape capsule:
                    RequirePositive(capsule.FirstRadius, capsule.Block.Index, "capsule first radius");
                    if (MathF.Abs(capsule.FirstRadius - capsule.SecondRadius) > float.Epsilon)
                        throw new NotSupportedException(
                            $"NIF capsule shape {capsule.Block.Index} has unequal endpoint radii.");
                    var first = Convert(capsule.First, unitsToMetres);
                    var second = Convert(capsule.Second, unitsToMetres);
                    var axis = second - first;
                    if (axis.LengthSquared() <= 0.0f)
                        throw new InvalidDataException($"NIF capsule shape {capsule.Block.Index} has coincident endpoints.");
                    var radius = capsule.FirstRadius * HavokToGameUnits * unitsToMetres;
                    var capsuleTransform = new Transform3D(
                        new Basis(Quaternion.FromEuler(Vector3.Zero)),
                        (first + second) * MidpointFactor);
                    capsuleTransform.Basis = BasisLookingAlongY(axis.Normalized());
                    Add(output, new CapsuleShape3D
                    {
                        Radius = radius,
                        Height = axis.Length() + 2.0f * radius,
                    }, localTransform * capsuleTransform, capsule.Block.Index, capsule.Material);
                    break;
                case FalloutNifListShape list:
                    if (list.Children.Length == 0)
                        throw new InvalidDataException($"NIF list shape {list.Block.Index} is empty.");
                    foreach (var child in list.Children)
                        BuildShape(source, child, unitsToMetres, mass, localTransform,
                            output, ref triangles, active);
                    break;
                case FalloutNifConvexTransformShape transformed:
                    BuildShape(source, transformed.Child, unitsToMetres, mass,
                        localTransform * MatrixTransform(transformed, unitsToMetres),
                        output, ref triangles, active);
                    break;
                default:
                    throw new NotSupportedException(
                        $"NIF collision shape block {reference} type {source.Blocks[reference].TypeName} is unsupported.");
            }
        }
        finally
        {
            active.Remove(reference);
        }
    }

    private static CollisionShape3D Add(
        ICollection<CollisionShape3D> output, Shape3D shape, Transform3D transform, int blockIndex, uint? material = null)
    {
        var node = new CollisionShape3D
        {
            Name = $"NifCollisionShape{blockIndex}",
            Shape = shape,
            Transform = transform,
        };
        if (material is { } value) node.SetMeta("opennv_havok_material", value);
        output.Add(node);
        return node;
    }

    internal static int[] PackedMaterials(FalloutNifPackedData data, IReadOnlyList<FalloutNifSubShape> parts)
    {
        var vertices = new int[data.Vertices.Length];
        var offset = 0;
        foreach (var part in parts)
        {
            var count = checked((int)part.VertexCount);
            if (count > vertices.Length - offset) throw new InvalidDataException("Packed material ranges exceed the source vertices.");
            Array.Fill(vertices, checked((int)part.Material), offset, count);
            offset += count;
        }
        if (offset != vertices.Length) throw new InvalidDataException("Packed material ranges do not cover the source vertices.");
        // Preserve source triangle order: the physics ray's face index addresses
        // this same table. A cross-material triangle remains explicitly unknown.
        return data.Triangles.Select(t => vertices[t.A] == vertices[t.B] && vertices[t.A] == vertices[t.C] ? vertices[t.A] : -1).ToArray();
    }

    internal static uint HitMaterial(Godot.Collections.Dictionary collision)
    {
        if (collision["collider"].AsGodotObject() is not CollisionObject3D body)
            throw new NotSupportedException("Hit collider has no source material owner.");
        var shape = body.ShapeOwnerGetOwner(body.ShapeFindOwner(collision["shape"].AsInt32())) as CollisionShape3D;
        if (shape?.HasMeta("opennv_havok_material") == true)
            return shape.GetMeta("opennv_havok_material").AsUInt32();
        if (shape?.HasMeta("opennv_havok_face_materials") == true && collision.TryGetValue("face_index", out var face))
        {
            var materials = shape.GetMeta("opennv_havok_face_materials").AsInt32Array();
            var index = face.AsInt32();
            if (index >= 0 && index < materials.Length && materials[index] >= 0) return (uint)materials[index];
            throw new NotSupportedException($"Hit triangle {index} has no unique source Havok material.");
        }
        throw new NotSupportedException("Hit shape has no source Havok material binding.");
    }

    private static Transform3D BodyTransform(FalloutNifRigidBody body, float unitsToMetres)
    {
        if (body.Block.TypeName == "bhkRigidBody")
            return Transform3D.Identity;
        var quaternion = new Quaternion(body.Rotation.X, body.Rotation.Y, body.Rotation.Z, body.Rotation.W);
        if (MathF.Abs(quaternion.LengthSquared() - 1.0f) > QuaternionTolerance)
            throw new InvalidDataException($"NIF rigid body T {body.Block.Index} quaternion is not normalized.");
        var source = new Basis(quaternion.Normalized());
        var rowMajor = new[]
        {
            source.X.X, source.Y.X, source.Z.X,
            source.X.Y, source.Y.Y, source.Z.Y,
            source.X.Z, source.Y.Z, source.Z.Z,
        };
        return new Transform3D(
            GamebryoCoordinate.ConvertBasis(rowMajor, 1.0f, "Havok rigid-body rotation"),
            Convert(body.Translation, unitsToMetres));
    }

    private static Transform3D MatrixTransform(FalloutNifConvexTransformShape shape, float unitsToMetres)
    {
        var value = shape.MatrixRowMajor;
        var rotation = new[]
        {
            value[MatrixM11], value[MatrixM12], value[MatrixM13],
            value[MatrixM21], value[MatrixM22], value[MatrixM23],
            value[MatrixM31], value[MatrixM32], value[MatrixM33],
        };
        return new Transform3D(
            GamebryoCoordinate.ConvertBasis(rotation, 1.0f, "Havok convex-transform matrix"),
            GamebryoCoordinate.ConvertVector(new Vector3(
                value[MatrixM41], value[MatrixM42], value[MatrixM43])) *
                (HavokToGameUnits * unitsToMetres));
    }

    private static Basis BasisLookingAlongY(Vector3 y)
    {
        var helper = MathF.Abs(y.Dot(Vector3.Up)) < ParallelAxisThreshold
            ? Vector3.Up
            : Vector3.Right;
        var x = helper.Cross(y).Normalized();
        return new Basis(x, y, x.Cross(y).Normalized());
    }

    private static Vector3 Convert(FalloutNifVector3 value, float unitsToMetres) =>
        GamebryoCoordinate.ConvertVector(new Vector3(value.X, value.Y, value.Z)) *
        (HavokToGameUnits * unitsToMetres);
    private static Vector3 Convert(FalloutNifVector4 value, float unitsToMetres) =>
        GamebryoCoordinate.ConvertVector(new Vector3(value.X, value.Y, value.Z)) *
        (HavokToGameUnits * unitsToMetres);
    private static Vector3 ConvertScaled(
        FalloutNifVector3 value, FalloutNifVector3 scale, float unitsToMetres) =>
        Convert(new FalloutNifVector3(value.X * scale.X, value.Y * scale.Y, value.Z * scale.Z),
            unitsToMetres);
    private static Vector3 ConvertAbsolute(FalloutNifVector3 value) =>
        GamebryoCoordinate.ConvertVector(new Vector3(
            MathF.Abs(value.X), MathF.Abs(value.Y), MathF.Abs(value.Z))).Abs();

    private static void RequirePositive(FalloutNifVector3 value, int blockIndex, string label)
    {
        if (value.X <= 0.0f || value.Y <= 0.0f || value.Z <= 0.0f)
            throw new InvalidDataException($"NIF shape {blockIndex} {label} are not positive.");
    }

    private static void RequirePositive(float value, int blockIndex, string label)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
            throw new InvalidDataException($"NIF shape {blockIndex} {label} is not positive.");
    }
}
