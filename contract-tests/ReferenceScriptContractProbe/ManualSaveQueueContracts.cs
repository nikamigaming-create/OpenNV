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

    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
