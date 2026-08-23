using Godot;

namespace OpenNV.Runtime;

internal partial class DoorInstance : Node3D
{
    private float _closedYaw;

    internal bool IsOpen { get; private set; }
    internal string ReferenceFormId { get; private set; } = "";
    internal string? DestinationReferenceFormId { get; private set; }
    internal DoorInstance? LinkedDoor { get; private set; }

    internal void Configure(string referenceFormId, float closedYaw, string? destinationReferenceFormId = null)
    {
        ReferenceFormId = referenceFormId;
        DestinationReferenceFormId = destinationReferenceFormId;
        _closedYaw = closedYaw;
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
        Rotation = new Vector3(0.0f, _closedYaw - (open ? MathF.PI / 2.0f : 0.0f), 0.0f);
        if (LinkedDoor is not null && LinkedDoor.IsOpen != open)
            LinkedDoor.SetOpen(open);
    }
}
