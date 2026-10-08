using System.Diagnostics;
using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

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
    private int _retiredPursuitSearches;
    private NativeNavigationWorkSnapshot? _routeWork;
    private void DisposeRouteSearch()
    {
        _routeSearch?.Dispose();
        _routeWork = NativeCapsuleNavigation.Work(_routeSearch) ?? _routeWork;
        _routeSearch = null;
    }

    private Vector3? PursuitTarget(Vector3 target, double delta, float stoppingDistance)
    {
        // Ground-relative source animations (including hovering creatures)
        // still need a supported controller root before planning floor edges.
        if (!_mover!.IsOnFloor() && _pursuitPath.Length == 0) return null;
        _routeClock -= delta;
        if (_routeDoor is not null && target.DistanceTo(_routeTarget) > _radius) ResetDoorNavigation();
        if (_routeSearch is not null && target.DistanceTo(_routeTarget) > _radius)
        {
            DisposeRouteSearch(); _routeClock = 0;
        }
        while (_pursuitCursor < _pursuitPath.Length)
        {
            var offset = _pursuitPath[_pursuitCursor] - _actor.GlobalPosition;
            var distance = new Vector2(offset.X, offset.Z).Length();
            if (distance > _mover.SafeMargin * 8)
            {
                _routeStall = distance < _waypointDistance - .001f ? 0 : _routeStall + delta;
                _waypointDistance = distance;
                break;
            }
            _pursuitCursor++;
            _waypointDistance = float.PositiveInfinity;
            _routeStall = 0;
        }
        if (_routeDoor is not null)
        {
            if (AdvanceDoorNavigation(delta))
                return _pursuitCursor < _pursuitPath.Length ? _pursuitPath[_pursuitCursor] : null;
        }
        if (_routeSearch is null && _routeClock <= 0 && (_pursuitCursor >= _pursuitPath.Length ||
            target.DistanceTo(_routeTarget) > _radius || _routeStall >= .75))
        {
            var started = Stopwatch.GetTimestamp();
            _routeRequests++;
            _routeWork = null;
            _pursuitPath = [];
            _pursuitCursor = 0;
            _routeTarget = target;
            _routeStall = 0;
            _waypointDistance = float.PositiveInfinity;
            try
            {
                var coarse = _context!.Route(_actor.GlobalPosition, target);
                if (coarse.Length == 0) throw new InvalidOperationException("No source navigation corridor.");
                var intent = _routeIntent = NativeCapsuleNavigation.Intent(_actor.GlobalPosition, coarse, target, stoppingDistance);
                _routeEnd = intent.Target;
                _routeArrivalRadius = intent.ArrivalRadius;
                _routeSpacing = Math.Max(.3f, _radius * 2);
                _routeRefinements = 0;
                _coarseRouteError = null;
                IReadOnlyList<Vector3> prefix = intent.ReferenceApproach ? [intent.Target] : intent.Corridor;
                _routeProbe = new(NativeCapsuleNavigation.FirstCorridorContact(_mover, _actor.GlobalPosition, prefix), _context.CollisionReference);
                // NAVM supplies bounded intent; the complete actor capsule and
                // ordinary support/residency queries validate every guide edge.
                _routeSearch = NativeCapsuleNavigation.Search(_mover, _actor.GlobalPosition, intent.Target,
                    _context.StepHeight, _routeSpacing, _context.Resident, 512, _routeProbe, _routeArrivalRadius,
                    intent.Corridor).GetEnumerator();
                _routeClock = .5;
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
        if (_routeSearch is not null) AdvanceRouteSearch();
        return _pursuitCursor < _pursuitPath.Length ? _pursuitPath[_pursuitCursor] : null;
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
                    _routeIntent!.Corridor).GetEnumerator();
                return;
            }
            BeginDoorNavigation();
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _routeMilliseconds += elapsed;
            _routeMaximumSliceMilliseconds = Math.Max(_routeMaximumSliceMilliseconds, elapsed);
            _routeWork = NativeCapsuleNavigation.Work(_routeSearch) ?? _routeWork;
        }
    }
}
