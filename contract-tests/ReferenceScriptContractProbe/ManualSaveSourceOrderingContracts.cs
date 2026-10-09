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
        Console.WriteLine("OPENNV_MANUAL_SAVE_SOURCE_ORDERING_CONTRACT_PASS actualScda=true sharedQueue=true " +
            "exactNativeSites=true pendingSourceNotOvertaken=true completedHistoryRetained=true sourceWriterSelfCapture=true " +
            "playerWriterNotInterleaved=true booleanAutoSaveRefused=true suspendedCursorRetained=true failureRetained=true " +
            "originalDestinations=true consumedSuffix=true noSourceReplay=true distinctInputRequests=true");
    }

    private static void PendingThenCompleted(string directory, RuntimeManualSaveOrigin origin)
    {
        using var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Once(), directory, phase: 10);
        var sourceWrites = 0; var playerWrites = 0;
        fixture.Bind(id =>
        {
            ++sourceWrites;
            Require(fixture.Owner.WritingRequestedSlot && fixture.Owner.Order.Writing!.Origin == RuntimeSaveRequestOrigin.ScriptForceSave,
                "Original source writer lost its actual queue capture phase.");
            fixture.Owner.RequireCapture();
            Require(RuntimeManualSaveSourceBoundary.Observe(fixture.Owner) is
                { Kind: RuntimeManualSaveAdmissionKind.Refused, Blocker: "ordered-save-writing" },
                "Player/menu observation interleaved with a genuine source writer.");
            return fixture.Write(id);
        });
        Require(fixture.Activate().Error is null && fixture.Value(1) == 1 && fixture.Value(2) == 7 && sourceWrites == 0 &&
            fixture.Owner.Receipt is { Disposition: "pending", Invocations: [{ Ended: true, SourceError: null }] },
            "Original compiled source invocation changed before player/menu admission.");
        var original = fixture.Owner.Order.Requests.Single();
        var originalReceipt = JsonSerializer.Serialize(fixture.Owner.Receipt);
        var manual = new RuntimeManualSaveRequests(); fixture.BindManual(manual);
        var cancelled = manual.Request(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, origin, fixture.Site(1));
        Require(cancelled.Order == original.Order + 1 && cancelled.Slot != original.Request &&
            manual.Source == fixture.Owner && RuntimeManualSaveSourceBoundary.Observe(fixture.Owner).Kind == RuntimeManualSaveAdmissionKind.Ready,
            "Retired pending requests lost their complete queue owner or separate native input identity.");
        // The complete pending denominator is capturable by its actual head;
        // it is not permission for a later native request to bypass that head.
        fixture.AdvancePhase();
        Reject(() => fixture.Owner.Order.Write(cancelled.Order, _ => throw new InvalidDataException("Later player writer ran.")));
        manual.Cancel("Authored player input cancelled before acquiring preparation.");
        manual.RetirePreparation();
        Require(fixture.Owner.Order.Find(cancelled.Order).Disposition == RuntimeSaveRequestDisposition.Cancelled &&
            JsonSerializer.Serialize(fixture.Owner.Receipt) == originalReceipt && sourceWrites == 0,
            "Player cancellation altered the independent original source request.");
        foreach (var blocker in new[] { "source-finite-audio", "paused", "native-menu" })
            Require(!fixture.Owner.Drain(() => blocker) && fixture.Owner.Pending && fixture.Owner.DeferredBy == blocker &&
                JsonSerializer.Serialize(fixture.Owner.Receipt) == originalReceipt && sourceWrites == 0,
                "Normal source save admission discarded an original pending destination.");
        Require(fixture.Owner.Drain(() => null) && sourceWrites == 1 &&
            fixture.Owner.Order.Find(original.Order) is { Disposition: RuntimeSaveRequestDisposition.Completed, Committed: not null } &&
            RuntimeManualSaveSourceBoundary.Observe(fixture.Owner).Kind == RuntimeManualSaveAdmissionKind.Ready,
            "Original source writer failed to commit its distinct queue head.");
        var completed = JsonSerializer.Serialize(fixture.Owner.Receipt);
        Reject(() => RuntimeManualSaveSourceBoundary.Observe(fixture.Owner, autoSaveRequested: true));
        Require(JsonSerializer.Serialize(fixture.Owner.Receipt) == completed,
            "A Boolean AutoSave substituted a source instruction or changed its history.");
        var request = manual.Request(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, origin, fixture.Site(2));
        using var sourceOrder = new RuntimeManualSaveSourceOrder(fixture.Owner, request, fixture.Phase);
        manual.Prepare(fixture.Phase, 1010, sourceOrder.ObserveAdmission(fixture.Owner, manual.Receipt!, fixture.Phase), []);
        fixture.AdvancePhase();
        Require(manual.DrainPrepared(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, 1020, () => [],
            () => sourceOrder.ObserveAdmission(fixture.Owner, manual.Receipt!, fixture.Phase), _ =>
            {
                sourceOrder.DrainBeforeManual(manual.Receipt!, fixture.Phase, () => null);
                return sourceOrder.WriteManual(manual.Receipt!, fixture.Phase, id =>
                {
                    ++playerWrites;
                    Require(fixture.Owner.Order.Writing!.Order == request.Order,
                        "Player writer captured without owning the actual common queue head.");
                    return fixture.Write(id);
                });
            }) && playerWrites == 1 && sourceWrites == 1 && manual.Receipt is { Disposition: "completed", CommittedSlot: not null } &&
            manual.Receipt.Origin == origin && manual.Receipt.Slot != original.Request &&
            manual.History.First().Disposition == "cancelled" && JsonSerializer.Serialize(fixture.Owner.Receipt) == completed &&
            fixture.Owner.Order.Requests.Select(row => row.Order).SequenceEqual(new ulong[] { 1, 2, 3 }) &&
            fixture.Owner.Order.Requests[1].Native!.Generation == 1 && fixture.Owner.Order.Requests[2].Native!.Generation == 2 &&
            fixture.Value(1) == 1 && fixture.Value(2) == 7,
            "Later player/menu commit replayed source effects, lost native input generations or bypassed queue ordering.");
        Require(!fixture.Owner.Drain(() => throw new InvalidDataException("Source writer replayed.")) &&
            !manual.DrainPrepared(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, 1030,
                () => throw new InvalidDataException("Completed native preparation replayed."),
                () => throw new InvalidDataException("Completed native admission replayed."),
                _ => throw new InvalidDataException("Completed player writer replayed.")),
            "Completed source/player writers were retried.");
        fixture.RequireSourceUnchanged();
    }

    private static void SuspendedAndFailed(string directory, RuntimeManualSaveOrigin origin)
    {
        using (var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Force(), directory))
        {
            fixture.Bind(fixture.Write);
            using var cursor = fixture.Steps().GetEnumerator();
            Require(cursor.MoveNext() && fixture.Owner.EnteredInvocations == 1 && fixture.Owner.Pending,
                "Source-order fixture did not retain its actual entered compiled invocation.");
            var before = JsonSerializer.Serialize(fixture.Owner.Receipt);
            var manual = new RuntimeManualSaveRequests(); fixture.BindManual(manual);
            var request = manual.Request(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, origin, fixture.Site(1));
            var refusal = RuntimeManualSaveSourceBoundary.Observe(fixture.Owner);
            Require(refusal.Kind == RuntimeManualSaveAdmissionKind.Refused &&
                fixture.Owner.PreparationBlocker(request.Order) == "entered-source-invocation",
                "An entered compiled cursor became an implicit completed source-save lease.");
            Reject(() => manual.Prepare(fixture.Phase, 1, refusal, []));
            Reject(() => new RuntimeManualSaveSourceOrder(fixture.Owner, request, fixture.Phase));
            manual.Cancel("Authored player input retired while original source cursor remained entered.");
            Require(fixture.Owner.EnteredInvocations == 1 && JsonSerializer.Serialize(fixture.Owner.Receipt) == before &&
                !cursor.MoveNext() && fixture.Owner.EnteredInvocations == 0,
                "Player cancellation or refusal retired, discarded or replayed the original compiled source cursor.");
        }
        using (var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Once(), directory))
        {
            var writes = 0;
            fixture.Bind(_ => { ++writes; throw new IOException("Original source slot failed."); });
            Require(fixture.Activate().Error is null, "Source failure fixture lost its actual consumed suffix.");
            fixture.AdvancePhase();
            Require(!fixture.Owner.Drain(() => null) && writes == 1, "Source failure fixture failed to retain its writer cause.");
            var failed = JsonSerializer.Serialize(fixture.Owner.Receipt);
            var manual = new RuntimeManualSaveRequests(); fixture.BindManual(manual);
            var later = manual.Request(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, origin, fixture.Site(1));
            Require(fixture.Owner.PreparationBlocker(later.Order)!.StartsWith("earlier-save-failed:", StringComparison.Ordinal) &&
                RuntimeManualSaveSourceBoundary.Observe(fixture.Owner).Kind == RuntimeManualSaveAdmissionKind.Refused,
                "A later real input overwrote or bypassed the earlier failed source queue head.");
            Reject(() => new RuntimeManualSaveSourceOrder(fixture.Owner, later, fixture.Phase));
            manual.Cancel("Later input cancelled after the independent original source writer failure.");
            Require(JsonSerializer.Serialize(fixture.Owner.Receipt) == failed && fixture.Value(1) == 1 && fixture.Value(2) == 7 &&
                !fixture.Owner.Drain(() => throw new InvalidDataException("Failed source writer retried.")) && writes == 1,
                "Player/menu cancellation recovered, replaced or replayed the failed original source writer.");
        }
    }

    private static void OwnedPreparationOrdersBothSlots(string directory, RuntimeManualSaveOrigin origin)
    {
        foreach (var failSource in new[] { false, true })
        {
            using var fixture = new CompiledManualSaveFixture(CompiledManualSaveFixture.Once(), directory, phase: 40);
            var sourceWrites = 0; var manualWrites = 0; var order = new List<ulong>();
            var manual = new RuntimeManualSaveRequests(); fixture.BindManual(manual);
            RuntimeManualSaveSourceOrder? sourceOrder = null;
            fixture.Bind(id =>
            {
                Require(sourceOrder is { Draining: true } && sourceOrder.PermitsOriginalSourceDrain(fixture.Owner, manual.Receipt!, fixture.Phase),
                    "Original writer lacks its real quiescent phase/preparation lease.");
                ++sourceWrites; order.Add(fixture.Owner.Order.Writing!.Order);
                fixture.Owner.RequireCapture();
                if (failSource) throw new IOException("Actual ordered source writer failure.");
                return fixture.Write(id);
            });
            Require(fixture.Activate().Error is null && fixture.Owner.Pending && fixture.Value(1) == 1 && fixture.Value(2) == 7,
                "Ordered preparation did not start from the actual retired ForceSave source invocation.");
            var original = fixture.Owner.Order.Requests.Single();
            var originalJson = JsonSerializer.Serialize(fixture.Owner.Receipt);
            var request = manual.Request(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, origin, fixture.Site(1));
            sourceOrder = new(fixture.Owner, request, fixture.Phase);
            using (sourceOrder)
            {
                manual.ObserveOrderedSourceSave(sourceOrder.Receipt);
                manual.Prepare(fixture.Phase, 2000, sourceOrder.ObserveAdmission(fixture.Owner, manual.Receipt!, fixture.Phase), []);
                Require(!sourceOrder.PermitsOriginalSourceDrain(fixture.Owner, manual.Receipt!, fixture.Phase) &&
                    sourceWrites == 0 && manualWrites == 0 && JsonSerializer.Serialize(fixture.Owner.Receipt) == originalJson,
                    "Paused preparation substituted a genuine later engine writer phase.");
                Reject(() => sourceOrder.DrainBeforeManual(manual.Receipt!, fixture.Phase, () => null));
                fixture.AdvancePhase();
                var saved = manual.DrainPrepared(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, 2010, () => [],
                    () => sourceOrder.ObserveAdmission(fixture.Owner, manual.Receipt!, fixture.Phase), _ =>
                    {
                        try
                        {
                            sourceOrder.DrainBeforeManual(manual.Receipt!, fixture.Phase, () =>
                                sourceOrder.PermitsOriginalSourceDrain(fixture.Owner, manual.Receipt!, fixture.Phase) ? null : "paused");
                            sourceOrder.Validate(fixture.Owner, manual.Receipt!, fixture.Phase);
                        }
                        finally { manual.ObserveOrderedSourceSave(sourceOrder.Receipt); }
                        return sourceOrder.WriteManual(manual.Receipt!, fixture.Phase, id =>
                        {
                            ++manualWrites; order.Add(fixture.Owner.Order.Writing!.Order); return fixture.Write(id);
                        });
                    });
                var manualReceipt = manual.Receipt!; var sourceReceipt = fixture.Owner.Receipt!;
                Require(sourceWrites == 1 && sourceReceipt.Slot == original.Request && fixture.Value(1) == 1 && fixture.Value(2) == 7 &&
                    manualReceipt.Origin == origin && manualReceipt.OrderedSourceSave!.Slot == original.Request,
                    "Preparation replaced original source identity, consumed effects or native origin.");
                if (failSource)
                    Require(!saved && manualWrites == 0 && manualReceipt.Disposition == "failed" &&
                        sourceReceipt is { Disposition: "failed", Error: "Actual ordered source writer failure." } &&
                        order.SequenceEqual(new ulong[] { original.Order }),
                        "Actual source writer failure was erased/retried or a later player writer overtook it.");
                else
                    Require(saved && manualWrites == 1 && order.SequenceEqual(new[] { original.Order, request.Order }) &&
                        manualReceipt.OrderedSourceSave is { Disposition: "completed" } &&
                        File.Exists(sourceReceipt.SlotPath) && File.Exists(manualReceipt.CommittedSlot!.Path) &&
                        sourceReceipt.SlotPath == original.DestinationPath && manualReceipt.CommittedSlot!.Path == fixture.Binding.SlotPath(request.Slot) &&
                        manualReceipt.Slot != original.Request && fixture.Owner.Order.Requests.All(row => row.Disposition == RuntimeSaveRequestDisposition.Completed),
                        "Original source and player/menu writers did not commit two original destinations once in order.");
                Require(!fixture.Owner.Drain(() => throw new InvalidDataException("Original source writer replayed.")) &&
                    !manual.DrainPrepared(fixture.Session, fixture.Binding.SourceCompatibilityId, fixture.Phase, 2020,
                        () => throw new InvalidDataException("Retired preparation replayed."),
                        () => throw new InvalidDataException("Retired admission replayed."),
                        _ => throw new InvalidDataException("Retired player writer replayed.")),
                    "Ordered completion/failure retried a writer.");
            }
            fixture.RequireSourceUnchanged();
        }
    }

    private static void Reject(Action action) => ScriptManualSaveContracts.Reject(action);
    private static void Require(bool value, string message) => ScriptManualSaveContracts.Require(value, message);
}
