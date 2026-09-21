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

    private Vector3? PursuitTarget(Vector3 target, double delta, float stoppingDistance)
    {
        // Ground-relative source animations (including hovering creatures)
        // still need a supported controller root before planning floor edges.
        if (!_mover!.IsOnFloor() && _pursuitPath.Length == 0) return null;
        _routeClock -= delta;
        if (_routeSearch is not null && target.DistanceTo(_routeTarget) > _radius)
        {
            _routeSearch.Dispose(); _routeSearch = null; _routeClock = 0;
        }
        while (_pursuitCursor < _pursuitPath.Length)
        {
            var offset = _pursuitPath[_pursuitCursor] - _actor.GlobalPosition;
            var distance = new Vector2(offset.X, offset.Z).Length();
            if (distance > _radius * .5f)
            {
                _routeStall = distance < _waypointDistance - .001f ? 0 : _routeStall + delta;
                _waypointDistance = distance;
                break;
            }
            _pursuitCursor++;
            _waypointDistance = float.PositiveInfinity;
            _routeStall = 0;
        }
        if (_routeSearch is null && _routeClock <= 0 && (_pursuitCursor >= _pursuitPath.Length ||
            target.DistanceTo(_routeTarget) > _radius || _routeStall >= .75))
        {
            var started = Stopwatch.GetTimestamp();
            _routeRequests++;
            _pursuitPath = [];
            _pursuitCursor = 0;
            _routeTarget = target;
            _routeStall = 0;
            _waypointDistance = float.PositiveInfinity;
            try
            {
                var coarse = _context!.Route(_actor.GlobalPosition, target);
                if (coarse.Length == 0) throw new InvalidOperationException("No source navigation corridor.");
                var length = _actor.GlobalPosition.DistanceTo(coarse[0]);
                for (var index = 1; index < coarse.Length; index++) length += coarse[index - 1].DistanceTo(coarse[index]);
                // End within the desired approach distance, not inside the
                // target's physical body. Keep room for waypoint tolerance.
                var approach = Math.Clamp(length - stoppingDistance + _radius * .5f, _radius * .5f, 8);
                var (end, _) = NativeCapsuleNavigation.CorridorPrefix(_actor.GlobalPosition, coarse, approach);
                // The source corridor carries intent; the actor's own complete
                // capsule, resident collision and floor rules supply clearance.
                _routeSearch = NativeCapsuleNavigation.Search(_mover, _actor.GlobalPosition, end,
                    _context.StepHeight, Math.Max(.3f, _radius * 2), _context.Resident, 512).GetEnumerator();
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
            _routeSearch!.Dispose(); _routeSearch = null;
        }
        catch (InvalidOperationException error)
        {
            _routeError = error.Message;
            _routeFailures = Math.Min(4, _routeFailures + 1);
            _routeClock = .5 * _routeFailures;
            _routeSearch!.Dispose(); _routeSearch = null;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _routeMilliseconds += elapsed;
            _routeMaximumSliceMilliseconds = Math.Max(_routeMaximumSliceMilliseconds, elapsed);
        }
    }
}
