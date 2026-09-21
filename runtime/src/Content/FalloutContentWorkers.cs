namespace OpenNV.Runtime.Content;

// Pure owned-data preparation only. Scene mutation, physics and input remain
// on their native owners. Share this limit across terrain and reference loads
// so two concurrent streams cannot each consume the whole processor pool.
internal static class FalloutContentWorkers
{
    internal static int AvailableProcessors { get; } = Environment.ProcessorCount;
    internal static long AvailableMemoryBytes { get; } = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    internal static int Concurrency { get; } = Budget(AvailableProcessors, AvailableMemoryBytes);
    private static readonly SemaphoreSlim Slots = new(Concurrency);

    internal static int Budget(int availableProcessors, long availableMemoryBytes)
    {
        if (availableProcessors <= 0 || availableMemoryBytes < 0) throw new ArgumentOutOfRangeException(nameof(availableProcessors));
        // Environment.ProcessorCount respects process affinity/CPU quotas.
        // Leave most scheduler capacity to latency-sensitive engine work. A
        // memory-constrained process also admits fewer simultaneous decodes.
        var memoryLimit = availableMemoryBytes is > 0 and < 8L * 1024 * 1024 * 1024 ? 2 : 4;
        return Math.Clamp(availableProcessors / 4, 1, memoryLimit);
    }

    internal static async Task<T> Run<T>(Func<T> prepare, CancellationToken cancellation = default)
    {
        await Slots.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await Task.Run(prepare, cancellation).ConfigureAwait(false); }
        finally { Slots.Release(); }
    }
}
