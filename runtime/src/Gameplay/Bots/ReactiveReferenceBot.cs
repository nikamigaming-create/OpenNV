using System.Numerics;

namespace OpenNV.Runtime.Gameplay.Bots;

internal sealed record BotObservation(string Scene, Vector3 Position, Vector3 Camera, Vector3 Forward,
    Vector3 Target, Vector3 Aim, string? AimedReference, bool Paused, bool MovementEnabled,
    bool LookingEnabled, bool Resident, string? Blocker, string InteractionState, bool TravelReady = false,
    BotDoorObservation? Door = null, long ProgressRevision = 0, string ActiveMenus = "", int ControlMask = 0,
    bool ModalInput = false, bool Loading = false, string? ExecutionFault = null, bool Defeated = false,
    BotCombatObservation? Combat = null);

internal sealed record BotDoorObservation(bool Open, bool Moving, bool Pending, string? Error = null);

internal sealed record BotNavigationRoute(IReadOnlyList<Vector3> Waypoints, Vector3 RequestedEndpoint,
    Vector3 ProjectedEndpoint, bool ReachesProjectedEndpoint, string? SourceIdentity = null, float ProjectionRadiusMeters = 0,
    string? RequiredDoor = null, BotNavigationRefinement? Refinement = null);

internal sealed record BotNavigationRefinement(string Scope, Vector3 Target, float ArrivalRadiusMeters,
    string SourceSha256, BotNavigationProjection? Projection = null);

internal sealed record BotNavigationProjection(Vector3 Requested, Vector3 Selected, float Radius, Vector3 Accepted);

// Goals use source references. Travel may approach an authored exterior object
// before it streams in, but arrival still requires its live presentation.
// Navigation and scene queries stay with the engine; steering is reusable C#.
internal sealed partial class ReactiveReferenceBot
{
    internal const float ControlWaitLimitSeconds = 30;
    private readonly Func<string, BotObservation> _observe;
    private readonly Func<Vector3, Vector3, float, BotNavigationRoute> _route;
    private readonly Func<Vector3, Vector3, Vector3, float, float, BotNavigationRoute?>? _approachRoute;
    private readonly Action? _cancelRoute;
    private readonly Action<SteeringIntent, bool> _input;
    private readonly ReactiveSteering _steering = new();
    private IReadOnlyList<Vector3>? _path;
    private BotNavigationRoute? _navigation;
    private BotObservation? _observation;
    private string? _reference, _goalReference, _scene, _interactionBefore;
    private string _mode = "interact", _phase = "idle";
    private string? _error;
    private Vector3 _plannedTarget;
    private Vector3 _segmentStart;
    private Vector3? _motionPosition;
    private int _waypoint, _replans, _movingTargetReplans;
    private int _obstructionReplans, _endpointReplans, _progressWaypoint = -1;
    private float _elapsed, _stalled, _waiting, _endpointAiming, _distance = 1.5f, _approachDistance = 1.5f;
    private float _projectionRadius = 2, _arrivalRadius, _bestWaypointDistance, _segmentStartDistance;
    private float _motionlessSeconds;
    private float _controlWaitSeconds;
    private string? _controlWaitReason;
    private readonly record struct GameplayProgress(string Scene, long QuestRevision, string ActiveMenus, int ControlMask,
        bool MovementEnabled, bool LookingEnabled, bool ModalInput);
    private GameplayProgress? _progress;
    private sealed record RouteRequest(Vector3 Start, Vector3 Endpoint, Vector3 Target);
    private RouteRequest? _request;
    private string? _routeDoor, _resumeMode;
    internal BotCampaignSkill CampaignSkill => new(_reference is not null, _phase, _error, _failureKind);
    internal object State => new
    {
        phase = _phase,
        reference = _goalReference,
        active = _reference is not null,
        mode = _mode,
        routeDoor = _routeDoor,
        scene = _scene,
        elapsedSeconds = _elapsed,
        waypoint = _waypoint,
        waypoints = _path?.Count ?? 0,
        replans = _replans,
        obstructionReplans = _obstructionReplans,
        endpointReplans = _endpointReplans,
        movingTargetReplans = _movingTargetReplans,
        stalledSeconds = _stalled,
        motionlessSeconds = _motionlessSeconds,
        controlWaitSeconds = _controlWaitSeconds,
        controlWaitLimitSeconds = ControlWaitLimitSeconds,
        controlWaitReason = _controlWaitReason,
        progress = _progress,
        requestedDistanceMeters = _distance,
        approachDistanceMeters = _approachDistance,
        targetDistanceMeters = _observation is { } observed ? Vector3.Distance(observed.Position, observed.Target) : (float?)null,
        targetFlatDistanceMeters = _observation is { } flat ? FlatDistance(flat.Position, flat.Target) : (float?)null,
        navigation = _navigation is { } navigation ? new
        {
            navigation.SourceIdentity,
            requestedEndpoint = Coordinates(navigation.RequestedEndpoint),
            projectedEndpoint = Coordinates(navigation.ProjectedEndpoint),
            segmentEndpoint = Coordinates(navigation.Waypoints[^1]),
            navigation.ReachesProjectedEndpoint,
            navigation.ProjectionRadiusMeters,
            refinement = navigation.Refinement is { } refinement ? new
            {
                refinement.Scope,
                target = Coordinates(refinement.Target),
                refinement.ArrivalRadiusMeters,
                refinement.SourceSha256,
                projection = refinement.Projection is { } projection ? new
                {
                    requested = Coordinates(projection.Requested),
                    selected = Coordinates(projection.Selected),
                    projection.Radius,
                    accepted = Coordinates(projection.Accepted)
                } : null
            } : null,
            projectionDistanceMeters = Vector3.Distance(navigation.RequestedEndpoint, navigation.ProjectedEndpoint),
            projectedEndpointDistanceMeters = _observation is { } current ? Vector3.Distance(current.Position, navigation.ProjectedEndpoint) : (float?)null,
            segmentEndpointDistanceMeters = _observation is { } position ? FlatDistance(position.Position, navigation.Waypoints[^1]) : (float?)null,
            segmentStartDistanceMeters = _segmentStartDistance,
            segmentMovementMeters = _observation is { } moved ? Vector3.Distance(_segmentStart, moved.Position) : (float?)null,
            segmentArrivalRadiusMeters = _arrivalRadius,
            plannedTarget = Coordinates(_plannedTarget),
            targetMovementMeters = _observation is { } target ? Vector3.Distance(target.Target, _plannedTarget) : (float?)null
        } : null,
        error = _error,
        failureKind = _failureKind,
        combatTakeover = _combatTakeover,
        pauseAfter = _pauseAfter,
        combat = _combat?.State,
        combatObservation = _combatObservation?.State,
        skills = _combatSkills.State,
        coverage = "ordinary source-reference navigation/activation and bounded receipt-verified single-ray combat; campaign curriculum, other weapon families and retail tactics unbound"
    };

    internal ReactiveReferenceBot(Func<string, BotObservation> observe,
        Func<Vector3, Vector3, float, BotNavigationRoute> route, Action<SteeringIntent, bool> input,
        Func<Vector3, Vector3, Vector3, float, float, BotNavigationRoute?>? approachRoute = null, Action? cancelRoute = null,
        VerifiedBotSkillLibrary? skills = null, Action? persistSkills = null)
    {
        _observe = observe; _route = route; _input = input; _approachRoute = approachRoute; _cancelRoute = cancelRoute;
        _combatSkills = skills ?? new(); _persistSkills = persistSkills;
    }

    internal void Start(string? reference, string mode, float distance, bool combatTakeover = true, bool pauseAfter = true)
    {
        if ((mode != "combat" && string.IsNullOrWhiteSpace(reference)) || mode is not ("interact" or "approach" or "follow" or "travel" or "combat") ||
            !float.IsFinite(distance) || distance is < .5f or > 5)
            throw new ArgumentException("Bot goal requires interact/approach/follow/travel with a source reference, or combat with an optional source threat; distance is 0.5-5 metres.");
        Stop(); _goalReference = string.IsNullOrWhiteSpace(reference) ? null : reference;
        _reference = _goalReference ?? CombatObservationIdentity; _mode = mode;
        _distance = _approachDistance = distance; _phase = "observing";
        _combat = null; _combatFeedbackPublished = false; _combatCount = 0; _phaseBeforeCombat = null;
        _combatTakeover = combatTakeover; _pauseAfter = pauseAfter; _combatObservation = null; _failureKind = null;
        _elapsed = _stalled = _waiting = _endpointAiming = 0;
        _replans = _obstructionReplans = _endpointReplans = _movingTargetReplans = 0; _error = null;
        _navigation = null; _observation = null; _projectionRadius = 2; _progressWaypoint = -1;
        _motionPosition = null; _motionlessSeconds = 0;
        _controlWaitSeconds = 0; _controlWaitReason = null; _progress = null;
    }

    internal void Stop(bool pauseAfter = false)
    {
        if (pauseAfter)
        {
            try { _combatObservation = _observe(CombatObservationIdentity).Combat; }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or KeyNotFoundException or System.Text.Json.JsonException)
            { _combatObservation = null; _error = "Stop protection observation failed: " + error.Message; }
        }
        _combat?.Cancel("Ordinary bot input yielded or stopped.");
        try { if (_reference is not null || pauseAfter) ReleaseBotControls(pauseAfter); }
        finally
        {
            _routeDoor = _resumeMode = null;
            _reference = null; _path = null; _scene = null;
            _interactionBefore = null; _steering.Reset(); _phase = "stopped";
            CancelRoute();
        }
        PublishCombatFeedback();
    }

    internal void Tick(float seconds)
    {
        if (_reference is null) return;
        try
        {
            if (!float.IsFinite(seconds) || seconds <= 0) throw new ArgumentException("Bot frame duration must be finite and positive.");
            var state = _observe(_mode == "combat" || _combat?.Active == true ? CombatObservationIdentity : _reference);
            _combatObservation = state.Combat;
            if (TickCombat(state, seconds)) return;
            if (state.ExecutionFault is { } fault) throw new InvalidOperationException("Gameplay execution stopped: " + fault);
            if (state.Defeated) throw new InvalidOperationException("Player died; load an earlier save to continue the ordinary goal.");
            if (!Finite(state.Position) || !Finite(state.Target)) throw new ArgumentException("Bot observation has a nonfinite position or target.");
            _observation = state;
            var progress = new GameplayProgress(state.Scene, state.ProgressRevision, state.ActiveMenus, state.ControlMask,
                state.MovementEnabled, state.LookingEnabled, state.ModalInput);
            if (_progress != progress) { _progress = progress; _controlWaitSeconds = 0; }
            // A real interaction may open a paused menu. Observe its outcome
            // before suspending clocks, without treating a delivery as success.
            if (_phase == "awaiting-interaction" && state.InteractionState != _interactionBefore)
            { Complete("interaction-observed"); return; }
            if (state.Paused || state.Loading)
            {
                CancelRoute(); _path = null;
                _input(default, false); _steering.Reset();
                if (_phase is not ("awaiting-interaction" or "awaiting-route-door"))
                    _phase = state.Loading ? "loading" : "paused";
                _motionPosition = state.Position; _motionlessSeconds = 0;
                return;
            }
            _elapsed += seconds;
            if (_elapsed > (_mode == "travel" ? 900 : 180)) throw new InvalidOperationException("Goal exceeded its execution bound.");
            if (_routeDoor is not null)
            {
                var door = state.Door ?? throw new InvalidOperationException("Route door lost its resident source motion owner.");
                if (door.Error is not null) throw new InvalidOperationException(door.Error);
                if (door.Open && !door.Moving && !door.Pending)
                {
                    _reference = _goalReference; _mode = _resumeMode!;
                    _routeDoor = _resumeMode = null; _approachDistance = _distance;
                    _projectionRadius = 2; _endpointReplans = _obstructionReplans = 0;
                    Replan("replanning-after-door"); return;
                }
                if (_phase == "awaiting-route-door" || door.Moving || door.Pending)
                {
                    _input(default, false); _waiting += seconds;
                    if (_waiting > 8) throw new InvalidOperationException("Ordinary route-door activation did not produce a settled open source door.");
                    return;
                }
            }
            if (_phase == "awaiting-interaction")
            {
                _input(default, false); _waiting += seconds;
                if (state.InteractionState != _interactionBefore) { Complete("interaction-observed"); return; }
                if (_waiting > 3) throw new InvalidOperationException("Activation had no observed gameplay response.");
                return;
            }
            if (state.ModalInput || !state.MovementEnabled || !state.LookingEnabled ||
                !(state.Resident || _mode == "travel" && state.TravelReady))
            {
                CancelRoute(); _path = null;
                _input(default, false); _steering.Reset(); _phase = "waiting-for-player-control";
                _controlWaitReason = state.ModalInput ? "modal input is held" : !state.MovementEnabled ? "source movement is disabled" :
                    !state.LookingEnabled ? "source looking is disabled" : "the source target or collision is not resident";
                _controlWaitSeconds += seconds;
                if (_controlWaitSeconds > ControlWaitLimitSeconds)
                    throw new InvalidOperationException($"No quest, objective, menu, scene or control progress for {ControlWaitLimitSeconds} seconds while {_controlWaitReason}.");
                _motionPosition = state.Position; _motionlessSeconds = 0; return;
            }
            _controlWaitSeconds = 0; _controlWaitReason = null;
            if (_scene != state.Scene)
            { CancelRoute(); _scene = state.Scene; _path = null; _stalled = _endpointAiming = 0; _projectionRadius = 2; _endpointReplans = 0; }

            var offset = state.Target - state.Position; offset.Y = 0;
            // Route waypoints have a 20 cm arrival radius. Use that same
            // tolerance at the requested standoff instead of stopping just
            // short and subsequently declaring the completed route blocked.
            var near = Vector3.Distance(state.Target, state.Position) <= _approachDistance +
                (_phase == "following-at-distance" ? .3f : Math.Min(.2f, _approachDistance * .25f));
            if (near && !state.Resident)
            {
                _input(default, false); _steering.Reset(); _phase = "waiting-for-target-residency";
                _waiting += seconds;
                if (_waiting > 15) throw new InvalidOperationException("Destination reached but source reference presentation was not admitted.");
                return;
            }
            var aim = state.Aim;
            var move = !near;
            if (near && _request is not null) CancelRoute();
            // A live ordinary activation ray is authoritative even when a
            // reference origin or its projected standoff lies farther away.
            if (_mode == "interact" && state.AimedReference == _reference)
            {
                CancelRoute();
                _interactionBefore = state.InteractionState; _phase = _routeDoor is null ? "awaiting-interaction" : "awaiting-route-door";
                _waiting = 0; _input(default, true);
                return;
            }
            if (move)
            {
                if (_path is not null && Vector3.Distance(state.Target, _plannedTarget) > .4f)
                { CancelRoute(); _path = null; _projectionRadius = 2; _endpointReplans = 0; }
                if (_path is null)
                {
                    if (_request is { } pending && (Vector3.Distance(pending.Start, state.Position) > .2f ||
                        Vector3.Distance(pending.Target, state.Target) > .4f)) CancelRoute();
                    _request ??= new(state.Position, state.Target - (offset.Length() > .001f ? Vector3.Normalize(offset) * _approachDistance : Vector3.Zero), state.Target);
                    var request = _request;
                    var destination = request.Endpoint;
                    var navigation = _approachRoute is not null
                        ? _approachRoute(request.Start, destination, request.Target, _projectionRadius, _approachDistance)
                        : _route(request.Start, destination, _projectionRadius);
                    if (navigation is null)
                    {
                        _input(default, false); _steering.Reset(); _phase = "planning";
                        _motionPosition = state.Position; _motionlessSeconds = _stalled = 0; return;
                    }
                    if (navigation.Waypoints.Count == 0) throw new InvalidOperationException("Navigation returned no route.");
                    if (!Finite(navigation.RequestedEndpoint) || !Finite(navigation.ProjectedEndpoint) || navigation.Waypoints.Any(point => !Finite(point)))
                        throw new InvalidOperationException("Navigation returned a nonfinite endpoint or waypoint.");
                    if (Vector3.Distance(navigation.RequestedEndpoint, destination) > .001f)
                        throw new InvalidOperationException("Navigation returned a route for another requested endpoint.");
                    CancelRoute();
                    if (navigation.RequiredDoor is { } requiredDoor)
                    {
                        if (_routeDoor is not null || requiredDoor == _reference)
                            throw new InvalidOperationException("Route door has no executable ordinary activation approach.");
                        _routeDoor = requiredDoor; _resumeMode = _mode; _reference = requiredDoor; _mode = "interact";
                        _approachDistance = 2; _projectionRadius = 2; _waiting = 0;
                        Replan("approaching-route-door"); return;
                    }
                    _navigation = navigation; _path = navigation.Waypoints;
                    _plannedTarget = request.Target; _waypoint = 0; _replans++;
                    _segmentStart = state.Position;
                    _segmentStartDistance = FlatDistance(state.Position, _path[^1]);
                    // A short segment must close most of its own distance;
                    // a fixed 20 cm tolerance can consume it before any input.
                    _arrivalRadius = Math.Clamp(_segmentStartDistance * .25f, .02f, .2f);
                    _progressWaypoint = -1; _endpointAiming = 0;
                }
                while (_waypoint < _path.Count && FlatDistance(state.Position, _path[_waypoint]) <= (_waypoint == _path.Count - 1 ? _arrivalRadius : .2f) &&
                    MathF.Abs(state.Position.Y - _path[_waypoint].Y) < .6f) _waypoint++;
                if (_waypoint == _path.Count)
                {
                    if (!_navigation!.ReachesProjectedEndpoint)
                    {
                        if (_segmentStartDistance <= _arrivalRadius)
                        { ReplanObstruction("segment already inside its arrival radius without closing the route"); return; }
                        // Only the next segment is capsule-verified. Replan
                        // from observed arrival before entering that corridor.
                        Replan("replanning-segment");
                        return;
                    }
                    if (Vector3.Distance(state.Target, _plannedTarget) > .01f)
                    {
                        // A reached standoff can become stale before the
                        // moving reference crosses the coarse replan threshold.
                        // Its new goal needs a new route, not tighter projection.
                        var closing = _segmentStartDistance - FlatDistance(state.Position, _path[^1]);
                        if (closing > .002f) _obstructionReplans = 0;
                        else if (++_obstructionReplans > 3)
                            throw new InvalidOperationException("Moving target replans made no capsule closing progress.");
                        _movingTargetReplans++; _endpointReplans = 0; _projectionRadius = 2;
                        Replan("replanning-moving-target");
                        return;
                    }
                    // A projected NAVM endpoint is not target-range arrival.
                    // Try the real interaction ray from this supported floor,
                    // then tighten source projection if it cannot reach.
                    move = false;
                    _endpointAiming += seconds;
                    if (_mode != "interact" || _endpointAiming > 3)
                    {
                        ReplanEndpoint(false);
                        return;
                    }
                }
                else { aim = _path[_waypoint]; aim.Y = state.Camera.Y - .08f; }
            }
            else if (_mode == "interact")
            {
                _endpointAiming += seconds;
                if (_endpointAiming > 3)
                {
                    // Standoff is an initial aiming position, not proof that
                    // the ordinary ray reaches the object's real geometry.
                    ReplanEndpoint(true);
                    return;
                }
            }
            else if (_mode == "follow")
            {
                // Following at distance is an observed arrival. Resume from
                // the next live target position instead of an old route end.
                CancelRoute(); _path = null; _projectionRadius = 2;
                _endpointReplans = _obstructionReplans = 0;
            }
            var intent = _steering.Step(state.Camera, state.Forward, aim, move, seconds);
            if (!move) intent = intent with { AimAt = state.Aim };
            if (intent.Forward)
            {
                var remaining = FlatDistance(state.Position, _path![_waypoint]);
                var closingTolerance = Math.Clamp(_arrivalRadius * .01f, .0005f, .002f);
                if (_progressWaypoint != _waypoint || remaining < _bestWaypointDistance - closingTolerance)
                { _progressWaypoint = _waypoint; _bestWaypointDistance = remaining; _stalled = 0; }
                else _stalled += seconds;
                // Moving targets may invalidate waypoints more frequently than
                // the stall interval. Keep a motion observation across replans.
                if (_motionPosition is not { } previous || Vector3.Distance(previous, state.Position) > .001f)
                { _motionPosition = state.Position; _motionlessSeconds = 0; }
                else _motionlessSeconds += seconds;
            }
            else { _stalled = _motionlessSeconds = 0; _motionPosition = state.Position; }
            if (_stalled > .7f || _motionlessSeconds > .7f)
            {
                ReplanObstruction(state.Blocker ?? "no capsule closing progress");
                return;
            }
            _phase = move ? "approaching" : !near ? "aiming-at-projected-endpoint" : _mode == "follow" ? "following-at-distance" : "aiming";
            if (near && _mode is "approach" or "travel") Complete("arrival-observed");
            else _input(intent, false);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException or KeyNotFoundException or System.Text.Json.JsonException)
        {
            Fail(error.Message, error.Message.StartsWith("Bot skill evidence persistence failed:", StringComparison.Ordinal) ? "evidence-store" :
                error is IOException or UnauthorizedAccessException ? "input-adapter" :
                error is NotSupportedException or InvalidDataException or KeyNotFoundException ||
                error.Message.StartsWith("Gameplay execution stopped:", StringComparison.Ordinal) ? "engine-owner" : "bot-policy");
        }
    }

    internal void Fail(string error, string kind = "bot-policy")
    {
        _error = error; _failureKind = kind;
        _combat?.Abort(kind, error);
        try { PublishCombatFeedback(); }
        catch (Exception persistence) when (persistence is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
        { _error += "; skill evidence persistence failed: " + persistence.Message; }
        try { CancelRoute(); }
        catch (Exception cancellation) when (cancellation is IOException or InvalidOperationException or NotSupportedException or ArgumentException)
        { _error += "; route cancellation failed: " + cancellation.Message; }
        _reference = null; _path = null; _steering.Reset(); _phase = "blocked";
        try { ReleaseBotControls(_pauseAfter && (_combat is not null || _mode == "combat")); }
        catch (Exception release) when (release is IOException or InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
        { _error += "; input release failed (device lease expires): " + release.Message; }
    }

    private void CancelRoute() { _request = null; _cancelRoute?.Invoke(); }
    private void Complete(string phase) { CancelRoute(); ReleaseBotControls(_pauseAfter); _reference = null; _path = null; _steering.Reset(); _phase = phase; }
    private void Replan(string phase)
    { CancelRoute(); _path = null; _input(default, false); _steering.Reset(); _stalled = _endpointAiming = 0; _phase = phase; }
    private void ReplanEndpoint(bool closer)
    {
        if (++_endpointReplans > 3)
            throw new InvalidOperationException("Navigation endpoint cannot reach the requested target after bounded closing replans.");
        if (closer) _approachDistance *= .5f;
        // Keep the observed source floor's vertical offset admissible while
        // reducing horizontal projection slack for an elevated object.
        var floorOffset = _navigation is { } navigation ? MathF.Abs(navigation.RequestedEndpoint.Y - navigation.ProjectedEndpoint.Y) : 0;
        _projectionRadius = Math.Max(_projectionRadius * .25f, floorOffset + .01f);
        Replan("replanning-endpoint");
    }
    private void ReplanObstruction(string reason)
    {
        _input(default, false);
        _motionlessSeconds = 0;
        if (++_obstructionReplans > 3)
            throw new InvalidOperationException("Navigation obstructed after bounded replans: " + reason);
        Replan("replanning-obstruction");
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    private static float[] Coordinates(Vector3 value) => [value.X, value.Y, value.Z];
    private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
}
