using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunReferencePackageOwnerJoinContracts()
    {
        using var fixture = new ReferencePositionFixture();
        fixture.PrepareReferenceAggregate();
        fixture.ExecuteReferenceAggregateCommand();
        var saved = fixture.CaptureReferenceAggregate();
        ValidateReferenceAggregate(fixture, saved);
        Require(saved.Common.PackageCommands!.Single() is { FailedAt: FalloutSourcePackageAssignmentStep.PackageFlag } &&
            saved.Common.CommandFields!.Single().Operations.Last().SavedPositionCall is { Returned: true },
            "Reference join test borrowed an authored whole-command return or lost the real Saved18 child.");
        RequireRefusal(() => ValidateReferenceAggregate(fixture, saved with { Parent = null }),
            FalloutReferencePackageOwnerValidation.ParentHistoryBoundary);
        RequireRefusal(() => new FalloutReferencePackageOwnerValidation(fixture.Records, saved.Common, saved.Owners.Position,
            saved.Owners.Changes, saved.Owners.Thread, saved.Owners.Files, saved.Parent),
            "source-cold-reference-World-process-history-unbound");
        Reject(() => ValidateReferenceAggregate(fixture, saved with { Runtime = saved.Runtime with { CapturedProcess = Guid.NewGuid() } }));

        var fields = saved.Common.CommandFields!.Single();
        var index = fields.Operations.ToList().FindLastIndex(value => value.SavedPositionCall is not null);
        var original = fields.Operations[index].SavedPositionCall!;
        var foreign = Guid.NewGuid();
        foreach (var changed in new[]
        {
            original with { PositionRead = original.PositionRead! with { Factory = foreign }, WorldspaceRead = original.WorldspaceRead! with { Factory = foreign } },
            original with { PositionRead = original.PositionRead! with { Sequence = original.PositionRead!.Sequence + 1 },
                WorldspaceRead = original.WorldspaceRead! with { Sequence = original.WorldspaceRead!.Sequence + 1 } },
            original with { WorldspaceRead = original.WorldspaceRead! with { Flags = original.WorldspaceRead!.Flags! with { Sequence = original.WorldspaceRead!.Flags!.Sequence + 1 } } },
            original with { WorldspaceRead = original.WorldspaceRead! with { Flags = original.WorldspaceRead!.Flags! with { Factory = foreign } } },
            original with { ParentRead = original.ParentRead! with { Factory = foreign },
                WorldspaceRead = original.WorldspaceRead! with { Parent = original.ParentRead! with { Factory = foreign } } },
            original with { ParentRead = original.ParentRead! with { Sequence = original.ParentRead!.Sequence + 1 },
                WorldspaceRead = original.WorldspaceRead! with { Parent = original.ParentRead! with { Sequence = original.ParentRead!.Sequence + 1 } } }
        })
        {
            var operations = fields.Operations.ToArray(); operations[index] = operations[index] with { SavedPositionCall = changed };
            var tampered = fields with { Operations = operations };
            // Each coherent pair passes the existing local receipt validator.
            FalloutActorPackageCommandFields.Validate(tampered, fields.Source, fixture.Stack, fixture.Actor);
            Reject(() => ValidateReferenceAggregate(fixture, saved with { Common = saved.Common with { CommandFields = [tampered] } }));
        }
        var initialization = fields.Operations.ToList().FindIndex(value => value.SavedPositionInitialization is not null);
        var badPrefix = fields.Operations.ToArray();
        badPrefix[initialization] = badPrefix[initialization] with
        { SavedPositionInitialization = badPrefix[initialization].SavedPositionInitialization! with
            { Prefix = badPrefix[initialization].SavedPositionInitialization!.Prefix with { Factory = foreign } } };
        FalloutActorPackageCommandFields.Validate(fields with { Operations = badPrefix }, fields.Source, fixture.Stack, fixture.Actor);
        Reject(() => ValidateReferenceAggregate(fixture, saved with { Common = saved.Common with { CommandFields = [fields with { Operations = badPrefix }] } }));

        var changes = saved.Owners.Changes.Calls.ToArray();
        var changeIndex = Array.FindLastIndex(changes, value => value.Mask == 0x800);
        changes[changeIndex] = changes[changeIndex] with { Invocation = changes[changeIndex].Invocation with { PackageCommand = Guid.NewGuid() } };
        FalloutSourceReferenceChangeManager.Validate(saved.Owners.Changes with { Calls = changes }, fixture.Source, fixture.Stack);
        Reject(() => ValidateReferenceAggregate(fixture, saved with { Owners = saved.Owners with { Changes = saved.Owners.Changes with { Calls = changes } } }));
        changes = saved.Owners.Changes.Calls.ToArray();
        changes[changeIndex] = changes[changeIndex] with { ThreadFlags = changes[changeIndex].ThreadFlags! with { Sequence = changes[changeIndex].ThreadFlags!.Sequence + 2 } };
        FalloutSourceReferenceChangeManager.Validate(saved.Owners.Changes with { Calls = changes }, fixture.Source, fixture.Stack);
        Reject(() => ValidateReferenceAggregate(fixture, saved with { Owners = saved.Owners with { Changes = saved.Owners.Changes with { Calls = changes } } }));

        var fileCalls = saved.Owners.Files.Calls.ToArray();
        var fileIndex = Array.FindLastIndex(fileCalls, value => value.Invocation.PackageCommand is not null);
        fileCalls[fileIndex] = fileCalls[fileIndex] with { Invocation = fileCalls[fileIndex].Invocation with { PackageCommand = Guid.NewGuid() } };
        FalloutReferenceFormFileAssociationState.Validate(saved.Owners.Files with { Calls = fileCalls }, fixture.Files.Source, fixture.Stack, fixture.Actor);
        Reject(() => ValidateReferenceAggregate(fixture, saved with { Owners = saved.Owners with { Files = saved.Owners.Files with { Calls = fileCalls } } }));

        var publications = saved.Owners.Position.Publications.ToArray();
        publications[0] = publications[0] with { PublishedSequence = 1 };
        Reject(() => ValidateReferenceAggregate(fixture, saved with { Owners = saved.Owners with { Position = saved.Owners.Position with { Publications = publications } } }));
        publications[0] = publications[0] with { PublishedSequence = null };
        RequireRefusal(() => ValidateReferenceAggregate(fixture, saved with { Owners = saved.Owners with { Position = saved.Owners.Position with { Publications = publications } } }),
            "source-cold-reference-publication-writer-boundary-unbound");

        var coldSaved = saved.Owners;
        var originalOperations = JsonSerializer.Serialize(fields.Operations);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            using var cold = new ReferencePositionFixture(coldSaved);
            var rebound = cold.Capture();
            var actualFlags = cold.Call(FalloutReferencePositionIngress.ActorPosition, inv => cold.Initializer.ReadReferenceFlags(inv, cold.RequireCurrent));
            Require(cold.Events.Count == 0 && cold.SavedBorrowEvents.Count == 0 &&
                JsonSerializer.Serialize(rebound.Fields.Operations) == originalOperations &&
                rebound.Position.Publications.Last().PublishedSequence == coldSaved.Position.Sequence &&
                rebound.Fields.ScriptInitialization!.Publications.Last().PublishedSequence == actualFlags.Sequence,
                "Cold retained owner histories replayed children or omitted exact publication boundaries.");
            // Parent's full old/fresh field publication chain is still unowned.
            var common = rebound.Common with { CommandFields = [rebound.Fields], PackageCommands = saved.Common.PackageCommands };
            RequireRefusal(() => new FalloutReferencePackageOwnerValidation(cold.Records, common, rebound.Position,
                rebound.Changes, rebound.Thread, rebound.Files, null), FalloutReferencePackageOwnerValidation.ParentHistoryBoundary);
            coldSaved = rebound;
        }
        using (var persistent = new ReferencePositionFixture())
        {
            persistent.PrepareReferenceAggregate();
            persistent.Call(FalloutReferencePositionIngress.CellAttachment, inv => persistent.Position.StorePersistentCell(inv, true, persistent.OtherCell));
            persistent.ExecuteReferenceAggregateCommand();
            var current = persistent.CaptureReferenceAggregate(); ValidateReferenceAggregate(persistent, current);
            Require(current.Common.CommandFields!.Single().Values.SavedPosition!.Space == persistent.SavedWorldspace,
                "Aggregate substituted ParentCELL for the current Extra12 winning CELL/WRLD association.");
        }
        using (var failed = new ReferencePositionFixture())
        {
            failed.PrepareReferenceAggregate(); failed.SavedBorrowFailure = "change-flags";
            failed.ExecuteReferenceAggregateCommand();
            var prefix = failed.CaptureReferenceAggregate(); ValidateReferenceAggregate(failed, prefix);
            Require(prefix.Common.PackageCommands!.Single().FailedAt == FalloutSourcePackageAssignmentStep.SavedPosition &&
                prefix.Common.CommandFields!.Single().Operations.Last().SavedPositionCall is { Returned: false, Stores.Count: 7 } &&
                prefix.Owners.Changes.Calls.Last() is { Returned: false, Mask: 0x800 },
                "Aggregate erased committed Extra18 when the original manager child failed.");
        }
        Console.WriteLine("OPENNV_REFERENCE_PACKAGE_JOIN_PASS authoredScopeOnly=true actualWriterBoundaries=true mergedFlags=true " +
            "originalMask800Caller=true actualThreadRead=true actualFilePredicate=true coherentTamperRejected=true " +
            "missingParentColdHistory=REFUSED wholeWorldNative=UNRETURNED");
    }

    private static void RequireRefusal(Action action, string boundary)
    {
        try { action(); }
        catch (NotSupportedException error) when (error.Message == boundary) { return; }
        throw new InvalidOperationException("Expected exact missing reference-owner capability: " + boundary);
    }
    private static void ValidateReferenceAggregate(ReferencePositionFixture fixture, ReferenceAggregateFixtureSnapshot saved) =>
        _ = new FalloutReferencePackageOwnerValidation(fixture.Records, saved.Common, saved.Owners.Position, saved.Owners.Changes,
            saved.Owners.Thread, saved.Owners.Files, saved.Parent, saved.Runtime);
    private sealed record ReferenceAggregateFixtureSnapshot(ReferencePositionFixtureSnapshot Owners, FalloutProcessCommonSnapshot Common,
        FalloutReferenceParentCellSnapshot? Parent, FalloutActorProcessRuntimeSnapshot Runtime);

    private sealed partial class ReferencePositionFixture
    {
        internal FalloutPackageRuntimeRegistry? _aggregateRegistry;
        private FalloutActorProcessRuntimeState? _aggregateRuntime;
        private FalloutReferenceParentCellHistory? _aggregateParent;
        private readonly List<FalloutSourcePackageAssignmentCall> _aggregateCommands = [];
        internal void PrepareReferenceAggregate()
        {
            Call(FalloutReferencePositionIngress.ConstructorStartup, inv => Position.StoreConstructorStartupPosition(inv, this));
            Initialize();
            Call(FalloutReferencePositionIngress.ActorPosition, inv => Position.InitializeConstructedPlayerFields(inv, AnimationSettings("", "")));
            Call(FalloutReferencePositionIngress.ConfiguredStartup, inv => Position.StoreConfiguredStartupPosition(inv, this));
            Call(FalloutReferencePositionIngress.ActorPosition, inv => Initializer.SetReferenceInitialized(inv, false, RequireCurrent));
            Call(FalloutReferencePositionIngress.ActorPosition, inv => Initializer.SetReferenceInitialized(inv, true, RequireCurrent));
            _aggregateRegistry = new(Records, Source.Fields.Controls.Main.EngineSha256, Stack);
            Common.BindSourcePackageRegistry(_aggregateRegistry);
            _aggregateRuntime = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(Source.Fields.Controls.Main.EngineSha256), Stack, Actor.Reference, _ => Actor);
            _parentProcess = _aggregateRuntime.Capture().CapturedProcess;
            _savedParentRevision = 1;
            _aggregateParent = new(Source, Stack, _parentProcess, _parentFactory, Fields.Constructor, Actor, _savedParentRevision, _savedParentCell);
        }
        internal void ExecuteReferenceAggregateCommand() => CallPackage(invocation =>
        {
            if (!Fields.HasConstructedSavedPositionInitialization) Fields.InitializeConstructedPlayerSavedPosition(invocation, Position.LastPrefix!);
            FalloutSourcePackageAssignmentCall? call = null;
            Reject(() => FalloutSourcePackageAssignment.Execute(invocation, null, new("PositionFixture.esm", 0x403),
                "authored-reference-join-caller", new ReferenceAggregatePackageConsumers(this), value => call = value));
            Require(call is not null, "Actual source package caller omitted its retained failed prefix.");
            _aggregateCommands.Add(call!); return true;
        });
        internal ReferenceAggregateFixtureSnapshot CaptureReferenceAggregate()
        {
            var owners = Capture();
            return new(owners, owners.Common with { CommandFields = [owners.Fields], PackageCommands = _aggregateCommands.ToArray() },
                _aggregateParent!.Capture(_savedParentRevision, _savedParentCell), _aggregateRuntime!.Capture());
        }
        private void RetireReferenceAggregateFixture() { _aggregateRegistry?.Dispose(); _aggregateRuntime?.Dispose(); _aggregateParent?.Retire(); }
    }
    private sealed class ReferenceAggregatePackageConsumers(ReferencePositionFixture fixture) : IFalloutSourcePackageAssignmentConsumers
    {
        public FalloutPackageCommandFieldsSource Source => fixture.Fields.Source;
        public FalloutPackageAssignmentSource AssignmentSource => FalloutPackageAssignmentSource.Reviewed(Source);
        public string Stack => fixture.Stack;
        public void RequireCurrent(FalloutPackageCommandFieldInvocation invocation) => fixture.RequirePackageCurrent(invocation);
        public FalloutSourceSelectedPackageRead ReadSelectedPackage(FalloutPackageCommandFieldInvocation invocation, Action<FalloutSourceSelectedPackageRead> observe)
        {
            RequireCurrent(invocation);
            var primary = fixture.Common.ReadSourcePackageSlot(fixture.Actor.Reference, 1, FalloutProcessPackageSlot.Primary);
            observe(new(primary, null));
            var selected = fixture.Common.ReadSourcePackageSlot(fixture.Actor.Reference, 1, FalloutProcessPackageSlot.Current);
            var read = new FalloutSourceSelectedPackageRead(primary, selected); observe(read); return read;
        }
        public FalloutProcessPackageRead ReadCurrentPackage(FalloutPackageCommandFieldInvocation invocation) => fixture.Common.ReadSourcePackageSlot(fixture.Actor.Reference, 1, FalloutProcessPackageSlot.Current);
        public sbyte ReadPackageKind(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package) => fixture._aggregateRegistry!.Require(package).Kind;
        public void SpecialPackageCallback(FalloutPackageCommandFieldInvocation invocation) => RequireCurrent(invocation);
        public FalloutProcessSceneQuery ReadProcess(FalloutPackageCommandFieldInvocation invocation) => fixture.Common.ReadSceneQueriesForPackageCommand(invocation);
        public void MarkEvent(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, uint mask) => fixture.Fields.MarkPackageEvent(invocation, package, mask);
        public void PackageChanged(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package) => throw new InvalidOperationException("Authored current package is actually absent.");
        public void ClearProcessFlag(FalloutPackageCommandFieldInvocation invocation) => fixture.Common.StorePackageRequestFlag(invocation, false, RequireCurrent);
        public void PackageStarted(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package) => RequireCurrent(invocation);
        public int EnsureProcedure(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package)
        {
            var actual = fixture._aggregateRegistry!.Require(package);
            var procedure = FalloutPackageAssignmentSource.PureProcedure(actual.Kind)!.Value;
            if (actual.Procedure == -1) fixture._aggregateRegistry.PublishProcedure(invocation, actual, procedure, "authored-real-pure-procedure-child");
            return actual.Procedure;
        }
        public void SavePosition(FalloutPackageCommandFieldInvocation invocation) => fixture.Fields.SavePosition(invocation, fixture);
        public void SetPackageFlag(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, uint mask) => throw new NotSupportedException("authored-later-package-child-remains-unowned");
        public void AssignActorPackage(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package, bool primary, bool alternate) => throw new InvalidOperationException("Unreturned child cannot reach Actor assignment.");
        public int ReadProcessTier(FalloutPackageCommandFieldInvocation invocation) => throw new InvalidOperationException("Unreturned child cannot read tier.");
        public uint StoreProcessClock(FalloutPackageCommandFieldInvocation invocation) => throw new InvalidOperationException("Unreturned child cannot store clock.");
        public void UpdateTierOne(FalloutPackageCommandFieldInvocation invocation) => throw new InvalidOperationException("Unreturned child cannot enter tier1.");
        public bool ReadActualPlayerIdentity(FalloutPackageCommandFieldInvocation invocation) => throw new InvalidOperationException("Unreturned child cannot query Player.");
        public void StorePlayerFourth(FalloutPackageCommandFieldInvocation invocation) => throw new InvalidOperationException("Unreturned child cannot store Fourth.");
    }
}
