using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal void StopCombatAlarmOnActor(FalloutFormKey target)
    {
        if (records.RuntimeFormId(target) != 0x14) _ = Actor(target);
        foreach (var instance in _instances.Values.Where(instance => instance.Engagement?.Target == target).ToArray())
        {
            if (instance.Engagement?.Target != target) continue;
            // The shared fighting flag is authoritative. A resident owner also
            // retires its current motion and publishes its actual combat-end event.
            instance.Engagement = null;
            instance.StopCombat?.Invoke();
        }
    }
}
