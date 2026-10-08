internal static partial class CellGraphAudit
{
    // Candidate selection only. The original Height/Floors arithmetic owns
    // membership, normals, descending height, duplicate removal and tie order.
    internal sealed class SourceFloorIndex
    {
        private const int LeafSize = 8;
        private readonly Triangle[] _triangles;
        private readonly Row[] _rows;
        private readonly int[] _unbounded;
        private readonly Node? _positive, _negative;
        private static readonly RowOrder XOrder = new(true), ZOrder = new(false);
        internal int TriangleCount => _triangles.Length;
        internal int DegenerateProjections { get; }
        internal int UnboundedProjections => _unbounded.Length;
        internal int Nodes { get; private set; }
        internal long Queries { get; private set; }
        internal long CandidateEvaluations { get; private set; }
        internal long VisitedNodes { get; private set; }

        internal SourceFloorIndex(IReadOnlyList<Triangle> triangles)
        {
            _triangles = triangles.ToArray();
            var positive = new List<Row>(); var negative = new List<Row>(); var unbounded = new List<int>();
            var degenerate = 0;
            for (var index = 0; index < _triangles.Length; index++)
            {
                var triangle = _triangles[index];
                var ax = triangle.A.X; var az = triangle.A.Z; var bx = triangle.B.X; var bz = triangle.B.Z; var cx = triangle.C.X; var cz = triangle.C.Z;
                var denominator = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
                var coefficients = new Coefficients(bz - cz, cx - bx, cz - az, ax - cx, cx, cz, denominator);
                if (!coefficients.IsFinite || !triangle.A.IsFinite() || !triangle.B.IsFinite() || !triangle.C.IsFinite())
                {
                    // Infinity/NaN can bypass the original comparison branches.
                    // Keep these rows unconditional rather than changing them.
                    unbounded.Add(index); continue;
                }
                if (MathF.Abs(denominator) < 1e-10f) { degenerate++; continue; }
                var centerX = (float)(((double)Math.Min(ax, Math.Min(bx, cx)) + Math.Max(ax, Math.Max(bx, cx))) / 2);
                var centerZ = (float)(((double)Math.Min(az, Math.Min(bz, cz)) + Math.Max(az, Math.Max(bz, cz))) / 2);
                var row = new Row(index, centerX, centerZ, coefficients);
                (denominator > 0 ? positive : negative).Add(row);
            }
            DegenerateProjections = degenerate; _unbounded = unbounded.ToArray();
            _rows = positive.Concat(negative).ToArray();
            _positive = Build(0, positive.Count); _negative = Build(positive.Count, negative.Count);
        }

        internal int[] CandidateIndices(float x, float z)
        {
            Queries++;
            if (!float.IsFinite(x) || !float.IsFinite(z))
            {
                CandidateEvaluations += _triangles.Length;
                return Enumerable.Range(0, _triangles.Length).ToArray();
            }
            var result = new List<int>(_unbounded);
            Visit(_positive); Visit(_negative);
            // Order is observable when equal-height hits have different owners
            // or normals, and when the caller chooses the first nearest hit.
            result.Sort(); CandidateEvaluations += result.Count;
            return result.ToArray();

            void Visit(Node? node)
            {
                if (node is null) return;
                VisitedNodes++;
                if (!node.Ranges.MayPass(x, z)) return;
                if (node.Left is null)
                {
                    for (var offset = node.Start; offset < node.Start + node.Count; offset++) result.Add(_rows[offset].Index);
                }
                else { Visit(node.Left); Visit(node.Right); }
            }
        }

        internal IReadOnlyList<Triangle> Candidates(float x, float z) =>
            CandidateIndices(x, z).Select(index => _triangles[index]).ToArray();

        private Node? Build(int start, int count)
        {
            if (count == 0) return null;
            var ranges = ProjectionRanges.From(_rows[start].Coefficients);
            var minX = _rows[start].X; var maxX = minX; var minZ = _rows[start].Z; var maxZ = minZ;
            for (var index = start + 1; index < start + count; index++)
            {
                ranges = ranges.Merge(_rows[index].Coefficients);
                minX = Math.Min(minX, _rows[index].X); maxX = Math.Max(maxX, _rows[index].X);
                minZ = Math.Min(minZ, _rows[index].Z); maxZ = Math.Max(maxZ, _rows[index].Z);
            }
            var node = new Node(ranges, start, count); Nodes++;
            if (count <= LeafSize) return node;
            var axisX = (double)maxX - minX >= (double)maxZ - minZ;
            Array.Sort(_rows, start, count, axisX ? XOrder : ZOrder);
            var left = count / 2;
            node.Left = Build(start, left); node.Right = Build(start + left, count - left);
            return node;
        }

        private readonly record struct Row(int Index, float X, float Z, Coefficients Coefficients);
        private sealed class RowOrder(bool axisX) : IComparer<Row>
        {
            public int Compare(Row first, Row second)
            {
                var result = (axisX ? first.X : first.Z).CompareTo(axisX ? second.X : second.Z);
                return result == 0 ? first.Index.CompareTo(second.Index) : result;
            }
        }
        private sealed class Node(ProjectionRanges ranges, int start, int count)
        {
            internal ProjectionRanges Ranges { get; } = ranges;
            internal int Start { get; } = start;
            internal int Count { get; } = count;
            internal Node? Left { get; set; }
            internal Node? Right { get; set; }
        }

        private readonly record struct Coefficients(float BzCz, float CxBx, float CzAz, float AxCx, float Cx, float Cz, float Denominator)
        {
            internal bool IsFinite => float.IsFinite(BzCz) && float.IsFinite(CxBx) && float.IsFinite(CzAz) && float.IsFinite(AxCx) &&
                float.IsFinite(Cx) && float.IsFinite(Cz) && float.IsFinite(Denominator);
        }
        private readonly record struct ProjectionRanges(Interval BzCz, Interval CxBx, Interval CzAz, Interval AxCx, Interval Cx, Interval Cz, Interval Denominator)
        {
            internal static ProjectionRanges From(Coefficients value) => new(Interval.Point(value.BzCz), Interval.Point(value.CxBx),
                Interval.Point(value.CzAz), Interval.Point(value.AxCx), Interval.Point(value.Cx), Interval.Point(value.Cz), Interval.Point(value.Denominator));
            internal ProjectionRanges Merge(Coefficients value) => new(BzCz.Include(value.BzCz), CxBx.Include(value.CxBx), CzAz.Include(value.CzAz),
                AxCx.Include(value.AxCx), Cx.Include(value.Cx), Cz.Include(value.Cz), Denominator.Include(value.Denominator));
            internal bool MayPass(float x, float z)
            {
                // Enclose the actual Float32 operations in Height, including its
                // existing -1e-5 tolerance. A loose interval keeps candidates;
                // it never substitutes an exact-arithmetic containment test.
                var dx = Interval.Subtract(Interval.Point(x), Cx); var dz = Interval.Subtract(Interval.Point(z), Cz);
                var a = Interval.Divide(Interval.Add(Interval.Multiply(BzCz, dx), Interval.Multiply(CxBx, dz)), Denominator);
                var b = Interval.Divide(Interval.Add(Interval.Multiply(CzAz, dx), Interval.Multiply(AxCx, dz)), Denominator);
                var c = Interval.Subtract(Interval.Subtract(Interval.Point(1), a), b);
                return !(a.High < -1e-5f || b.High < -1e-5f || c.High < -1e-5f);
            }
        }

        private readonly record struct Interval(float Low, float High)
        {
            private static Interval Full => new(float.NegativeInfinity, float.PositiveInfinity);
            private bool Finite => float.IsFinite(Low) && float.IsFinite(High);
            internal static Interval Point(float value) => new(value, value);
            internal Interval Include(float value) => new(Math.Min(Low, value), Math.Max(High, value));
            internal static Interval Add(Interval a, Interval b) => !a.Finite || !b.Finite ? Full :
                Rounded((double)a.Low + b.Low, (double)a.High + b.High);
            internal static Interval Subtract(Interval a, Interval b) => !a.Finite || !b.Finite ? Full :
                Rounded((double)a.Low - b.High, (double)a.High - b.Low);
            internal static Interval Multiply(Interval a, Interval b)
            {
                if (!a.Finite || !b.Finite) return Full;
                var aa = (double)a.Low * b.Low; var ab = (double)a.Low * b.High; var ba = (double)a.High * b.Low; var bb = (double)a.High * b.High;
                return Rounded(Math.Min(Math.Min(aa, ab), Math.Min(ba, bb)), Math.Max(Math.Max(aa, ab), Math.Max(ba, bb)));
            }
            internal static Interval Divide(Interval a, Interval b)
            {
                if (!a.Finite || !b.Finite || b.Low <= 0 && b.High >= 0) return Full;
                var aa = (double)a.Low / b.Low; var ab = (double)a.Low / b.High; var ba = (double)a.High / b.Low; var bb = (double)a.High / b.High;
                return Rounded(Math.Min(Math.Min(aa, ab), Math.Min(ba, bb)), Math.Max(Math.Max(aa, ab), Math.Max(ba, bb)));
            }
            private static Interval Rounded(double low, double high)
            {
                if (!double.IsFinite(low) || !double.IsFinite(high)) return Full;
                // Float endpoints are widened by one representable value after
                // every operation, enclosing rounding and double-round ties.
                return new(MathF.BitDecrement((float)low), MathF.BitIncrement((float)high));
            }
        }
    }

    private static FloorHit[] Floors(SourceFloorIndex index, float x, float z) => Floors(index.Candidates(x, z), x, z);

    // Contract observations call the same projection owners as the real audit;
    // the neutral tuple exposes exact hit values without native scene entities.
    internal static (string Reference, int Shape, float Height, float NormalY)[] ProjectFloors(IReadOnlyList<Triangle> triangles, float x, float z) =>
        Floors(triangles, x, z).Select(hit => (hit.Reference, hit.Shape, hit.Height, hit.NormalY)).ToArray();
    internal static (string Reference, int Shape, float Height, float NormalY)[] ProjectFloors(SourceFloorIndex index, float x, float z) =>
        Floors(index, x, z).Select(hit => (hit.Reference, hit.Shape, hit.Height, hit.NormalY)).ToArray();
}
