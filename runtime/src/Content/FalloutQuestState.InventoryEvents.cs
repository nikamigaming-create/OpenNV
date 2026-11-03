using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutQuestState
{
    internal FalloutQuestState CopyForInventoryEvents(FalloutHudNotifications? stagedNotifications = null)
    {
        var result = new FalloutQuestState(stack, stagedNotifications);
        result.Restore(Capture());
        result.Revision = Revision; result.ProgressRevision = ProgressRevision;
        return result;
    }

    internal FalloutInventoryChangeLease PrepareInventoryReplacement(FalloutQuestState staged,
        long expectedRevision, IReadOnlyList<FalloutQuestObjectiveChange> changes)
    {
        var before = CopyForInventoryEvents(); var after = staged.CopyForInventoryEvents();
        var started = false;
        void Publish(FalloutQuestState source)
        {
            _states.Clear(); foreach (var item in source._states) _states.Add(item.Key, item.Value);
            Revision = source.Revision; ProgressRevision = source.ProgressRevision;
        }
        return new(() =>
        {
            if (Revision != expectedRevision)
                throw new InvalidOperationException("Quest state changed during inventory-event preparation.");
            started = true; Publish(after);
        }, () => { if (started) Publish(before); }, () =>
        {
            foreach (var change in changes) ObjectiveChanged?.Invoke(change);
        });
    }
}
