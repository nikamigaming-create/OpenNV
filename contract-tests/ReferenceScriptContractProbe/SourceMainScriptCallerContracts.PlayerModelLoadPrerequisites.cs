using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerModelLoadPrerequisiteContracts()
    {
        using (var fixture = new ModelLoadPrerequisiteFixture())
        {
            fixture.Scripts.Session.SetInCharGen(true);
            fixture.WithLoad(load =>
            {
                var before = fixture.State.ReadEarlyGate(load, fixture.Reference.Changes);
                Require(before.Flags.Flags == FalloutReferencePositionSource.InitialChangeManagerFlags &&
                    before.CharacterGeneration is { Value: 0 } && before.InventoryPreparationRequired,
                    "Loader confused existing manager bit-zero suppression with bit-one or guessed character creation already entered.");
            });
            fixture.ClearStartupManagerFlag();
            var started = fixture.StartByte();
            Require(started is { Returned: true, Stored: not null, Before: 0, After: 1, EnclosingHelperReturned: false } &&
                started.SelectedQuest == fixture.Bootstrap.Quest.FormKey && fixture.State.Process != fixture.Reference.Changes.Process &&
                fixture.State.Process != fixture.Reference.Fields.Process,
                "Character-generation prefix invented a byte from Script state or conflated engine/reference/Common processes.");
            fixture.WithLoad(load =>
            {
                var gate = fixture.State.ReadEarlyGate(load, fixture.Reference.Changes);
                Require(gate.Flags.Flags == 0 && gate.CharacterGeneration is { Value: 1 } && !gate.InventoryPreparationRequired &&
                    gate.Flags.Factory == fixture.Reference.Changes.Factory && gate.CharacterGeneration.Factory == fixture.State.Factory,
                    "Actual entered character-generation writer did not own the general loader gate.");
            });
            var first = fixture.State.Capture();
            Reject(() => fixture.State.StoreEnteredCharacterGeneration(started.Invocation, fixture.Bootstrap));
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                first = JsonSerializer.Deserialize<FalloutPlayerModelStartupSnapshot>(JsonSerializer.Serialize(first))!;
                FalloutPlayerModelLoadStartupState.Validate(first, fixture.Source, fixture.Reference.Stack, fixture.Reference.Records);
            }
            Reject(() => FalloutPlayerModelLoadStartupState.Validate(first with { Value = 0 }, fixture.Source,
                fixture.Reference.Stack, fixture.Reference.Records));
            var forged = first.Calls.ToArray(); forged[0] = forged[0] with { Setting = "00000881" };
            Reject(() => FalloutPlayerModelLoadStartupState.Validate(first with { Calls = forged }, fixture.Source,
                fixture.Reference.Stack, fixture.Reference.Records));
            forged = first.Calls.ToArray(); forged[0] = forged[0] with { EnclosingHelperReturned = true };
            Reject(() => FalloutPlayerModelLoadStartupState.Validate(first with { Calls = forged }, fixture.Source,
                fixture.Reference.Stack, fixture.Reference.Records));
            fixture.InvalidateByte();
            fixture.WithLoad(load => Reject(() => fixture.State.ReadEarlyGate(load, fixture.Reference.Changes)));
            Require(fixture.State.Capture().WriterBoundary == "authored-actual-independent-global-writer",
                "Unknown entered global writer left the earlier constructor/start value current.");
            fixture.TeardownByte();
            fixture.WithLoad(load => Require(fixture.State.ReadEarlyGate(load, fixture.Reference.Changes) is
                { CharacterGeneration.Value: 0, InventoryPreparationRequired: true },
                "Actual teardown zero store did not dominate the old independent global value/boundary."));
        }
        using (var fixture = new ModelLoadPrerequisiteFixture())
        {
            fixture.FailAfterStore = true;
            Reject(() => fixture.StartByte());
            var saved = fixture.State.Capture();
            Require(saved.Value == 1 && saved.Calls.Single() is { Returned: false, Stored: not null, After: 1, Failure: not null },
                "Failed post-store callback erased its independent committed startup byte or returned the helper.");
            fixture.WithLoad(load => Require(fixture.State.ReadEarlyGate(load, fixture.Reference.Changes) is
                { CharacterGeneration.Value: 1, InventoryPreparationRequired: false } && fixture.State.SaveBlocker is not null,
                "A failed caller erased a separately committed byte, or that byte falsely returned the enclosing helper."));
            var forged = saved.Calls.Single() with { Returned = true, Failure = null, Stored = null, Step = FalloutPlayerModelStartupStep.Returned };
            Reject(() => FalloutPlayerModelLoadStartupState.Validate(saved with { Calls = [forged] }, fixture.Source,
                fixture.Reference.Stack, fixture.Reference.Records));
        }
        using (var fixture = new ModelLoadPrerequisiteFixture())
        {
            fixture.CaptureDuringWriter = true;
            Reject(() => fixture.StartByte());
            Require(fixture.State.Capture() is { Value: 0, Calls: [{ Returned: false, Stored: null }] },
                "Swallowed source-byte capture reentry advanced into a store.");
        }
        using (var fixture = new ModelLoadPrerequisiteFixture())
        {
            fixture.WithLoad(load => Reject(() => fixture.Reference.Changes.ReadPlayerModelLoadFlags(load, fixture.Source, actual =>
            {
                fixture.RequireLoad(actual);
                Reject(() => fixture.Reference.Changes.Capture());
            })));
            fixture.Reference.Call(FalloutReferencePositionIngress.CopyOrLoad,
                current => fixture.Reference.Changes.ObserveUnownedWriter(current, "authored-change-manager-current-copy"));
            fixture.WithLoad(load => Reject(() => fixture.State.ReadEarlyGate(load, fixture.Reference.Changes)));
        }
        using (var fixture = new ModelLoadPrerequisiteFixture())
        {
            fixture.StartByte(); var saved = fixture.State.Capture(); var calls = saved.Calls.ToArray();
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                using var records = FalloutPluginStack.Load(fixture.Reference.Records.Plugins.Select(context =>
                    new FalloutPluginSource(context.Plugin.Name, context.Plugin.Path)).ToArray());
                var current = new FalloutPlayerModelLoadStartupState(fixture.Source, records, fixture.Settings, fixture.Reference.Stack,
                    Guid.NewGuid(), (_, _) => throw new InvalidOperationException("Cold field cannot replay startup."),
                    _ => throw new InvalidOperationException("Cold field fixture does not execute a loader."),
                    () => records.RequireLiveLoadReceipt(records.LoadReceipt), saved);
                var captured = current.Capture();
                Require(captured.Value == 0 && captured.Calls.SequenceEqual(calls) && captured.Publications.Count == cycle + 2 &&
                    captured.Publications[^1].RecordsFactory == records.LoadReceipt.Factory,
                    "Fresh process replayed byte-one/startup or reused the old actual reader/PE-loader identity.");
                var publications = captured.Publications.ToArray(); publications[^1] = publications[^1] with { Value = 1 };
                Reject(() => FalloutPlayerModelLoadStartupState.Validate(captured with { Publications = publications }, fixture.Source,
                    fixture.Reference.Stack, records));
                current.Retire(); saved = JsonSerializer.Deserialize<FalloutPlayerModelStartupSnapshot>(JsonSerializer.Serialize(captured))!;
            }
        }
        Console.WriteLine("OPENNV_PLAYER_MODEL_LOAD_PREREQUISITES_PASS sameChangeManager=true enteredStartupByte=true inventoryGateReal=true failedStoreRetained=true freshPEGlobalCold3=true wholeLoader=UNRETURNED");
    }

    internal static void AuditOwnedPlayerModelLoadPrerequisites(string executable, FalloutPlayerModelLoadContinuationSource continuation)
    {
        var source = FalloutExecutableStringTable.ReadPlayerModelLoadPrerequisites(executable, continuation,
            continuation.Animation.Tail.Movement.Cells.Reference);
        Require(source.CharacterGenerationQuest.Declaration.Name == "sCharGenQuest:General" &&
            source.CharacterGenerationQuest.Declaration.Collection == FalloutIniCollection.Main &&
            FalloutPlayerModelLoadPrerequisiteSource.InventoryPreparationSuppressionMask == 2,
            "Owned loader changed its actual current gate or startup setting association.");
        Console.WriteLine($"OPENNV_OWNED_PLAYER_MODEL_LOAD_PREREQUISITES_PASS source={source.Identity} sharedFlags=true PEZeroFilledByte=true deferredStartupStore=true wholeLoader=UNEXECUTED");
    }

    private sealed class ModelLoadPrerequisiteFixture : IDisposable
    {
        internal readonly ReferencePositionFixture Reference;
        internal readonly FalloutPlayerModelLoadPrerequisiteSource Source;
        internal readonly FalloutInstallationSettings Settings;
        internal readonly FalloutNewGameBootstrap Bootstrap;
        internal readonly FalloutQuestScripts Scripts;
        internal readonly FalloutPlayerModelLoadStartupState State;
        private readonly FalloutReferenceWorld _world;
        private FalloutReferenceModelDataInvocation? _load;
        private FalloutPlayerModelStartupInvocation? _writer;
        private int _writerChecks;
        internal bool FailAfterStore, CaptureDuringWriter;
        internal ModelLoadPrerequisiteFixture()
        {
            Reference = ReferencePositionFixture.CreateModelLoadPrerequisiteRecords();
            var fields = Reference.Source.Fields;
            var mover = FalloutPlayerMoverDeltaSource.Reviewed(FalloutEarlyControllerSource.Reviewed(fields, Reference.Thread.Source));
            var tail = FalloutPlayerReturnTailSource.Reviewed(FalloutPlayerActorMovementSource.Reviewed(mover,
                FalloutInitialPlacementCellsSource.Reviewed(Reference.Source), FalloutPlayerWorldOrientationSource.Reviewed(fields.Controls)),
                FalloutStandalonePlayerSceneSource.Read(fields.Controls.Main));
            Source = new(AuthoredModelContinuation(tail), Reference.Source,
                new(new("sCharGenQuest:General", FalloutIniCollection.Main, 0), "00000880"),
                FalloutPlayerModelLoadPrerequisiteSource.ContractDigest);
            Settings = FalloutInstallationSettings.ReadLayers([], [new("General", "SCharGenQuest", "00000880")]);
            _world = new(Reference.Records); var quests = new FalloutQuestState(Reference.Records);
            var scripts = new FalloutQuestScripts(Reference.Records, quests, new HashSet<FalloutFormKey>(), new FalloutPlayerInventory(),
                defaultProcessingDelay: 0, references: _world);
            Scripts = scripts;
            Bootstrap = new(Reference.Records, Settings, quests, scripts, _world,
                (_, _, _, _) => throw new InvalidOperationException("Byte fixture cannot execute compiled commands."),
                _ => throw new InvalidOperationException("Byte fixture cannot execute compiled effects."), () => true);
            State = new(Source, Reference.Records, Settings, Reference.Stack, Guid.NewGuid(), RequireWriter,
                RequireLoad, () => Reference.Records.RequireLiveLoadReceipt(Reference.Records.LoadReceipt));
        }
        internal void ClearStartupManagerFlag() => Reference.Call(FalloutReferencePositionIngress.ConfiguredStartup,
            Reference.Changes.ClearStartupSuppression);
        internal FalloutPlayerModelStartupCall StartByte()
        {
            _writerChecks = 0;
            _writer = new(Guid.NewGuid(), State.Process, Environment.CurrentManagedThreadId,
                FalloutPlayerModelStartupIngress.CharacterGenerationStart, Bootstrap.Quest.FormKey);
            try { return State.StoreEnteredCharacterGeneration(_writer, Bootstrap); } finally { _writer = null; }
        }
        internal void TeardownByte()
        {
            _writer = new(Guid.NewGuid(), State.Process, Environment.CurrentManagedThreadId, FalloutPlayerModelStartupIngress.LoadingMenuTeardown);
            try { State.StoreEnteredLoadingMenuTeardown(_writer); } finally { _writer = null; }
        }
        internal void InvalidateByte()
        {
            _writer = new(Guid.NewGuid(), State.Process, Environment.CurrentManagedThreadId, FalloutPlayerModelStartupIngress.UnownedWriter);
            try { State.ObserveEnteredWriter(_writer, "authored-actual-independent-global-writer"); } finally { _writer = null; }
        }
        private void RequireWriter(FalloutPlayerModelStartupInvocation invocation, FalloutNewGameBootstrap? bootstrap)
        {
            if (_writer != invocation || invocation.Ingress == FalloutPlayerModelStartupIngress.CharacterGenerationStart && !ReferenceEquals(bootstrap, Bootstrap))
                throw new InvalidOperationException("Authored byte writer left the actual selected bootstrap call.");
            if (CaptureDuringWriter) { try { State.Capture(); } catch (InvalidOperationException) { } }
            if (FailAfterStore && ++_writerChecks == 3) throw new InvalidOperationException("authored-following-source-guard-failure");
        }
        internal void WithLoad(Action<FalloutReferenceModelDataInvocation> action)
        {
            _load = new(Guid.NewGuid(), Reference.Actor, Reference.Changes.Process, Reference.Fields.Constructor,
                Reference.Fields.Process, 1, Environment.CurrentManagedThreadId, FalloutReferenceModelDataIngress.ThreeDLoad);
            try { action(_load); } finally { _load = null; }
        }
        internal void RequireLoad(FalloutReferenceModelDataInvocation invocation)
        { if (_load != invocation) throw new InvalidOperationException("Authored loader left the actual current reference/Common scope."); }
        public void Dispose() { State.Retire(); _world.Dispose(); Reference.Dispose(); }
    }
    private sealed partial class ReferencePositionFixture
    {
        internal static ReferencePositionFixture CreateModelLoadPrerequisiteRecords() => new(appendedRows: Join(
            Row("QUST", 0x880, Part("EDID", "LoaderStart\0"u8.ToArray()), Part("DATA", [0, 0])),
            Row("QUST", 0x881, Part("EDID", "OtherStart\0"u8.ToArray()), Part("DATA", [0, 0]))));
    }
}
