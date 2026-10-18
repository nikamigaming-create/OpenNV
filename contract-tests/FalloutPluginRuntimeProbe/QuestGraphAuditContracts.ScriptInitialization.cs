using OpenNV.Runtime.Content;

internal static partial class QuestGraphAuditContracts
{
    internal static void EmbeddedScriptCount()
    {
        var root = Path.Combine(Path.GetTempPath(), $"opennv-embedded-scripts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var observer = FalloutPluginRecord.ReadObserver;
        try
        {
            var header = ScriptHeader(); header[16] = 1;
            var source = Path.Combine(root, "Source.esm");
            File.WriteAllBytes(source, Join(Header(),
                Record("FUTR", 0x100, 0, Field("SCHR", header), Field("SCHR", header)),
                Record("FUTR", 0x101, 0, Field("SCHR", header)),
                Record("SCPT", 0x102, 0, Field("SCHR", header))));
            File.WriteAllBytes(Path.Combine(root, "Winner.esp"), Join(Header("Source.esm"),
                Record("FUTR", 0x100, 0, Field("SCHR", header)),
                Record("FUTR", 0x101, FalloutPluginRecord.DeletedFlag, Field("SCHR", [1]))));
            using (var records = FalloutPluginStack.Load(root, ["Source.esm", "Winner.esp"]))
            {
                var reads = 0;
                FalloutPluginRecord.ReadObserver = (record, _) =>
                {
                    if (record.Signature == "FUTR") Interlocked.Increment(ref reads);
                };
                var counts = new long[16];
                Parallel.For(0, counts.Length, index => counts[index] = records.CountEmbeddedQuestScripts());
                Require(counts.All(value => value == 1) && reads == 1,
                    "Embedded count lost winning/unknown/deleted declarations or repeated its complete scan.");
                var delayed = new FalloutQuestScriptInitialization(records, 5);
                var immediate = new FalloutQuestScriptInitialization(records, 0);
                Require(delayed.EmbeddedQuestScripts == 1 && immediate.EmbeddedQuestScripts == 1 && reads == 1 &&
                    delayed.Definitions.Single().Value.InitialPhase == 2.5f &&
                    immediate.Definitions.Single().Value.InitialPhase == 0,
                    "Source reuse retained a previous owner's default delay or replayed source observations.");
                using (var separate = FalloutPluginStack.Load(root, ["Source.esm"]))
                    Require(separate.CountEmbeddedQuestScripts() == 3,
                        "Embedded declaration count escaped its winning source stack.");
                records.Dispose();
                try { records.CountEmbeddedQuestScripts(); throw new InvalidDataException("Disposed source count remained usable."); }
                catch (ObjectDisposedException) { }
            }
            using (var records = FalloutPluginStack.Load(root, ["Source.esm"]))
            using (var cancellation = new CancellationTokenSource())
            {
                var reads = 0;
                FalloutPluginRecord.ReadObserver = (record, _) =>
                {
                    if (record.Signature == "FUTR" && Interlocked.Increment(ref reads) == 1)
                        cancellation.Cancel();
                };
                try
                {
                    _ = Task.Run(() => records.CountEmbeddedQuestScripts(cancellation.Token)).GetAwaiter().GetResult();
                    throw new InvalidDataException("Cancelled embedded declaration scan published a partial count.");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                Require(Task.Run(() => records.CountEmbeddedQuestScripts()).GetAwaiter().GetResult() == 3 && reads == 3,
                    "Cancelled source work polluted the shared count or lost an embedded declaration.");
                try
                {
                    records.CountEmbeddedQuestScripts(cancellation.Token);
                    throw new InvalidDataException("Cancelled cached declaration request returned a count.");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                Require(reads == 3, "Cancelled cache access reread source declarations.");
            }
            File.WriteAllBytes(source, Join(Header(), Record("FUTR", 0x100, 0, Field("SCHR", [1]))));
            using (var records = FalloutPluginStack.Load(root, ["Source.esm"]))
            {
                var reads = 0;
                FalloutPluginRecord.ReadObserver = (record, _) => { if (record.Signature == "FUTR") reads++; };
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try { records.CountEmbeddedQuestScripts(); throw new InvalidDataException("Malformed embedded header passed."); }
                    catch (NotSupportedException error) when (error.Message.Contains("SCHR", StringComparison.Ordinal)) { }
                }
                Require(reads == 2, "Failed source scans published a partial cached count.");
            }
            Console.WriteLine("OPENNV_EMBEDDED_SCRIPT_COUNT_PASS winners=true fullGraph=true concurrent=true independentClocks=true instanceOwned=true failuresRetained=true disposed=true cancelledPartialNotPublished=true workerRetryComplete=true");
        }
        finally
        {
            FalloutPluginRecord.ReadObserver = observer;
            Directory.Delete(root, recursive: true);
        }
    }
}
