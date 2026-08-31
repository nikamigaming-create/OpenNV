using Godot;

using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.World.Streaming;

internal sealed class CellActiveSet
{
    private readonly IReadOnlyDictionary<string, SpaceState> _spaces;
    private readonly List<Update> _updates = new();
    private IReadOnlySet<string> _activeCellFormIds = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase);

    internal CellActiveSet(
        IEnumerable<Space> spaces,
        IEnumerable<(string FromCellFormId, string ToCellFormId)> edges)
    {
        var states = spaces
            .Select(SpaceState.Capture)
            .ToArray();
        _spaces = states.ToDictionary(
            state => state.FormId,
            StringComparer.OrdinalIgnoreCase);
        if (_spaces.Count != states.Length)
            throw new InvalidOperationException("Active CELL set contains duplicate CELL identities.");
        var roots = states.SelectMany(state => state.Roots).ToArray();
        if (roots.Distinct().Count() != roots.Length)
            throw new InvalidOperationException("Active CELL set contains duplicate owned roots.");

        foreach (var edge in edges)
        {
            if (!_spaces.ContainsKey(edge.FromCellFormId) ||
                !_spaces.ContainsKey(edge.ToCellFormId))
                throw new InvalidOperationException(
                    $"Active CELL edge references an unloaded CELL: " +
                    $"{edge.FromCellFormId} -> {edge.ToCellFormId}");
            if (edge.FromCellFormId.Equals(
                    edge.ToCellFormId,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Active CELL edge cannot link a CELL to itself: {edge.FromCellFormId}");
        }
    }

    internal IReadOnlySet<string> ActiveCellFormIds => _activeCellFormIds;

    internal IReadOnlyList<Update> Updates => _updates;

    internal void Activate(string activeCellFormId)
    {
        if (!_spaces.ContainsKey(activeCellFormId))
            throw new InvalidOperationException(
                $"Cannot activate an unloaded CELL: {activeCellFormId}");

        var next = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            activeCellFormId,
        };
        foreach (var state in _spaces.Values)
            state.SetActive(next.Contains(state.FormId));
        _activeCellFormIds = next;
        _updates.Add(new Update(
            activeCellFormId,
            next.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            _spaces.Keys
                .Where(value => !next.Contains(value))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()));
    }

    internal IReadOnlyList<SpaceSnapshot> Snapshot() => _spaces.Values
        .OrderBy(state => state.FormId, StringComparer.OrdinalIgnoreCase)
        .Select(state => state.Snapshot(_activeCellFormIds.Contains(state.FormId)))
        .ToArray();

    internal readonly record struct Space(
        string FormId,
        IReadOnlyList<Node3D> Roots,
        IReadOnlyList<Light3D> Lights);

    internal readonly record struct Update(
        string CurrentCellFormId,
        IReadOnlyList<string> ActiveCellFormIds,
        IReadOnlyList<string> SuspendedCellFormIds);

    internal readonly record struct SpaceSnapshot(
        string FormId,
        bool Active,
        int Roots,
        int SourceVisibleRoots,
        int VisibleRoots,
        int SourceProcessingRoots,
        int ProcessingRoots,
        int CollisionObjects,
        int SourceEnabledCollisionObjects,
        int EnabledCollisionObjects,
        int RigidBodies,
        int SourceFrozenRigidBodies,
        int FrozenRigidBodies,
        int Lights,
        int SourceVisibleLights,
        int VisibleLights);

    private sealed class SpaceState
    {
        private readonly IReadOnlyList<NodeState> _roots;
        private readonly IReadOnlyList<CollisionState> _collisions;
        private readonly IReadOnlyList<RigidBodyState> _rigidBodies;
        private readonly IReadOnlyList<LightState> _lights;

        private SpaceState(
            string formId,
            IReadOnlyList<NodeState> roots,
            IReadOnlyList<CollisionState> collisions,
            IReadOnlyList<RigidBodyState> rigidBodies,
            IReadOnlyList<LightState> lights)
        {
            FormId = formId;
            _roots = roots;
            _collisions = collisions;
            _rigidBodies = rigidBodies;
            _lights = lights;
        }

        internal string FormId { get; }

        internal IReadOnlyList<Node3D> Roots => _roots.Select(root => root.Node).ToArray();

        internal static SpaceState Capture(Space space)
        {
            var roots = space.Roots.Distinct().ToArray();
            if (roots.Length == 0 || roots.Length != space.Roots.Count)
                throw new InvalidOperationException(
                    $"CELL activity ownership is empty or duplicated: {space.FormId}");
            if (roots.Any(root => roots.Any(other => other != root && other.IsAncestorOf(root))))
                throw new InvalidOperationException(
                    $"CELL activity roots overlap: {space.FormId}");
            return new SpaceState(
                FalloutFormId.Normalize(space.FormId),
                roots.Select(root => new NodeState(
                    root,
                    root.Visible,
                    root.ProcessMode)).ToArray(),
                roots.SelectMany(NodeTraversal.SelfAndDescendants<CollisionObject3D>)
                    .Distinct()
                    .Select(collision => new CollisionState(collision, collision.CollisionLayer))
                    .ToArray(),
                roots.SelectMany(NodeTraversal.SelfAndDescendants<RigidBody3D>)
                    .Distinct()
                    .Select(body => new RigidBodyState(body, body.Freeze))
                    .ToArray(),
                space.Lights
                    .Distinct()
                    .Select(light => new LightState(light, light.Visible))
                    .ToArray());
        }

        internal void SetActive(bool active)
        {
            foreach (var root in _roots)
            {
                root.Node.Visible = active && root.Visible;
                root.Node.ProcessMode = active
                    ? root.ProcessMode
                    : Node.ProcessModeEnum.Disabled;
            }
            foreach (var collision in _collisions)
                collision.Node.CollisionLayer = active ? collision.Layer : 0u;
            foreach (var body in _rigidBodies)
                body.Node.Freeze = active ? body.Frozen : true;
            foreach (var light in _lights)
                light.Node.Visible = active && light.Visible;
        }

        internal SpaceSnapshot Snapshot(bool active) => new(
            FormId,
            active,
            _roots.Count,
            _roots.Count(root => root.Visible),
            _roots.Count(root => root.Node.Visible),
            _roots.Count(root => root.ProcessMode != Node.ProcessModeEnum.Disabled),
            _roots.Count(root => root.Node.ProcessMode != Node.ProcessModeEnum.Disabled),
            _collisions.Count,
            _collisions.Count(collision => collision.Layer != 0u),
            _collisions.Count(collision => collision.Node.CollisionLayer != 0u),
            _rigidBodies.Count,
            _rigidBodies.Count(body => body.Frozen),
            _rigidBodies.Count(body => body.Node.Freeze),
            _lights.Count,
            _lights.Count(light => light.Visible),
            _lights.Count(light => light.Node.Visible));

    }

    private readonly record struct NodeState(
        Node3D Node,
        bool Visible,
        Node.ProcessModeEnum ProcessMode);

    private readonly record struct CollisionState(CollisionObject3D Node, uint Layer);

    private readonly record struct RigidBodyState(RigidBody3D Node, bool Frozen);

    private readonly record struct LightState(Light3D Node, bool Visible);
}
