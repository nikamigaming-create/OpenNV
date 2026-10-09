using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private FalloutFormKey? _followRoutePackage;
    private string? _followCaptureBlocker;

    private string FollowNavigationSource()
    {
        var hash = (_context?.NavigationSourceSha256 ??
            throw new NotSupportedException("Follow has no actual navigation graph source provider."))();
        if (hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("Follow navigation provider returned an invalid source identity.");
        return hash;
    }

    private void SetFollowBlocker(string? failure)
    {
        if (_state.ProcedureCaptureBlocker is not null && _state.ProcedureCaptureBlocker != _followCaptureBlocker)
        {
            if (failure is not null) throw new NotSupportedException("Follow overlaps an independent unretired procedure capture owner.");
            return;
        }
        _state.ProcedureCaptureBlocker = _followCaptureBlocker = failure;
    }

    internal void BeginFollowObservation() => SetFollowBlocker("Follow requires its first actual target and native motion observation.");

    internal bool AdvanceFollowPackageMotion(FalloutPluginRecord package, FalloutFollowPackage follow, double delta)
    {
        follow.RequireActorTarget(_records, _state.Reference);
        if (follow.Form != package.FormKey) throw new InvalidDataException("Follow declaration differs from its active package.");
        if (OwnsPose || !PackageMovementReady) return false;
        Node3D? target;
        bool running;
        if (follow.Target == _records.RuntimeFormKey(0x14))
        {
            var player = _context!.Player();
            target = player is { CollisionResident: true } && player.IsInsideTree() &&
                _context!.Resident(player.GlobalPosition) ? player : null;
            running = player?.Activity.Running == true;
        }
        else
        {
            var candidates = CombatActors.Where(actor => actor._state.Reference == follow.Target &&
                actor._actor.IsInsideTree() && !actor._actor.IsQueuedForDeletion()).ToArray();
            if (candidates.Length > 1) throw new InvalidDataException("Follow target has ambiguous actual native actor publication.");
            var other = candidates.SingleOrDefault();
            target = other is not null && _world.IsEnabled(follow.Target) &&
                _context!.Resident(other._actor.GlobalPosition) ? other._actor : null;
            running = other?.Activity.Running == true;
        }
        if (target is null)
        {
            StopPackageMotion();
            SetFollowBlocker("Follow fixed source target has no current enabled native publication.");
            return false;
        }
        var source = FollowNavigationSource();
        if (_followRoutePackage != package.FormKey)
        {
            // A new package/combat return owns a new pursuit. Retirement does
            // not complete the old package or replay any of its source keys.
            DisposeRouteSearch(); ResetDoorNavigation();
            _pursuitPath = []; _pursuitCursor = 0; _routeClock = _routeStall = 0;
            _routeFailures = 0; _routeError = null; _waypointDistance = float.PositiveInfinity;
            _routeTarget = target.GlobalPosition; _followRoutePackage = package.FormKey;
        }
        AdvancePackageMotion(package, target.GlobalPosition, follow.Distance * _skeleton.UnitsToMetres, running, delta);
        if (_state.PackageMotion is not { } motion || motion.Package != package.FormKey)
        {
            SetFollowBlocker("Follow has no returned native motion publication.");
            return true;
        }
        if (_routeSearch is not null || _routeDoor is not null)
        {
            SetFollowBlocker(_routeDoor is not null ? "Follow entered route-door continuation is unowned." :
                "Follow native route iterator has not returned.");
            return true;
        }
        var progress = new FalloutFollowProgress(follow.Target, source,
            [_routeTarget.X, _routeTarget.Y, _routeTarget.Z],
            _pursuitPath.Select(point => new[] { point.X, point.Y, point.Z }).ToArray(), _pursuitCursor,
            _routeClock, _routeStall, float.IsPositiveInfinity(_waypointDistance) ? null : _waypointDistance,
            _routeError, _routeFailures);
        progress.Validate();
        _state.PackageMotion = motion with { Follow = progress };
        SetFollowBlocker(null);
        return true;
    }

    internal FalloutActorPackageMotion? CaptureFollowMotion(FalloutFollowPackage? active, FalloutFollowElection election)
    {
        var motion = _state.PackageMotion;
        if (active is null) return motion;
        if (motion is null || motion.Package != active.Form)
            throw new NotSupportedException("Follow has no first actual native motion continuation.");
        if (_followCaptureBlocker is not null || motion.Follow is not { } progress)
            throw new NotSupportedException(_followCaptureBlocker ?? "Follow has no complete native continuation.");
        var captured = progress.Copy() with { Election = election };
        active.ValidateContinuation(_records, _state.Reference, captured);
        return motion with { Follow = captured };
    }

    private void RestoreFollowRoute(FalloutActorPackageMotion motion)
    {
        if (motion.Follow is not { } progress) return;
        var source = FalloutFollowPackage.Read(_records.GetEffective(motion.Package));
        source.ValidateContinuation(_records, _state.Reference, progress);
        if (!progress.NavigationSha256.Equals(FollowNavigationSource(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Saved Follow route differs from the current actual navigation graph.");
        _pursuitPath = progress.RouteWaypoints.Select(point => new Vector3(point[0], point[1], point[2])).ToArray();
        _pursuitCursor = progress.RouteCursor;
        _routeTarget = new(progress.RouteTarget[0], progress.RouteTarget[1], progress.RouteTarget[2]);
        _routeClock = progress.RetrySeconds; _routeStall = progress.StallSeconds;
        _waypointDistance = progress.WaypointDistance ?? float.PositiveInfinity;
        _routeError = progress.RouteError; _routeFailures = progress.RouteFailures;
        _followRoutePackage = motion.Package;
    }

    internal void InvalidateFollowRoute() => _followRoutePackage = null;

    internal void RetireFollowRoute()
    {
        if (_followRoutePackage is not null && !OwnsPose)
        {
            DisposeRouteSearch(); ResetDoorNavigation();
            _pursuitPath = []; _pursuitCursor = 0; _routeClock = _routeStall = 0;
            _routeError = null; _routeFailures = 0; _waypointDistance = float.PositiveInfinity;
        }
        _followRoutePackage = null;
        SetFollowBlocker(null);
    }
}
