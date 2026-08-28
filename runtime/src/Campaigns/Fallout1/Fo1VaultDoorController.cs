using Godot;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1VaultDoorControllerNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloatNEgativE96Point0f = -96.0f;
    internal const float PresentationFloat0Point001f = 0.001f;
    internal const float PresentationFloat0Point08f = 0.08f;
    internal const float PresentationFloat0Point94f = 0.94f;
    internal const float PresentationFloat0Point999f = 0.999f;
    internal const float PresentationFloat4Point6f = 4.6f;
}

internal sealed class Fo1VaultDoorController
{
    private readonly Node3D _leaf;
    private readonly Vector3 _closedPosition;
    private readonly Vector3 _closedRotation;
    private readonly Vector3 _openOffset;

    internal Fo1VaultDoorController(Node3D leaf, Aabb bounds)
    {
        _leaf = leaf;
        _closedPosition = leaf.Position;
        _closedRotation = leaf.Rotation;
        var lateral = new Basis(Vector3.Up, _closedRotation.Y) * Vector3.Right;
        _openOffset = lateral.Normalized() * MathF.Max(Fo1VaultDoorControllerNumericContracts.PresentationFloat4Point6f, bounds.Size.X * Fo1VaultDoorControllerNumericContracts.PresentationFloat0Point94f);
        SetOpenAmount(0.0f);
    }

    internal float OpenAmount { get; private set; }
    internal bool IsClosed => OpenAmount <= Fo1VaultDoorControllerNumericContracts.PresentationFloat0Point001f;
    internal bool IsOpen => OpenAmount >= Fo1VaultDoorControllerNumericContracts.PresentationFloat0Point999f;

    internal void SetOpenAmount(float amount)
    {
        OpenAmount = Math.Clamp(amount, 0.0f, 1.0f);
        var eased = OpenAmount * OpenAmount * (3.0f - 2.0f * OpenAmount);
        _leaf.Position = _closedPosition + _openOffset * eased +
            Vector3.Up * (MathF.Sin(eased * MathF.PI) * Fo1VaultDoorControllerNumericContracts.PresentationFloat0Point08f);
        _leaf.Rotation = _closedRotation +
            new Vector3(0.0f, 0.0f, Mathf.DegToRad(Fo1VaultDoorControllerNumericContracts.PresentationFloatNEgativE96Point0f) * eased);
    }

    internal object Report() => new
    {
        state = IsClosed ? "closed" : IsOpen ? "open" : "moving",
        openAmount = OpenAmount,
        closedPosition = new[] { _closedPosition.X, _closedPosition.Y, _closedPosition.Z },
        openOffsetMeters = new[] { _openOffset.X, _openOffset.Y, _openOffset.Z },
        adaptation = "bounded 3D presentation control; an open live-control state is not claimed as retail door-state parity",
    };
}
