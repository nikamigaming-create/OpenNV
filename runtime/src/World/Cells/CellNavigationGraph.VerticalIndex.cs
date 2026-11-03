using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class CellNavigationGraph
{
    private sealed partial class NavigationMeshRecord
    {
        private readonly Lazy<VerticalTriangleIndex> _verticalTriangleIndex;

        // Actor floor admission asks for vertical projections repeatedly during
        // source arrival, route edges and actual movement. Index immutable XY
        // bounds; the original exact triangle and height predicates still own
        // every admitted floor, including overlapping layers and source ties.
        private sealed class VerticalTriangleIndex
        {
            private readonly record struct BoundsNode(Vector2 Minimum, Vector2 Maximum,
                int Start, int Count, int Left, int Right);
            private readonly NavigationMeshRecord _mesh;
            private readonly int[] _triangles;
            private readonly BoundsNode[] _nodes;

            internal VerticalTriangleIndex(NavigationMeshRecord mesh)
            {
                _mesh = mesh;
                _triangles = Enumerable.Range(0, mesh.Triangles.Count).ToArray();
                var bounds = new (Vector2 Minimum, Vector2 Maximum)[_triangles.Length];
                for (var index = 0; index < bounds.Length; index++)
                {
                    var triangle = mesh.Triangles[index];
                    var a = mesh.Vertices[triangle.VertexIndices[0]];
                    var b = mesh.Vertices[triangle.VertexIndices[1]];
                    var c = mesh.Vertices[triangle.VertexIndices[2]];
                    bounds[index] = (
                        new(MathF.BitDecrement(Math.Min(a.X, Math.Min(b.X, c.X))),
                            MathF.BitDecrement(Math.Min(a.Y, Math.Min(b.Y, c.Y)))),
                        new(MathF.BitIncrement(Math.Max(a.X, Math.Max(b.X, c.X))),
                            MathF.BitIncrement(Math.Max(a.Y, Math.Max(b.Y, c.Y)))));
                }
                var nodes = new List<BoundsNode>();
                int Build(int start, int count)
                {
                    var (minimum, maximum) = bounds[_triangles[start]];
                    for (var index = start + 1; index < start + count; index++)
                    {
                        var next = bounds[_triangles[index]];
                        minimum = new(Math.Min(minimum.X, next.Minimum.X), Math.Min(minimum.Y, next.Minimum.Y));
                        maximum = new(Math.Max(maximum.X, next.Maximum.X), Math.Max(maximum.Y, next.Maximum.Y));
                    }
                    var slot = nodes.Count; nodes.Add(default);
                    if (count <= 8) nodes[slot] = new(minimum, maximum, start, count, -1, -1);
                    else
                    {
                        var axis = (double)maximum.Y - minimum.Y > (double)maximum.X - minimum.X ? 1 : 0;
                        Array.Sort(_triangles, start, count, Comparer<int>.Create((left, right) =>
                        {
                            var a = bounds[left]; var b = bounds[right];
                            var order = ((double)a.Minimum[axis] + a.Maximum[axis]).CompareTo(
                                (double)b.Minimum[axis] + b.Maximum[axis]);
                            return order != 0 ? order : left.CompareTo(right);
                        }));
                        var half = count / 2;
                        var left = Build(start, half); var right = Build(start + half, count - half);
                        nodes[slot] = new(minimum, maximum, 0, 0, left, right);
                    }
                    return slot;
                }
                Build(0, _triangles.Length);
                _nodes = nodes.ToArray();
            }

            internal NearestTriangleResult? Nearest(Vector3 point)
            {
                var best = new NearestTriangleResult(-1, default, float.PositiveInfinity);
                Visit(0, point, ref best);
                return best.Index >= 0 ? best : null;
            }

            private void Visit(int slot, Vector3 point, ref NearestTriangleResult best)
            {
                var node = _nodes[slot];
                if (point.X < node.Minimum.X || point.Y < node.Minimum.Y ||
                    point.X > node.Maximum.X || point.Y > node.Maximum.Y) return;
                if (node.Count == 0)
                {
                    Visit(node.Left, point, ref best); Visit(node.Right, point, ref best);
                    return;
                }
                for (var offset = node.Start; offset < node.Start + node.Count; offset++)
                {
                    var index = _triangles[offset];
                    if ((_mesh.Triangles[index].Flags & 8) != 0 ||
                        _mesh.VerticalTrianglePoint(index, point) is not { } plane ||
                        !NativeActorNavigationHeight.Admits(point.Z, plane.Z)) continue;
                    var distance = point.DistanceSquaredTo(plane);
                    if (float.IsFinite(distance) && (distance < best.DistanceSquared ||
                        distance == best.DistanceSquared && (best.Index < 0 || index < best.Index)))
                        best = new(index, plane, distance);
                }
            }
        }
    }
}
