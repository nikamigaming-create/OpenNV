using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class ManualSaveQueueContracts
{
    internal static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-manual-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            BackgroundCommit(directory);
            var session = Guid.NewGuid(); const string source = "synthetic-owned-source-stack";
            var queue = new RuntimeManualSaveRequests(); var request = queue.Request(session, source, 20);
            var coalesced = queue.Request(session, source, 21);
            Require(coalesced.Slot == request.Slot && coalesced.RequestCount == 2, "Repeated pending F5 allocated a second slot.");
            var voice = new FalloutFiniteSoundVoice(10, new("Fixture.esm", 0x100), 7, new("Fixture.esm", 0x200),
                new string('a', 64), "sound\\fixture\\finite.wav", new string('b', 64));
            var writes = 0;
            var authoritative = "before-finished";
            RuntimeSaveSlotMetadata Write(Guid id)
            {
                writes++;
                var path = Path.Combine(directory, id.ToString("N") + ".json");
                File.WriteAllText(path, authoritative);
                return new(id.ToString("N"), path, "synthetic", null, null, null, DateTime.UtcNow);
            }
            Require(!queue.Drain(session, source, 20, () => throw new InvalidDataException("Same-phase admission ran."), Write) && writes == 0,
                "Manual request captured within its input phase.");
            Require(!queue.Drain(session, source, 21, () => new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: [voice]), Write) &&
                queue.Pending && queue.Receipt!.DeferredVoices!.Single() == voice && writes == 0,
                "An active source voice was discarded, falsely completed or saved.");
            authoritative = "genuine-later-complete-state";
            Require(queue.Drain(session, source, 22, () => new(RuntimeManualSaveAdmissionKind.Ready), Write) && writes == 1 &&
                queue.Receipt!.CommittedSlot?.Id == request.Slot.ToString("N") &&
                queue.Receipt.AwaitedVoices?.Single() == voice && queue.Receipt.DeferredVoices is null &&
                File.ReadAllText(queue.Receipt.CommittedSlot.Path) == authoritative,
                "Manual request did not commit the actual later complete state to its retained slot.");
            Require(!queue.Drain(session, source, 23, () => throw new InvalidDataException("Completed request replayed."), Write) && writes == 1,
                "Completed player save invoked its writer twice.");
            foreach (var blocker in new[] { "player-defeated", "cancelled-source-voice", "unowned-physical-pose", "failed-source-event" })
            {
                queue.Request(session, source, 24);
                queue.Drain(session, source, 25, () => new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: [voice]), Write);
                Require(!queue.Drain(session, source, 26, () => new(RuntimeManualSaveAdmissionKind.Refused, blocker), Write) &&
                    !queue.Pending && queue.Receipt!.Disposition == "failed" && queue.Receipt.Error == blocker &&
                    queue.Receipt.AwaitedVoices?.Single() == voice && writes == 1,
                    "An independent unknown, failed or defeated owner was waived.");
            }
            queue.Request(session, source, 26);
            Require(!queue.Drain(Guid.NewGuid(), source, 27, () => throw new InvalidDataException("Other session queried."), Write) &&
                queue.Receipt!.Disposition == "cancelled" && writes == 1, "Pending request migrated to a new session.");
            queue.Request(session, source, 28);
            Require(!queue.Drain(session, "different-source", 29, () => throw new InvalidDataException("Other source queried."), Write) &&
                queue.Receipt!.Disposition == "cancelled" && writes == 1, "Pending request migrated to another source stack.");
            queue.Request(session, source, 30);
            Require(!queue.Drain(session, source, 31, () => new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: [voice, voice]), Write) &&
                queue.Receipt!.Disposition == "failed" && writes == 1, "Duplicate source wait generations were admitted.");
            queue.Request(session, source, 32);
            Require(!queue.Drain(session, source, 33, () => new(RuntimeManualSaveAdmissionKind.Ready), _ => throw new IOException("Writer unavailable.")) &&
                queue.Receipt!.Error == "Writer unavailable." && writes == 1, "Write failure became a completed save.");
            queue.Request(session, source, 34); queue.Cancel("Actual quit before commit.");
            Require(queue.Receipt!.Disposition == "cancelled" && queue.History.Any(receipt => receipt.Disposition == "completed") &&
                queue.History.Count(receipt => receipt.Disposition == "failed") == 6 && writes == 1,
                "Retry/cancellation erased previous manual request histories or replayed a write.");
            Console.WriteLine("OPENNV_MANUAL_SAVE_QUEUE_CONTRACT_PASS phase=true knownFiniteAudioOnly=true laterCompleteState=true retainedSlot=true once=true independentRefused=true death=true sourceSessionCancel=true history=true writerFailure=true campaignAndParity=unverified");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void BackgroundCommit(string directory)
    {
        var queue = new RuntimeManualSaveRequests();
        var session = Guid.NewGuid();
        var request = queue.Request(session, "background", 1);
        queue.Prepare(1, 1, new(RuntimeManualSaveAdmissionKind.Ready), []);
        var completion = new TaskCompletionSource<RuntimeSaveSlotMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captures = 0;
        Require(!queue.DrainPreparedBackground(session, "background", 2, 2, () => [],
            () => new(RuntimeManualSaveAdmissionKind.Ready), _ => { captures++; return completion.Task; }) &&
            queue.Pending && queue.Writing && captures == 1, "Background commit blocked or published before completion.");
        for (var frame = 0; frame < 100; frame++)
            Require(!queue.CompletePendingWrite() && queue.Pending, "An unfinished writer blocked or falsely completed.");
        var path = Path.Combine(directory, request.Slot.ToString("N") + ".json");
        File.WriteAllText(path, "committed");
        completion.SetResult(new(request.Slot.ToString("N"), path, "synthetic", null, null, null, DateTime.UtcNow));
        Require(queue.CompletePendingWrite() && !queue.Writing && !queue.Pending && captures == 1,
            "Background commit did not publish exactly once on its owner.");
        queue.Request(session, "background", 3);
        queue.Prepare(3, 3, new(RuntimeManualSaveAdmissionKind.Ready), []);
        var failed = new TaskCompletionSource<RuntimeSaveSlotMetadata>();
        queue.DrainPreparedBackground(session, "background", 4, 4, () => [],
            () => new(RuntimeManualSaveAdmissionKind.Ready), _ => failed.Task);
        failed.SetException(new IOException("background write failed"));
        Require(!queue.CompletePendingWrite(wait: true) && !queue.Writing &&
            queue.Receipt!.Disposition == "failed" && queue.Receipt.Error == "background write failed",
            "Writer failure or retirement join lost its terminal receipt.");
        request = queue.Request(session, "background", 5);
        queue.Prepare(5, 5, new(RuntimeManualSaveAdmissionKind.Ready), []);
        var steps = 0;
        var commits = 0;
        RuntimeManualSaveWork Work(Guid id) => new(() => ++steps == 4, () =>
        {
            commits++;
            var destination = Path.Combine(directory, id.ToString("N") + ".json");
            File.WriteAllText(destination, "sliced");
            return Task.FromResult(new RuntimeSaveSlotMetadata(id.ToString("N"), destination,
                "synthetic", null, null, null, DateTime.UtcNow));
        });
        Require(!queue.DrainPreparedWork(session, "background", 6, 6, () => [],
            () => new(RuntimeManualSaveAdmissionKind.Ready), Work) && steps == 1 && commits == 0,
            "Capture ignored its first frame boundary.");
        Require(queue.Request(session, "background", 6).Slot == request.Slot &&
            !queue.CompletePendingWrite() && steps == 2 && commits == 0,
            "A coalesced request restarted the paused capture.");
        Require(queue.CompletePendingWrite(wait: true) && steps == 4 && commits == 1 && !queue.Writing,
            "Retirement failed to finish capture on its owner before joining the commit.");
        Console.WriteLine("OPENNV_BACKGROUND_SAVE_CONTRACT_PASS nonblocking=true captureOnce=true ownerCompletion=true failure=true retirementJoin=true");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
