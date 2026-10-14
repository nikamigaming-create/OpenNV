using System.Numerics;

namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutEarlyControllerConvexFace(IReadOnlyList<int> Vertices, FalloutPhysicalVector3 OutwardNormal);
internal sealed record FalloutEarlyControllerConvexEdge(int First, int Second);

// Geometric ownership is independent of the original hull's byte ordering,
// half-edge representation and retry policy. It never reports those returned.
internal sealed class FalloutEarlyControllerConvexGeometry
{
    internal const int StandardPointCount = 18;
    internal const string CanonicalBoundary = "source-controller-canonical-half-edge-plane-order-and-packed-byte-parity-unqualified";
    internal IReadOnlyList<FalloutPhysicalVector3> Points { get; }
    internal IReadOnlyList<FalloutEarlyControllerConvexFace> Faces { get; }
    internal IReadOnlyList<FalloutEarlyControllerConvexEdge> Edges { get; }
    internal IReadOnlyList<int> Vertices { get; }
    internal uint RadiusBits { get; }
    internal FalloutPhysicalVector3 Minimum { get; }
    internal FalloutPhysicalVector3 Maximum { get; }

    internal FalloutEarlyControllerConvexGeometry(FalloutStandardControllerConvexInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Points.Count > StandardPointCount)
            throw new NotSupportedException("The standard controller geometric capability is bounded to its eighteen source points.");
        var points = inputs.Points.Select(value => value.Position).ToArray();
        var radius = BitConverter.UInt32BitsToSingle(inputs.SpecializedRadiusBits);
        if (points.Length < 4 || points.Any(value => !Finite(value)) || !float.IsFinite(radius) || radius <= 0)
            throw new InvalidDataException("Controller convex geometry requires a finite volume and positive source radius.");
        RadiusBits = inputs.SpecializedRadiusBits; Points = Array.AsReadOnly(points);
        var exact = ExactPoints(points);
        var faces = new List<FalloutEarlyControllerConvexFace>(); var keys = new HashSet<string>(StringComparer.Ordinal);
        // Source standard construction has eighteen points. This bounded
        // supporting-plane search runs once for the shared shape cache.
        for (var first = 0; first < points.Length - 2; ++first)
            for (var second = first + 1; second < points.Length - 1; ++second)
                for (var third = second + 1; third < points.Length; ++third)
                {
                    var normal = Cross(exact[second] - exact[first], exact[third] - exact[first]);
                    if (normal.Zero) continue;
                    var positive = false; var negative = false; var coplanar = new List<int>();
                    for (var index = 0; index < exact.Length; ++index)
                    {
                        var orientation = Dot(normal, exact[index] - exact[first]).Sign;
                        if (orientation > 0) positive = true;
                        else if (orientation < 0) negative = true;
                        else coplanar.Add(index);
                        if (positive && negative) break;
                    }
                    if (positive && negative || !positive && !negative) continue;
                    var key = string.Join(",", coplanar);
                    if (!keys.Add(key)) continue;
                    if (positive) normal = -normal;
                    var polygon = PlanarHull(coplanar, exact, normal);
                    if (polygon.Length < 3) throw new InvalidDataException("Controller convex supporting face has no area.");
                    var oriented = Cross(exact[polygon[1]] - exact[polygon[0]], exact[polygon[2]] - exact[polygon[0]]);
                    if (Dot(oriented, normal).Sign < 0) Array.Reverse(polygon);
                    faces.Add(new(Array.AsReadOnly(polygon), OutwardNormal(points[polygon[0]], points[polygon[1]], points[polygon[2]])));
                }
        if (faces.Count < 4) throw new InvalidDataException("Controller convex input has no closed three-dimensional hull.");
        var edges = new Dictionary<(int First, int Second), int>();
        foreach (var face in faces)
            for (var index = 0; index < face.Vertices.Count; ++index)
            {
                var first = face.Vertices[index]; var second = face.Vertices[(index + 1) % face.Vertices.Count];
                var key = first < second ? (first, second) : (second, first);
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        var vertices = faces.SelectMany(value => value.Vertices).Distinct().Order().ToArray();
        if (edges.Values.Any(value => value != 2) || vertices.Length - edges.Count + faces.Count != 2)
            throw new InvalidDataException("Controller convex geometric faces do not form a closed boundary.");
        Faces = Array.AsReadOnly(faces.ToArray()); Vertices = Array.AsReadOnly(vertices);
        Edges = Array.AsReadOnly(edges.Keys.Select(value => new FalloutEarlyControllerConvexEdge(value.First, value.Second)).ToArray());
        Minimum = FalloutPhysicalVector3.FromValues(points.Min(value => value.XValue) - radius,
            points.Min(value => value.YValue) - radius, points.Min(value => value.ZValue) - radius);
        Maximum = FalloutPhysicalVector3.FromValues(points.Max(value => value.XValue) + radius,
            points.Max(value => value.YValue) + radius, points.Max(value => value.ZValue) + radius);
    }

    internal FalloutPhysicalVector3 Support(FalloutPhysicalVector3 direction)
    {
        if (!Finite(direction)) throw new InvalidDataException("Controller support query has a nonfinite direction.");
        var length = Math.Sqrt((double)direction.XValue * direction.XValue + (double)direction.YValue * direction.YValue +
            (double)direction.ZValue * direction.ZValue);
        if (length == 0) throw new InvalidDataException("Controller support query requires a direction.");
        var point = Points.MaxBy(value => (double)value.XValue * direction.XValue + (double)value.YValue * direction.YValue +
            (double)value.ZValue * direction.ZValue);
        var radius = BitConverter.UInt32BitsToSingle(RadiusBits);
        return FalloutPhysicalVector3.FromValues((float)(point.XValue + radius * direction.XValue / length),
            (float)(point.YValue + radius * direction.YValue / length), (float)(point.ZValue + radius * direction.ZValue / length));
    }

    internal static FalloutPhysicalVector3 FaceCenter(FalloutEarlyControllerConvexFace face, IReadOnlyList<FalloutPhysicalVector3> points) =>
        FalloutPhysicalVector3.FromValues((float)face.Vertices.Average(index => (double)points[index].XValue),
            (float)face.Vertices.Average(index => (double)points[index].YValue), (float)face.Vertices.Average(index => (double)points[index].ZValue));

    private readonly record struct ExactVector(BigInteger X, BigInteger Y, BigInteger Z)
    {
        internal bool Zero => X.IsZero && Y.IsZero && Z.IsZero;
        internal BigInteger Axis(int index) => index switch { 0 => X, 1 => Y, 2 => Z, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
        public static ExactVector operator -(ExactVector left, ExactVector right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static ExactVector operator -(ExactVector value) => new(-value.X, -value.Y, -value.Z);
    }
    private static ExactVector Cross(ExactVector first, ExactVector second) =>
        new(first.Y * second.Z - first.Z * second.Y, first.Z * second.X - first.X * second.Z, first.X * second.Y - first.Y * second.X);
    private static BigInteger Dot(ExactVector first, ExactVector second) => first.X * second.X + first.Y * second.Y + first.Z * second.Z;
    private static ExactVector[] ExactPoints(FalloutPhysicalVector3[] points)
    {
        var values = points.SelectMany(value => new[] { value.X, value.Y, value.Z }).Select(Word).ToArray();
        var exponent = values.Where(value => !value.Significand.IsZero).Select(value => value.Exponent).DefaultIfEmpty(0).Min();
        var integers = values.Select(value => value.Significand << (value.Exponent - exponent)).ToArray();
        return Enumerable.Range(0, points.Length).Select(index => new ExactVector(integers[index * 3], integers[index * 3 + 1], integers[index * 3 + 2])).ToArray();
        static (BigInteger Significand, int Exponent) Word(uint bits)
        {
            var exponent = (int)((bits >> 23) & 0xff); var fraction = bits & 0x7fffff;
            var significand = new BigInteger(exponent == 0 ? fraction : fraction | 0x800000);
            if ((bits & 0x80000000) != 0) significand = -significand;
            return (significand, exponent == 0 ? -149 : exponent - 150);
        }
    }
    private static int[] PlanarHull(IReadOnlyList<int> coplanar, ExactVector[] points, ExactVector normal)
    {
        var drop = Enumerable.Range(0, 3).MaxBy(axis => BigInteger.Abs(normal.Axis(axis)));
        var axes = Enumerable.Range(0, 3).Where(axis => axis != drop).ToArray();
        var ordered = coplanar.OrderBy(index => points[index].Axis(axes[0])).ThenBy(index => points[index].Axis(axes[1]))
            .DistinctBy(index => (points[index].Axis(axes[0]), points[index].Axis(axes[1]))).ToArray();
        var polygon = new List<int>();
        foreach (var index in ordered) Add(index, 0);
        var lowerCount = polygon.Count;
        for (var index = ordered.Length - 2; index >= 0; --index) Add(ordered[index], lowerCount - 1);
        if (polygon.Count > 1) polygon.RemoveAt(polygon.Count - 1);
        return polygon.ToArray();
        void Add(int index, int start)
        {
            while (polygon.Count >= start + 2 && Orientation(polygon[^2], polygon[^1], index).Sign <= 0) polygon.RemoveAt(polygon.Count - 1);
            polygon.Add(index);
        }
        BigInteger Orientation(int first, int second, int third)
        {
            var a = points[second] - points[first]; var b = points[third] - points[first];
            return a.Axis(axes[0]) * b.Axis(axes[1]) - a.Axis(axes[1]) * b.Axis(axes[0]);
        }
    }
    private static FalloutPhysicalVector3 OutwardNormal(FalloutPhysicalVector3 first, FalloutPhysicalVector3 second, FalloutPhysicalVector3 third)
    {
        var ax = (double)second.XValue - first.XValue; var ay = (double)second.YValue - first.YValue; var az = (double)second.ZValue - first.ZValue;
        var bx = (double)third.XValue - first.XValue; var by = (double)third.YValue - first.YValue; var bz = (double)third.ZValue - first.ZValue;
        var x = ay * bz - az * by; var y = az * bx - ax * bz; var z = ax * by - ay * bx;
        var length = Math.Sqrt(x * x + y * y + z * z);
        if (!double.IsFinite(length) || length == 0) throw new InvalidDataException("Controller convex normal has no finite magnitude.");
        return FalloutPhysicalVector3.FromValues((float)(x / length), (float)(y / length), (float)(z / length));
    }
    private static bool Finite(FalloutPhysicalVector3 value) => float.IsFinite(value.XValue) && float.IsFinite(value.YValue) && float.IsFinite(value.ZValue);
}
