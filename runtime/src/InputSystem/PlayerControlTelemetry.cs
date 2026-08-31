using Godot;

namespace OpenNV.Runtime.InputSystem;

internal sealed class PlayerControlTelemetry
{
    private bool _positionInitialized;
    private Vector3 _initialBodyPosition;
    private Vector3 _initialLeftHandPosition;
    private Vector3 _initialRightHandPosition;
    private float _maximumLocomotionMeters;
    private float _maximumLeftHandTravelMeters;
    private float _maximumRightHandTravelMeters;
    private float _maximumMoveStickMagnitude;
    private float _maximumTurnStickMagnitude;
    private float _maximumEyeHeightErrorMeters;
    private float _maximumSnapPivotErrorMeters;
    private int _trackedFrames;
    private int _snapTurns;
    private int _activationEdges;
    private int _acceptedActivations;
    private int _fireEdges;
    private int _acceptedFireActions;
    private int _reloadEdges;
    private int _acceptedReloadActions;
    private int _saveEdges;
    private bool _floorObserved;

    internal void Observe(
        Vector3 bodyPosition,
        Vector3 leftHandPosition,
        Vector3 rightHandPosition,
        Vector2 moveStick,
        Vector2 turnStick,
        bool bothHandsTracked,
        bool floorSupportsBody,
        float eyeY,
        float? floorY,
        float desiredEyeHeightMeters)
    {
        if (!_positionInitialized && bothHandsTracked)
        {
            _positionInitialized = true;
            _initialBodyPosition = bodyPosition;
            _initialLeftHandPosition = leftHandPosition;
            _initialRightHandPosition = rightHandPosition;
        }
        if (_positionInitialized)
        {
            _maximumLocomotionMeters = MathF.Max(
                _maximumLocomotionMeters,
                HorizontalDistance(_initialBodyPosition, bodyPosition));
            _maximumLeftHandTravelMeters = MathF.Max(
                _maximumLeftHandTravelMeters,
                _initialLeftHandPosition.DistanceTo(leftHandPosition));
            _maximumRightHandTravelMeters = MathF.Max(
                _maximumRightHandTravelMeters,
                _initialRightHandPosition.DistanceTo(rightHandPosition));
        }
        _maximumMoveStickMagnitude = MathF.Max(_maximumMoveStickMagnitude, moveStick.Length());
        _maximumTurnStickMagnitude = MathF.Max(_maximumTurnStickMagnitude, turnStick.Length());
        if (bothHandsTracked)
            _trackedFrames++;
        // A floor-relative standing-height contract is meaningful only when the
        // raycast floor agrees with the capsule's supporting foot plane. At a
        // ledge, the capsule may still be grounded while the head ray already
        // sees the lower floor; that difference is step height, not calibration.
        if (floorSupportsBody && floorY is { } floor)
        {
            _floorObserved = true;
            _maximumEyeHeightErrorMeters = MathF.Max(
                _maximumEyeHeightErrorMeters,
                MathF.Abs(eyeY - floor - desiredEyeHeightMeters));
        }
    }

    internal void RecordSnapTurn(float pivotErrorMeters)
    {
        _snapTurns++;
        _maximumSnapPivotErrorMeters = MathF.Max(_maximumSnapPivotErrorMeters, pivotErrorMeters);
    }

    internal void RecordActivation(bool accepted)
    {
        _activationEdges++;
        if (accepted)
            _acceptedActivations++;
    }

    internal void RecordFire(bool accepted)
    {
        _fireEdges++;
        if (accepted)
            _acceptedFireActions++;
    }

    internal void RecordReload(bool accepted)
    {
        _reloadEdges++;
        if (accepted)
            _acceptedReloadActions++;
    }

    internal void RecordSave() => _saveEdges++;

    internal Snapshot Report() => new(
        _maximumLocomotionMeters,
        _maximumLeftHandTravelMeters,
        _maximumRightHandTravelMeters,
        _maximumMoveStickMagnitude,
        _maximumTurnStickMagnitude,
        _maximumEyeHeightErrorMeters,
        _maximumSnapPivotErrorMeters,
        _trackedFrames,
        _snapTurns,
        _activationEdges,
        _acceptedActivations,
        _fireEdges,
        _acceptedFireActions,
        _reloadEdges,
        _acceptedReloadActions,
        _saveEdges,
        _floorObserved);

    private static float HorizontalDistance(Vector3 from, Vector3 to)
    {
        var delta = to - from;
        delta.Y = 0.0f;
        return delta.Length();
    }

    internal readonly record struct Snapshot(
        float MaximumLocomotionMeters,
        float MaximumLeftHandTravelMeters,
        float MaximumRightHandTravelMeters,
        float MaximumMoveStickMagnitude,
        float MaximumTurnStickMagnitude,
        float MaximumEyeHeightErrorMeters,
        float MaximumSnapPivotErrorMeters,
        int TrackedFrames,
        int SnapTurns,
        int ActivationEdges,
        int AcceptedActivations,
        int FireEdges,
        int AcceptedFireActions,
        int ReloadEdges,
        int AcceptedReloadActions,
        int SaveEdges,
        bool FloorObserved);
}
