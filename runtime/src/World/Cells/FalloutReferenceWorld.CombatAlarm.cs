using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal void StartCombat(FalloutFormKey reference, FalloutFormKey target)
    {
        if (records.RuntimeFormId(reference) == 0x14)
            throw new NotSupportedException("Scripted player combat has no player engagement owner.");
        var actor = Actor(reference);
        if (target == reference) throw new InvalidDataException("An actor cannot start combat against itself.");
        if (records.RuntimeFormId(target) != 0x14) _ = Actor(target);
        if (!IsEnabled(reference) || actor.Injury?.Dead == true || actor.Unconscious)
            throw new NotSupportedException("Scripted combat for a disabled, dead or unconscious actor is unbound.");
        if (actor.Engagement is { } current)
        {
            if (current.Target == target) return;
            throw new NotSupportedException("Scripted combat target replacement is unbound.");
        }
        actor.AggroWarning = null;
        actor.Engagement = new(target);
    }

    internal void StopCombatAlarmOnActor(FalloutFormKey target)
    {
        if (records.RuntimeFormId(target) != 0x14) _ = Actor(target);
        foreach (var instance in _instances.Values.Where(instance => instance.Engagement?.Target == target ||
            instance.AggroWarning?.Target == target).ToArray())
        {
            if (instance.Engagement?.Target != target && instance.AggroWarning?.Target != target) continue;
            // The shared fighting flag is authoritative. A resident owner also
            // retires its current motion and publishes its actual combat-end event.
            instance.Engagement = null;
            instance.AggroWarning = null;
            instance.StopCombat?.Invoke();
        }
    }
}
