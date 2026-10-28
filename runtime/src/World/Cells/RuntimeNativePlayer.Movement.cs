using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutPlayerMovementSpeed? _movementSpeed;
    private FalloutPlayerMovement ResolveMovementSpeed(bool running)
    {
        var values = _combatActorValue ?? throw new NotSupportedException("Player movement has no actor-value owner.");
        var vitals = _limbVitals?.Invoke() ?? throw new NotSupportedException("Player movement has no limb/vitals owner.");
        var owner = _movementSpeed ?? throw new NotSupportedException("Player movement has no source speed owner.");
        return owner.Calculate(running, Activity.Sneaking, WeaponDrawn, vitals, values,
            (_combatPerks ?? throw new NotSupportedException("Player movement has no perk owner."))(),
            _combatCondition ?? throw new NotSupportedException("Player movement has no condition owner."));
    }

    internal bool Overencumbered => (_movementSpeed ?? throw new NotSupportedException("Player movement has no source speed owner."))
        .Overencumbered(_combatActorValue ?? throw new NotSupportedException("Player movement has no actor-value owner."),
            (_combatPerks ?? throw new NotSupportedException("Player movement has no perk owner."))(),
            _combatCondition ?? throw new NotSupportedException("Player movement has no condition owner."));

    internal float CalculateRunSpeed()
    {
        var result = ResolveMovementSpeed(running: true);
        return !result.Overencumbered ? result.Speed :
            throw new InvalidOperationException("Overencumbered player cannot fast travel.");
    }

    // Use the same source, posture, equipment and injury owners as movement.
    // Unlike fast travel, ordinary movement may have an overencumbered speed.
    internal float PredictMovementSpeedMeters(bool running)
    {
        var speed = ResolveMovementSpeed(running).Speed * UnitsToMeters;
        return float.IsFinite(speed) && speed >= 0 ? speed :
            throw new InvalidDataException("Player movement speed is not finite and nonnegative.");
    }

    internal object MovementSpeedObservation
    {
        get
        {
            try { return new { runMetersPerSecond = PredictMovementSpeedMeters(true), walkMetersPerSecond = PredictMovementSpeedMeters(false), error = (string?)null }; }
            catch (Exception error) { return new { runMetersPerSecond = (float?)null, walkMetersPerSecond = (float?)null, error = error.Message }; }
        }
    }

    private float MovementSpeed(bool running)
    {
        try
        {
            var result = ResolveMovementSpeed(running);
            Running = result.Running;
            _crippledLegCount = result.CrippledLegs;
            _crippledLegSpeedMultiplier = result.LimbMultiplier;
            _crippledLegMovementError = null;
            PublishLimbMovementState();
            return result.Speed * UnitsToMeters;
        }
        catch (Exception error)
        {
            Running = false;
            _crippledLegCount = null;
            _crippledLegSpeedMultiplier = 0;
            _crippledLegMovementError = error.Message;
            PublishLimbMovementState();
            return 0;
        }
    }
}
