using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class ManualSaveQueueContracts
{
    internal static void Run()
    {
        var voice = new FalloutFiniteSoundVoice(10, new("Fixture.esm", 0x100), 7, new("Fixture.esm", 0x200),
            new string('a', 64), "sound\\fixture\\finite.wav", new string('b', 64));
        using (var fixture = new CompiledManualSaveFixture([], phase: 20))
        {
            var queue = Joined(fixture);
            var first = Request(fixture, queue, 1);
            var second = Request(fixture, queue, 2);
            using var sourceOrder = new RuntimeManualSaveSourceOrder(fixture.Owner, first, fixture.Phase);
            Require(first.Slot != second.Slot && second.Order == first.Order + 1 && queue.Source == fixture.Owner,
                "Distinct native input requests lost their source queue or shared one fabricated slot.");
            var writes = 0;
            RuntimeSaveSlotMetadata Write(Guid id)
            {
                sourceOrder.DrainBeforeManual(queue.Receipt!, fixture.Phase, () => null);
                return sourceOrder.WriteManual(queue.Receipt!, fixture.Phase, slot => { writes++; return fixture.Write(slot); });
            }
            Require(!queue.Drain(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase,
                () => throw new InvalidDataException("Same-phase admission ran."), Write), "Input phase entered capture.");
            fixture.AdvancePhase();
            Require(!queue.Drain(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase,
                () => new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: [voice]), Write) &&
                queue.Receipt!.DeferredVoices!.Single() == voice && writes == 0, "Known finite generation lost its pending slot.");
            fixture.AdvancePhase();
            Require(queue.Drain(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase,
                () => new(RuntimeManualSaveAdmissionKind.Ready), Write) && writes == 1 &&
                queue.Receipt!.CommittedSlot!.Id == first.Slot.ToString("N") &&
                queue.Receipt.AwaitedVoices!.Single() == voice && File.Exists(queue.Receipt.CommittedSlot.Path),
                "The exact later complete state failed its original slot write: " + queue.Receipt!.Error);
            Require(!queue.Drain(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase,
                () => throw new InvalidDataException("Completed admission replayed."), Write), "Completed slot replayed.");
            queue.RetirePreparation();
            Require(queue.Receipt!.Slot == second.Slot && queue.Pending, "Retirement lost the independently queued second input.");
            queue.Cancel("Actual quit before second write.");
            Require(queue.History is [{ Disposition: "completed" }, { Disposition: "cancelled" }] && writes == 1,
                "Second cancellation erased the earlier completed source request.");
        }
        foreach (var blocker in new[] { "player-defeated", "cancelled-source-voice", "unowned-physical-pose", "failed-source-event" })
        {
            using var fixture = new CompiledManualSaveFixture([], phase: 1);
            var queue = Joined(fixture); Request(fixture, queue, 1); fixture.AdvancePhase();
            queue.Drain(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase,
                () => new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: [voice]), NoWrite);
            fixture.AdvancePhase();
            Require(!queue.Drain(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase,
                () => new(RuntimeManualSaveAdmissionKind.Refused, blocker), NoWrite) &&
                queue.Receipt is { Disposition: "failed" } failed && failed.Error == blocker &&
                failed.AwaitedVoices!.Single() == voice && fixture.Owner.Order.Requests.Single().Error == blocker,
                "An independent failed owner disappeared from the shared queue.");
        }
        foreach (var otherSession in new[] { false, true })
        {
            using var fixture = new CompiledManualSaveFixture([], phase: 1);
            var queue = Joined(fixture); Request(fixture, queue, 1); fixture.AdvancePhase();
            Require(!queue.Drain(otherSession ? Guid.NewGuid() : fixture.Session,
                otherSession ? fixture.Binding.SourceCompatibilityId : "different-source", fixture.Phase,
                () => throw new InvalidDataException("Foreign admission queried."), NoWrite) &&
                queue.Receipt!.Disposition == "cancelled", "A request migrated across source/session ownership.");
        }
        using (var fixture = new CompiledManualSaveFixture([], phase: 1))
        {
            var queue = Joined(fixture); Request(fixture, queue, 1); fixture.AdvancePhase();
            Require(!queue.Drain(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase,
                () => new(RuntimeManualSaveAdmissionKind.FiniteSourceAudio, Voices: [voice, voice]), NoWrite) &&
                queue.Receipt!.Disposition == "failed", "Duplicate native wait generations were admitted.");
        }
        using (var fixture = new CompiledManualSaveFixture([], phase: 1))
        {
            var queue = Joined(fixture); Request(fixture, queue, 1); fixture.AdvancePhase();
            Require(!queue.Drain(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase,
                () => new(RuntimeManualSaveAdmissionKind.Ready), _ => throw new IOException("Writer unavailable.")) &&
                queue.Receipt!.Error == "Writer unavailable.", "Writer failure became a completed source request.");
        }
        Console.WriteLine("OPENNV_MANUAL_SAVE_QUEUE_CONTRACT_PASS authored=true joinedSource=true distinctNativeSites=true phase=true knownFiniteAudioOnly=true originalSlot=true once=true independentRefused=true sourceSessionCancel=true history=true writerFailure=true campaignAndParity=unverified");
    }
    private static RuntimeManualSaveRequests Joined(CompiledManualSaveFixture fixture)
    {
        var queue = new RuntimeManualSaveRequests(); fixture.BindManual(queue); return queue;
    }
    private static RuntimeManualSaveReceipt Request(CompiledManualSaveFixture fixture, RuntimeManualSaveRequests queue, ulong generation) =>
        queue.Request(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, site: fixture.Site(generation));
    private static RuntimeSaveSlotMetadata NoWrite(Guid id) => throw new InvalidDataException("Refused state reached its writer.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
