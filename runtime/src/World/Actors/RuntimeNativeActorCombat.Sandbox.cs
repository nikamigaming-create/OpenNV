using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private string? _sandboxCaptureBlocker;
    internal IFalloutNativeSandboxActions? SandboxActions { get; set; }

    private void SetSandboxBlocker(string? failure)
    {
        if (_state.ProcedureCaptureBlocker is not null && _state.ProcedureCaptureBlocker != _sandboxCaptureBlocker)
        {
            if (failure is not null) throw new NotSupportedException("Sandbox overlaps an independent unretired capture owner.");
            return;
        }
        _state.ProcedureCaptureBlocker = _sandboxCaptureBlocker = failure;
    }

    internal void BeginSandboxObservation() => SetSandboxBlocker("Sandbox requires its first native area/motion observation.");

    internal void AdvanceSandbox(FalloutPluginRecord package, FalloutSandboxPackage source,
        FalloutSandboxState state, double seconds)
    {
        if (source.Form != package.FormKey) throw new InvalidDataException("Sandbox native owner differs from its active package.");
        if (OwnsPose || !PackageMovementReady) return;
        if (state.Selected is not null)
        {
            var action = SandboxActions ?? throw new NotSupportedException("Sandbox active action lost its actual native consumer.");
            action.Advance(seconds);
            state.AdvanceNativeAtHour(action.GameHour(), action.Retire, action.ObserveReturned, state.Registry.RememberReturned);
            return;
        }
        if (source.LocationType == 0) state.ObserveSourceArea(source.Resolve(_records, _world, _state.Reference));
        var area = state.Area;
        if (area.Cell != _world.Placement(_state.Reference).Cell)
            throw new NotSupportedException("Sandbox source area requires actual other-CELL package travel.");
        var authored = area.Center is { } p ? _actor.GetParent<Node3D>().ToGlobal(
            new Vector3(p[0], p[2], -p[1]) * _skeleton.UnitsToMetres) : _actor.GlobalPosition;
        var target = area.Center is null ? authored : ProjectPackageDestination(authored);
        var tolerance = Math.Max(area.Radius * _skeleton.UnitsToMetres, _mover!.SafeMargin * 8);
        AdvancePackageMotion(package, target, tolerance, source.Running, seconds, source.WeaponDrawn, requireArrivalHeight: true);
        state.ObserveLocation(_mover.IsOnFloor() && _actor.GlobalPosition.DistanceTo(target) <= tolerance);
        if (_state.PackageMotion is not { } motion || motion.Package != source.Form)
        { SetSandboxBlocker("Sandbox native area motion has not returned."); return; }
        if (_routeSearch is not null || _routeDoor is not null)
        {
            SetSandboxBlocker(_routeDoor is not null ? "Sandbox entered route-door continuation is unowned." :
                "Sandbox native route iterator has not returned.");
            return;
        }
        SetSandboxBlocker(null);
        if (!state.AtLocation) return;
        if (source.Vetoes == 63) return;
        var actions = SandboxActions ?? throw new NotSupportedException(
            "Sandbox reached enabled source world actions without its candidate registry/filter/order and native action producer.");
        var registry = state.Registry.Observe(actions.Timer(), actions.RescanInterval(), PackageRandom,
            () => actions.Discovery(source, area), actions.RepeatMilliseconds());
        state.SelectNative(registry.Candidates, actions.Duration, actions.SelectionTime,
            state.ActionWeights(actions.Context(), registry.Availability), state.Registry.RepeatedReference);
        if (state.Selected is not null) state.EnterAction(actions.Enter);
        else throw new NotSupportedException("Sandbox empty eligible election requires its original fallback wait child; no completion or substitute retry clock is admitted.");
    }

    internal FalloutActorPackageMotion CaptureSandboxMotion(FalloutSandboxState state, FalloutFollowElection election)
    {
        if (_sandboxCaptureBlocker is not null || _routeSearch is not null || _routeDoor is not null ||
            _state.PackageMotion is not { } motion)
            throw new NotSupportedException(_sandboxCaptureBlocker ?? "Sandbox has no fully returned native route/motion owner.");
        var route = new FalloutSandboxNativeRoute(FollowNavigationSource(),
            [_routeTarget.X, _routeTarget.Y, _routeTarget.Z], _pursuitPath.Select(p => new[] { p.X, p.Y, p.Z }).ToArray(),
            _pursuitCursor, _routeClock, _routeStall, float.IsPositiveInfinity(_waypointDistance) ? null : _waypointDistance,
            _routeError, _routeFailures);
        var native = state.Selected is { } selected ?
            (SandboxActions ?? throw new NotSupportedException("Sandbox capture lost its actual native action owner.")).Capture(selected) : null;
        var snapshot = state.Capture(election) with { Route = route, NativeIdle = native };
        snapshot.Validate();
        return motion with { Sandbox = snapshot };
    }

    private void RestoreSandboxRoute(FalloutActorPackageMotion motion)
    {
        if (motion.Sandbox?.Route is not { } saved) return;
        saved.Validate();
        if (!saved.NavigationSha256.Equals(FollowNavigationSource(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cold Sandbox route differs from the actual source navigation graph.");
        _pursuitPath = saved.Waypoints.Select(point => new Vector3(point[0], point[1], point[2])).ToArray();
        _pursuitCursor = saved.Cursor; _routeTarget = new(saved.Target[0], saved.Target[1], saved.Target[2]);
        _routeClock = saved.RetrySeconds; _routeStall = saved.StallSeconds;
        _waypointDistance = saved.WaypointDistance ?? float.PositiveInfinity;
        _routeError = saved.Error; _routeFailures = saved.Failures;
        SetSandboxBlocker(null);
    }

    internal bool RetireSandbox(FalloutSandboxState? state)
    {
        if (state?.Failure is { } failure)
            throw new NotSupportedException("Sandbox retirement retains an entered failed suffix: " + failure);
        try
        {
            if (state?.Selected is not null)
            {
                var actions = SandboxActions ?? throw new NotSupportedException("Sandbox active action retirement lost its genuine producer.");
                if (!state.CancelNative(actions.Retire, actions.ObserveReturned)) return false;
            }
            DisposeRouteSearch(); ResetDoorNavigation();
            _pursuitPath = []; _pursuitCursor = 0;
            SetSandboxBlocker(null);
            return true;
        }
        catch (Exception error)
        {
            state?.RetainFailure(error);
            _sandboxCaptureBlocker ??= "Sandbox native retirement failed: " + error.Message;
            _state.ProcedureCaptureBlocker ??= _sandboxCaptureBlocker;
            throw;
        }
    }
}
