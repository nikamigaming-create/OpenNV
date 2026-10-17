using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeCreature
{
    private FalloutTravelPackage? _travelPackage;
    private FalloutTravelProgress? _travelProgress;
    private Vector3? _travelDestination;
    private object? TravelState => _travelPackage is null ? null : new
    {
        source = _travelPackage,
        progress = _travelProgress,
        endpoint = _travelDestination is { } point ? new[] { point.X, point.Y, point.Z } : null,
        endIdlePending = PackageEndIdlePending,
        unbound = new[] { "retail-arrival-and-transition-timing" }
    };

    private void BeginTravel(FalloutPluginRecord package, bool restored)
    {
        var source = FalloutTravelPackage.Read(package);
        if (source.LocationType == 2 || source.WeaponDrawn || source.ContinueCombat || source.NoCombatAlert || source.ContinueIfPlayerNear)
            throw new NotSupportedException("Creature Travel requires its current-location, drawn-weapon or continuing-package selection owner.");
        var retained = restored ? _aiState!.PackageMotion?.Travel : null;
        var progress = retained ?? source.Start(_aiRecords!, _aiWorld!, Appearance.Reference!.Value);
        source.Validate(_aiRecords!, _aiWorld!, Appearance.Reference!.Value, progress);
        var arrivedThroughPortal = progress.PortalArrival is not null;
        if (arrivedThroughPortal) progress = progress with { PortalArrival = null };
        _travelPackage = source; _travelProgress = progress; _travelDestination = null;
        _aiState!.ProcedureCaptureBlocker = retained is null || arrivedThroughPortal ? "Travel has started without its first native motion observation." : null;
    }

    private void AdvanceTravel(double delta)
    {
        var source = _travelPackage!;
        var progress = _travelProgress!;
        if (source.LocationType == 0 && !progress.Complete)
        {
            var current = source.Start(_aiRecords!, _aiWorld!, Appearance.Reference!.Value);
            if (current.Cell != progress.Cell || !current.Location.SequenceEqual(progress.Location))
            { progress = current; _travelDestination = null; }
        }
        _travelProgress = progress;
        if (AdvancePackagePortal(progress.Cell, source.Running, source.Sneaking, delta)) return;
        var authored = GetParent<Node3D>().ToGlobal(new Vector3(progress.Location[0], progress.Location[2], -progress.Location[1]) * Skeleton.UnitsToMetres);
        var destination = _travelDestination ??= Combat!.ProjectPackageDestination(authored);
        var radius = Math.Max(source.Radius * Skeleton.UnitsToMetres, SafeMargin * 8);
        Combat!.AdvancePackageMotion(_aiPackage!, progress.Complete ? GlobalPosition : destination,
            progress.Complete ? 0 : radius, source.Running, delta, requireArrivalHeight: true, sneaking: source.Sneaking);
        var arrived = !progress.Complete && IsOnFloor() && GlobalPosition.DistanceTo(destination) <= radius;
        if (arrived) progress = progress with { Complete = true };
        _travelProgress = progress; Combat.SetTravelProgress(progress); _aiState!.ProcedureCaptureBlocker = null;
        // Arrival is retained before any embedded end result or idle executes.
        // A failure keeps that consumed prefix with the reference script owner.
        if (arrived)
        {
            _packageEvents!.Complete();
            if (!PackageEndIdlePending && source.OncePerDay) _evaluateRequested = true;
        }
    }
}
