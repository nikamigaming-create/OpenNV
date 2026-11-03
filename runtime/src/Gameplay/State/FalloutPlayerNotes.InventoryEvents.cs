namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerNotes
{
    internal FalloutPlayerNotes CopyForInventoryEvents()
    {
        var result = new FalloutPlayerNotes(_records, Capture());
        result.Revision = Revision;
        return result;
    }

    internal FalloutInventoryChangeLease PrepareInventoryReplacement(FalloutPlayerNotes staged, long expectedRevision)
    {
        var before = CopyForInventoryEvents();
        var after = staged.CopyForInventoryEvents();
        var started = false;
        void Publish(FalloutPlayerNotes source, long revision)
        {
            _notes.Clear(); _notes.AddRange(source._notes); Known = source.Known; Revision = revision;
        }
        return new(() =>
        {
            if (Revision != expectedRevision)
                throw new InvalidOperationException("Note membership changed during inventory-event preparation.");
            started = true; Publish(after, after.Revision);
        }, () => { if (started) Publish(before, expectedRevision); });
    }
}
