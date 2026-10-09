using System.Security.Cryptography;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class CompiledScriptContracts
{
    private sealed record SaveOrderFixtureSnapshot(RuntimeSaveRequestOrderSnapshot Order,
        FalloutQuestScriptsSnapshot Scripts, IReadOnlyList<FalloutQuestSnapshot> Quests,
        IReadOnlyList<FalloutReferenceSnapshot> References, string Schema = "opennv-authored-save-order-fixture/v1");

    internal static void SaveOrder()
    {
        var directory = Directory.CreateTempSubdirectory("opennv-unified-save-order-");
        try
        {
            var input = Path.Combine(directory.FullName, "Bytecode.esm");
            var body = Join(Instruction(0x1d), Block(0, Join(Call(0x115e, U16(0)),
                Call(0x1217, U16(0)), Call(0x1217, U16(0)), RecurrenceAdd(2, 7))));
            File.WriteAllBytes(input, Join(RecurrenceFixture(null, body), Record("CELL", 0x800, Field("DATA", U16(1)))));
            var sourceHash = SHA256.HashData(File.ReadAllBytes(input));
            using var records = FalloutPluginStack.Load(directory.FullName, ["Bytecode.esm"]);
            using var world = new FalloutReferenceWorld(records);
            var quests = new FalloutQuestState(records);
            quests.SetRunning(Key(0x20), true); quests.SetRunning(Key(0x24), true);
            var scripts = RecurrenceScheduler(records, quests, world);
            var executor = RecurrenceExecutor(records, world, quests, _ => throw new InvalidDataException("Unexpected fixture effect."));
            var canonical = Path.Combine(directory.FullName, "continue.json");
            var written = new List<ulong>();
            ulong phase = 30;
            const string identity = "authored-unified-source-save-order";
            Bind(world, scripts, quests, () => phase);
            scripts.AdvanceClaimed(Key(0x20), .25, RecurrenceHost(executor));
            var requested = world.ScriptManualSaves.Order.Requests.ToArray();
            Require(requested.Select(row => row.Order).SequenceEqual(new ulong[] { 1, 2, 3 }) &&
                requested.Select(row => row.Origin).SequenceEqual(new[] { RuntimeSaveRequestOrigin.ScriptAutoSave,
                    RuntimeSaveRequestOrigin.ScriptForceSave, RuntimeSaveRequestOrigin.ScriptForceSave }) &&
                requested.Select(row => row.Request).Distinct().Count() == 3 && requested.All(row => row.Invocation?.Disposition == RuntimeSaveInvocationDisposition.Completed) &&
                quests.Variable(Key(0x20), 2) == 7 && written.Count == 0,
                "Actual SCDA calls coalesced, wrote inline or lost the consumed suffix/retirement.");
            Require(!world.ScriptManualSaves.Drain(() => throw new InvalidDataException("Same-phase admission ran.")),
                "Source queue wrote within the requesting engine phase.");

            // A different real scheduler invocation stops after its ForceSave.
            // The earlier head must retain this exact live suffix in its bytes.
            scripts.AdvanceClaimed(Key(0x24), .25, RecurrenceHost(executor,
                _ => world.ScriptManualSaves.Order.Requests.Count < 4));
            Require(world.ScriptManualSaves.Order.Requests.Count == 4 &&
                world.ScriptManualSaves.Order.Find(4).Invocation is { Disposition: RuntimeSaveInvocationDisposition.Suspended, SuspendedSlice: not null } &&
                quests.Variable(Key(0x24), 1) == 1 && quests.Variable(Key(0x24), 2) == 0,
                "Pending source tail has no actual stopped-after-request SCDA cursor.");
            phase++;
            Require(world.ScriptManualSaves.Drain(() => null) && written.SequenceEqual(new ulong[] { 1 }),
                "Earlier AutoSave did not own the first ordinary writer.");
            var saved = JsonSerializer.Deserialize<SaveOrderFixtureSnapshot>(File.ReadAllText(canonical))!;
            Require(saved.Order.CapturedOrder == 1 && saved.Order.Requests.Count == 4 &&
                JsonSerializer.Serialize(saved.Order.Requests[3].Invocation!.SuspendedSlice) ==
                    JsonSerializer.Serialize(saved.Scripts.Instances.Single(row => row.Quest == Key(0x24)).Compiled!.Pending!.LastSlice),
                "Earlier checkpoint omitted/reconstructed its later actual pending queue.");
            var suspensions = RuntimeSaveRequestColdLoad.SuspendedSlices(saved.Scripts);
            RuntimeSaveRequestOrder.ValidateSnapshot(saved.Order, identity, records, suspensions);
            var wrongOffset = saved.Order.Requests[1] with { Script = saved.Order.Requests[1].Script! with { Statement = 0 } };
            Reject(() => RuntimeSaveRequestOrder.ValidateSnapshot(saved.Order with
                { Requests = saved.Order.Requests.Select(row => row.Order == 2 ? wrongOffset : row).ToArray() }, identity, records, suspensions));
            Reject(() => RuntimeSaveRequestOrder.ValidateSnapshot(saved.Order, identity, records, []));
            var overtaken = saved.Order.Requests[2] with { Disposition = RuntimeSaveRequestDisposition.Completed,
                Committed = new(saved.Order.Requests[2].Request.ToString("N"), saved.Order.Requests[2].DestinationPath,
                    "authored-contract", null, null, null, DateTime.UtcNow), CommittedSha256 = new string('a', 64) };
            Reject(() => RuntimeSaveRequestOrder.ValidateSnapshot(saved.Order with
                { Requests = saved.Order.Requests.Select(row => row.Order == 3 ? overtaken : row).ToArray() }, identity, records, suspensions));

            using var cold = new FalloutReferenceWorld(records);
            var coldQuests = new FalloutQuestState(records); coldQuests.Restore(saved.Quests);
            cold.Restore(saved.References);
            var coldScripts = RecurrenceScheduler(records, coldQuests, cold);
            coldScripts.Restore(saved.Scripts);
            ulong coldPhase = 0;
            Bind(cold, coldScripts, coldQuests, () => coldPhase);
            cold.ScriptManualSaves.RestoreOrder(RuntimeSaveRequestColdLoad.Read(canonical, identity, records, saved.Order, saved.Scripts));
            Require(cold.ScriptManualSaves.Order.Epoch != saved.Order.Epoch &&
                cold.ScriptManualSaves.Order.Capture().Handoffs.Single().Kind == RuntimeSaveRequestHandoffKind.SameProcessSession &&
                cold.ScriptManualSaves.Order.Find(2).RequestedPhase == 30 &&
                !cold.ScriptManualSaves.Drain(() => throw new InvalidDataException("Old process phase authorized a writer.")),
                "Cold queue lost origin phases or reused an old phase as new execution authority.");
            var coldExecutor = RecurrenceExecutor(records, cold, coldQuests, _ => throw new InvalidDataException("Unexpected cold effect."));
            coldScripts.AdvanceClaimed(Key(0x24), 0, RecurrenceHost(coldExecutor));
            Require(cold.ScriptManualSaves.Order.Requests.Count == 4 && coldQuests.Variable(Key(0x24), 1) == 1 &&
                coldQuests.Variable(Key(0x24), 2) == 1 && cold.ScriptManualSaves.Order.Find(4).Invocation is
                    { Disposition: RuntimeSaveInvocationDisposition.Completed } ended && ended.RetiredSession == cold.ScriptManualSaves.ExecutionSession,
                "Cold actual suffix replayed ForceSave/prefix or guessed its requesting invocation retirement.");
            coldPhase++;
            foreach (var expected in new ulong[] { 2, 3, 4 })
                Require(cold.ScriptManualSaves.Drain(() => null) && written[^1] == expected,
                    "Cold pending queue lost original monotonic destination order.");
            Require(written.SequenceEqual(new ulong[] { 1, 2, 3, 4 }) &&
                sourceHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(input))),
                "Save ordering replayed a committed writer or mutated its source fixture.");
            SaveOrderManualInputs(directory.FullName, records);

            void Bind(FalloutReferenceWorld selectedWorld, FalloutQuestScripts selectedScripts, FalloutQuestState selectedQuests, Func<ulong> clock)
            {
                var owner = selectedWorld.ScriptManualSaves;
                string Slot(Guid id) => Path.Combine(canonical + RuntimeSaveSlotCatalog.SlotDirectorySuffix, id.ToString("N") + ".json");
                void Capture()
                {
                    var order = owner.CaptureOrder(); var schedule = selectedScripts.Capture();
                    RuntimeSaveRequestOrder.ValidateSnapshot(order, identity, records, RuntimeSaveRequestColdLoad.SuspendedSlices(schedule));
                    RuntimeSaveRequestOrder.RequirePublishedCapture(order);
                    File.WriteAllText(canonical, JsonSerializer.Serialize(new SaveOrderFixtureSnapshot(order, schedule,
                        selectedQuests.Capture(), selectedWorld.Capture())));
                }
                var catalog = new RuntimeSaveSlotCatalog(canonical, root =>
                {
                    var captured = root.Deserialize<SaveOrderFixtureSnapshot>()!;
                    RuntimeSaveRequestOrder.ValidateSnapshot(captured.Order, identity, records, RuntimeSaveRequestColdLoad.SuspendedSlices(captured.Scripts));
                });
                owner.Bind(id =>
                {
                    var request = owner.Order.Writing!; written.Add(request.Order);
                    return catalog.Create(id, Capture);
                }, _ => throw new InvalidDataException("Unexpected source writer failure."), clock,
                    new(identity, canonical, Slot), request =>
                    {
                        written.Add(request.Order); Capture();
                        return new("current", canonical, "authored-contract", null, null, null, File.GetLastWriteTimeUtc(canonical));
                    });
            }
        }
        finally { directory.Delete(true); }
        Console.WriteLine("OPENNV_UNIFIED_SAVE_ORDER_CONTRACT_PASS actualScda=true eachCallUnique=true autoForceOrder=true " +
            "retiredLease=true suspendedTail=true sourceMutationRefused=true noOvertaking=true freshSessionPhase=true prefixReplay=false " +
            "genuineNewProcessAndNativePause=unexecuted");
    }

    private static void SaveOrderManualInputs(string directory, FalloutPluginStack records)
    {
        using var world = new FalloutReferenceWorld(records);
        var owner = world.ScriptManualSaves;
        const string identity = "authored-input-save-order";
        var canonical = Path.Combine(directory, "manual-continue.json");
        ulong phase = 100;
        var failures = 0;
        var catalog = new RuntimeSaveSlotCatalog(canonical, root =>
        {
            var snapshot = root.Deserialize<RuntimeSaveRequestOrderSnapshot>()!;
            RuntimeSaveRequestOrder.ValidateSnapshot(snapshot, identity, records, []);
            RuntimeSaveRequestOrder.RequirePublishedCapture(snapshot);
        });
        void Capture() => File.WriteAllText(canonical, JsonSerializer.Serialize(owner.CaptureOrder()));
        owner.Bind(id => catalog.Create(id, Capture), _ => ++failures, () => phase,
            new(identity, canonical, id => Path.Combine(canonical + RuntimeSaveSlotCatalog.SlotDirectorySuffix, id.ToString("N") + ".json")),
            _ => throw new IOException("authored-continue-write-failure"));
        var session = Guid.NewGuid();
        var manual = new RuntimeManualSaveRequests(); manual.Bind(owner, session, identity);
        RuntimeSaveNativeSite Site(ulong generation) => new(session, generation, records.RuntimeFormKey(0x14), Key(0x800));
        var first = manual.Request(session, identity, phase, site: Site(1));
        var second = manual.Request(session, identity, phase, site: Site(2));
        var third = owner.RequestNative(RuntimeSaveRequestOrigin.NativePipBoyClose, Site(3));
        Require(first.Order == 1 && second.Order == 2 && third.Order == 3 && first.Slot != second.Slot &&
            manual.History.Count == 2 && manual.History.All(row => row.RequestCount == 1),
            "Actual player requests coalesced or lost their distinct shared order/input sites.");
        manual.Cancel("actual first input cancellation"); manual.RetirePreparation();
        Require(owner.Order.Find(1).Disposition == RuntimeSaveRequestDisposition.Cancelled && manual.Receipt!.Order == 2 &&
            owner.Order.Find(3).Disposition == RuntimeSaveRequestDisposition.Pending,
            "Player cancellation changed an unrelated source/native request or lost the next input.");
        using (var lease = new RuntimeManualSaveSourceOrder(owner, manual.Receipt!, phase))
        {
            manual.Prepare(phase, 1000, new(RuntimeManualSaveAdmissionKind.Ready), []);
            phase++;
            Reject(() => owner.Order.Write(third.Order, _ => throw new InvalidDataException("Later native writer ran.")));
            Require(manual.DrainPrepared(session, identity, phase, 1001, () => [],
                () => lease.ObserveAdmission(owner, manual.Receipt!, phase), id =>
                {
                    lease.DrainBeforeManual(manual.Receipt!, phase, () => null);
                    return lease.WriteManual(manual.Receipt!, phase, selected => catalog.Create(selected, Capture));
                }) && manual.Receipt is { Disposition: "completed", CommittedSlot: not null } &&
                owner.Order.Find(2).Disposition == RuntimeSaveRequestDisposition.Completed,
                "Prepared player writer bypassed its actual queue head/destination commitment.");
        }
        manual.RetirePreparation();
        Require(!owner.Drain(() => "animation-sound-continuation: unowned-loop") && failures == 0 &&
            owner.Order.Head?.Order == 3 && owner.DeferredBy == "animation-sound-continuation: unowned-loop",
            "Source audio blocker was discarded or an unsupported loop was declared finished.");
        Require(!owner.Drain(() => null) && failures == 1 && owner.Order.Head is
            { Order: 3, Disposition: RuntimeSaveRequestDisposition.Failed, Error: "authored-continue-write-failure" },
            "Actual writer failure lost its own order/cause.");
        var fourth = manual.Request(session, identity, phase, site: Site(4)); phase++;
        Require(owner.PreparationBlocker(fourth.Order)?.StartsWith("earlier-save-failed:", StringComparison.Ordinal) == true,
            "A later input writer overtook an earlier failed native request.");
        Reject(() => owner.Order.Write(fourth.Order, _ => throw new InvalidDataException("Failed-head writer was bypassed.")));
        manual.CancelQueued("actual input scope retired");
        Require(owner.Order.Find(4).Disposition == RuntimeSaveRequestDisposition.Cancelled &&
            owner.Order.Find(3).Disposition == RuntimeSaveRequestDisposition.Failed,
            "Input scope retirement cleared an unrelated committed/native failure.");
        Console.WriteLine("OPENNV_UNIFIED_SAVE_INPUT_ORDER_CONTRACT_PASS distinctInputRequests=true exactCancellation=true " +
            "actualSlotCommit=true noNativeOvertaking=true loopRefusalRetained=true writerFailureRetained=true");
    }
}
