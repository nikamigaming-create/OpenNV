using Godot;

using OpenNV.Runtime.SceneGraph;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Interactions;

internal partial class PoolTableInstance : Node3D
{
    private readonly List<PoolBallInstance> _balls = new();
    private PoolConfiguration _configuration = null!;
    private GameplaySession _session = null!;
    private Node3D _cuePlacement = null!;
    private Node3D _cueVisual = null!;
    private Node3D _rackPlacement = null!;
    private PoolBallInstance _cueBall = null!;
    private string _cueTipEndpoint = "";
    private Aabb _cueBounds;
    private int _flatPowerIndex;
    private float _strokeCooldownSeconds;
    private Vector3 _previousTrackedTip;
    private bool _hasTrackedTip;

    internal string ReferenceFormId { get; private set; } = "";
    internal string PresentationModelPath { get; private set; } = "";
    internal string GameplayCollisionSource { get; private set; } = "";
    internal bool IsPlayActive { get; private set; }
    internal int BallCount => _balls.Count;
    internal int PocketedBallCount => _balls.Count(ball => ball.IsPocketed);
    internal IReadOnlyList<PoolBallInstance> Balls => _balls;
    internal PoolBallInstance CueBall => _cueBall;

    internal void Configure(
        string referenceFormId,
        string presentationModelPath,
        string gameplayCollisionSource,
        RuntimeConfiguration configuration,
        GameplaySession session)
    {
        ReferenceFormId = referenceFormId;
        PresentationModelPath = presentationModelPath;
        GameplayCollisionSource = gameplayCollisionSource;
        _configuration = configuration.Pool;
        _session = session;
        Name = $"POOL_TABLE_{referenceFormId}";
    }

    internal void CompleteSetup(
        Node3D cuePlacement,
        Node3D cueVisual,
        Node3D rackPlacement,
        IReadOnlyList<PoolBallInstance> balls,
        string cueTipEndpoint)
    {
        _cuePlacement = cuePlacement;
        _cueVisual = cueVisual;
        _rackPlacement = rackPlacement;
        _cueTipEndpoint = cueTipEndpoint;
        _balls.AddRange(balls);
        _cueBall = _balls.Single(ball => ball.Role == "cue-ball");
        foreach (var ball in _balls)
            ball.Table = this;
        _cueBounds = VisualBounds(_cueVisual);
        if (_cueBounds.Size.IsZeroApprox())
            throw new InvalidOperationException("Authored pool cue has no renderable bounds.");
        _session.RegisterPool(this);
    }

    public override void _PhysicsProcess(double delta)
    {
        _strokeCooldownSeconds = MathF.Max(
            0.0f,
            _strokeCooldownSeconds - (float)delta);
        foreach (var ball in _balls.Where(ball => !ball.IsPocketed))
        {
            if (ball.GlobalPosition.Y < GlobalPosition.Y)
                ball.SetPocketed();
        }
    }

    internal CuePresentation CreateCuePresentation()
    {
        var visual = _cueVisual.Duplicate((int)Node.DuplicateFlags.Default) as Node3D
            ?? throw new InvalidOperationException("Could not duplicate the authored pool cue.");
        visual.Transform = Transform3D.Identity;
        var center = _cueBounds.GetCenter();
        var tip = _cueTipEndpoint switch
        {
            "maximum-z" => new Vector3(center.X, center.Y, _cueBounds.End.Z),
            "minimum-z" => new Vector3(center.X, center.Y, _cueBounds.Position.Z),
            _ => throw new InvalidOperationException($"Unsupported pool cue tip endpoint: {_cueTipEndpoint}"),
        };
        return new CuePresentation(visual, tip);
    }

    internal void SetPlayActive(bool active)
    {
        IsPlayActive = active;
        _cuePlacement.Visible = !active;
        _rackPlacement.Visible = !active;
        _hasTrackedTip = false;
        _session.Notify(
            active
                ? $"Pool practice active • power {FlatPowerMetersPerSecond:F2} m/s • wheel changes power • R resets"
                : "Pool practice ended");
    }

    internal void CycleFlatPower(int direction)
    {
        if (direction == 0)
            return;
        _flatPowerIndex += Math.Sign(direction);
        if (_flatPowerIndex < 0)
            _flatPowerIndex = _configuration.FlatStrikeSpeedsMetersPerSecond.Count - 1;
        else if (_flatPowerIndex >= _configuration.FlatStrikeSpeedsMetersPerSecond.Count)
            _flatPowerIndex = 0;
        _session.Notify($"Pool strike power {FlatPowerMetersPerSecond:F2} m/s");
    }

    internal bool StrikeFlat(Vector3 cameraForward)
    {
        cameraForward.Y = 0.0f;
        if (cameraForward.IsZeroApprox())
            return false;
        return Strike(cameraForward.Normalized(), FlatPowerMetersPerSecond, "desktop");
    }

    internal void SelectMaximumFlatPowerForProof()
    {
        _flatPowerIndex = Enumerable.Range(
                0,
                _configuration.FlatStrikeSpeedsMetersPerSecond.Count)
            .MaxBy(index => _configuration.FlatStrikeSpeedsMetersPerSecond[index]);
    }

    internal float SelectedFlatPowerMetersPerSecond => FlatPowerMetersPerSecond;

    internal bool UpdateTrackedCue(Vector3 trackedTip, bool strokeArmed, double delta)
    {
        if (!_hasTrackedTip)
        {
            _previousTrackedTip = trackedTip;
            _hasTrackedTip = true;
            return false;
        }
        var movement = trackedTip - _previousTrackedTip;
        var previous = _previousTrackedTip;
        _previousTrackedTip = trackedTip;
        if (!strokeArmed || _strokeCooldownSeconds > 0.0f || movement.IsZeroApprox() || delta <= 0.0)
            return false;
        var closest = Geometry3D.GetClosestPointToSegment(_cueBall.GlobalPosition, previous, trackedTip);
        if (closest.DistanceTo(_cueBall.GlobalPosition) > _cueBall.CollisionRadiusMeters)
            return false;
        var direction = movement.Normalized();
        var towardBall = (_cueBall.GlobalPosition - previous).Normalized();
        if (direction.Dot(towardBall) <= 0.0f)
            return false;
        var measuredSpeed = movement.Length() / (float)delta;
        if (measuredSpeed < _configuration.XrMinimumTipSpeedMetersPerSecond)
            return false;
        var speed = MathF.Min(
            measuredSpeed,
            _configuration.XrMaximumTipSpeedMetersPerSecond);
        return Strike(direction, speed * _configuration.XrImpulseScale, "openxr-tracked-cue");
    }

    internal void ResetAuthored()
    {
        foreach (var ball in _balls)
            ball.ResetAuthored();
        _strokeCooldownSeconds = 0.0f;
        _session.Save();
        _session.Notify(_configuration.ResetStatusText);
    }

    internal PoolState CaptureState() => new(
        ReferenceFormId,
        _balls.Select(ball => ball.CaptureState()).ToArray());

    internal void RestoreState(PoolState state)
    {
        if (!state.ReferenceFormId.Equals(ReferenceFormId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pool state belongs to another table.");
        var states = state.Balls.ToDictionary(
            ball => ball.ReferenceFormId,
            StringComparer.OrdinalIgnoreCase);
        if (states.Count != _balls.Count)
            throw new InvalidOperationException("Pool save ball set disagrees with the prepared cell.");
        foreach (var ball in _balls)
            ball.RestoreState(states[ball.ReferenceFormId]);
    }

    private float FlatPowerMetersPerSecond =>
        _configuration.FlatStrikeSpeedsMetersPerSecond[_flatPowerIndex];

    private bool Strike(Vector3 direction, float speedMetersPerSecond, string source)
    {
        if (_cueBall.IsPocketed)
        {
            _session.Notify("Cue ball is pocketed • press R to reset");
            return false;
        }
        _cueBall.ApplyCentralImpulse(direction * _cueBall.Mass * speedMetersPerSecond);
        _cueBall.Sleeping = false;
        _strokeCooldownSeconds = _configuration.XrStrikeCooldownSeconds;
        _session.Notify($"Pool strike • {source} • {speedMetersPerSecond:F2} m/s");
        return true;
    }

    private static Aabb VisualBounds(Node3D root)
    {
        var hasBounds = false;
        var bounds = new Aabb();
        foreach (var mesh in NodeTraversal.Descendants<MeshInstance3D>(root))
        {
            var meshBounds = mesh.GetAabb();
            foreach (var x in new[] { meshBounds.Position.X, meshBounds.End.X })
                foreach (var y in new[] { meshBounds.Position.Y, meshBounds.End.Y })
                    foreach (var z in new[] { meshBounds.Position.Z, meshBounds.End.Z })
                    {
                        var point = root.ToLocal(mesh.ToGlobal(new Vector3(x, y, z)));
                        bounds = hasBounds ? bounds.Expand(point) : new Aabb(point, Vector3.Zero);
                        hasBounds = true;
                    }
        }
        return bounds;
    }

    internal readonly record struct CuePresentation(Node3D Visual, Vector3 TipGodotUnits);

    internal readonly record struct PoolState(
        string ReferenceFormId,
        IReadOnlyList<PoolBallInstance.BallState> Balls);
}
