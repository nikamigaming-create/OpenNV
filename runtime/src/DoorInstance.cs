using Godot;

namespace OpenNV.Runtime;

internal partial class DoorInstance : Node3D
{
    private float _closedYaw;
    private float _openAngleRadians;
    private Node3D? _articulationTarget;
    private ArticulationSequence _openArticulation;
    private ArticulationSequence _closeArticulation;
    private Tween? _articulationTween;

    internal bool IsOpen { get; private set; }
    internal string ReferenceFormId { get; private set; } = "";
    internal string? DestinationReferenceFormId { get; private set; }
    internal TeleportDestination? Destination { get; private set; }
    internal DoorInstance? LinkedDoor { get; private set; }
    internal bool HasSourceArticulation => _articulationTarget is not null;
    internal bool ArticulationMoving => _articulationTween?.IsRunning() == true;
    internal bool SourceOpenTerminalApplied =>
        _articulationTarget is not null &&
        IsOpen &&
        !ArticulationMoving &&
        TransformsMatch(_articulationTarget.Transform, _openArticulation.Terminal);

    internal void Configure(
        string referenceFormId,
        float closedYaw,
        float openAngleDegrees,
        string? destinationReferenceFormId = null,
        TeleportDestination? destination = null)
    {
        if ((destinationReferenceFormId is null) != (destination is null))
            throw new InvalidOperationException(
                "Door XTEL reference and destination transform must be present together.");
        ReferenceFormId = referenceFormId;
        DestinationReferenceFormId = destinationReferenceFormId;
        Destination = destination;
        _closedYaw = closedYaw;
        _openAngleRadians = Mathf.DegToRad(openAngleDegrees);
        RestoreOpenState(false);
    }

    internal void ConfigureSourceArticulation(
        Node3D target,
        Basis referenceBasis,
        ArticulationSequence open,
        ArticulationSequence close)
    {
        if (_articulationTarget is not null ||
            !IsDescendantOf(target, this) ||
            !IsFinite(referenceBasis) ||
            !IsValid(open) ||
            !IsValid(close) ||
            !TransformsMatch(open.Initial, close.Terminal))
            throw new InvalidOperationException(
                $"Door source articulation is incomplete or mismatched: {ReferenceFormId}");
        _articulationTarget = target;
        _openArticulation = open;
        _closeArticulation = close;
        Basis = referenceBasis;
        RestoreOpenState(IsOpen);
    }

    internal void Link(DoorInstance reciprocal)
    {
        if (reciprocal == this ||
            DestinationReferenceFormId != reciprocal.ReferenceFormId ||
            reciprocal.DestinationReferenceFormId != ReferenceFormId)
            throw new InvalidOperationException("Door link is not a reciprocal XTEL pair.");
        LinkedDoor = reciprocal;
        reciprocal.LinkedDoor = this;
        RestoreOpenState(IsOpen || reciprocal.IsOpen);
    }

    internal void SetOpen(bool open)
    {
        IsOpen = open;
        if (_articulationTarget is null)
        {
            Rotation = new Vector3(
                0.0f,
                _closedYaw - (open ? _openAngleRadians : 0.0f),
                0.0f);
        }
        else
        {
            _articulationTween?.Kill();
            var sequence = open ? _openArticulation : _closeArticulation;
            _articulationTarget.Transform = sequence.Initial;
            var tween = CreateTween();
            tween.SetProcessMode(Tween.TweenProcessMode.Physics);
            tween.TweenProperty(
                _articulationTarget,
                "transform",
                sequence.Terminal,
                sequence.DurationSeconds);
            tween.TweenCallback(Callable.From(() =>
            {
                _articulationTarget.Transform = sequence.Terminal;
                if (_articulationTween == tween)
                    _articulationTween = null;
            }));
            _articulationTween = tween;
        }
        if (LinkedDoor is not null && LinkedDoor.IsOpen != open)
            LinkedDoor.SetOpen(open);
    }

    internal void RestoreOpenState(bool open)
    {
        IsOpen = open;
        _articulationTween?.Kill();
        _articulationTween = null;
        if (_articulationTarget is null)
        {
            Rotation = new Vector3(
                0.0f,
                _closedYaw - (open ? _openAngleRadians : 0.0f),
                0.0f);
        }
        else
        {
            _articulationTarget.Transform = open
                ? _openArticulation.Terminal
                : _closeArticulation.Terminal;
        }
        if (LinkedDoor is not null && LinkedDoor.IsOpen != open)
            LinkedDoor.RestoreOpenState(open);
    }

    internal async Task WaitForArticulation()
    {
        while (ArticulationMoving)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private static bool IsDescendantOf(Node node, Node ancestor)
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
            if (parent == ancestor)
                return true;
        return false;
    }

    private static bool IsValid(ArticulationSequence sequence) =>
        float.IsFinite(sequence.DurationSeconds) &&
        sequence.DurationSeconds > 0.0f &&
        IsFinite(sequence.Initial) &&
        IsFinite(sequence.Terminal);

    private static bool IsFinite(Transform3D transform) =>
        IsFinite(transform.Basis) &&
        IsFinite(transform.Origin);

    private static bool IsFinite(Basis basis) =>
        IsFinite(basis.X) &&
        IsFinite(basis.Y) &&
        IsFinite(basis.Z) &&
        MathF.Abs(basis.Determinant()) > 0.000001f;

    private static bool IsFinite(Vector3 vector) =>
        float.IsFinite(vector.X) &&
        float.IsFinite(vector.Y) &&
        float.IsFinite(vector.Z);

    private static bool TransformsMatch(Transform3D left, Transform3D right) =>
        left.Origin.IsEqualApprox(right.Origin) &&
        left.Basis.IsEqualApprox(right.Basis);

    internal readonly record struct ArticulationSequence(
        Transform3D Initial,
        Transform3D Terminal,
        float DurationSeconds);

    internal readonly record struct TeleportDestination(
        Vector3 PositionGameUnits,
        float YawGodotRadians);
}
