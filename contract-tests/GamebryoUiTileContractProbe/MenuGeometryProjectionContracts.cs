using System.Numerics;
using OpenNV.Runtime.Content;

internal static class MenuGeometryProjectionContracts
{
    internal static void Run()
    {
        TriangleExtent();
        NearFarAndCulling();
        DisplayedViewport();
        DisplayedClipping();
        SharedEdgesAndDepth();
        InvalidSources();
        Console.WriteLine("OPENNV_MENU_GEOMETRY_PROJECTION_CONTRACT_PASS triangleExtent=true sixPlanes=true noClamp=true nearFar=true winding=true mirroredCull=true displayedTransform=true parentClip=true sharedEdges=true depth=true invalid=true");
    }

    private static readonly FalloutMenuClipPlane[] Planes =
    [
        new(new(0, 0, -1), -1), new(new(0, 0, 1), 10),
        new(new(-1, 0, -1), 0), new(new(0, 1, -1), 0),
        new(new(1, 0, -1), 0), new(new(0, -1, -1), 0)
    ];
    private static FalloutMenuGeometryProjection Frustum() => new(Planes);
    private static Vector2 Project(Vector3 world) => new((.5f + world.X / (2 * world.Z)) * 1280, (.5f - world.Y / (2 * world.Z)) * 720);

    private static void TriangleExtent()
    {
        var owner = Frustum();
        var offscreen = new FalloutMenuWorldTriangle(new(-1, -5, 2), new(1, -5, 2), new(0, -3, 2));
        var rawCenter = Project((offscreen.A + offscreen.B + offscreen.C) / 3);
        Check(rawCenter.Y > 720 && owner.Project(offscreen, FalloutMenuTriangleCull.None, Project).Count == 0,
            "An in-front off-screen triangle earned an input target or a clamped center.");
        Check(offscreen.A.Y == -5 && offscreen.B.Y == -5 && offscreen.C.Y == -3,
            "Clipping changed the original source geometry.");
        // Its projected AABB overlaps the viewport corner; none of its actual
        // triangle lies in the frustum. A box cannot establish a drawn target.
        var boxOverlap = new FalloutMenuWorldTriangle(new(1.5f, 3, 2), new(3, 1.5f, 2), new(3, 3, 2));
        Check(owner.Project(boxOverlap, FalloutMenuTriangleCull.None, Project).Count == 0,
            "An overlapping source bound was mistaken for projected triangle coverage.");
        var partial = new FalloutMenuWorldTriangle(new(-4, 1, 2), new(1, 1, 2), new(0, -1, 2));
        var fragments = owner.Project(partial, FalloutMenuTriangleCull.None, Project);
        Check(fragments.Count >= 1 && fragments.All(triangle => Math.Abs(triangle.SignedArea) > 0),
            "Partial side-plane geometry lost its actual nonzero clipped extent.");
        foreach (var triangle in fragments)
        {
            var point = triangle.Interior;
            Check(point.X >= 0 && point.X < 1280 && point.Y >= 0 && point.Y < 720,
                "A source triangle interior was outside its actual render extent.");
        }
        var degenerate = new FalloutMenuWorldTriangle(new(0, 0, 2), new(0, 0, 2), new(0, 0, 2));
        Check(owner.Project(degenerate, FalloutMenuTriangleCull.None, Project).Count == 0,
            "A point or zero-area source primitive earned a target.");
    }

    private static void NearFarAndCulling()
    {
        var owner = Frustum();
        var near = new FalloutMenuWorldTriangle(new(0, .2f, .5f), new(1, -.5f, 2), new(-1, -.5f, 2));
        var clipped = owner.Clip(near);
        Check(clipped.Count >= 3 && clipped.All(point => point.Z >= 1) && clipped.Any(point => point.Z == 1),
            "Near-plane crossing was discarded or projected behind the actual clipping boundary.");
        var far = new FalloutMenuWorldTriangle(new(0, .2f, 12), new(1, -.5f, 2), new(-1, -.5f, 2));
        clipped = owner.Clip(far);
        Check(clipped.Count >= 3 && clipped.All(point => point.Z <= 10) && clipped.Any(point => point.Z == 10),
            "Far-plane crossing kept geometry past the renderer's actual range.");
        var behind = new FalloutMenuWorldTriangle(new(-1, 1, -2), new(1, 1, -2), new(0, -1, -2));
        Check(owner.Project(behind, FalloutMenuTriangleCull.None, Project).Count == 0,
            "Geometry behind the camera gained a projected target.");
        var front = new FalloutMenuWorldTriangle(new(-1, 1, 2), new(1, 1, 2), new(0, -1, 2));
        var back = new FalloutMenuWorldTriangle(front.A, front.C, front.B);
        Check(owner.Project(front, FalloutMenuTriangleCull.Back, Project).Count == 1 &&
            owner.Project(front, FalloutMenuTriangleCull.Front, Project).Count == 0 &&
            owner.Project(back, FalloutMenuTriangleCull.Back, Project).Count == 0 &&
            owner.Project(back, FalloutMenuTriangleCull.Front, Project).Count == 1 &&
            owner.Project(back, FalloutMenuTriangleCull.None, Project).Count == 1,
            "Declared clockwise front-face or disabled culling differs between projection and input.");
        var reflected = new FalloutMenuWorldTriangle(new(-front.A.X, front.A.Y, front.A.Z),
            new(-front.B.X, front.B.Y, front.B.Z), new(-front.C.X, front.C.Y, front.C.Z));
        Check(owner.Project(reflected, FalloutMenuGeometryProjection.EffectiveCull(FalloutMenuTriangleCull.Back, true, false), Project).Count == 1 &&
            owner.Project(reflected, FalloutMenuTriangleCull.Back, Project).Count == 0 &&
            FalloutMenuGeometryProjection.EffectiveCull(FalloutMenuTriangleCull.Back, true, true) == FalloutMenuTriangleCull.Back &&
            FalloutMenuGeometryProjection.EffectiveCull(FalloutMenuTriangleCull.Front, false, true) == FalloutMenuTriangleCull.Back &&
            FalloutMenuGeometryProjection.EffectiveCull(FalloutMenuTriangleCull.None, true, false) == FalloutMenuTriangleCull.None,
            "The actual reflected-instance/camera culling variant changed displayed triangle admission.");
        // Exact boundary vertices do not duplicate into artificial polygon
        // growth or invent a sliver outside the side planes.
        var boundary = new FalloutMenuWorldTriangle(new(-1, 1, 1), new(1, 1, 1), new(0, -1, 1));
        Check(owner.Clip(boundary).Count == 3, "Closed frustum edges duplicated source vertices.");
    }

    private static void DisplayedViewport()
    {
        var pixels = new Vector2(640, 360);
        var transform = Matrix3x2.CreateScale(509.5f / pixels.X, 286.59375f / pixels.Y) *
            Matrix3x2.CreateRotation(.19f) * Matrix3x2.CreateTranslation(31, 27);
        var mapping = new FalloutMenuViewportTransform(pixels, transform);
        var original = new Vector2(127.5f, 179.25f); var displayed = mapping.ToOwner(original);
        Check(mapping.TryPixels(displayed, out var actual) && Vector2.Distance(actual, original) < 1e-4,
            "Fractional extent, displayed offset or canvas rotation used a window-size shortcut.");
        Check(!mapping.TryPixels(mapping.ToOwner(new(-12, 200)), out _) &&
            !mapping.TryPixels(mapping.ToOwner(new(700, 200)), out _),
            "A point outside the actual displayed texture was moved into its source pixel domain.");
        var identity = new FalloutMenuViewportTransform(new(320, 180), Matrix3x2.Identity);
        Check(identity.TryPixels(Vector2.Zero, out _) && !identity.TryPixels(new(320, 10), out _) &&
            !identity.TryPixels(new(10, 180), out _), "The actual texture extent lost its half-open input boundary.");
    }

    private static void SharedEdgesAndDepth()
    {
        var first = new FalloutMenuWorldTriangle(new(-1, 1, 2), new(1, 1, 2), new(1, -1, 2));
        var second = new FalloutMenuWorldTriangle(first.A, first.C, new(-1, -1, 2));
        Check(FalloutMenuGeometryProjection.RayTriangle(Vector3.Zero, Vector3.UnitZ, first, out var a) &&
            FalloutMenuGeometryProjection.RayTriangle(Vector3.Zero, Vector3.UnitZ, second, out var b) && a == 2 && b == 2,
            "Adjacent source triangles opened a crack on their closed shared edge.");
        var farther = new FalloutMenuWorldTriangle(first.A * 2, first.B * 2, first.C * 2);
        Check(FalloutMenuGeometryProjection.RayTriangle(Vector3.Zero, Vector3.UnitZ, farther, out var far) && far > a,
            "Actual target depth ordering was replaced by source enumeration order.");
        Check(!FalloutMenuGeometryProjection.RayTriangle(Vector3.Zero, -Vector3.UnitZ, first, out _) &&
            !FalloutMenuGeometryProjection.RayTriangle(Vector3.Zero, Vector3.UnitX, first, out _),
            "Backward or parallel rays invented a source triangle intersection.");
    }

    private static void DisplayedClipping()
    {
        IReadOnlyList<Vector2> viewport = [new(0, 0), new(500, 0), new(500, 300), new(0, 300)];
        IReadOnlyList<Vector2> parent = [new(200, 100), new(300, 100), new(300, 200), new(200, 200)];
        var clip = new FalloutMenuDisplayClip([viewport, parent]);
        Check(clip.Clip(new(10, 10), new(100, 10), new(50, 80)).Count == 0,
            "A triangle hidden by its actual parent clip supplied a click point.");
        var polygon = clip.Clip(new(180, 120), new(280, 120), new(260, 250));
        Check(polygon.Count >= 3 && polygon.All(point => point.X >= 200 && point.X <= 300 && point.Y >= 100 && point.Y <= 200),
            "Parent clipping failed to retain the actual displayed fragment.");
        var interior = (polygon[0] + polygon[1] + polygon[2]) / 3;
        Check(clip.Contains(interior) && !clip.Contains(new(180, 140)),
            "A parent clip was replaced by the unclipped source control bound.");
        var rotation = Matrix3x2.CreateRotation(.31f) * Matrix3x2.CreateTranslation(20, 40);
        var rotated = parent.Select(point => Vector2.Transform(point, rotation)).ToArray();
        var transformedClip = new FalloutMenuDisplayClip([rotated]);
        Check(transformedClip.Contains(Vector2.Transform(new(250, 150), rotation)) &&
            !transformedClip.Contains(Vector2.Transform(new(190, 150), rotation)),
            "A rotated actual canvas clip used an axis-aligned window approximation.");
        Reject(() => new FalloutMenuDisplayClip([]));
        Reject(() => new FalloutMenuDisplayClip([[Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero]]));
        Reject(() => new FalloutMenuDisplayClip([[new(0, 0), new(1, 1), new(1, 0), new(0, 1)]]));
    }

    private static void InvalidSources()
    {
        Reject(() => new FalloutMenuGeometryProjection(Planes[..5]));
        var missing = Planes.ToArray(); missing[0] = new(Vector3.Zero, 1);
        Reject(() => new FalloutMenuGeometryProjection(missing));
        var bad = Planes.ToArray(); bad[0] = new(new(float.NaN, 0, 1), 1);
        Reject(() => new FalloutMenuGeometryProjection(bad));
        Reject(() => new FalloutMenuViewportTransform(Vector2.Zero, Matrix3x2.Identity));
        Reject(() => new FalloutMenuViewportTransform(new(10, 10), new Matrix3x2()));
        Reject(() => new FalloutMenuViewportTransform(new(10, 10), Matrix3x2.CreateTranslation(float.NaN, 0)));
        var owner = Frustum();
        var triangle = new FalloutMenuWorldTriangle(new(-1, 1, 2), new(1, 1, 2), new(0, -1, 2));
        Reject(() => owner.Project(triangle, (FalloutMenuTriangleCull)17, Project));
        Reject(() => FalloutMenuGeometryProjection.EffectiveCull((FalloutMenuTriangleCull)17, true, false));
        Reject(() => owner.Project(triangle, FalloutMenuTriangleCull.None, _ => new(float.NaN, 0)));
        Reject(() => owner.Clip(triangle with { A = new(float.PositiveInfinity, 0, 2) }));
        Reject(() => FalloutMenuGeometryProjection.RayTriangle(Vector3.Zero, Vector3.Zero, triangle, out _));
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid projection source was accepted.");
    }
}
