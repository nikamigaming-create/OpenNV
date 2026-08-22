using Godot;

namespace OpenNV.Runtime;

internal partial class DoorInstance : Node3D
{
    private float _closedYaw;

    internal bool IsOpen { get; private set; }
    internal string ReferenceFormId { get; private set; } = "";

    internal void Configure(string referenceFormId, float closedYaw)
    {
        ReferenceFormId = referenceFormId;
        _closedYaw = closedYaw;
        SetOpen(false);
    }

    internal void SetOpen(bool open)
    {
        IsOpen = open;
        Rotation = new Vector3(0.0f, _closedYaw - (open ? MathF.PI / 2.0f : 0.0f), 0.0f);
    }
}
