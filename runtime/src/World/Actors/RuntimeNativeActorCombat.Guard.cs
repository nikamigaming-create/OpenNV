using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private (FalloutFormKey Package, Vector3 Authored, Vector3 Floor)? _guardDestination;

    internal FalloutTravelProgress AdvanceGuard(FalloutPluginRecord package, FalloutGuardPackage source,
        FalloutTravelProgress progress, double delta)
    {
        var current = source.Start(_records, _world, _state.Reference);
        if (current.Cell != progress.Cell) throw new NotSupportedException("Guard marker moved outside its resident cell.");
        if (!current.Location.SequenceEqual(progress.Location)) progress = current;
        if (progress.Complete) source.RequireStationaryBehavior();
        var authored = _actor.GetParent<Node3D>().ToGlobal(
            new Vector3(progress.Location[0], progress.Location[2], -progress.Location[1]) * _skeleton.UnitsToMetres);
        if (_guardDestination is not { } previous || previous.Package != package.FormKey || previous.Authored != authored)
            _guardDestination = (package.FormKey, authored, ProjectPackageDestination(authored));
        var destination = _guardDestination.Value.Floor;
        var actor = _actor as CharacterBody3D ?? throw new NotSupportedException("Guard has no native actor capsule.");
        var tolerance = actor.SafeMargin * 8;
        AdvancePackageMotion(package, progress.Complete ? _actor.GlobalPosition : destination,
            progress.Complete ? 0 : tolerance, source.Running, delta, source.WeaponDrawn, requireArrivalHeight: true);
        if (!progress.Complete && actor.IsOnFloor() && _actor.GlobalPosition.DistanceTo(destination) <= tolerance)
            progress = progress with { Complete = true };
        if (_state.PackageMotion is not { } motion) throw new NotSupportedException("Guard has no observed native motion.");
        _state.PackageMotion = motion with { Guard = progress };
        _state.ProcedureCaptureBlocker = null;
        // Approach does not finish an ongoing Guard package or dispatch OnEnd.
        // A reached unowned next phase remains visible with retained progress.
        if (progress.Complete) source.RequireStationaryBehavior();
        return progress;
    }
}
