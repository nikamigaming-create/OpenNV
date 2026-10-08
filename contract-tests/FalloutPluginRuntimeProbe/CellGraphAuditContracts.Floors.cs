using Godot;

internal static partial class CellGraphAuditContracts
{
    private static void FloorProjectionCandidates()
    {
        CellGraphAudit.Triangle Triangle(Vector3 a, Vector3 b, Vector3 c, int shape, string owner) => new(a, b, c, shape, owner);
        CellGraphAudit.Triangle Horizontal(float x, float z, float height, int shape, string owner) =>
            Triangle(new(x, height, z), new(x + 1, height, z), new(x, height, z + 1), shape, owner);
        var simple = Horizontal(0, 0, 2, 7, "floor");
        var single = new CellGraphAudit.SourceFloorIndex([simple]);
        var center = CellGraphAudit.ProjectFloors(single, .25f, .25f);
        Require(center.Length == 1 && center[0] == ("floor", 7, 2f, -1f),
            "Independent horizontal projection height/owner/normal changed.");
        Require(CellGraphAudit.ProjectFloors(single, -5e-6f, .25f).Length == 1 &&
            single.CandidateIndices(-5e-6f, .25f).SequenceEqual([0]),
            "Candidate bounds lost a point outside the triangle AABB but inside the existing barycentric tolerance.");
        Require(CellGraphAudit.ProjectFloors(single, -2e-5f, .25f).Length == 0 &&
            CellGraphAudit.ProjectFloors(single, 1, 0).Length == 1 && CellGraphAudit.ProjectFloors(single, 0, 0).Length == 1,
            "Exact edges/vertices or the original outside tolerance changed.");
        CellGraphAudit.Triangle[] mutable = [simple];
        var snapshot = new CellGraphAudit.SourceFloorIndex(mutable);
        mutable[0] = Horizontal(0, 0, 100, 99, "changed-caller");
        Require(CellGraphAudit.ProjectFloors(snapshot, .25f, .25f).Single() == ("floor", 7, 2f, -1f),
            "A source candidate index retained a mutable caller triangle array.");
        var slope = Triangle(new(0, 0, 0), new(2, 2, 0), new(0, 4, 2), 5, "slope");
        Require(CellGraphAudit.ProjectFloors(new CellGraphAudit.SourceFloorIndex([slope]), .5f, .5f).Single().Height == 1.5f,
            "Independent tilted-plane height differs.");
        CellGraphAudit.Triangle[] ties = [Horizontal(0, 0, 2, 3, "lower"), Horizontal(0, 0, 9, 4, "top-first"),
            Horizontal(0, 0, 9, 4, "top-second"), Horizontal(0, 0, 9, 4, "top-first")];
        var tied = CellGraphAudit.ProjectFloors(new CellGraphAudit.SourceFloorIndex(ties), .25f, .25f);
        Require(tied.Select(hit => hit.Reference).SequenceEqual(["top-first", "top-second", "lower"]),
            "Equal-height input order or exact duplicate suppression changed.");

        var vertical = Triangle(new(0, 0, 0), new(0, 4, 0), new(0, 0, 2), 8, "vertical");
        var collapsed = Triangle(new(3, 2, 5), new(3, 2, 5), new(3, 2, 5), 9, "collapsed");
        var tiny = Triangle(new(0, 0, 0), new(1e-8f, 0, 0), new(0, 0, 1e-8f), 10, "below-denominator-threshold");
        var degenerate = new CellGraphAudit.SourceFloorIndex([vertical, collapsed, tiny]);
        Require(degenerate.TriangleCount == 3 && degenerate.DegenerateProjections == 3 &&
            CellGraphAudit.ProjectFloors(degenerate, 0, 0).Length == 0,
            "Degenerate/vertical rows disappeared from the source denominator or acquired invented support.");
        var reverse = simple with { A = simple.B, B = simple.A };
        var sharedEdge = Triangle(new(1, 2, 0), new(1, 2, 1), new(0, 2, 1), 7, "floor");
        var placement = CellGraphAudit.ReferenceTransform([-71, 39, 123], [.19f, -.37f, 1.1f], 2.3f, .037f);
        var moved = slope with { A = placement * slope.A, B = placement * slope.B, C = placement * slope.C, Reference = "placed" };
        var large = Triangle(new(1e30f, 1, 1e30f), new(-1e30f, 2, 1e30f), new(1e30f, 3, -1e30f), 11, "overflow");
        var nonfinite = simple with { A = new(float.NaN, 2, 0), Reference = "unknown-geometry" };
        var thin = Triangle(new(10000, 4, 10000), new(10000.002f, 7, 10000.001f), new(10000.003f, 2, 10000.002f), 12, "thin-offset");
        var signedZero = Triangle(new(-0f, 1, -0f), new(2, 1, 0), new(0, 1, 2), 13, "signed-zero");
        var rows = ties.Concat(new[] { simple, reverse, sharedEdge, slope, moved, vertical, collapsed, tiny, large, nonfinite, thin, signedZero }).ToArray();
        var points = new List<(float X, float Z)> { (.25f, .25f), (.5f, .5f), (1, 0), (0, 0), (0, 1), (-5e-6f, .25f),
            (-2e-5f, .25f), (10000.002f, 10000.001f), (1e30f, -1e30f), (float.MaxValue, 0), (float.NaN, 0),
            (float.PositiveInfinity, float.NegativeInfinity) };
        foreach (var row in rows.Where(row => row.A.IsFinite() && row.B.IsFinite() && row.C.IsFinite()))
        {
            points.Add((row.A.X, row.A.Z)); points.Add((row.B.X, row.B.Z)); points.Add((row.C.X, row.C.Z));
            var centroid = (row.A + row.B + row.C) / 3; points.Add((centroid.X, centroid.Z));
        }
        for (var x = -4; x <= 4; x++)
            for (var z = -4; z <= 4; z++) points.Add((x * .25f, z * .25f));
        Compare(rows, points);
        Compare([], [(0, 0), (float.NaN, float.PositiveInfinity)]);

        // Independent placed triangles fill a broad scene. Source count is not
        // capped; candidate reduction is checked without timing assumptions.
        var separated = Enumerable.Range(0, 4096).Select(index => Horizontal(index % 64 * 3, index / 64 * 3,
            index % 17, index, "placed-" + index)).ToArray();
        var broad = new CellGraphAudit.SourceFloorIndex(separated);
        foreach (var target in new[] { (.25f, .25f), (117.25f, 153.25f), (189.25f, 189.25f), (-1000f, 1000f) })
        {
            var candidates = broad.CandidateIndices(target.Item1, target.Item2);
            Require(candidates.Length < separated.Length / 8,
                "The spatial candidate owner still enumerates the entire separated scene for one projection.");
        }
        Compare(separated, [(.25f, .25f), (117.25f, 153.25f), (189.25f, 189.25f), (-1000, 1000)]);
        Console.WriteLine("OPENNV_CELL_GRAPH_FLOOR_CANDIDATES_PASS exhaustiveHitEquality=true everyAcceptedTriangleCandidate=true edgeTolerance=true ties=true overlap=true placements=true vertical=true degenerate=true nonfiniteRetained=true noSourceCaps=true");

        void Compare(IReadOnlyList<CellGraphAudit.Triangle> source, IEnumerable<(float X, float Z)> samples)
        {
            var index = new CellGraphAudit.SourceFloorIndex(source);
            Require(index.TriangleCount == source.Count, "Candidate preparation dropped an original source triangle.");
            foreach (var point in samples)
            {
                var candidates = index.CandidateIndices(point.X, point.Z);
                Require(candidates.SequenceEqual(candidates.Distinct().Order()) && candidates.All(value => value >= 0 && value < source.Count),
                    "Candidate enumeration duplicated, reordered or invented source triangle identities.");
                var admitted = candidates.ToHashSet();
                for (var slot = 0; slot < source.Count; slot++)
                    if (CellGraphAudit.ProjectFloors(new[] { source[slot] }, point.X, point.Z).Length != 0)
                        Require(admitted.Contains(slot), "Conservative enumeration lost an accepted original triangle, including a duplicate hit.");
                var expected = CellGraphAudit.ProjectFloors(source, point.X, point.Z);
                var actual = CellGraphAudit.ProjectFloors(index, point.X, point.Z);
                Require(actual.Length == expected.Length && actual.Zip(expected).All(pair =>
                    pair.First.Reference == pair.Second.Reference && pair.First.Shape == pair.Second.Shape &&
                    BitConverter.SingleToUInt32Bits(pair.First.Height) == BitConverter.SingleToUInt32Bits(pair.Second.Height) &&
                    BitConverter.SingleToUInt32Bits(pair.First.NormalY) == BitConverter.SingleToUInt32Bits(pair.Second.NormalY)),
                    "Accelerated projection differs from exhaustive Float32 heights/normals/owners/order.");
            }
        }
    }
}
