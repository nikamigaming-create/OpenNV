using OpenNV.Runtime.World.Physics;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunEarlyControllerConvexGeometryContracts()
    {
        FalloutPhysicalVector3[] cube =
        [
            P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, -1), P(-1, 1, 1),
            P(1, -1, -1), P(1, -1, 1), P(1, 1, -1), P(1, 1, 1)
        ];
        var geometry = Build(cube);
        Require(geometry.Faces.Count == 6 && geometry.Edges.Count == 12 && geometry.Vertices.Count == 8 &&
            geometry.Minimum == P(-1.1f, -1.1f, -1.1f) && geometry.Maximum == P(1.1f, 1.1f, 1.1f),
            "Finite geometric hull changed cube faces/edges or omitted the source rounded radius from bounds.");
        var expected = (float)(1 + .1f / Math.Sqrt(3)); var support = geometry.Support(P(1, 1, 1));
        Require(support == P(expected, expected, expected), "Rounded convex support did not include the spherical source shell.");
        var augmented = Build([.. cube, P(0, 0, 0), P(1, 0, 0), P(1, 1, 1)]);
        Require(augmented.Faces.Count == 6 && augmented.Edges.Count == 12 && augmented.Vertices.Count == 8 &&
            augmented.Support(P(1, 1, 1)) == support,
            "Interior, coplanar or repeated points changed the geometric boundary.");
        var thin = Build(cube.Select(value => value with { Z = BitConverter.SingleToUInt32Bits(value.ZValue * MathF.ScaleB(1, -20)) }).ToArray());
        Require(thin.Faces.Count == 6 && thin.Edges.Count == 12,
            "A coordinate tolerance erased a finite thin source hull.");
        Reject(() => Build([P(0, 0, 0), P(1, 0, 0), P(0, 1, 0), P(1, 1, 0)]));
        Reject(() => Build([P(0, 0, 0), P(0, 0, 0), P(0, 0, 0), P(0, 0, 0)]));
        Reject(() => Build([P(float.NaN, 0, 0), P(1, 0, 0), P(0, 1, 0), P(0, 0, 1)]));
        Reject(() => Build(Enumerable.Repeat(P(0, 0, 0), FalloutEarlyControllerConvexGeometry.StandardPointCount + 1).ToArray()));
        var inputs = new FalloutPhysicalConstructorInputs(FalloutPhysicalConstructorInputs.ReviewedEngineSha256, new(0, 0));
        var standard = new FalloutEarlyControllerConvexGeometry(inputs.ProjectStandardConvex(inputs.ProjectPlayer(default, default), 0));
        Require(standard.Points.Count == 18 && standard.Faces.Count == 24 && standard.Edges.Count == 40 && standard.Vertices.Count == 18 &&
            standard.RadiusBits == FalloutPhysicalConstructorInputs.ContactToleranceBits,
            "Source eighteen-point constructor changed its finite geometric boundary or specialized radius.");
        var shifted = new FalloutEarlyControllerConvexGeometry(inputs.ProjectStandardConvex(inputs.ProjectPlayer(default, P(3, 5, 0)), 0));
        Require(shifted.Points.Count == 18 && shifted.Faces.Count >= 4 && shifted.Vertices.Count >= 4,
            "Nonzero source center was restricted to a zero-component fixture.");
        Console.WriteLine("OPENNV_EARLY_CONTROLLER_GEOMETRY_PASS finiteExactOrientation=true closedBoundary=true radiusSupport=true " +
            "coplanarAndInterior=true thinVolume=true source18=true nonzeroCenter=true nativeResources=SEPARATE_AUDIT canonicalBytes=UNQUALIFIED");

        static FalloutPhysicalVector3 P(float x, float y, float z) => FalloutPhysicalVector3.FromValues(x, y, z);
        static FalloutEarlyControllerConvexGeometry Build(FalloutPhysicalVector3[] points) => new(new(
            points.Select((value, index) => new FalloutPhysicalVector4(value, (uint)index)).ToArray(),
            FalloutPhysicalConstructorInputs.KeepDistanceBits, FalloutPhysicalConstructorInputs.ContactToleranceBits, 0));
    }
}
