using System.Numerics;

namespace OpenNV.Runtime.Gameplay.Bots;

internal sealed record BotObservation(string Scene, Vector3 Position, Vector3 Camera, Vector3 Forward,
    Vector3 Target, Vector3 Aim, string? AimedReference, bool Paused, bool MovementEnabled,
    bool LookingEnabled, bool Resident, string? Blocker, string InteractionState);

// A goal is a resident source reference, never a timed sequence of poses.
// Navigation and scene queries stay with the engine; steering is reusable C#.
internal sealed class ReactiveReferenceBot
{
    private readonly Func<string, BotObservation> _observe;
    private readonly Func<Vector3, Vector3, IReadOnlyList<Vector3>> _route;
    private readonly Action<SteeringIntent, bool> _input;
    private readonly ReactiveSteering _steering = new();
    private IReadOnlyList<Vector3>? _path;
    private string? _reference, _scene, _interactionBefore;
    private string _mode = "interact", _phase = "idle";
    private string? _error;
    private Vector3 _plannedTarget, _progressPosition;
    private int _waypoint, _replans;
    private float _elapsed, _stalled, _waiting, _distance = 1.5f;
    internal object State => new
    {
        phase = _phase,
        reference = _reference,
        mode = _mode,
        scene = _scene,
        elapsedSeconds = _elapsed,
        waypoint = _waypoint,
        waypoints = _path?.Count ?? 0,
        replans = _replans,
        stalledSeconds = _stalled,
        error = _error,
        coverage = "resident-reference approach/follow/activation; campaign decision-making and combat tactics unbound"
    };

    internal ReactiveReferenceBot(Func<string, BotObservation> observe,
        Func<Vector3, Vector3, IReadOnlyList<Vector3>> route, Action<SteeringIntent, bool> input)
    { _observe = observe; _route = route; _input = input; }

    internal void Start(string reference, string mode, float distance)
    {
        if (string.IsNullOrWhiteSpace(reference) || mode is not ("interact" or "approach" or "follow") ||
            !float.IsFinite(distance) || distance is < .5f or > 5)
            throw new ArgumentException("Bot goal requires a source reference, interact/approach/follow and distance 0.5-5 metres.");
        Stop(); _reference = reference; _mode = mode; _distance = distance; _phase = "observing";
        _elapsed = _stalled = _waiting = 0; _replans = 0; _error = null;
    }

    internal void Stop()
    {
        if (_reference is not null) _input(default, false);
        _reference = null; _path = null; _scene = null;
        _interactionBefore = null; _steering.Reset(); _phase = "stopped";
    }

    internal void Tick(float seconds)
    {
        if (_reference is null) return;
        try
        {
            var state = _observe(_reference);
            _elapsed += seconds;
            if (_elapsed > 180) throw new InvalidOperationException("Goal exceeded its three-minute bound.");
            if (_phase == "awaiting-interaction")
            {
                _input(default, false); _waiting += seconds;
                if (state.InteractionState != _interactionBefore) { Complete("interaction-observed"); return; }
                if (_waiting > 3) throw new InvalidOperationException("Activation had no observed gameplay response.");
                return;
            }
            if (state.Paused || !state.MovementEnabled || !state.LookingEnabled || !state.Resident)
            { _input(default, false); _steering.Reset(); _phase = "waiting-for-player-control"; return; }
            if (_scene != state.Scene)
            { _scene = state.Scene; _path = null; _stalled = 0; _progressPosition = state.Position; }

            var offset = state.Target - state.Position; offset.Y = 0;
            // Route waypoints have a 20 cm arrival radius. Use that same
            // tolerance at the requested standoff instead of stopping just
            // short and subsequently declaring the completed route blocked.
            var near = offset.Length() <= _distance + (_phase == "following-at-distance" ? .3f : .2f);
            var aim = state.Aim;
            var move = !near;
            if (move)
            {
                if (_path is null || Vector3.Distance(state.Target, _plannedTarget) > .4f)
                {
                    var destination = state.Target - Vector3.Normalize(offset) * _distance;
                    _path = _route(state.Position, destination);
                    if (_path.Count == 0) throw new InvalidOperationException("Navigation returned no route.");
                    _plannedTarget = state.Target; _waypoint = 0; _replans++;
                }
                while (_waypoint < _path.Count && FlatDistance(state.Position, _path[_waypoint]) < .2f &&
                    MathF.Abs(state.Position.Y - _path[_waypoint].Y) < .6f) _waypoint++;
                if (_waypoint == _path.Count)
                {
                    move = false;
                    _waiting += seconds;
                    if (_waiting > 3 && _mode != "interact")
                        throw new InvalidOperationException("Route ended outside the requested approach distance.");
                }
                else { aim = _path[_waypoint]; aim.Y = state.Camera.Y - .08f; }
            }
            var intent = _steering.Step(state.Camera, state.Forward, aim, move, seconds);
            if (!move) intent = intent with { AimAt = state.Aim };
            if (intent.Forward && Vector3.Distance(state.Position, _progressPosition) < .04f) _stalled += seconds;
            else { _progressPosition = state.Position; _stalled = 0; }
            if (_stalled > .7f)
                throw new InvalidOperationException("Navigation obstructed: " + (state.Blocker ?? "no capsule progress; dynamic avoidance is unbound"));
            _phase = move ? "approaching" : _mode == "follow" ? "following-at-distance" : "aiming";
            var activate = _mode == "interact" && state.AimedReference == _reference;
            if (activate)
            {
                _interactionBefore = state.InteractionState; _phase = "awaiting-interaction";
                _waiting = 0; _input(intent with { Forward = false, AimAt = state.Aim }, true);
            }
            else if (near && _mode == "approach") Complete("arrival-observed");
            else _input(intent, false);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or ArgumentException or NotSupportedException or KeyNotFoundException or System.Text.Json.JsonException)
        {
            Fail(error.Message);
        }
    }

    internal void Fail(string error)
    {
        _error = error; _reference = null; _path = null; _steering.Reset(); _phase = "blocked";
        try { _input(default, false); }
        catch (Exception release) when (release is IOException or InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
        { _error += "; input release failed (device lease expires): " + release.Message; }
    }

    private void Complete(string phase) { _input(default, false); _reference = null; _path = null; _steering.Reset(); _phase = phase; }
    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
