using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunReferencePositionOwnershipContracts()
    {
        using (var f = new ReferencePositionFixture())
        {
            var initial = f.Call(FalloutReferencePositionIngress.ControllerConstruction, f.Position.ReadConstructorPosition);
            Require(initial.Position == new FalloutPackageRawPosition(0, 0, 0) &&
                f.Call(FalloutReferencePositionIngress.ControllerConstruction, f.Position.ReadReferenceScale) is { Bits: 0x3f800000, ConstructorOnly: true },
                "Early descriptor lost actual constructor XYZ/scale or borrowed a pending/native pose.");
            Reject(() => f.Call(FalloutReferencePositionIngress.ActorPosition, f.Position.ReadCurrentPosition));
            var first = f.Call(FalloutReferencePositionIngress.ConstructorStartup, inv => f.Position.StoreConstructorStartupPosition(inv, f));
            Require(first is { Returned: true, Change.Suppressed: true } && first.After.Position == new FalloutPackageRawPosition(0x45000000, 0x45000000, 0) &&
                f.Events.SequenceEqual(new[] { "flags", "file", "kind", "change", "reset" }) && f.Changes.Capture().Entries.Count == 0,
                "First source writer lost predicate/store/mask2/reset order or ignored manager constructor flag1.");
            Reject(() => f.Call(FalloutReferencePositionIngress.ControllerConstruction, f.Position.ReadConstructorPosition));
            f.Initialize();
            var prefix = f.Call(FalloutReferencePositionIngress.ActorPosition, inv => f.Position.InitializeConstructedPlayerFields(inv, AnimationSettings("", "")));
            Require(prefix.Returned && f.Call(FalloutReferencePositionIngress.ActorPosition, f.Position.ReadCurrentPosition).Position == first.After.Position,
                "Independent prefix admitted stale constructor position or demanded whole physical return.");
            f.Events.Clear();
            var second = f.Call(FalloutReferencePositionIngress.ConfiguredStartup, inv => f.Position.StoreConfiguredStartupPosition(inv, f));
            Require(second.Returned && second.After.Position == new FalloutPackageRawPosition(0x45000000, 0x45000000, 0x43000000) &&
                f.Events.SequenceEqual(new[] { "clear", "cells", "yaw", "flags", "file", "kind", "change", "flags", "reset" }) &&
                f.Changes.Capture().Entries.Single().Flags == 2,
                "Configured writer substituted the earlier vector or changed its actual child order.");
            f.Call(FalloutReferencePositionIngress.ActorPosition, inv =>
            {
                var firstRead = f.Changes.ReferenceChanged(inv, 2, false, f);
                var secondRead = f.Changes.ReferenceChanged(inv, 2, false, f);
                Require(firstRead.Returned && secondRead.Returned && firstRead.ThreadFlags!.Invocation == secondRead.ThreadFlags!.Invocation &&
                    firstRead.ThreadFlags.Sequence < secondRead.ThreadFlags.Sequence,
                    "Repeated current-thread reads in one source operation were rejected or replayed a stale receipt.");
                return secondRead;
            });
            var saved = f.Capture();
            var bad = saved.Position.Calls.ToArray(); bad[^1] = bad[^1] with { Stores = bad[^1].Stores.Skip(1).ToArray() };
            Reject(() => FalloutSourceReferencePositionState.Validate(saved.Position with { Calls = bad }, f.Source, f.Stack, f.Actor));
            for (var cycle = 0; cycle < 3; cycle++)
            {
                using var cold = new ReferencePositionFixture(saved);
                Require(cold.Events.Count == 0 && JsonSerializer.Serialize(cold.Position.ConstructorStartup) == JsonSerializer.Serialize(first) &&
                    JsonSerializer.Serialize(cold.Position.Capture().Calls) == JsonSerializer.Serialize(saved.Position.Calls),
                    "Cold reference owner replayed startup stores/children or changed historical lifetimes.");
                Reject(() => cold.Call(FalloutReferencePositionIngress.ControllerConstruction, cold.Position.ReadConstructorPosition));
                Require(cold.Call(FalloutReferencePositionIngress.ActorPosition, cold.Position.ReadCurrentPosition).Position == second.After.Position,
                    "Cold current position borrowed the fresh constructor or native pose.");
                saved = cold.Capture();
            }
        }
        foreach (var failure in new[] { "change", "reset", "reentry" })
        {
            using var f = new ReferencePositionFixture(); f.FailChild = failure;
            Reject(() => f.Call(FalloutReferencePositionIngress.ConstructorStartup, inv => f.Position.StoreConstructorStartupPosition(inv, f)));
            var call = f.Position.ConstructorStartup!;
            Require(!call.Returned && call.Stores.Count == 3 && call.After.Position == new FalloutPackageRawPosition(0x45000000, 0x45000000, 0) &&
                (failure == "change" ? call.Change is null && !f.Events.Contains("reset") : call.Change is { Returned: true }),
                "Failed mask2/reset child erased committed raw XYZ or executed a later child.");
            Reject(() => f.Call(FalloutReferencePositionIngress.ConfiguredStartup, inv => f.Position.StoreConfiguredStartupPosition(inv, f)));
            Require(f.Position.ConfiguredStartup is { Returned: false, Stores.Count: 0 } && f.Position.SaveBlocker is not null,
                "Unreturned first source writer was crossed by configured placement.");
            _ = f.Position.Capture();
        }
        using (var f = new ReferencePositionFixture())
        {
            var read = f.Call(FalloutReferencePositionIngress.ActorPosition, f.Files.ReadCurrentFile);
            Require(read is { Count: 0, File: null } && read.Process != f.Common.ReadSceneQueries(f.Actor.Reference, 1).Process,
                "Current form-file getter used the owning plugin or collapsed Common and reference fields.");
            f.Call(FalloutReferencePositionIngress.CopyOrLoad, inv => f.Files.ObserveUnownedWriter(inv, "authored-association-copy"));
            Reject(() => f.Call(FalloutReferencePositionIngress.ActorPosition, f.Files.ReadCurrentFile));
            var snapshot = f.Files.Capture();
            var history = snapshot.Calls.ToArray(); history[0] = history[0] with { Read = read with { File = "PositionFixture.esm" } };
            Reject(() => FalloutReferenceFormFileAssociationState.Validate(snapshot with { Calls = history }, f.Files.Source, f.Stack, f.Actor));
        }
        RunInitialPlacementCellContracts();
        Console.WriteLine("OPENNV_REFERENCE_POSITION_PASS authoredScopeOnly=true independentRawFields=true pairedXY=true " +
            "twoStartupVectors=true mask2BeforeReset=true formFileNotOwningPlugin=true failedPrefixRetained=true coldCycles=3 originalStartup=UNEXECUTED");
    }

    private static void RunInitialPlacementCellContracts()
    {
        using var f = new ReferencePositionFixture();
        var empty = f.Call(FalloutReferencePositionIngress.ConfiguredStartup, f.Cells.ReadSelectedCell);
        Require(empty is { CurrentCell: null, Diameter: 5, Slot: 12, Cell: null }, "TES selection borrowed pending transfer/ParentCELL or a metadata null.");
        f.Call(FalloutReferencePositionIngress.CellAttachment, inv => f.Cells.StoreGridSlot(inv, 2, 2, f.Cell));
        Require(f.Call(FalloutReferencePositionIngress.ConfiguredStartup, f.Cells.ReadSelectedCell).Cell == f.Cell,
            "Current TES center did not read its actual mutable CELL slot.");
        f.Call(FalloutReferencePositionIngress.CellAttachment, inv => f.Cells.StoreCurrentCell(inv, f.OtherCell));
        f.Call(FalloutReferencePositionIngress.CopyOrLoad, inv => f.Cells.ObserveUnownedWriter(inv, FalloutInitialPlacementCellLane.Grid, "authored-grid-resize"));
        Require(f.Call(FalloutReferencePositionIngress.ConfiguredStartup, f.Cells.ReadSelectedCell) is { CurrentCell: not null, Diameter: null, Slot: null } read && read.Cell == f.OtherCell,
            "Nonnull current TES CELL did not short circuit an independently unavailable grid.");
        f.Call(FalloutReferencePositionIngress.CellAttachment, inv => f.Cells.StoreCurrentCell(inv, null));
        Reject(() => f.Call(FalloutReferencePositionIngress.ConfiguredStartup, f.Cells.ReadSelectedCell));
        _ = f.Cells.Capture();
        Require(FalloutInitialPlacementCellsSource.NormalizeGridDiameter(0) == 5 && FalloutInitialPlacementCellsSource.NormalizeGridDiameter(4) == 5 &&
            FalloutInitialPlacementCellsSource.NormalizeGridDiameter(6) == 7 && FalloutInitialPlacementCellsSource.NormalizeGridDiameter(7) == 7,
            "Original unsigned grid normalization changed minimum/odd behavior.");
        using var failed = new ReferencePositionFixture(constructGrid: false);
        Reject(() => failed.Call(FalloutReferencePositionIngress.ControllerConstruction, inv => failed.Cells.ConstructGrid(inv, GridInput(6))));
        Require(failed.Cells.GridConstruction is { Returned: false, SettingStored: false, Stores.Count: 0 },
            "Grid normalization silently substituted another mutable Main INI authority.");
        Reject(() => failed.Call(FalloutReferencePositionIngress.ControllerConstruction, inv => failed.Cells.ConstructGrid(inv, GridInput(5))));
        _ = failed.Cells.Capture();
    }
    private static FalloutIniValue GridInput(uint value) => new(new("uGridsToLoad:General", FalloutIniCollection.Main, 5), value, "authored-real-Main-INI-cell");

    private sealed record ReferencePositionFixtureSnapshot(FalloutReferencePositionSnapshot Position, FalloutReferenceChangeSnapshot Changes,
        FalloutReferenceFormFileSnapshot Files, FalloutInitialPlacementCellSnapshot Cells, FalloutSourceThreadReferenceFlagsSnapshot Thread,
        FalloutPackageCommandFieldSnapshot Fields, FalloutProcessCommonSnapshot Common);
    private sealed class ReferencePositionFixture : IFalloutReferencePositionConsumers, IFalloutReferenceChangeConsumers, IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "OpenNV-ReferencePosition-" + Guid.NewGuid().ToString("N"));
        private readonly bool _ownsRecords;
        private FalloutReferencePositionInvocation? _current;
        private FalloutReferenceScriptExtraInitializationInvocation? _script;
        internal readonly FalloutPluginStack Records;
        internal readonly FalloutActorProcessCommonState Common;
        internal readonly FalloutPlayerThreadContextState Thread;
        internal readonly FalloutActorPackageCommandFields Fields;
        internal readonly FalloutReferenceScriptExtraInitializer Initializer;
        internal readonly FalloutSourceReferencePositionState Position;
        internal readonly FalloutSourceReferenceChangeManager Changes;
        internal readonly FalloutReferenceFormFileAssociationState Files;
        internal readonly FalloutSourceInitialPlacementCells Cells;
        internal readonly FalloutReferencePositionSource Source;
        internal readonly FalloutCombatActorIdentity Actor;
        internal readonly string Stack = new('7', 64);
        internal readonly FalloutFormKey Cell = new("PositionFixture.esm", 0x400), OtherCell = new("PositionFixture.esm", 0x401);
        private readonly Guid _parentProcess = Guid.NewGuid(), _parentFactory = Guid.NewGuid();
        internal readonly List<string> Events = [];
        internal string? FailChild;
        public Guid Factory { get; } = Guid.NewGuid();
        internal ReferencePositionFixture(ReferencePositionFixtureSnapshot? saved = null, bool constructGrid = true,
            FalloutPluginStack? original = null, FalloutPlayerMovementControlSource? controls = null)
        {
            _ownsRecords = original is null;
            if (_ownsRecords)
            {
            Directory.CreateDirectory(_directory);
            File.WriteAllBytes(Path.Combine(_directory, "PositionFixture.esm"), Join(
                Row("TES4", 0, Part("HEDR", new byte[12])),
                Row("NPC_", 7, Part("EDID", "Player\0"u8.ToArray()), Part("ACBS", new byte[24]), Part("RNAM", BitConverter.GetBytes(0x19u))),
                Row("RACE", 0x19, Part("EDID", "Race\0"u8.ToArray())), Row("DOBJ", 0x31, Part("DATA", new byte[84])),
                Row("CELL", 0x400, Part("EDID", "Cell\0"u8.ToArray())), Row("CELL", 0x401, Part("EDID", "OtherCell\0"u8.ToArray()))));
            Records = FalloutPluginStack.Load(_directory, ["PositionFixture.esm"]);
            }
            else { Records = original!; Stack = original!.OwnedSource!.StackId; }
            var main = controls?.Main ?? FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64), FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
            var basis = Records.GetEffective(Records.RuntimeFormKey(7));
            Actor = new(Records.RuntimeFormKey(0x14), basis.FormKey, "ENGINE_PLAYER", 0, main.EngineSha256, basis.Signature, basis.Flags,
                Convert.ToHexString(SHA256.HashData(basis.ReadData())).ToLowerInvariant(), true);
            Common = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256), Stack, _ => Actor,
                _ => throw new InvalidOperationException("Reference scalar query cannot create gameplay pools."),
                _ => throw new InvalidOperationException("Reference scalar query cannot create a native body."), saved?.Common);
            if (saved is null) Common.Construct(Actor.Reference, 1, FalloutDetectionProcessLevel.High);
            Thread = new(FalloutPlayerThreadContextSource.Reviewed(main), Guid.NewGuid());
            var fieldSource = FalloutPackageCommandFieldsSource.Reviewed(controls ?? FalloutPlayerMovementControlSource.Reviewed(main));
            Fields = new(fieldSource, Stack, Actor, Common.ReadSceneQueries(Actor.Reference, 1).Process, 1, Guid.NewGuid(), _ => throw new InvalidOperationException("No actual package command entered this scalar fixture."), saved: saved?.Fields);
            Source = FalloutReferencePositionSource.Reviewed(fieldSource);
            Thread.BindReferenceChangeFlags(Source, saved?.Thread);
            Initializer = new(FalloutReferenceScriptExtraInitializerSource.Reviewed(fieldSource), Records, Fields, Thread, invocation =>
            { if (invocation != _script) throw new InvalidOperationException("Authored Script initializer left its actual scope."); });
            Position = new(Source, Records, Stack, Actor, Thread.Process, Fields.Constructor, RequireCurrent, saved?.Position);
            Changes = new(Source, Stack, Thread.Process, Guid.NewGuid(), RequireCurrent, saved?.Changes);
            Files = new(FalloutReferenceFormFileSource.Reviewed(Source), Records, Stack, Thread.Process, Fields.Constructor, Actor, RequireCurrent, saved?.Files);
            Cells = new(FalloutInitialPlacementCellsSource.Reviewed(Source), Records, Stack, Guid.NewGuid(), Guid.NewGuid(), RequireCurrent, saved?.Cells);
            if (saved is null && constructGrid) Call(FalloutReferencePositionIngress.ControllerConstruction, invocation => Cells.ConstructGrid(invocation, GridInput(5)));
        }
        internal void Initialize()
        {
            _script = new(Guid.NewGuid(), Actor, Fields.Process, 1, Fields.Constructor, Environment.CurrentManagedThreadId);
            try { _ = Initializer.Initialize(_script); } finally { _script = null; }
        }
        internal T Call<T>(FalloutReferencePositionIngress ingress, Func<FalloutReferencePositionInvocation, T> action)
        {
            if (_current is not null) throw new InvalidOperationException("Authored reference caller reentered.");
            _current = new(Guid.NewGuid(), Actor, Thread.Process, Fields.Constructor, Fields.Process, 1, Environment.CurrentManagedThreadId, ingress,
                ingress == FalloutReferencePositionIngress.PackageCommand ? Guid.NewGuid() : null);
            try { return action(_current); } finally { _current = null; }
        }
        internal void Call(FalloutReferencePositionIngress ingress, Action<FalloutReferencePositionInvocation> action) => Call(ingress, inv => { action(inv); return true; });
        public void RequireCurrent(FalloutReferencePositionInvocation invocation)
        { if (_current != invocation || invocation.ThreadId != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Authored reference left its actual caller."); }
        public FalloutReferenceFlagRead ReadReferenceFlags(FalloutReferencePositionInvocation invocation)
        { Events.Add("flags"); return Initializer.ReadReferenceFlags(invocation, RequireCurrent); }
        public bool ReadPersistentReference(FalloutReferencePositionInvocation invocation, FalloutReferenceFlagRead flags)
        { Events.Add("file"); Require(Files.ReadCurrentFile(invocation).File is null, "Expected actual empty constructor file list."); return (flags.Flags & 0x400) != 0; }
        public byte ReadBaseFormKind(FalloutReferencePositionInvocation invocation)
        { RequireCurrent(invocation); Events.Add("kind"); Require(Records.GetEffective(Actor.Base).Signature == "NPC_", "Wrong authored current Base kind."); return 0x2a; }
        public FalloutReferenceParentCellRead ReadCurrentParentCell(FalloutReferencePositionInvocation invocation)
        { RequireCurrent(invocation); return new("authored-actual-World-ParentCELL", Stack, _parentProcess, _parentFactory, Fields.Constructor, Actor, 0, null, Source.Fields.Controls.Main.EngineSha256); }
        public FalloutFormKey? ReadCellWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey cell) =>
            throw new InvalidOperationException("Actual null selected branch cannot query a CELL worldspace.");
        public void DetachWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey worldspace) => throw new InvalidOperationException("NPC kind cannot detach worldspace.");
        public void AttachWorldspace(FalloutReferencePositionInvocation invocation, FalloutFormKey worldspace) => throw new InvalidOperationException("NPC kind cannot attach worldspace.");
        public FalloutReferenceChangeCall ReferenceChanged(FalloutReferencePositionInvocation invocation, uint mask)
        { Events.Add("change"); if (FailChild == "change") throw new NotSupportedException("authored-mask2-child"); return Changes.ReferenceChanged(invocation, mask, false, this); }
        public void ResetCurrentControllerAndModel(FalloutReferencePositionInvocation invocation, FalloutPackageRawPosition position)
        {
            RequireCurrent(invocation); Events.Add("reset");
            if (FailChild == "reset") throw new NotSupportedException("authored-real-reset-child");
            if (FailChild == "reentry") { try { Position.Capture(); } catch (InvalidOperationException) { } }
        }
        public FalloutPackageRawPosition SelectInitialPlacement(FalloutReferencePositionInvocation invocation, FalloutPackageRawPosition supplied)
        { Events.Add("cells"); Require(Cells.ReadSelectedCell(invocation).Cell is null, "Actual nonnull CELL requires a separate transform."); return supplied; }
        public void SetInitialPlacementYaw(FalloutReferencePositionInvocation invocation) { RequireCurrent(invocation); Events.Add("yaw"); }
        public void ClearStartupChangeSuppression(FalloutReferencePositionInvocation invocation) { Events.Add("clear"); _ = Changes.ClearStartupSuppression(invocation); }
        public FalloutSourceThreadReferenceFlagsRead ReadThreadFlags(FalloutReferencePositionInvocation invocation) => Thread.ReadReferenceChangeFlags(invocation.Call, Factory, () => RequireCurrent(invocation));
        public uint ReadSourceFormId(FalloutReferencePositionInvocation invocation) { RequireCurrent(invocation); return Records.RuntimeFormId(invocation.Actor.Reference); }
        public bool ReadDataHandlerSuppression(FalloutReferencePositionInvocation invocation, uint sourceFormId) => throw new InvalidOperationException("Actual reference flags lack DataHandler selector bit5.");
        internal ReferencePositionFixtureSnapshot Capture() => JsonSerializer.Deserialize<ReferencePositionFixtureSnapshot>(JsonSerializer.Serialize(
            new ReferencePositionFixtureSnapshot(Position.Capture(), Changes.Capture(), Files.Capture(), Cells.Capture(), Thread.CaptureReferenceChangeFlags(), Fields.Capture(), Common.Capture())))!;
        private static byte[] Row(string signature, uint form, params byte[][] fields)
        { var data = Join(fields); var bytes = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), form); data.CopyTo(bytes, 24); return bytes; }
        private static byte[] Part(string signature, byte[] data)
        { var bytes = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 0); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), checked((ushort)data.Length)); data.CopyTo(bytes, 6); return bytes; }
        private static byte[] Join(params byte[][] values) => values.SelectMany(value => value).ToArray();
        public void Dispose()
        {
            try { Position.Retire(); Changes.Retire(); Files.Retire(); Cells.Retire(); Fields.Retire(); Thread.Retire(); Common.Dispose(); if (_ownsRecords) Records.Dispose(); }
            finally { if (_ownsRecords) Directory.Delete(_directory, true); }
        }
    }
}
