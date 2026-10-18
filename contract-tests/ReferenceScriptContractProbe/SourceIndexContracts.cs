using OpenNV.Runtime.Content;

internal static class SourceIndexContracts
{
    internal static void Verify<T>(string root, string plugin,
        Func<FalloutPluginStack, CancellationToken, T> read, Func<T, string> describe,
        string observedSignature = "REFR", string? cacheReadSignature = null) where T : class
    {
        var previous = FalloutPluginRecord.ReadObserver;
        try
        {
            using var records = FalloutPluginStack.Load(root, [plugin]);
            using var cancellation = new CancellationTokenSource();
            var caller = Environment.CurrentManagedThreadId;
            var observed = 0;
            FalloutPluginRecord.ReadObserver = (record, _) =>
            {
                if (record.Signature != observedSignature) return;
                Require(Environment.CurrentManagedThreadId != caller, "Source preparation ran on its caller thread.");
                if (Interlocked.Increment(ref observed) == 1) cancellation.Cancel();
            };
            try
            {
                _ = Task.Run(() => read(records, cancellation.Token)).GetAwaiter().GetResult();
                throw new InvalidDataException("Cancelled source preparation published an index.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            Require(observed > 0, "Source cancellation was not exercised during an actual declaration read.");
            observed = 0;
            FalloutPluginRecord.ReadObserver = (record, _) =>
            {
                if (cacheReadSignature is null || record.Signature == cacheReadSignature) Interlocked.Increment(ref observed);
            };
            var complete = Task.Run(() => read(records, default)).GetAwaiter().GetResult();
            var afterRead = observed;
            Parallel.For(0, 16, _ => Require(ReferenceEquals(complete, read(records, default)),
                "Concurrent callers did not reuse the completed source index."));
            try
            {
                read(records, cancellation.Token);
                throw new InvalidDataException("Cancelled cache access returned an index.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            Require(observed == afterRead, "Cached source declarations were reread.");
            FalloutPluginRecord.ReadObserver = null;
            using (var independent = FalloutPluginStack.Load(root, [plugin]))
            {
                var separate = read(independent, default);
                Require(!ReferenceEquals(complete, separate) && describe(complete) == describe(separate),
                    "Cancelled preparation retained partial declarations or escaped its source stack.");
            }
            using (var concurrent = FalloutPluginStack.Load(root, [plugin]))
            {
                observed = 0;
                FalloutPluginRecord.ReadObserver = (record, _) =>
                {
                    if (cacheReadSignature is null || record.Signature == cacheReadSignature) Interlocked.Increment(ref observed);
                };
                var values = new T[16];
                Parallel.For(0, values.Length, index => values[index] = read(concurrent, default));
                Require(values.All(value => ReferenceEquals(value, values[0])) && observed == afterRead,
                    "Concurrent cold preparation published multiple indexes or repeated the source scan.");
                FalloutPluginRecord.ReadObserver = null;
            }
            records.Dispose();
            try
            {
                read(records, default);
                throw new InvalidDataException("Disposed source index remained usable.");
            }
            catch (ObjectDisposedException) { }
            Console.WriteLine($"OPENNV_SOURCE_INDEX_CONTRACT_PASS kind={typeof(T).Name} worker=true cancelledPartialNotPublished=true " +
                "concurrentCold=true cancelledRetry=true instanceOwned=true disposedRefused=true sourceExecution=uninspected");
        }
        finally { FalloutPluginRecord.ReadObserver = previous; }
    }

    internal static void VerifyFailure(string root, byte[] malformed, Func<FalloutPluginStack, object> read)
    {
        var path = Path.Combine(root, "Malformed.esm"); File.WriteAllBytes(path, malformed);
        var previous = FalloutPluginRecord.ReadObserver;
        try
        {
            using var records = FalloutPluginStack.Load(root, ["Malformed.esm"]);
            var observed = 0;
            FalloutPluginRecord.ReadObserver = (_, _) => observed++;
            for (var attempt = 0; attempt < 2; ++attempt)
            {
                var before = observed;
                try { _ = read(records); }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException)
                {
                    Require(observed > before, "Failed source index retained a partial cached result.");
                    continue;
                }
                throw new InvalidDataException("Malformed declaration disappeared from the source index.");
            }
            Console.WriteLine("OPENNV_SOURCE_INDEX_FAILURE_RETRY_PASS malformedRefused=true partialCache=false attempts=2");
        }
        finally { FalloutPluginRecord.ReadObserver = previous; }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidDataException(message);
    }
}
