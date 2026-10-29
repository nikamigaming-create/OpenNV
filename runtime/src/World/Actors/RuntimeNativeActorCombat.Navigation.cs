using System.Diagnostics;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal readonly record struct NativePackageRouteIdentity(FalloutFormKey Package, Vector3 Target, float Radius,
    string SourceSha256, NativeActorCombatContext Context, ulong Body, ulong PhysicsWorld, Shape3D Envelope, Vector3 Scale)
{
    internal bool Matches(NativePackageRouteIdentity current) => Package == current.Package && Target == current.Target &&
        Radius == current.Radius && SourceSha256.Equals(current.SourceSha256, StringComparison.OrdinalIgnoreCase) &&
        ReferenceEquals(Context, current.Context) && Body == current.Body && PhysicsWorld == current.PhysicsWorld &&
        ReferenceEquals(Envelope, current.Envelope) && Scale.IsEqualApprox(current.Scale);
}

internal sealed record NativePackageRouteRequest(NativePackageRouteIdentity Identity, NativeActorNavigationRoute Source,
    NativeNavigationIntent Intent, int SourcePoints);

// Disposable query evidence. Only Wander's consumed request and actual pose
// enter saves; a reconstructed pending request must earn a new live receipt.
internal sealed record NativePackageRouteReceipt(NativePackageRouteRequest Request, Vector3[] Path)
{
    internal static bool ContinuesPose(FalloutActorPackageMotion? previous, FalloutFormKey package,
        Vector3 position, Quaternion rotation) => previous is not null && previous.Package == package &&
        position == new Vector3(previous.Position[0], previous.Position[1], previous.Position[2]) &&
        rotation.IsEqualApprox(new(previous.Rotation[0], previous.Rotation[1], previous.Rotation[2], previous.Rotation[3]));

    internal bool Matches(NativePackageRouteIdentity current, Vector3[] path) =>
        Request.Identity.Matches(current) && ReferenceEquals(Path, path) &&
        Request.Source.SourceSha256?.Equals(current.SourceSha256, StringComparison.OrdinalIgnoreCase) == true;

    private bool Reached(Vector3 position, bool onFloor, int cursor) => Path.Length != 0 && cursor == Path.Length &&
        onFloor && position.IsFinite() && position.DistanceTo(Path[^1]) <= Request.Identity.Radius &&
        Request.Source.TerminalFloor is { } floor && floor(position) && floor(Path[^1]);

    internal NativeNavigationIntent? Approach(NativePackageRouteIdentity current, Vector3[] path,
        Vector3 position, bool onFloor, int cursor) => Matches(current, path) &&
        !Request.Intent.ReferenceApproach && Request.Intent.Projection is null &&
        Request.Intent.ArrivalRadius == 0 && Request.SourcePoints == Request.Source.Points.Length &&
        Request.SourcePoints > 0 && Request.Intent.Resume == Request.SourcePoints &&
        Request.Intent.Target == Request.Source.Points[^1] && Reached(position, onFloor, cursor)
            ? NativeCapsuleNavigation.ApproachAfterProjection(position, Path[^1], current.Target,
                current.Radius, Request.Source.TerminalFloor!) : null;

    internal bool Arrived(NativePackageRouteIdentity current, Vector3[] path, Vector3 position, bool onFloor, int cursor) =>
        Matches(current, path) && Request.Intent.ReferenceApproach && Request.Intent.Projection is null &&
        Request.Intent.Target == current.Target && Request.Intent.ArrivalRadius == current.Radius &&
        Request.Intent.Corridor.Count == 0 && Reached(position, onFloor, cursor);
}

internal sealed partial class RuntimeNativeActorCombat
{
    private Vector3[] _pursuitPath = [];
    private int _pursuitCursor, _routeRequests, _routeFailures;
    private Vector3 _routeTarget;
    private double _routeClock, _routeStall, _routeMilliseconds, _routeSourceMilliseconds, _routeMaximumSliceMilliseconds;
    private float _waypointDistance = float.PositiveInfinity;
    private string? _routeError;
    private IEnumerator<IReadOnlyList<Vector3>?>? _routeSearch;
    private NativeNavigationProbe? _routeProbe;
    private Vector3 _routeEnd;
    private float _routeSpacing;
    private float _routeArrivalRadius;
    private int _routeRefinements, _routeRefinementRequests;
    private string? _coarseRouteError;
    private NativeNavigationIntent? _routeIntent;
    private Func<Vector3, bool>? _routeArrival;
    private int _retiredPursuitSearches;
    private NativeNavigationWorkSnapshot? _routeWork;
    private NativePackageRouteRequest? _packageRouteRequest;
    private NativePackageRouteReceipt? _packageRouteReceipt;
    private FalloutFormKey? _routePackage;
    private long _packageRouteArrivals;

    private NativePackageRouteIdentity PackageRouteIdentity(FalloutFormKey package, Vector3 target, float radius, string sourceHash)
        => new(package, target, radius, sourceHash, _context!, _mover!.GetRid().Id,
            _mover.GetWorld3D().GetInstanceId(), _movementEnvelope!.Shape!, _actor.GlobalBasis.Scale);

    private void InvalidatePackageRouteReceipt()
    {
        _packageRouteRequest = null;
        _packageRouteReceipt = null;
    }

    private void ValidatePackageRouteContinuity(FalloutFormKey package, Vector3 target, float radius, string? sourceHash)
    {
        if (_packageRouteRequest is null && _packageRouteReceipt is null) return;
        if (sourceHash is null || !NativePackageRouteReceipt.ContinuesPose(_state.PackageMotion, package,
                _actor.GlobalPosition, _actor.GlobalBasis.Orthonormalized().GetRotationQuaternion()))
        { InvalidatePackageRouteReceipt(); return; }
        var current = PackageRouteIdentity(package, target, radius, sourceHash);
        if (_packageRouteRequest is { } request && !request.Identity.Matches(current))
        { InvalidatePackageRouteReceipt(); return; }
        if (_packageRouteReceipt is { } receipt && !receipt.Matches(current, _pursuitPath)) _packageRouteReceipt = null;
    }

    private void BeginNativeRouteSearch(NativeNavigationIntent intent, Func<Vector3, bool>? arrival,
        NativePackageRouteRequest? request, FalloutFormKey? package)
    {
        _pursuitPath = []; _pursuitCursor = 0;
        _waypointDistance = float.PositiveInfinity; _routeStall = 0;
        _packageRouteRequest = request; _packageRouteReceipt = null;
        _routePackage = package;
        _routeIntent = intent; _routeArrival = arrival;
        _routeEnd = intent.Target; _routeArrivalRadius = intent.ArrivalRadius;
        _routeSpacing = Math.Max(.3f, _radius * 2);
        _routeRefinements = 0; _coarseRouteError = null;
        IReadOnlyList<Vector3> prefix = intent.ReferenceApproach ? [intent.Target] : intent.Corridor;
        _routeProbe = new(NativeCapsuleNavigation.FirstCorridorContact(_mover!, _actor.GlobalPosition, prefix), _context!.CollisionReference);
        _routeSearch = NativeCapsuleNavigation.Search(_mover!, _actor.GlobalPosition, intent.Target,
            _context.StepHeight, _routeSpacing, _context.Resident, 512, _routeProbe, intent.ArrivalRadius,
            intent.Corridor, arrival).GetEnumerator();
        _routeClock = .5;
    }
    private void DisposeRouteSearch()
    {
        _routeSearch?.Dispose();
        _routeWork = NativeCapsuleNavigation.Work(_routeSearch) ?? _routeWork;
        _routeSearch = null;
    }

    private Vector3? PursuitTarget(Vector3 target, double delta, float stoppingDistance,
        FalloutFormKey? package = null, string? navigationSha256 = null)
    {
        if (package is null || navigationSha256 is null) InvalidatePackageRouteReceipt();
        // Ground-relative source animations (including hovering creatures)
        // still need a supported controller root before planning floor edges.
        if (!_mover!.IsOnFloor() && _pursuitPath.Length == 0) return null;
        _routeClock -= delta;
        if (_routeDoor is not null && target.DistanceTo(_routeTarget) > _radius) ResetDoorNavigation();
        // Finish this bounded query against its captured destination. Repeated
        // target motion must not discard every query before pursuit can move.
        ConsumeReachedPursuitWaypoints();
        if (_pursuitCursor < _pursuitPath.Length)
        {
            var offset = _pursuitPath[_pursuitCursor] - _actor.GlobalPosition;
            var distance = new Vector2(offset.X, offset.Z).Length();
            _routeStall = distance < _waypointDistance - .001f ? 0 : _routeStall + delta;
            _waypointDistance = distance;
        }
        if (_routeDoor is not null)
        {
            if (AdvanceDoorNavigation(delta))
                return _pursuitCursor < _pursuitPath.Length ? _pursuitPath[_pursuitCursor] : null;
        }
        if (_routeSearch is null && _routeDoor is null && package is { } owner && navigationSha256 is { } sourceHash &&
            _packageRouteReceipt is { } completed)
        {
            var current = PackageRouteIdentity(owner, target, stoppingDistance, sourceHash);
            if (completed.Arrived(current, _pursuitPath, _actor.GlobalPosition, _mover.IsOnFloor(), _pursuitCursor)) return null;
            if (completed.Approach(current, _pursuitPath, _actor.GlobalPosition, _mover.IsOnFloor(), _pursuitCursor) is { } approach)
            {
                // The exact terminal source prefix was physically walked. Its
                // supported endpoint admits a new query, never final arrival.
                ++_routeRequests;
                _routeWork = null;
                BeginNativeRouteSearch(approach, null, completed.Request with { Identity = current, Intent = approach }, owner);
            }
        }
        if (_routeSearch is null && _routeClock <= 0 && (_pursuitCursor >= _pursuitPath.Length ||
            target.DistanceTo(_routeTarget) > _radius || _routeStall >= .75))
        {
            var started = Stopwatch.GetTimestamp();
            _routeRequests++;
            _routeWork = null;
            _pursuitPath = [];
            _pursuitCursor = 0;
            InvalidatePackageRouteReceipt();
            _routeTarget = target;
            _routeStall = 0;
            _waypointDistance = float.PositiveInfinity;
            _routePackage = package;
            try
            {
                var context = _context!;
                // Package destinations are already source-projected. They need
                // authored prefix regions, but not combat's whole-component
                // destination-region search on every short movement slice.
                var source = context.RouteRegion?.Invoke(_actor.GlobalPosition, target,
                    _state.Engagement is not null ? stoppingDistance : 0);
                var coarse = source?.Points ?? context.Route(_actor.GlobalPosition, target);
                if (coarse.Length == 0) throw new InvalidOperationException("No source navigation corridor.");
                var intent = NativeCapsuleNavigation.Intent(_actor.GlobalPosition, coarse, target, stoppingDistance,
                    projection: source?.Projection);
                NativePackageRouteRequest? request = package is { } requestPackage && navigationSha256 is { } expected &&
                    source is { SourceSha256: { } actualHash, TerminalFloor: not null } &&
                    actualHash.Equals(expected, StringComparison.OrdinalIgnoreCase) &&
                    stoppingDistance > 0
                    ? new(PackageRouteIdentity(requestPackage, target, stoppingDistance, expected), source, intent, coarse.Length) : null;
                // NAVM supplies bounded intent; the complete actor capsule and
                // ordinary support/residency queries validate every guide edge.
                BeginNativeRouteSearch(intent, SourceArrival(intent, coarse, source), request, package);
            }
            catch (InvalidOperationException error)
            {
                _routeError = error.Message;
                _routeFailures = Math.Min(4, _routeFailures + 1);
                _routeClock = .5 * _routeFailures;
            }
            finally
            {
                _routeSourceMilliseconds = _routeMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                _routeMaximumSliceMilliseconds = 0;
            }
        }
        if (_routeSearch is not null)
        {
            AdvanceRouteSearch();
            // Completion can publish an already-reached endpoint or prefix.
            // Consume it before choosing a movement clip this same tick, but
            // do not charge another stall delta or change retry admission.
            ConsumeReachedPursuitWaypoints();
        }
        return _pursuitCursor < _pursuitPath.Length ? _pursuitPath[_pursuitCursor] : null;
    }

    internal static Func<Vector3, bool>? SourceArrival(NativeNavigationIntent intent, IReadOnlyList<Vector3> path,
        NativeActorNavigationRoute? source) => intent.Projection is not null ? source?.Arrival :
        !intent.ReferenceApproach && NativeCapsuleNavigation.CanUsePrefixRegion(intent.Target, intent.Resume, path)
            ? source?.PrefixArrival?.Invoke(intent.Target, intent.Resume) : null;

    private void ConsumeReachedPursuitWaypoints()
    {
        while (_pursuitCursor < _pursuitPath.Length)
        {
            var offset = _pursuitPath[_pursuitCursor] - _actor.GlobalPosition;
            if (new Vector2(offset.X, offset.Z).Length() > _mover!.SafeMargin * 8) break;
            // A package receipt needs actual traversal at every native landing,
            // not horizontal coincidence with a waypoint on another floor.
            if (_packageRouteReceipt is { } receipt && ReferenceEquals(receipt.Path, _pursuitPath) &&
                (!_mover.IsOnFloor() || offset.Length() > receipt.Request.Identity.Radius)) break;
            _pursuitCursor++;
            _waypointDistance = float.PositiveInfinity;
            _routeStall = 0;
        }
    }

    // An iterator with no movement intent is an unused native query, not an
    // outstanding gameplay event. Retain its completed failure and any door
    // obligation; never retire a requested pursuit merely because it is waiting.
    private void RetireUnusedPursuitSearch()
    {
        if (_routeSearch is null) return;
        DisposeRouteSearch();
        _retiredPursuitSearches++;
    }
    private void AdvanceRouteSearch()
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (!NativeCapsuleNavigation.Advance(_routeSearch!, out var path)) return;
            _pursuitPath = path!.ToArray();
            _packageRouteReceipt = _packageRouteRequest is { } request && _pursuitPath.Length != 0
                ? new(request, _pursuitPath) : null;
            _routeError = null;
            _routeFailures = 0;
            _routeClock = .5;
            DisposeRouteSearch();
        }
        catch (InvalidOperationException error)
        {
            _routeError = error.Message;
            _routeFailures = Math.Min(4, _routeFailures + 1);
            _routeClock = .5 * _routeFailures;
            DisposeRouteSearch();
            // The actor radius already includes its source and instance scale.
            var refinedSpacing = NativeCapsuleNavigation.RefinementSpacing(_routeSpacing, _radius, 1);
            if (_routeRefinements == 0 && refinedSpacing < _routeSpacing)
            {
                // A diameter-sized lattice can skip an executable short
                // support transition. Refine once, with the same complete
                // capsule, floor rules, node limit and shared time budget.
                _coarseRouteError = error.Message;
                _routeSpacing = refinedSpacing;
                _routeRefinements++;
                _routeRefinementRequests++;
                _routeSearch = NativeCapsuleNavigation.Search(_mover!, _actor.GlobalPosition, _routeEnd,
                    _context!.StepHeight, _routeSpacing, _context.Resident, 512, _routeProbe, _routeArrivalRadius,
                    _routeIntent!.Corridor, _routeArrival).GetEnumerator();
                return;
            }
            BeginDoorNavigation();
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _routeMilliseconds += elapsed;
            _routeMaximumSliceMilliseconds = Math.Max(_routeMaximumSliceMilliseconds, elapsed);
        }
    }
}
