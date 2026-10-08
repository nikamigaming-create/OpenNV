using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

internal static partial class CellGraphAudit
{
    internal static CollisionMath ReadPlacedCollision(FalloutNifFile source, float units)
    {
        if (!float.IsFinite(units) || units <= 0) throw new ArgumentException("Collision source units must be finite and positive.");
        var row = new ResourceRow(); var triangles = new List<Triangle>();
        if (source.Roots.Count != 1)
            row.Failures.Add(new { lane = "placed-root-admission", error = "Placed multi-root NIF ownership is unbound." });
        else
            WalkVisual(source, source.Roots[0], Transform3D.Identity, units, triangles, row, [], placedRoot: true);
        return new(triangles, row.CollisionShapes, row.CollisionTriangles, row.Failures);
    }

    private static string Canonical(string path) => FalloutBsaArchive.CanonicalPath(path).Replace('\\', '/');
    private static IEnumerable<string> Textures(FalloutNifObject obj) => obj switch
    {
        FalloutNifShaderTextureSet set => set.Textures.Where(path => !string.IsNullOrEmpty(path)),
        FalloutNifNoLightingProperty effect when effect.FileName.Length != 0 => [effect.FileName],
        FalloutNifTileShaderProperty tile when tile.FileName.Length != 0 => [tile.FileName],
        FalloutNifSkyShaderProperty sky when sky.FileName.Length != 0 => [sky.FileName],
        FalloutNifSourceTexture texture when texture.FileName.Length != 0 => [texture.FileName],
        _ => []
    };
    internal static Transform3D ReferenceTransform(float[] position, float[] rotation, float scale, float units) => new(
        GamebryoCoordinate.ConvertReferenceEuler(new(rotation[0], rotation[1], rotation[2]), scale),
        GamebryoCoordinate.ConvertVector(new(position[0], position[1], position[2])) * units);
    private static Transform3D LocalTransform(FalloutNifTransform source, float units) => new(
        GamebryoCoordinate.ConvertBasis(source.RotationRowMajor, source.Scale, "source diagnostic local transform"),
        GamebryoCoordinate.ConvertVector(new(source.Translation.X, source.Translation.Y, source.Translation.Z)) * units);

    private static void WalkVisual(FalloutNifFile source, int index, Transform3D parent, float units,
        List<Triangle> triangles, ResourceRow row, HashSet<int> active, bool placedRoot = false)
    {
        if (!active.Add(index)) throw new InvalidDataException("Visual graph cycle at " + index);
        try
        {
            var obj = source.ReadObject(index);
            var transform = obj switch { FalloutNifNode node => node.Transform, FalloutNifGeometry geometry => geometry.Transform, _ => null };
            if (transform is null) return;
            var pose = parent * (placedRoot ? Transform3D.Identity : LocalTransform(transform, units));
            var attachment = obj switch { FalloutNifNode node => node.CollisionObject, FalloutNifGeometry geometry => geometry.CollisionObject, _ => -1 };
            if (attachment != -1)
            {
                try
                {
                    if (source.ReadObject(attachment) is not FalloutNifCollisionObject collision || collision.Target != index ||
                        (collision.Flags & ~109) != 0 || source.ReadObject(collision.Body) is not FalloutNifRigidBody body)
                        throw new NotSupportedException("Native visual collision attachment is not admitted.");
                    var bodyPose = NativeNifCollisionBuilder.BodyTransform(body, units);
                    WalkShape(source, body.Shape, pose * bodyPose, units, body.Mass, triangles, row, []);
                }
                catch (ConvexListProjectionBoundary error)
                {
                    row.Failures.Add(new { lane = "collision-floor-projection", block = error.Shape, type = "bhkConvexListShape",
                        nativeImplementationOwner = "NativeNifCollisionBuilder.BuildConvexList", nativeAdmission = "unverified",
                        sourceOperation = "ordered-independent-convex-leaf-union", declarations = error.Declarations,
                        mathProjection = "uninspected", error = error.Message });
                }
                catch (Exception error) { row.Failures.Add(new { lane = "source-collision-admission", block = index, error = error.InnerException?.Message ?? error.Message }); }
            }
            if (obj is FalloutNifNode owner)
                foreach (var child in owner.Children.Where(child => child != -1)) WalkVisual(source, child, pose, units, triangles, row, active);
        }
        finally { active.Remove(index); }
    }

    private static void WalkShape(FalloutNifFile source, int index, Transform3D pose, float units, float mass,
        List<Triangle> triangles, ResourceRow row, HashSet<int> active)
    {
        if (!active.Add(index)) throw new InvalidDataException("Collision shape cycle at " + index);
        try
        {
            switch (source.ReadObject(index))
            {
                case FalloutNifMoppShape mopp: WalkShape(source, mopp.Child, pose, units, mass, triangles, row, active); break;
                case FalloutNifListShape list:
                    foreach (var child in list.Children) WalkShape(source, child, pose, units, mass, triangles, row, active);
                    break;
                case FalloutNifConvexListShape convexList:
                    throw new ConvexListProjectionBoundary(index, ConvexListDeclarationContext(source, convexList));
                case FalloutNifConvexTransformShape transform:
                    var matrix = NativeNifCollisionBuilder.MatrixTransform(transform, units);
                    WalkShape(source, transform.Child, pose * matrix, units, mass, triangles, row, active); break;
                case FalloutNifPackedShape packed:
                    if (mass != 0) throw new NotSupportedException("Non-static packed collision is rejected by the runtime.");
                    if (source.ReadObject(packed.Data) is not FalloutNifPackedData data) throw new InvalidDataException("Packed shape has no packed data.");
                    if (data.SubShapes.Length != 0 && data.SubShapes.Sum(part => (long)part.VertexCount) != data.Vertices.Length)
                        throw new InvalidDataException("Packed sub-shape ranges do not cover vertices.");
                    var vertices = data.Vertices.Select(vertex => pose * (GamebryoCoordinate.ConvertVector(new(vertex.X * packed.Scale.X,
                        vertex.Y * packed.Scale.Y, vertex.Z * packed.Scale.Z)) * (7 * units))).ToArray();
                    foreach (var triangle in data.Triangles) triangles.Add(new(vertices[triangle.A], vertices[triangle.B], vertices[triangle.C], index, ""));
                    row.CollisionTriangles += data.Triangles.Length; row.CollisionShapes++; break;
                case FalloutNifBoxShape box:
                    if (box.Dimensions.X <= 0 || box.Dimensions.Y <= 0 || box.Dimensions.Z <= 0) throw new InvalidDataException("Box dimensions are invalid.");
                    var size = GamebryoCoordinate.ConvertVector(new(box.Dimensions.X, box.Dimensions.Y, box.Dimensions.Z)).Abs() * (7 * units);
                    var points = Enumerable.Range(0, 8).Select(corner => pose * new Vector3((corner & 1) == 0 ? -size.X : size.X,
                        (corner & 2) == 0 ? -size.Y : size.Y, (corner & 4) == 0 ? -size.Z : size.Z)).ToArray();
                    int[] faces = [0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3];
                    for (var i = 0; i < faces.Length; i += 3) triangles.Add(new(points[faces[i]], points[faces[i + 1]], points[faces[i + 2]], index, ""));
                    row.CollisionShapes++; break;
                case FalloutNifConvexVerticesShape convex:
                    if (convex.Vertices.Length < 4 || convex.Vertices.Any(vertex => vertex.W != 0)) throw new InvalidDataException("Convex vertices violate the native owner contract.");
                    row.CollisionShapes++; break;
                case FalloutNifSphereShape sphere:
                    if (sphere.Radius <= 0) throw new InvalidDataException("Sphere radius is invalid."); row.CollisionShapes++; break;
                case FalloutNifCapsuleShape capsule:
                    if (capsule.FirstRadius <= 0 || MathF.Abs(capsule.FirstRadius - capsule.SecondRadius) > float.Epsilon)
                        throw new NotSupportedException("Capsule radii violate the native owner contract."); row.CollisionShapes++; break;
                default: throw new NotSupportedException("Collision shape has no native owner: " + source.Blocks[index].TypeName);
            }
        }
        finally { active.Remove(index); }
    }

    private static FloorHit[] Floors(IReadOnlyList<Triangle> triangles, float x, float z)
    {
        var hits = new List<FloorHit>();
        foreach (var triangle in triangles)
        {
            var y = Height(triangle.A.X, triangle.A.Z, triangle.A.Y, triangle.B.X, triangle.B.Z, triangle.B.Y,
                triangle.C.X, triangle.C.Z, triangle.C.Y, x, z);
            if (y is null) continue;
            var normal = (triangle.B - triangle.A).Cross(triangle.C - triangle.A).Normalized();
            hits.Add(new(triangle.Reference, triangle.Shape, y.Value, normal.Y));
        }
        return hits.OrderByDescending(hit => hit.Height).Distinct().ToArray();
    }
    private static float? Height(float ax, float az, float ay, float bx, float bz, float by, float cx, float cz, float cy, float x, float z)
    {
        var denominator = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
        if (MathF.Abs(denominator) < 1e-10f) return null;
        var a = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / denominator;
        var b = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / denominator;
        var c = 1 - a - b;
        if (a < -1e-5f || b < -1e-5f || c < -1e-5f) return null;
        return a * ay + b * by + c * cy;
    }
    private static float[] NavigationFloor(IReadOnlyList<FalloutNavigationMesh> meshes, float x, float y) => meshes.SelectMany(mesh => mesh.Triangles.Select(triangle =>
    {
        var a = mesh.Vertices[triangle.Vertices[0]]; var b = mesh.Vertices[triangle.Vertices[1]]; var c = mesh.Vertices[triangle.Vertices[2]];
        return Height(a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z, x, y);
    })).Where(height => height is not null).Select(height => height!.Value).Distinct().Order().ToArray();
}
