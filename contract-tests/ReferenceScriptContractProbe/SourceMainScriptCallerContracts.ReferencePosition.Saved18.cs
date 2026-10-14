using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void RunTypedSavedPositionBorrowContracts()
    {
        using (var f = new ReferencePositionFixture())
        {
            f.PrepareSavedPosition();
            var first = f.SaveCurrentPosition();
            Require(first is { Returned: true, PositionRead: not null, ParentRead.Cell: null, WorldspaceRead: not null } &&
                first.WorldspaceRead is { PersistentLookup: true, PersistentPresent: false, Worldspace: null } &&
                first.After is { Space: null, RotationZ: 0x3f800000 } && first.After.Position == f.Position.Capture().Values.Position &&
                first.Stores.Count == 7 && f.Changes.Capture().Entries.Single().Flags == 0x802,
                "Saved18 did not borrow current raw XYZ and actual null ParentCELL before the selected Extra12 lookup and mask800.");
            var firstPosition = first.PositionRead!; var firstParent = first.ParentRead!;
            var firstWorldspace = first.WorldspaceRead!; var firstSaved = first.After!;
            Require(firstPosition.Process == f.Position.Process && firstPosition.Factory == f.Position.Factory &&
                firstPosition.Process != firstParent.Process && firstParent.Process != first.Invocation.Process &&
                firstWorldspace.Flags!.Process == first.Invocation.Process &&
                firstPosition.Process != firstWorldspace.Flags!.Process && firstWorldspace.Factory == firstPosition.Factory,
                "Saved18 collapsed World ParentCELL, reference/Main and Common flag lifetimes.");
            var fields = f.Fields.Capture();
            Require(fields.Values.Position == new FalloutPackageRawPosition(0, 0, 0) && fields.Values.ParentCell is null &&
                fields.Availability.Where(value => value.Available).Select(value => value.Lane)
                    .SequenceEqual(new[] { FalloutPackageCommandFieldLane.ScriptExtra, FalloutPackageCommandFieldLane.SavedPosition }),
                "Typed reference borrows copied fields or admitted unrelated constructor lanes.");
            Require(f.Changes.Capture().Calls.Last().Invocation.PackageCommand == first.Invocation.SourceCommand,
                "Mask800 child lost its actual entered source package command.");

            var next = new FalloutPackageRawPosition(0x3f800000, 0x40000000, 0x40400000);
            f.Call(FalloutReferencePositionIngress.ActorPosition, inv => f.Position.StoreActorPosition(inv, next, f));
            f.SetAuthoredParentCell(f.Cell);
            f.SavedRotationZ = 0x40000000;
            var interior = f.SaveCurrentPosition();
            Require(interior.ParentCell == f.Cell && interior.WorldspaceRead is
                { Flags: null, PersistentLookup: false, Worldspace: null } && interior.After!.Space == f.Cell &&
                interior.After.Identity == firstSaved.Identity && interior.After.Position == next &&
                interior.RotationZ == 0x40000000 && interior.After.RotationZ == firstSaved.RotationZ && interior.Stores.Count == 4,
                "Current interior ParentCELL did not short circuit persistent lookup or Saved18 rewrote retained rotation.");

            f.Call(FalloutReferencePositionIngress.CellAttachment, inv => f.Position.StorePersistentCell(inv, true, f.OtherCell));
            f.SetAuthoredParentCell(null);
            var exterior = f.SaveCurrentPosition();
            Require(exterior.ParentCell is null && exterior.WorldspaceRead is
                { PersistentLookup: true, PersistentPresent: true, PersistentCell: not null, Worldspace: not null } &&
                exterior.WorldspaceRead.PersistentCell == f.OtherCell && exterior.After!.Space == f.SavedWorldspace &&
                exterior.After.Identity == firstSaved.Identity && exterior.After.Position == next,
                "Null raw ParentCELL borrowed a pending CELL instead of current Extra12 and its Worldspace child.");

            var saved = f.Capture();
            ValidateSaved18BorrowTampering(f, saved.Fields);
            var originalCalls = JsonSerializer.Serialize(saved.Position.Calls);
            var originalOperations = JsonSerializer.Serialize(saved.Fields.Operations);
            var originalInitialization = JsonSerializer.Serialize(f.Fields.LastSavedPositionInitialization);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                using var cold = new ReferencePositionFixture(saved);
                Require(cold.Events.Count == 0 && cold.SavedBorrowEvents.Count == 0 &&
                    JsonSerializer.Serialize(cold.Position.Capture().Calls) == originalCalls &&
                    JsonSerializer.Serialize(cold.Fields.Capture().Operations) == originalOperations &&
                    JsonSerializer.Serialize(cold.Fields.LastSavedPositionInitialization) == originalInitialization &&
                    cold.Fields.HasConstructedSavedPositionInitialization,
                    "Cold Saved18 replayed prefix admission, reference stores or consumer callbacks.");
                Reject(() => cold.CallPackage(inv => cold.Fields.InitializeConstructedPlayerSavedPosition(inv, cold.Position.LastPrefix!)));
                var current = cold.SaveCurrentPosition();
                Require(current.After!.Identity == firstSaved.Identity && current.After.RotationZ == firstSaved.RotationZ &&
                    current.After.Position == next && current.After.Space == cold.SavedWorldspace &&
                    current.PositionRead!.Factory == cold.Position.Factory && current.PositionRead.Factory != exterior.PositionRead!.Factory &&
                    current.ParentRead!.Factory != exterior.ParentRead!.Factory && current.Invocation.Process == cold.Fields.Process &&
                    current.WorldspaceRead!.Flags!.Factory == cold.Initializer.Factory,
                    "Cold Saved18 substituted a constructor/native pose or borrowed retired reference/World/Common fields.");
                saved = cold.Capture();
                originalCalls = JsonSerializer.Serialize(saved.Position.Calls);
                originalOperations = JsonSerializer.Serialize(saved.Fields.Operations);
            }
        }
        foreach (var child in new[] { "ParentCELL", "Worldspace", "change-flags" })
        {
            using var f = new ReferencePositionFixture();
            f.PrepareSavedPosition();
            f.SetAuthoredParentCell(f.OtherCell);
            f.SavedBorrowFailure = child;
            Reject(() => f.SaveCurrentPosition());
            var failed = f.Fields.LastSavedPositionCall!;
            Require(!failed.Returned && failed.Failure is not null && failed.PositionRead is not null &&
                (child == "change-flags" ? failed is { ChangeEntered: true, Stores.Count: 7, After: not null } :
                    failed is { ChangeEntered: false, Stores.Count: 0, After: null }),
                "Failed typed reference getter/change child erased committed Extra18 stores or fabricated later stores.");
            if (child == "change-flags")
            {
                var change = f.Changes.Capture().Calls.Last();
                Require(!change.Returned && change.Mask == 0x800 && change.Invocation.PackageCommand == failed.Invocation.SourceCommand &&
                    f.Changes.Capture().Entries.Single().Flags == 2,
                    "Failed actual mask800 child rewrote the shared record or lost its original command linkage.");
            }
            var saved = f.Capture();
            using var cold = new ReferencePositionFixture(saved);
            Require(cold.SavedBorrowEvents.Count == 0 && cold.Fields.SaveBlocker is not null &&
                JsonSerializer.Serialize(cold.Fields.LastSavedPositionCall) == JsonSerializer.Serialize(failed),
                "Cold failure replayed an external child or replaced an unreturned Saved18 prefix.");
        }
        using (var f = new ReferencePositionFixture())
        {
            f.PrepareSavedPosition();
            f.Call(FalloutReferencePositionIngress.CopyOrLoad,
                inv => f.Position.ObserveUnownedWriter(inv, FalloutReferencePositionLane.PositionMode, "authored-current-adjusted-mode-writer"));
            Reject(() => f.SaveCurrentPosition());
            Require(f.Fields.LastSavedPositionCall is { Position: null, ParentCellReturned: false, Stores.Count: 0 } &&
                f.Fields.Capture().Values.SavedPosition is null,
                "Typed borrow used constructor mode0 after a current unowned mode writer.");
        }
        Console.WriteLine("OPENNV_TYPED_SAVED18_BORROW_PASS authoredScopeOnly=true currentRawXYZ=true independentParentLineage=true " +
            "persistentExtra12=true retainedRotation=true sharedMask800=true failedStoresRetained=true coldCycles=3 originalCommand=UNEXECUTED");
    }

    private static void ValidateSaved18BorrowTampering(ReferencePositionFixture fixture, FalloutPackageCommandFieldSnapshot saved)
    {
        var index = saved.Operations.ToList().FindLastIndex(value => value.SavedPositionCall is not null);
        var original = saved.Operations[index].SavedPositionCall!;
        var wrongActor = original.PositionRead!.Actor with { BaseSha256 = new string('c', 64) };
        foreach (var changed in new[]
        {
            original with { PositionRead = original.PositionRead! with { Actor = wrongActor } },
            original with { PositionRead = original.PositionRead! with { Mode = 1 } },
            original with { PositionRead = original.PositionRead! with { Sequence = -1 } },
            original with { ParentRead = original.ParentRead! with { Process = Guid.Empty } },
            original with { ParentRead = original.ParentRead! with { ExecutableSha256 = new string('b', 64) } },
            original with { WorldspaceRead = original.WorldspaceRead! with { Factory = Guid.NewGuid() } },
            original with { WorldspaceRead = original.WorldspaceRead! with { Constructor = Guid.NewGuid() } },
            original with { WorldspaceRead = original.WorldspaceRead! with { Parent = original.ParentRead! with { Sequence = original.ParentRead!.Sequence + 1 } } },
            original with { WorldspaceRead = original.WorldspaceRead! with { Flags = original.WorldspaceRead!.Flags! with { Process = original.PositionRead!.Process } } },
            original with { WorldspaceRead = original.WorldspaceRead! with { PersistentLookup = false } }
        })
        {
            var operations = saved.Operations.ToArray(); operations[index] = operations[index] with { SavedPositionCall = changed };
            Reject(() => FalloutActorPackageCommandFields.Validate(saved with { Operations = operations }, fixture.Fields.Source, fixture.Stack, fixture.Actor));
        }
        var admissionIndex = saved.Operations.ToList().FindIndex(value => value.SavedPositionInitialization is not null);
        var admission = saved.Operations[admissionIndex].SavedPositionInitialization!;
        var badAdmission = saved.Operations.ToArray();
        badAdmission[admissionIndex] = badAdmission[admissionIndex] with
        { SavedPositionInitialization = admission with { Prefix = admission.Prefix with { Returned = false, Failure = "authored-unreturned-prior-source-child" } } };
        Reject(() => FalloutActorPackageCommandFields.Validate(saved with { Operations = badAdmission }, fixture.Fields.Source, fixture.Stack, fixture.Actor));
    }

    private sealed record SavedBorrowFixtureSnapshot(FalloutFormKey? ParentCell, long ParentRevision, uint RotationZ);
    private sealed partial class ReferencePositionFixture : IFalloutPackageReferenceFieldConsumers
    {
        private FalloutPackageCommandFieldInvocation? _package;
        private FalloutFormKey? _savedParentCell;
        private long _savedParentRevision;
        private readonly Guid _savedRotationProcess = Guid.NewGuid(), _savedRotationFactory = Guid.NewGuid(), _savedRotationConstructor = Guid.NewGuid();
        private bool _savedChangeEntered;
        internal uint SavedRotationZ = 0x3f800000;
        internal string? SavedBorrowFailure;
        internal readonly List<string> SavedBorrowEvents = [];
        internal FalloutFormKey SavedWorldspace => new("PositionFixture.esm", 0x402);
        FalloutPackageCommandFieldsSource IFalloutPackageReferenceFieldConsumers.Source => Fields.Source;
        string IFalloutPackageReferenceFieldConsumers.Stack => Stack;
        FalloutCombatActorIdentity IFalloutPackageReferenceFieldConsumers.Actor => Actor;
        Guid IFalloutPackageReferenceFieldConsumers.Process => Fields.Process;
        long IFalloutPackageReferenceFieldConsumers.Epoch => Fields.Epoch;
        void IFalloutPackageReferenceFieldConsumers.RequireCurrent(FalloutPackageCommandFieldInvocation invocation) => RequirePackageCurrent(invocation);
        internal void PrepareSavedPosition()
        {
            Call(FalloutReferencePositionIngress.ConstructorStartup, inv => Position.StoreConstructorStartupPosition(inv, this));
            Initialize();
            Call(FalloutReferencePositionIngress.ActorPosition, inv => Position.InitializeConstructedPlayerFields(inv, AnimationSettings("", "")));
            Call(FalloutReferencePositionIngress.ConfiguredStartup, inv => Position.StoreConfiguredStartupPosition(inv, this));
            CallPackage(inv => Fields.InitializeConstructedPlayerSavedPosition(inv, Position.LastPrefix!));
        }
        internal void SetAuthoredParentCell(FalloutFormKey? cell) { _savedParentCell = cell; _savedParentRevision++; }
        private void RestoreSavedBorrowFixture(SavedBorrowFixtureSnapshot? saved)
        { if (saved is not null) { _savedParentCell = saved.ParentCell; _savedParentRevision = saved.ParentRevision; SavedRotationZ = saved.RotationZ; } }
        private SavedBorrowFixtureSnapshot CaptureSavedBorrowFixture() => new(_savedParentCell, _savedParentRevision, SavedRotationZ);
        internal T CallPackage<T>(Func<FalloutPackageCommandFieldInvocation, T> action)
        {
            if (_package is not null || _current is not null) throw new InvalidOperationException("Authored Saved18 caller reentered.");
            _package = new(Guid.NewGuid(), Actor, Fields.Process, Fields.Epoch, Environment.CurrentManagedThreadId);
            try { return action(_package); } finally { _package = null; }
        }
        internal void RequirePackageCurrent(FalloutPackageCommandFieldInvocation invocation)
        { if (_package != invocation || invocation.ThreadId != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Saved18 left its actual authored package caller."); }
        internal FalloutPackageCommandSavedPositionCall SaveCurrentPosition() => CallPackage(inv => Fields.SavePosition(inv, this));
        FalloutPackageCommandRawRotationRead IFalloutPackageReferenceFieldConsumers.ReadRawRotationZ(FalloutPackageCommandFieldInvocation invocation)
        {
            RequirePackageCurrent(invocation); SavedBorrowEvents.Add("raw-RZ");
            return new(Fields.Source.Controls.Identity, Stack, _savedRotationProcess, _savedRotationFactory, _savedRotationConstructor,
                Actor.Reference, Actor.Base, _savedParentRevision, SavedRotationZ);
        }
        FalloutReferencePositionRead IFalloutPackageReferenceFieldConsumers.ReadCurrentReferencePosition(FalloutPackageCommandFieldInvocation invocation)
        { RequirePackageCurrent(invocation); SavedBorrowEvents.Add("raw-XYZ"); return Call(FalloutReferencePositionIngress.PackageCommand, Position.ReadCurrentPosition); }
        FalloutReferenceParentCellRead IFalloutPackageReferenceFieldConsumers.ReadCurrentReferenceParentCell(FalloutPackageCommandFieldInvocation invocation)
        {
            RequirePackageCurrent(invocation); SavedBorrowEvents.Add("ParentCELL");
            if (SavedBorrowFailure == "ParentCELL") throw new NotSupportedException("authored-current-World-ParentCELL-child");
            return Call(FalloutReferencePositionIngress.PackageCommand, inv => Position.ReadCurrentParentCell(inv, this));
        }
        FalloutReferenceWorldspaceRead IFalloutPackageReferenceFieldConsumers.ReadCurrentReferenceWorldspace(FalloutPackageCommandFieldInvocation invocation)
        { RequirePackageCurrent(invocation); SavedBorrowEvents.Add("Worldspace"); return Call(FalloutReferencePositionIngress.PackageCommand, inv => Position.ReadWorldspace(inv, this)); }
        FalloutFormKey? IFalloutPackageReferenceFieldConsumers.ReadWorldspace(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey? parentCell) =>
            throw new InvalidOperationException("Typed Saved18 used the legacy Worldspace fallback.");
        void IFalloutPackageReferenceFieldConsumers.ReferenceChanged(FalloutPackageCommandFieldInvocation invocation, uint mask)
        {
            RequirePackageCurrent(invocation); SavedBorrowEvents.Add("mask800");
            Require(mask == 0x800, "Saved18 lost its original reference-change mask.");
            _savedChangeEntered = true;
            try { Call(FalloutReferencePositionIngress.PackageCommand, inv => Changes.ReferenceChanged(inv, mask, false, this)); }
            finally { _savedChangeEntered = false; }
        }
        private void FailSavedFlagRead()
        { if (_savedChangeEntered && SavedBorrowFailure == "change-flags") throw new NotSupportedException("authored-current-reference-flag-child"); }
        private FalloutFormKey? ReadAuthoredCellWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey cell)
        {
            RequireCurrent(invocation);
            if (SavedBorrowFailure == "Worldspace") throw new NotSupportedException("authored-CELL-Worldspace-child");
            return FalloutCellSceneReader.ReadDefinition(Records, cell).Worldspace;
        }
        private static byte[] SavedBorrowCells()
        {
            var exterior = Row("CELL", 0x401, Part("EDID", "OtherCell\0"u8.ToArray()), Part("DATA", [0]), Part("XCLC", new byte[8]));
            var group = new byte[24 + exterior.Length];
            "GRUP"u8.CopyTo(group); BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(4), (uint)group.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(8), 0x402); BinaryPrimitives.WriteUInt32LittleEndian(group.AsSpan(12), 1);
            exterior.CopyTo(group, 24);
            return Join(Row("CELL", 0x400, Part("EDID", "Cell\0"u8.ToArray()), Part("DATA", [1])),
                Row("WRLD", 0x402, Part("EDID", "World\0"u8.ToArray())), group,
                Row("PACK", 0x403, Part("PKDT", [0, 0, 0, 0, 6, 0, 0, 0])));
        }
    }
}
