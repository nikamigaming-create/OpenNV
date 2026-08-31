using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed record GamebryoPackageTravelState(
    string PackageFormId,
    string TargetKind,
    string TargetFormId,
    IReadOnlyList<Vector3> Waypoints,
    int WaypointIndex,
    float SpeedCellUnitsPerSecond,
    float ArrivalToleranceCellUnits,
    Transform3D Transform,
    bool Arrived);

internal sealed class GamebryoPackageTravel
{
    internal const float ExactArrivalToleranceCellUnits = 0.0f;

    private readonly string _packageFormId;
    private readonly SourcePackagePlacement _target;
    private readonly IReadOnlyList<Vector3> _waypoints;
    private readonly float _speedCellUnitsPerSecond;
    private readonly float _arrivalToleranceCellUnits;
    private int _waypointIndex;

    private GamebryoPackageTravel(
        string packageFormId,
        SourcePackagePlacement target,
        IReadOnlyList<Vector3> waypoints,
        float speedCellUnitsPerSecond,
        float arrivalToleranceCellUnits,
        Transform3D transform,
        bool arrived)
    {
        _packageFormId = packageFormId;
        _target = target;
        _waypoints = waypoints;
        _speedCellUnitsPerSecond = speedCellUnitsPerSecond;
        _arrivalToleranceCellUnits = arrivalToleranceCellUnits;
        Transform = transform;
        Arrived = arrived;
        _waypointIndex = arrived ? waypoints.Count : 0;
    }

    internal Transform3D Transform { get; private set; }
    internal bool Arrived { get; private set; }
    internal Vector3? NextWaypoint => Arrived || _waypointIndex >= _waypoints.Count
        ? null
        : _waypoints[_waypointIndex];

    internal static GamebryoPackageTravel Start(
        string packageFormId,
        SourcePackagePlacement target,
        Transform3D current,
        IReadOnlyList<Vector3> sourceWaypoints,
        float speedCellUnitsPerSecond,
        float arrivalToleranceCellUnits)
    {
        Validate(packageFormId, target, current, arrivalToleranceCellUnits);
        if (!float.IsFinite(speedCellUnitsPerSecond) || speedCellUnitsPerSecond <= 0.0f)
            throw new InvalidOperationException("Source package travel speed is invalid.");
        if (sourceWaypoints.Any(waypoint => !waypoint.IsFinite()))
            throw new InvalidOperationException("Source package travel waypoint is invalid.");
        var waypoints = sourceWaypoints.ToList();
        if (waypoints.Count == 0 || !waypoints[^1].IsEqualApprox(target.SourceTransform.Origin))
            waypoints.Add(target.SourceTransform.Origin);
        var atTarget = current.Origin.DistanceTo(target.SourceTransform.Origin) <=
            arrivalToleranceCellUnits;
        return new GamebryoPackageTravel(
            packageFormId,
            target,
            waypoints,
            speedCellUnitsPerSecond,
            arrivalToleranceCellUnits,
            atTarget ? target.SourceTransform : current,
            atTarget);
    }

    internal static GamebryoPackageTravel ArriveAtSourceTarget(
        string packageFormId,
        SourcePackagePlacement target,
        Transform3D current,
        float arrivalToleranceCellUnits)
    {
        Validate(packageFormId, target, current, arrivalToleranceCellUnits);
        return new GamebryoPackageTravel(
            packageFormId,
            target,
            Array.Empty<Vector3>(),
            0.0f,
            arrivalToleranceCellUnits,
            target.SourceTransform,
            true);
    }

    internal static GamebryoPackageTravel Restore(
        GamebryoPackageTravelState state,
        SourcePackagePlacement target)
    {
        Validate(
            state.PackageFormId,
            target,
            state.Transform,
            state.ArrivalToleranceCellUnits);
        if (!state.TargetKind.Equals(target.Kind, StringComparison.Ordinal) ||
            !state.TargetFormId.Equals(
                target.TargetFormId,
                StringComparison.OrdinalIgnoreCase) ||
            state.Waypoints.Any(waypoint => !waypoint.IsFinite()) ||
            !float.IsFinite(state.SpeedCellUnitsPerSecond) ||
            state.SpeedCellUnitsPerSecond < 0.0f ||
            state.WaypointIndex < 0 ||
            state.WaypointIndex > state.Waypoints.Count ||
            state.Arrived != (state.WaypointIndex == state.Waypoints.Count) ||
            state.Arrived && state.Transform.Origin.DistanceTo(
                target.SourceTransform.Origin) > state.ArrivalToleranceCellUnits ||
            !state.Arrived && state.SpeedCellUnitsPerSecond <= 0.0f)
            throw new InvalidOperationException(
                "Saved source package travel state differs from its owned target.");
        var restored = new GamebryoPackageTravel(
            state.PackageFormId,
            target,
            state.Waypoints.ToArray(),
            state.SpeedCellUnitsPerSecond,
            state.ArrivalToleranceCellUnits,
            state.Transform,
            state.Arrived)
        {
            _waypointIndex = state.WaypointIndex,
        };
        return restored;
    }

    internal static GamebryoPackageTravel RestoreSettledAtSourceTarget(
        string packageFormId,
        SourcePackagePlacement target,
        Transform3D savedTransform,
        float arrivalToleranceCellUnits)
    {
        Validate(packageFormId, target, savedTransform, arrivalToleranceCellUnits);
        if (savedTransform.Origin.DistanceTo(target.SourceTransform.Origin) >
            arrivalToleranceCellUnits)
            throw new InvalidOperationException(
                "Saved source package rest position differs from its owned target.");
        return new GamebryoPackageTravel(
            packageFormId,
            target,
            Array.Empty<Vector3>(),
            0.0f,
            arrivalToleranceCellUnits,
            savedTransform,
            true);
    }

    internal bool Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0.0)
            throw new InvalidOperationException("Source package travel delta is invalid.");
        if (Arrived)
            return false;
        var remaining = _speedCellUnitsPerSecond * (float)deltaSeconds;
        while (_waypointIndex < _waypoints.Count)
        {
            var waypoint = _waypoints[_waypointIndex];
            var offset = waypoint - Transform.Origin;
            var distance = offset.Length();
            if (distance <= _arrivalToleranceCellUnits || remaining >= distance)
            {
                Transform = new Transform3D(Transform.Basis, waypoint);
                remaining = Math.Max(0.0f, remaining - distance);
                _waypointIndex++;
                continue;
            }
            if (remaining > 0.0f)
                Transform = new Transform3D(
                    Transform.Basis,
                    Transform.Origin + offset / distance * remaining);
            return false;
        }
        Transform = _target.SourceTransform;
        Arrived = true;
        return true;
    }

    internal void Publish(Node3D actorPlacement)
    {
        actorPlacement.Transform = Transform;
        actorPlacement.SetMeta("opennv_package_form_id", _packageFormId);
        actorPlacement.SetMeta("opennv_package_target_form_id", _target.TargetFormId);
        actorPlacement.SetMeta("opennv_package_target_kind", _target.Kind);
        actorPlacement.SetMeta("opennv_package_arrived", Arrived);
    }

    internal GamebryoPackageTravelState CaptureState() => new(
        _packageFormId,
        _target.Kind,
        _target.TargetFormId,
        _waypoints.ToArray(),
        _waypointIndex,
        _speedCellUnitsPerSecond,
        _arrivalToleranceCellUnits,
        Transform,
        Arrived);

    private static void Validate(
        string packageFormId,
        SourcePackagePlacement target,
        Transform3D current,
        float arrivalToleranceCellUnits)
    {
        if (string.IsNullOrWhiteSpace(packageFormId) ||
            string.IsNullOrWhiteSpace(target.Kind) ||
            string.IsNullOrWhiteSpace(target.TargetFormId) ||
            !target.SourceTransform.IsFinite() ||
            target.SourceTransform.Basis.Determinant() <= 0.0f ||
            !current.IsFinite() ||
            !float.IsFinite(arrivalToleranceCellUnits) ||
            arrivalToleranceCellUnits < 0.0f)
            throw new InvalidOperationException("Source package travel contract is invalid.");
    }
}
