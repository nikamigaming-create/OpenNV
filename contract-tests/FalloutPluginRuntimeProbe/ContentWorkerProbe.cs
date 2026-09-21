using OpenNV.Runtime.Content;

internal static class ContentWorkerProbe
{
    internal static void Run()
    {
        if (FalloutContentWorkers.Budget(1, 2L << 30) != 1 || FalloutContentWorkers.Budget(4, 16L << 30) != 1 ||
            FalloutContentWorkers.Budget(8, 16L << 30) != 2 || FalloutContentWorkers.Budget(28, 32L << 30) != 4 ||
            FalloutContentWorkers.Budget(64, 4L << 30) != 2)
            throw new InvalidDataException("Content budget ignored CPU availability or memory pressure.");
        var active = 0; var maximum = 0;
        var entered = new CountdownEvent(FalloutContentWorkers.Concurrency);
        using var release = new ManualResetEventSlim();
        var work = Enumerable.Range(0, FalloutContentWorkers.Concurrency * 3).Select(index =>
            FalloutContentWorkers.Run(() =>
            {
                var count = Interlocked.Increment(ref active);
                lock (entered)
                {
                    maximum = Math.Max(maximum, count);
                    if (entered.CurrentCount > 0) entered.Signal();
                }
                release.Wait(); Interlocked.Decrement(ref active);
                return index;
            })).ToArray();
        try
        {
            if (!entered.Wait(TimeSpan.FromSeconds(10))) throw new InvalidDataException("Content workers did not start.");
            using var cancelled = new CancellationTokenSource();
            var waiting = FalloutContentWorkers.Run<int>(() => throw new InvalidOperationException("Cancelled content ran."), cancelled.Token);
            cancelled.Cancel();
            try { waiting.GetAwaiter().GetResult(); throw new InvalidDataException("Cancelled content was admitted."); }
            catch (OperationCanceledException) { }
        }
        finally { release.Set(); }
        if (!Task.WhenAll(work).GetAwaiter().GetResult().SequenceEqual(Enumerable.Range(0, work.Length)) ||
            maximum > FalloutContentWorkers.Concurrency || active != 0)
            throw new InvalidDataException("Content concurrency exceeded its limit or lost results.");
        entered.Dispose();
        Console.WriteLine($"OPENNV_CONTENT_WORKERS_PASS workers={maximum} bounded=true cancellation=true results=true");
    }
}
