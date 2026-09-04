using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeVigorTrigger : Area3D
{
    private Action _entered = null!;
    private bool _accepted;

    internal void Configure(
        Vector3 dimensionsMeters,
        uint playerCollisionLayer,
        Action entered)
    {
        if (dimensionsMeters.X <= 0.0f || dimensionsMeters.Y <= 0.0f || dimensionsMeters.Z <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(dimensionsMeters));
        if (playerCollisionLayer == 0)
            throw new ArgumentOutOfRangeException(nameof(playerCollisionLayer));
        _entered = entered ?? throw new ArgumentNullException(nameof(entered));
        Name = "NativeVigorTrigger";
        CollisionLayer = 0;
        CollisionMask = playerCollisionLayer;
        Monitoring = true;
        Monitorable = false;
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = dimensionsMeters },
        });
        BodyEntered += body =>
        {
            if (_accepted || body is not RuntimeNativePlayer)
                return;
            _entered();
            _accepted = true;
        };
    }
}

internal partial class RuntimeNativeVigorActivator : Node
{
    private Action _activate = null!;

    internal void Configure(Action activate)
    {
        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
        Name = "NativeVigorActivator";
    }

    internal void Activate() => _activate();
}
