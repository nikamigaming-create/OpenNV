using System.Text.Json;
using OpenNV.Runtime.Diagnostics.Parity;

internal static class SequentialInputProbe
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-sequential-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "unjoined.jsonl");
        var journalPath = Path.Combine(directory, "receipts.jsonl");
        var binding = new RecordedInputBinding(new string('a', 64), "actual-opennv-scene", [new("Source.esm", new string('b', 64))]);
        var input = JsonSerializer.SerializeToElement(new { op = "key", key = "W", pressed = true, leaseMilliseconds = 100 });
        try
        {
            using (var writer = new RecordedInputWriter(path, new(RecordedInputTape.UnjoinedSchema, "retail", null,
                new string('c', 64), "receipt-observed", new string('d', 64))))
            { writer.Append(10, "unjoined:retail-native-input", input); writer.Finish(50); }
            var tape = RecordedInputTape.Read(path);
            Reject(() => new RecordedInputPlayback(tape, binding, 1000));
            Reject(() => RecordedInputTape.ValidateHeader(tape.Header with { Binding = binding }));
            Reject(() => RecordedInputTape.ValidateHeader(tape.Header with { RetailExecutableSha256 = null }));
            var replay = RecordedInputPlayback.UnjoinedDiagnostic(tape, 1000);
            var dispatches = 0; var releases = 0;
            replay.Advance(10, () => binding.StateKey, _ => ++dispatches, () => ++releases);
            replay.Advance(50, () => "different-actual-scene", _ => ++dispatches, () => ++releases);
            Require(replay.Complete && dispatches == 1 && releases == 1 && replay.DeliveryReturnedOrdinal == 1,
                "Explicit unjoined replay invented a checkpoint join or lost its actual returned prefix.");

            replay = RecordedInputPlayback.UnjoinedDiagnostic(tape, 1000); dispatches = 0; releases = 0;
            replay.Advance(10, () => binding.StateKey, _ => { ++dispatches; throw new IOException("Authored post-delivery evidence failure."); },
                () => { ++releases; throw new IOException("Authored release failure."); });
            replay.Advance(50, () => binding.StateKey, _ => ++dispatches, () => ++releases);
            Require(dispatches == 1 && releases == 1 && !replay.Active && !replay.Complete && replay.Cursor == 0 &&
                replay.AttemptedOrdinal == 1 && replay.DeliveryEntered && !replay.DeliveryReturned && replay.ReleaseError is not null,
                "A committed delivery callback failure was retried or its release failure disappeared.");
            replay = RecordedInputPlayback.UnjoinedDiagnostic(tape, 1000);
            replay.Advance(50, () => binding.StateKey, _ => { }, () => { });
            replay.RetainEvidenceFailure("Authored retirement evidence failure.");
            Require(!replay.Complete && !replay.Active && replay.Cursor == 1 && replay.DeliveryReturnedOrdinal == 1,
                "Receipt retirement failure erased returned input or became successful completion.");

            using (var journal = new RecordedInputDeliveryJournal(journalPath, new { authored = true }))
            { journal.Append("adapter-returned", 10, new { delivered = true, input }); journal.Finish(50, null); }
            Reject(() => new RecordedInputDeliveryJournal(journalPath, new { authored = true }));
            Console.WriteLine("OPENNV_SEQUENTIAL_INPUT_CONTRACT_PASS authored=true explicitUnjoined=true boundReplayRefusesUnjoined=true " +
                "attemptPrefixRetained=true callbackNoRetry=true releaseFailureRetained=true receiptRetirementFailureRetained=true " +
                "nativeRetailAndGodotRun=UNEXECUTED gameplayParity=UNVERIFIED framesRecorded=false");
        }
        finally
        {
            foreach (var file in new[] { path, journalPath }) if (File.Exists(file)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception failure) when (failure is InvalidDataException or IOException) { return; }
        throw new InvalidDataException("Invalid replay admission/evidence replacement was accepted.");
    }
}
