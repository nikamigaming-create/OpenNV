using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutPatrolRoute? _patrol;
    private FalloutPatrolProgress? _patrolProgress;
    private (Vector3 Authored, Vector3 Floor)? _patrolDestination;
    private object? PatrolState => _patrol is null ? null : new { source = _patrol, progress = _patrolProgress };

    private FalloutPatrolRoute ReadPatrol(FalloutPluginRecord package)
    {
        var route = FalloutPatrolRoute.Read(_aiRecords!, package, Appearance.Reference!.Value);
        if (!route.Repeatable && package.ReadSubrecords().Any(field => field.Signature == "PSDT") &&
            FalloutPackageSchedule.Read(package).Duration != 0)
            throw new NotSupportedException("Creature patrol duration requires its endpoint schedule hold owner.");
        foreach (var point in route.Points)
        {
            if (point.Idles?.Idles.Count > 0 || point.ArrivalIdle is not null)
                throw new NotSupportedException("Creature patrol marker idles require their collection and animation continuation owner.");
            point.Arrival?.ValidateScript();
            point.Arrival?.RequireEmptyTopic();
        }
        return route;
    }

    private void BeginPatrol(FalloutPatrolRoute route, bool restored)
    {
        _patrol = route; _patrolDestination = null;
        _patrolProgress = restored ? _aiState!.PackageMotion!.Patrol : null;
        if (_patrolProgress is not null) route.Validate(_patrolProgress);
        _aiState!.ProcedureCaptureBlocker = restored ? null : "Patrol has started without its first native motion observation.";
    }

    private Vector3 PatrolPosition(FalloutPatrolPoint point)
    {
        var world = _aiWorld!;
        var placement = world.Placement(point.Reference);
        if (world.TravelRegion(placement.Cell) != world.TravelRegion(world.Placement(Appearance.Reference!.Value).Cell))
            throw new NotSupportedException("Creature patrol marker requires its other-region route owner.");
        return GetParent<Node3D>().ToGlobal(new Vector3(placement.Position[0], placement.Position[2],
            -placement.Position[1]) * Skeleton.UnitsToMetres);
    }

    private void AdvancePatrol(double delta)
    {
        var route = _patrol!;
        var progress = _patrolProgress ??= route.Start(point => GlobalPosition.DistanceSquaredTo(PatrolPosition(point)));
        var point = route.Points[progress.Index];
        var authored = PatrolPosition(point);
        if (_patrolDestination is not { } previous || !previous.Authored.IsEqualApprox(authored))
            _patrolDestination = (authored, Combat!.ProjectPackageDestination(authored));
        var destination = _patrolDestination.Value.Floor;
        // Match the native NPC patrol's finite arrival tolerance. Authored NAVM
        // and supported collision floors can differ by centimetres on slopes;
        // the actor must still stand on the actual floor at the destination.
        var radius = Math.Max(route.Radius * Skeleton.UnitsToMetres, .25f);
        Combat!.AdvancePackageMotion(_aiPackage!, destination, radius, route.Running, delta,
            route.WeaponDrawn, requireArrivalHeight: true);
        var arrived = IsOnFloor() && GlobalPosition.DistanceTo(destination) <= radius;
        var next = route.Advance(progress, arrived, delta);
        // Commit arrival before its results. An event fault retains the consumed
        // movement prefix and cannot replay those results on the next frame.
        _patrolProgress = next; Combat.SetPatrolProgress(next); _aiState!.ProcedureCaptureBlocker = null;
        if (!progress.Arrived && next.Arrived && point.Arrival is { } program)
        {
            try
            {
                if (ExecutePackageEvent is { } execute) execute(program, Appearance.Reference!.Value);
                else program.RequireEmptyScript();
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException)
            {
                _aiState.ScriptError ??= $"Package XPPA {point.Reference}: {error.Message}";
                throw;
            }
        }
        if (next.Arrived && point.FaceHeading)
        {
            var rotation = _aiWorld!.Placement(point.Reference).RotationRadians;
            var basis = OpenNV.Runtime.Formats.Gamebryo.GamebryoCoordinate.ConvertReferenceEuler(
                new(rotation[0], rotation[1], rotation[2]), 1);
            Combat.FacePackageDirection(-(GetParent<Node3D>().GlobalBasis * basis.Z), delta);
        }
        if (!next.Complete) return;
        _packageEvents!.Complete();
        _evaluateRequested = true;
        // Selection observes arrival results before deciding whether this
        // nonrepeatable source package still wins and may restart.
    }
}
