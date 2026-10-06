using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static class ManualSaveSourceOrderingContracts
{
    internal static void Run(string artifactDirectory)
    {
        foreach (var origin in new[] { RuntimeManualSaveOrigin.PlayerInput, RuntimeManualSaveOrigin.SessionMenu })
        {
            PendingThenCompleted(artifactDirectory, origin);
            SuspendedAndFailed(artifactDirectory, origin);
            OwnedPreparationOrdersBothSlots(artifactDirectory, origin);
        }
        Console.WriteLine("OPENNV_MANUAL_SAVE_SOURCE_ORDERING_CONTRACT_PASS pendingSourceRefused=true completedHistoryAdmitted=true sourceWriterSelfCapture=true playerWriterNotInterleaved=true autoSaveRefused=true suspendedCursorRetained=true failureRetained=true originalSlot=true consumedSuffix=true noSourceReplay=true");
    }

    private static void PendingThenCompleted(string directory, RuntimeManualSaveOrigin origin)
    {
        using var fixture = new ScriptSaveFixture("if saved == 0\nset saved to 1\nForceSave\nset suffix to 7\nendif",
            artifactDirectory: directory);
        var sourceWrites = 0; var playerWrites = 0;
        fixture.Owner.Bind(id =>
        {
            sourceWrites++;
            Require(fixture.Owner.WritingRequestedSlot, "Original ForceSave writer lost its own capture phase.");
            fixture.Owner.RequireCapture();
            Require(RuntimeManualSaveSourceBoundary.Observe(fixture.Owner).Blocker == "source-manual-save-writing",
                "Player/menu save interleaved with the original source writer's self-capture exception.");
            return fixture.Write(id);
        }, _ => throw new InvalidDataException("Successful original source writer failed."));
        Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is null &&
            fixture.Value(1) == 1 && fixture.Value(2) == 7 && sourceWrites == 0 &&
            fixture.Owner.Receipt is { Disposition: "pending", Invocations: [{ Ended: true, SourceError: null }] },
            "Original ForceSave invocation or its consumed suffix was changed before player/menu admission.");
        var original = fixture.Owner.Receipt!;
        var pending = JsonSerializer.Serialize(original);
        var manual = new RuntimeManualSaveRequests(); var session = Guid.NewGuid();
        const string source = "synthetic-source-ordering";
        manual.Request(session, source, 10, origin);
        var refused = RuntimeManualSaveSourceBoundary.Observe(fixture.Owner);
        Require(refused.Kind == RuntimeManualSaveAdmissionKind.Refused && refused.Blocker!.StartsWith("source-manual-save:", StringComparison.Ordinal),
            "Pending original ForceSave became an audio-drain or capture exemption.");
        Reject(() => manual.Prepare(10, 1000, refused, []));
        manual.Fail(refused.Blocker!);
        Reject(fixture.Owner.RequireCapture);
        Require(fixture.Owner.Pending && fixture.Owner.Receipt!.Slot == original.Slot &&
            JsonSerializer.Serialize(fixture.Owner.Receipt) == pending && sourceWrites == 0 && playerWrites == 0 &&
            fixture.Value(1) == 1 && fixture.Value(2) == 7,
            "Player/menu refusal cleared, cancelled, replayed or superseded the original source request.");
        fixture.Owner.AdvancePhase();
        foreach (var blocker in new[] { "source-finite-audio", "paused", "native-menu" })
            Require(!fixture.Owner.Drain(() => blocker) && fixture.Owner.Pending && fixture.Owner.DeferredBy == blocker &&
                JsonSerializer.Serialize(fixture.Owner.Receipt) == pending && sourceWrites == 0,
                "Normal source finite-audio/pause/menu ordering discarded its pending slot.");
        Require(fixture.Owner.Drain(() => null) && sourceWrites == 1 && fixture.Owner.Receipt!.Slot == original.Slot &&
            fixture.Owner.Receipt is { Disposition: "completed", Invocations: [{ Ended: true, SourceError: null }] },
            "Original ForceSave did not commit its original slot through its original writer.");
        var completed = fixture.Owner.Receipt!;
        Require(JsonSerializer.Serialize(completed with { Disposition = "pending", SlotPath = null }) == pending &&
            RuntimeManualSaveSourceBoundary.Observe(fixture.Owner).Kind == RuntimeManualSaveAdmissionKind.Ready,
            "Completed original source history was erased or remained an artificial player/menu blocker.");
        Require(RuntimeManualSaveSourceBoundary.Observe(fixture.Owner, autoSaveRequested: true) is
            { Kind: RuntimeManualSaveAdmissionKind.Refused, Blocker: "concurrent-auto-save" } &&
            ReferenceEquals(fixture.Owner.Receipt, completed), "Pending AutoSave was cleared or its ordering was guessed.");
        manual.Request(session, source, 11, origin);
        manual.Prepare(11, 1010, RuntimeManualSaveSourceBoundary.Observe(fixture.Owner), []);
        Require(manual.DrainPrepared(session, source, 12, 1020, () => [],
            () => RuntimeManualSaveSourceBoundary.Observe(fixture.Owner), id =>
            {
                playerWrites++;
                fixture.Owner.RequireCapture();
                return fixture.Write(id);
            }) && playerWrites == 1 && sourceWrites == 1 && manual.Receipt!.Slot != original.Slot &&
            manual.Receipt.Origin == origin && manual.History.First().Disposition == "failed" &&
            ReferenceEquals(fixture.Owner.Receipt, completed) && File.Exists(completed.SlotPath) &&
            fixture.Value(1) == 1 && fixture.Value(2) == 7,
            "A later complete player/menu save replayed source effects/writer, replaced source history or reused its slot.");
        Require(!fixture.Owner.Drain(() => throw new InvalidDataException("Completed source request replayed admission.")) &&
            !manual.DrainPrepared(session, source, 13, 1030, () => throw new InvalidDataException("Completed player lease replayed."),
                () => RuntimeManualSaveSourceBoundary.Observe(fixture.Owner), _ => throw new InvalidDataException("Player writer replayed.")) &&
            sourceWrites == 1 && playerWrites == 1, "Completed original/player writers were retried.");
    }

    private static void SuspendedAndFailed(string directory, RuntimeManualSaveOrigin origin)
    {
        using (var fixture = new ScriptSaveFixture("ForceSave", artifactDirectory: directory))
        {
            fixture.Owner.Bind(fixture.Write, _ => { });
            var program = FalloutGameModeProgram.Read("begin GameMode\nForceSave\nend");
            using var cursor = fixture.Owner.Execute(fixture.Caller, fixture.Script, program,
                program.Steps(_ => 0, (_, _) => { }, (_, _) => fixture.Owner.Request(program.LastStatement))).GetEnumerator();
            Require(cursor.MoveNext() && fixture.Owner.EnteredInvocations == 1 && fixture.Owner.Pending,
                "Source-order fixture never retained its actual entered invocation.");
            var before = JsonSerializer.Serialize(fixture.Owner.Receipt);
            var manual = new RuntimeManualSaveRequests(); manual.Request(Guid.NewGuid(), "source-ordering", 1, origin);
            var refusal = RuntimeManualSaveSourceBoundary.Observe(fixture.Owner);
            Reject(() => manual.Prepare(1, 1, refusal, [])); manual.Fail(refusal.Blocker!);
            Require(fixture.Owner.EnteredInvocations == 1 && JsonSerializer.Serialize(fixture.Owner.Receipt) == before &&
                !cursor.MoveNext() && fixture.Owner.EnteredInvocations == 0,
                "Player/menu refusal closed, discarded or replayed the original suspended source cursor.");
        }
        using (var fixture = new ScriptSaveFixture("set saved to 1\nForceSave\nset suffix to 7", artifactDirectory: directory))
        {
            var writes = 0;
            fixture.Owner.Bind(_ => { writes++; throw new IOException("Original source slot failed."); }, _ => { });
            Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is null, "Source failure fixture did not consume its original suffix.");
            fixture.Owner.AdvancePhase(); Require(!fixture.Owner.Drain(() => null) && writes == 1, "Source failure fixture did not retain its real writer failure.");
            var failed = fixture.Owner.Receipt;
            Require(RuntimeManualSaveSourceBoundary.Observe(fixture.Owner).Kind == RuntimeManualSaveAdmissionKind.Refused &&
                ReferenceEquals(fixture.Owner.Receipt, failed) && fixture.Value(1) == 1 && fixture.Value(2) == 7 &&
                !fixture.Owner.Drain(() => throw new InvalidDataException("Failed source request retried.")) && writes == 1,
                "Player/menu admission recovered, cancelled or replayed historical source writer failure.");
        }
    }

    private static void OwnedPreparationOrdersBothSlots(string directory, RuntimeManualSaveOrigin origin)
    {
        foreach (var failSource in new[] { false, true })
        {
            using var fixture = new ScriptSaveFixture("if saved == 0\nset saved to 1\nForceSave\nset suffix to 7\nendif",
                artifactDirectory: directory);
            ulong phase = 40;
            var sourceWrites = 0; var manualWrites = 0; var order = new List<string>();
            var manual = new RuntimeManualSaveRequests();
            RuntimeManualSaveSourceOrder? sourceOrder = null;
            fixture.Owner.Bind(id =>
            {
                Require(sourceOrder is { Draining: true } &&
                    sourceOrder.PermitsOriginalSourceDrain(fixture.Owner, manual.Receipt!, phase),
                    "Original writer has no matching quiescent phase/generation/preparation lease.");
                sourceWrites++; order.Add("original-source");
                fixture.Owner.RequireCapture();
                if (failSource) throw new IOException("Actual ordered source writer failure.");
                return fixture.Write(id);
            }, _ => { }, () => phase);
            Require(fixture.Scripts.Activate(fixture.Caller, fixture.Player).Error is null &&
                fixture.Owner.Pending && fixture.Value(1) == 1 && fixture.Value(2) == 7,
                "Ordered preparation did not start from an actually retired original ForceSave invocation.");
            var original = fixture.Owner.Receipt!;
            var originalJson = JsonSerializer.Serialize(original);
            var request = manual.Request(Guid.NewGuid(), "owned-source-order", phase, origin);
            sourceOrder = new(fixture.Owner, request, phase);
            using (sourceOrder)
            {
                manual.ObserveOrderedSourceSave(sourceOrder.Receipt);
                manual.Prepare(phase, 2000, sourceOrder.ObserveAdmission(fixture.Owner, manual.Receipt!, phase), []);
                Require(!sourceOrder.PermitsOriginalSourceDrain(fixture.Owner, manual.Receipt!, phase) &&
                    sourceWrites == 0 && manualWrites == 0 && JsonSerializer.Serialize(fixture.Owner.Receipt) == originalJson,
                    "Any pause/menu or input-phase observation became permission to run the source writer.");
                Reject(() => sourceOrder.DrainBeforeManual(manual.Receipt!, phase, () => null));
                phase++;
                var saved = manual.DrainPrepared(request.Session, request.SourceCompatibilityId, phase, 2010, () => [],
                    () => sourceOrder.ObserveAdmission(fixture.Owner, manual.Receipt!, phase), id =>
                    {
                        try
                        {
                            sourceOrder.DrainBeforeManual(manual.Receipt!, phase, () =>
                                sourceOrder.PermitsOriginalSourceDrain(fixture.Owner, manual.Receipt!, phase) ? null : "paused");
                            fixture.Owner.RequireCapture();
                            sourceOrder.Validate(fixture.Owner, manual.Receipt!, phase);
                        }
                        finally { manual.ObserveOrderedSourceSave(sourceOrder.Receipt); }
                        manualWrites++; order.Add("ordinary-manual");
                        return fixture.Write(id);
                    });
                var manualReceipt = manual.Receipt ?? throw new InvalidDataException("Ordered preparation lost its manual receipt.");
                var sourceReceipt = fixture.Owner.Receipt ?? throw new InvalidDataException("Ordered preparation lost its original source receipt.");
                Require(sourceWrites == 1 && sourceReceipt.Slot == original.Slot &&
                    fixture.Value(1) == 1 && fixture.Value(2) == 7 &&
                    manualReceipt.Origin == origin && manualReceipt.OrderedSourceSave?.Slot == original.Slot,
                    "Ordered preparation replaced source GUID/history, consumed effects or manual origin.");
                if (failSource)
                    Require(!saved && manualWrites == 0 && manualReceipt.Disposition == "failed" &&
                        sourceReceipt is { Disposition: "failed", Error: "Actual ordered source writer failure." } &&
                        order.SequenceEqual(["original-source"]), "Source failure was erased/retried or ordinary writer ran after it.");
                else
                    Require(saved && manualWrites == 1 && order.SequenceEqual(["original-source", "ordinary-manual"]) &&
                        manualReceipt.OrderedSourceSave is { Disposition: "completed" } &&
                        File.Exists(sourceReceipt.SlotPath) && File.Exists(manualReceipt.CommittedSlot!.Path) &&
                        manualReceipt.Slot != original.Slot &&
                        JsonSerializer.Serialize(sourceReceipt with { Disposition = "pending", SlotPath = null }) == originalJson,
                        "Original ForceSave and F5/menu did not commit two exact distinct slots once in order.");
                Require(!fixture.Owner.Drain(() => throw new InvalidDataException("Ordered source writer replayed.")) &&
                    !manual.DrainPrepared(request.Session, request.SourceCompatibilityId, ++phase, 2020, () => [],
                        () => throw new InvalidDataException("Completed/failed ordered request replayed."),
                        _ => throw new InvalidDataException("Ordered manual writer replayed.")),
                    "Ordered completion/failure retried a writer.");
            }
        }
    }

    private static void Reject(Action action)
    {
        try { action(); } catch (NotSupportedException) { return; }
        throw new InvalidDataException("Unowned source save ordering was admitted.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
}
