using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class CellNavigationGraph
{
    private readonly ProjectionIndex? _projectionIndex;

    private NearestNodeResult NearestNode(Vector3 point) => _projectionIndex is not null && point.IsFinite()
        ? _projectionIndex.Nearest(point) : NearestNodeSorted(point);

    // Bounding boxes index immutable source meshes, never a current position,
    // collision result or reachable-floor decision. Leaves retain the original
    // exact mesh distance and FormID visitation, including visited failures.
    private sealed class ProjectionIndex
    {
        private readonly record struct BoundsNode(Vector3 Minimum, Vector3 Maximum, int Mesh,
            int Left, int Right, int FirstMesh);
        private readonly record struct Priority(float Distance, int FirstMesh) : IComparable<Priority>
        {
            public int CompareTo(Priority other)
            {
                var distance = Distance.CompareTo(other.Distance);
                return distance != 0 ? distance : FirstMesh.CompareTo(other.FirstMesh);
            }
        }
        private readonly NavigationMeshRecord[] _meshes;
        private readonly BoundsNode[] _nodes;
        private readonly object _poolGate = new();
        private readonly Stack<PriorityQueue<int, Priority>> _pool = new();

        internal ProjectionIndex(NavigationMeshRecord[] meshes)
        {
            _meshes = meshes;
            var indices = Enumerable.Range(0, meshes.Length).ToArray();
            var nodes = new List<BoundsNode>(checked(meshes.Length * 2 - 1));
            int Build(int start, int count)
            {
                var first = meshes[indices[start]].ProjectionBounds;
                var minimum = first.Minimum; var maximum = first.Maximum;
                var firstMesh = indices[start];
                for (var index = start + 1; index < start + count; index++)
                {
                    var bounds = meshes[indices[index]].ProjectionBounds;
                    minimum = new(Math.Min(minimum.X, bounds.Minimum.X), Math.Min(minimum.Y, bounds.Minimum.Y), Math.Min(minimum.Z, bounds.Minimum.Z));
                    maximum = new(Math.Max(maximum.X, bounds.Maximum.X), Math.Max(maximum.Y, bounds.Maximum.Y), Math.Max(maximum.Z, bounds.Maximum.Z));
                    firstMesh = Math.Min(firstMesh, indices[index]);
                }
                var slot = nodes.Count; nodes.Add(default);
                if (count == 1)
                    nodes[slot] = new(minimum, maximum, indices[start], -1, -1, firstMesh);
                else
                {
                    var axis = 0;
                    if ((double)maximum.Y - minimum.Y > (double)maximum.X - minimum.X) axis = 1;
                    if ((double)maximum.Z - minimum.Z > (double)maximum[axis] - minimum[axis]) axis = 2;
                    Array.Sort(indices, start, count, Comparer<int>.Create((left, right) =>
                    {
                        var a = meshes[left].ProjectionBounds; var b = meshes[right].ProjectionBounds;
                        var order = ((double)a.Minimum[axis] + a.Maximum[axis]).CompareTo((double)b.Minimum[axis] + b.Maximum[axis]);
                        return order != 0 ? order : left.CompareTo(right);
                    }));
                    var half = count / 2;
                    var left = Build(start, half); var right = Build(start + half, count - half);
                    nodes[slot] = new(minimum, maximum, -1, left, right, firstMesh);
                }
                return slot;
            }
            Build(0, indices.Length);
            _nodes = nodes.ToArray();
        }

        internal NearestNodeResult Nearest(Vector3 point)
        {
            PriorityQueue<int, Priority> pending;
            lock (_poolGate) pending = _pool.Count == 0 ? new() : _pool.Pop();
            try
            {
                void Enqueue(int index)
                {
                    var node = _nodes[index];
                    var distance = node.Mesh >= 0 ? _meshes[node.Mesh].BoundsDistanceSquared(point) :
                        point.DistanceSquaredTo(new Vector3(Math.Clamp(point.X, node.Minimum.X, node.Maximum.X),
                            Math.Clamp(point.Y, node.Minimum.Y, node.Maximum.Y), Math.Clamp(point.Z, node.Minimum.Z, node.Maximum.Z)));
                    pending.Enqueue(index, new(distance, node.FirstMesh));
                }
                Enqueue(0);
                var hasBest = false;
                NearestNodeResult best = default;
                while (pending.TryDequeue(out var index, out var priority))
                {
                    // Union bounds are a monotone lower bound on the original
                    // leaf-box distances. Equality must remain visitable.
                    if (hasBest && priority.Distance > best.DistanceSquared) break;
                    var node = _nodes[index];
                    if (node.Mesh < 0) { Enqueue(node.Left); Enqueue(node.Right); continue; }
                    var mesh = _meshes[node.Mesh];
                    var nearest = mesh.NearestTriangle(point);
                    if (!hasBest || nearest.DistanceSquared < best.DistanceSquared ||
                        nearest.DistanceSquared == best.DistanceSquared &&
                        StringComparer.OrdinalIgnoreCase.Compare(mesh.FormId, best.Node.NavMesh.FormId) < 0)
                    {
                        best = new(new(mesh, nearest.Index), nearest.Point, nearest.DistanceSquared);
                        hasBest = true;
                    }
                }
                return best;
            }
            finally
            {
                var retain = pending.EnsureCapacity(0) <= 8192;
                pending.Clear();
                if (retain)
                    lock (_poolGate)
                        if (_pool.Count < 4) _pool.Push(pending);
            }
        }
    }
}
