namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerVitals
{
    internal float BaseActionPoints
    {
        get
        {
            // Base AV12's derived callback reads bounded current Agility.
            var agility = _actorValues?.ReadBoundedCurrent(10) ?? _lastSpecial?.Agility ??
                throw new InvalidOperationException("Base action points have no base Agility owner.");
            var value = (float)(_apBase + _apAgility * agility);
            return float.IsFinite(value) && value >= 0 ? value : throw new InvalidDataException("Base action points are invalid.");
        }
    }
    internal void SpendActionPoints(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var state = State;
        if (amount > state.ExactActionPoints) throw new InvalidOperationException("Insufficient action points.");
        PublishActionPoints(state.ExactActionPoints - amount);
    }

    internal void RestoreActionPoints(float amount)
    {
        if (!float.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        PublishActionPoints(Math.Min(State.MaximumActionPoints, State.ExactActionPoints + amount));
    }

    private void PublishActionPoints(float remaining)
    {
        var displayed = checked((int)MathF.Ceiling(remaining));
        Publish(State with { ActionPoints = displayed, ActionPointFraction = displayed - remaining });
    }
}
