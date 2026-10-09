using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerVitals
{
    // Invoked only by the admitted XP-origin player update. This does not
    // allocate skills/perks or open a menu inside a script command.
    internal void AdvancePlayerLevel(int requested)
    {
        var current = State;
        if (requested != checked(current.Level + 1))
            throw new InvalidDataException("Player advancement must consume exactly one gained level.");
        if (current.ExperiencePoints < ExperienceThreshold(requested))
            throw new NotSupportedException("Queued advancement below its original XP threshold has no admitted consumption policy.");
        var values = _actorValues ?? throw new NotSupportedException("Player advancement has no shared actor-value owner.");
        var next = GameplayVitals.Derive(_baseHealth, requested, values.ReadPermanent(7), values.ReadBoundedCurrent(10),
            _healthEndurance, _healthLevel, _apBase, _apAgility, current.ExperiencePoints, _xpBase, _xpBump);
        SetDerived(next);
    }

    internal void RequireProgressJoin(int level, int experience, int nextThreshold)
    {
        var current = State;
        if (current.Level != level || current.ExperiencePoints != experience ||
            current.NextLevelExperiencePoints != nextThreshold || ExperienceThreshold(checked(level + 1)) != nextThreshold)
            throw new InvalidDataException("Player progression differs from its actual level/XP/threshold owner.");
    }
}
