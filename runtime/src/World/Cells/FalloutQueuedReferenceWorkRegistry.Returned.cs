namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutQueuedReferenceWorkRegistry
{
    internal void ForgetRetired(Guid identity)
    {
        RequireThread();
        if (!_work.TryGetValue(identity, out var entry) || entry.Read is not { Retired: true } read || !read.ReadTask.IsCompleted)
            throw new InvalidDataException("Actual source read registry cannot release a live/foreign queued object.");
        if (entry.Failure is not null) throw new InvalidOperationException("Queued registry retains its actual construction/retirement failure.", entry.Failure);
        _work.Remove(identity);
    }
}
