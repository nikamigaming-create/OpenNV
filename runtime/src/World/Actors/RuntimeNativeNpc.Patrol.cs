using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal partial class RuntimeNativeNpc
{
    private FalloutPatrolRoute? _patrol;
    private FalloutPatrolProgress? _patrolProgress;
    private bool _patrolRestored;
    private string? _patrolStatus;
    private (Vector3 Authored, Vector3 Floor)? _patrolDestination;

    private void BeginPatrol(FalloutPluginRecord package)
    {
        var route = FalloutPatrolRoute.Read(_aiStack!, package, Appearance.Reference!.Value);
        _packageEvents!.Change(_packageIdleSource);
        _patrol = route; _patrolProgress = null; _patrolRestored = false; _patrolDestination = null;
        _aiPackage = package; _packageIdles = null;
        GD.Print($"OPENNV_NATIVE_PATROL_READY reference={Appearance.Reference} package={package.FormKey} points={route.Points.Count} circular={route.Circular} repeat={route.Repeatable}");
    }

    public override void _PhysicsProcess(double delta)
    {
        Combat?.StopPackageMotion();
        if (_patrol is not { } route || Combat is null || _aiError is not null || Combat.OwnsPose ||
            !Combat.PackageMovementReady || _conversationTarget is not null) return;
        try
        {
            Vector3 PositionOf(FalloutPatrolPoint point) => GetParent<Node3D>().ToGlobal(
                new Vector3(point.Position[0], point.Position[2], -point.Position[1]) * Skeleton.UnitsToMetres);
            if (!_patrolRestored)
            {
                _patrolRestored = true;
                _patrolProgress = Combat.PackageMotion?.Package == route.Package ? Combat.PackageMotion.Patrol : null;
                if (_patrolProgress is not null) route.Validate(_patrolProgress);
                else _patrolProgress = route.Start(point => GlobalPosition.DistanceSquaredTo(PositionOf(point)));
                if (_patrolProgress.Arrived) StartPatrolIdles(route.Points[_patrolProgress.Index]);
            }
            var progress = _patrolProgress!;
            var point = route.Points[progress.Index];
            var reference = _aiCell!.References.SingleOrDefault(value => value.FormKey == point.Reference);
            if (reference is null) { _patrolStatus = "waiting-for-marker-residency"; return; }
            var local = _referenceTransform!(reference);
            var target = GetParent<Node3D>().ToGlobal(local.Origin);
            // Markers are often authored above the walkway. Resolve their
            // destination on source NAVM once, while native collision still
            // owns every movement step and the final supported arrival.
            if (_patrolDestination is not { } previous || !previous.Authored.IsEqualApprox(target))
                _patrolDestination = (target, Combat.ProjectPackageDestination(target));
            target = _patrolDestination.Value.Floor;
            // Source radius applies at every point. A zero radius still needs
            // an explicit native tolerance; never compare only X/Z on a bridge.
            var radius = Math.Max(route.Radius * Skeleton.UnitsToMetres, .25f);
            Combat.AdvancePackageMotion(_aiPackage!, target, radius, route.Running, delta, route.WeaponDrawn, requireArrivalHeight: true);
            var reached = IsOnFloor() && GlobalPosition.DistanceTo(target) <= radius + .1f;
            var next = route.Advance(progress, reached, delta);
            if (!progress.Arrived && next.Arrived)
            {
                point.Arrival?.RequireEmptyScript();
                if (point.ArrivalIdle is { } idle) PlayIdle(_aiStack!, idle, "patrol-arrival");
                StartPatrolIdles(point);
                GD.Print($"OPENNV_NATIVE_PATROL_ARRIVAL reference={Appearance.Reference} marker={point.Reference} wait={point.WaitSeconds:R}");
            }
            if (next.Arrived && point.FaceHeading) Combat.FacePackageDirection(-(GetParent<Node3D>().GlobalBasis * local.Basis.Z), delta);
            if (next.Index != progress.Index || progress.Arrived && !next.Arrived) { CancelIdle(); _packageIdles = null; }
            _patrolProgress = next;
            Combat.SetPatrolProgress(next);
            _patrolStatus = next.Complete ? "complete" : next.Arrived ? "waiting" : "travelling";
            if (next.Complete)
            {
                _packageEvents!.Complete();
                // A still-valid nonrepeatable package starts again after its
                // end event. Its source schedule is separately admitted.
                _patrolProgress = route.Start(_ => 0); Combat.SetPatrolProgress(_patrolProgress);
                _packageEvents.Change(null); _packageEvents.Change(_packageIdleSource);
            }
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or IOException)
        {
            Combat.StopPackageMotion(); _aiError = error.Message;
            GD.PushError($"OPENNV_NATIVE_PATROL_DIVERGENCE reference={Appearance.Reference}: {_aiError}");
        }
    }

    private void StartPatrolIdles(FalloutPatrolPoint point)
    {
        if (point.Idles is { } source)
            _packageIdles = new(source, _idleReplays, idle => _idleConditions!.AllPass(idle, EvaluateAiCondition), Combat!.PackageRandom);
        _packageIdleError = null;
    }
}
