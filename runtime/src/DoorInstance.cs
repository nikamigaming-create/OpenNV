using Godot;

namespace OpenNV.Runtime;

internal partial class DoorInstance : Node3D
{
    private float _closedYaw;
    private float _openAngleRadians;

    internal bool IsOpen { get; private set; }
    internal string ReferenceFormId { get; private set; } = "";
    internal string? DestinationReferenceFormId { get; private set; }
    internal TeleportDestination? Destination { get; private set; }
    internal DoorInstance? LinkedDoor { get; private set; }

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
        SetOpen(false);
    }

    internal void Link(DoorInstance reciprocal)
    {
        if (reciprocal == this ||
            DestinationReferenceFormId != reciprocal.ReferenceFormId ||
            reciprocal.DestinationReferenceFormId != ReferenceFormId)
            throw new InvalidOperationException("Door link is not a reciprocal XTEL pair.");
        LinkedDoor = reciprocal;
        reciprocal.LinkedDoor = this;
        SetOpen(IsOpen || reciprocal.IsOpen);
    }

    internal void SetOpen(bool open)
    {
        IsOpen = open;
        Rotation = new Vector3(0.0f, _closedYaw - (open ? _openAngleRadians : 0.0f), 0.0f);
        if (LinkedDoor is not null && LinkedDoor.IsOpen != open)
            LinkedDoor.SetOpen(open);
    }

    internal readonly record struct TeleportDestination(
        Vector3 PositionGameUnits,
        float YawGodotRadians);
}
