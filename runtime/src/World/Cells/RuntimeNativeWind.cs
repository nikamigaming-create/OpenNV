using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

// One exterior owns wind for its resident source bodies. Tree notifications
// include streamed additions; a disabled/warm reference cannot receive force.
internal partial class RuntimeNativeWind : Node
{
    private readonly HashSet<RuntimeNifRigidBody> _bodies = [];
    private Func<(float Speed, float Heading)> _sample = null!;
    private Node _scene = null!;
    private SceneTree? _tree;
    private float _units;
    private long _steps, _wakes;
    private float _speed, _heading;

    internal void Configure(Func<(float Speed, float Heading)> sample, float unitsToMetres)
    {
        if (!float.IsFinite(unitsToMetres) || unitsToMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsToMetres));
        _sample = sample; _units = unitsToMetres;
    }

    internal object State => new
    {
        speed = _speed,
        headingRadians = _heading,
        bodies = _bodies.Count,
        steps = _steps,
        wakes = _wakes,
        active = _bodies.Count(body => body.CanProcess() && !body.Freeze),
        sleeping = _bodies.Count(body => body.Sleeping),
        instances = _bodies.Select(body => new
        {
            path = body.GetPath().ToString(),
            position = new[] { body.GlobalPosition.X, body.GlobalPosition.Y, body.GlobalPosition.Z },
            velocity = new[] { body.LinearVelocity.X, body.LinearVelocity.Y, body.LinearVelocity.Z },
            sleeping = body.Sleeping,
            active = body.CanProcess() && !body.Freeze
        }).ToArray(),
        boundary = "source-wind-flag;weather-strength;per-body-gusts;native-contacts;retail-motion-unmatched"
    };

    public override void _EnterTree()
    {
        _scene = GetParent(); _tree = GetTree();
        foreach (var body in _scene.FindChildren("*", "", true, false).OfType<RuntimeNifRigidBody>()) Added(body);
        _tree.NodeAdded += Added; _tree.NodeRemoved += Removed;
    }

    private void Added(Node node)
    {
        if (node is RuntimeNifRigidBody body && _scene.IsAncestorOf(body) &&
            body.GetViewport() == _scene.GetViewport() && body.RespondsToWind) _bodies.Add(body);
    }

    private void Removed(Node node) { if (node is RuntimeNifRigidBody body) _bodies.Remove(body); }

    public override void _PhysicsProcess(double delta)
    {
        (_speed, _heading) = _sample();
        if (_speed == 0 || delta <= 0) return;
        foreach (var body in _bodies)
        {
            if (!body.CanProcess() || body.Freeze || body.IsQueuedForDeletion()) continue;
            if (body.Sleeping) _wakes++;
            body.ApplyWind(_speed, _heading, delta, _units);
            _steps++;
        }
    }

    public override void _ExitTree()
    {
        if (_tree is not null) { _tree.NodeAdded -= Added; _tree.NodeRemoved -= Removed; }
        _tree = null; _bodies.Clear();
    }
}
