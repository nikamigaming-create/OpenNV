using Godot;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class CellNavigationGraph
{
    private readonly ProjectionIndex? _projectionIndex;

    private NearestNodeResult NearestNode(Vector3 point) => _projectionIndex is not null && point.IsFinite()
        ? _projectionIndex.Nearest(point) : NearestNodeSorted(point);

    private NearestNodeResult NearestActorNode(Vector3 point)
    {
        if (_projectionIndex is not null) return _projectionIndex.NearestVertical(point) ?? NearestNode(point);
        NearestNodeResult? best = null;
        foreach (var mesh in _projectionMeshes)
        {
            var bounds = mesh.ProjectionBounds;
            if (!NativeActorNavigationHeight.ColumnIntersects(point, bounds.Minimum, bounds.Maximum) ||
                mesh.NearestVerticalTriangle(point) is not { } nearest) continue;
            if (best is null || nearest.DistanceSquared < best.Value.DistanceSquared)
                best = new(new(mesh, nearest.Index), nearest.Point, nearest.DistanceSquared);
        }
        // Outside-column projection retains the existing edge-query lane. It
        // cannot create horizontal membership or physical ground support.
        return best ?? NearestNode(point);
    }

    private bool HasApproachRegion(Vector3 target, float radius, bool actorHeightProjection)
    {
        if (_projectionIndex is not null) return _projectionIndex.AnyApproachRegion(target, radius, actorHeightProjection);
        foreach (var mesh in _projectionMeshes)
        {
            var bounds = mesh.ProjectionBounds;
            if (ApproachBounds(target, radius, actorHeightProjection, bounds.Minimum, bounds.Maximum) &&
                MeshHasApproachRegion(mesh, target, radius, actorHeightProjection)) return true;
        }
        return false;
    }

    private static bool ApproachBounds(Vector3 target, float radius, bool actorHeightProjection, Vector3 minimum, Vector3 maximum)
    {
        // Rounded source plane normals can put the admitted plane sample
        // just outside vertex Z bounds. Actor height is tested only by the
        // exact triangle predicate; the broad phase conservatively uses XY.
        var nearest = new Vector3(Math.Clamp(target.X, minimum.X, maximum.X), Math.Clamp(target.Y, minimum.Y, maximum.Y),
            actorHeightProjection ? target.Z : Math.Clamp(target.Z, minimum.Z, maximum.Z));
        return nearest.DistanceSquaredTo(target) <= radius * radius;
    }

    private static bool MeshHasApproachRegion(NavigationMeshRecord mesh, Vector3 target, float radius, bool actorHeightProjection)
    {
        for (var index = 0; index < mesh.Triangles.Count; index++)
            if ((mesh.Triangles[index].Flags & 8) == 0 && InApproachRegion(new(mesh, index), target, radius, actorHeightProjection)) return true;
        return false;
    }

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

        internal bool AnyApproachRegion(Vector3 target, float radius, bool actorHeightProjection) =>
            AnyApproachRegion(0, target, radius, actorHeightProjection);

        private bool AnyApproachRegion(int index, Vector3 target, float radius, bool actorHeightProjection)
        {
            var node = _nodes[index];
            if (!ApproachBounds(target, radius, actorHeightProjection, node.Minimum, node.Maximum)) return false;
            if (node.Mesh >= 0) return MeshHasApproachRegion(_meshes[node.Mesh], target, radius, actorHeightProjection);
            return AnyApproachRegion(node.Left, target, radius, actorHeightProjection) ||
                AnyApproachRegion(node.Right, target, radius, actorHeightProjection);
        }

        internal NearestNodeResult? NearestVertical(Vector3 point)
        {
            PriorityQueue<int, Priority> pending;
            lock (_poolGate) pending = _pool.Count == 0 ? new() : _pool.Pop();
            try
            {
                void Enqueue(int index)
                {
                    var node = _nodes[index];
                    if (!NativeActorNavigationHeight.ColumnIntersects(point, node.Minimum, node.Maximum)) return;
                    var dz = Math.Clamp(point.Z, node.Minimum.Z, node.Maximum.Z) - point.Z;
                    pending.Enqueue(index, new(dz * dz, node.FirstMesh));
                }
                Enqueue(0);
                NearestNodeResult? best = null;
                while (pending.TryDequeue(out var index, out var priority))
                {
                    if (best is { } retained && priority.Distance > retained.DistanceSquared) break;
                    var node = _nodes[index];
                    if (node.Mesh < 0) { Enqueue(node.Left); Enqueue(node.Right); continue; }
                    var mesh = _meshes[node.Mesh];
                    if (mesh.NearestVerticalTriangle(point) is not { } nearest) continue;
                    if (best is null || nearest.DistanceSquared < best.Value.DistanceSquared ||
                        nearest.DistanceSquared == best.Value.DistanceSquared &&
                        StringComparer.OrdinalIgnoreCase.Compare(mesh.FormId, best.Value.Node.NavMesh.FormId) < 0)
                        best = new(new(mesh, nearest.Index), nearest.Point, nearest.DistanceSquared);
                }
                return best;
            }
            finally
            {
                var retain = pending.EnsureCapacity(0) <= 8192;
                pending.Clear();
                if (retain) lock (_poolGate) if (_pool.Count < 4) _pool.Push(pending);
            }
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
