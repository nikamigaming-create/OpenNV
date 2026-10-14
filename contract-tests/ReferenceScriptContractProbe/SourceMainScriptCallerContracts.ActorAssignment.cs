using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunActorPackageAssignmentContracts()
    {
        foreach (var invalidated in new[] { false, true })
        {
            using var fixture = new ActorAssignmentFixture();
            if (invalidated) fixture.Loading.RetainUnqualifiedRecordLoadPrefix(Guid.NewGuid(), fixture.Reference.Stack, () => { });
            fixture.Execute(expectFailure: true);
            var call = fixture.Reference.Common.LastActorPackageAssignment!;
            Require(call.Step == FalloutActorAssignmentStep.Failed && call.PackageMutation is { Step: FalloutPackageStateStep.Returned } &&
                call.Linked is { Returned: true, Equal: true, NullCleanupReturned: true, Stores.Count: 0 } &&
                call.ByteStore is { Returned: true } && call.AnimationProcess?.AnimationContext is null &&
                (invalidated ? call.HighReset is null : call.HighReset is { Returned: true }) &&
                fixture.Reference.Common.Capture().Current.Single().ActorAssignment!.HighCalls.Single() is { } high &&
                high.Stores.Count == (invalidated ? 6 : 18) && high.InlineListEmpty &&
                (invalidated ? high is { Returned: false, FailedAt: FalloutActorHighResetStep.Loading } : high.Returned),
                "Actor prefix lost real Current/byte/linked/reset stores or crossed an unavailable loading/follower owner.");
            var snapshot = fixture.Reference.Common.Capture(); FalloutActorProcessCommonState.Validate(snapshot);
            var fields = snapshot.Current.Single().ActorAssignment!;
            Require(fields.LinkedCalls.Select(value => value.Occurrence).SequenceEqual(
                [FalloutActorLinkedOccurrence.ActorAssignment, FalloutActorLinkedOccurrence.SharedReset]),
                "Two genuine null setters were deduplicated or omitted their source occurrences.");
            Require(fixture.Registry.Require(fixture.Package).LifetimeCount == 1 && fixture.Hours == 1 &&
                fixture.Reference.Common.ReadSourcePackageSlot(fixture.Reference.Actor.Reference, 1, FalloutProcessPackageSlot.Current).Fields.Fields.TimeBits == fixture.HourBits,
                "Current setter used another package, fabricated a count or ignored actual mutable GameHour.");
            Reject(() => fixture.Reference.Common.ExecuteActorPackageAssignment(fixture.Invocation!, fixture.Package, false, false, fixture, _ => { }));
            Reject(() => FalloutActorProcessCommonState.Validate(snapshot with { Current = [snapshot.Current.Single() with
            { ActorAssignment = fields with { Values = fields.Values with { Tier = 1 } } }] }));
            Reject(() => FalloutActorProcessCommonState.Validate(snapshot with { Current = [snapshot.Current.Single() with
            { ActorAssignment = fields with { LinkedCalls = [fields.LinkedCalls[0], fields.LinkedCalls[0]] } }] }));
        }
        using (var fixture = new ActorAssignmentFixture())
        {
            fixture.InCommand(invocation => fixture.Reference.Call(FalloutReferencePositionIngress.PackageCommand,
                current => fixture.Reference.Position.StoreActorYawMode(current, 4)));
            fixture.Execute(expectFailure: true);
            Require(fixture.Reference.Common.LastActorPackageAssignment is { FailedAt: FalloutActorAssignmentStep.ExceptionalStatus,
                StatusReads.Count: 2, PackageMutation: null }, "Nonzero assignment status used neutral life or skipped a reached original child.");
        }
        using (var fixture = new ActorAssignmentFixture())
        {
            fixture.InCommand(invocation => fixture.Reference.Common.StoreActorAssignmentAdjustment(invocation, 0x3f800000, fixture.RequireCurrent));
            fixture.Execute(expectFailure: true);
            Require(fixture.Reference.Common.LastActorPackageAssignment is { FailedAt: FalloutActorAssignmentStep.PositiveAdjustment,
                PackageMutation: null, ByteStore: null }, "Positive adjustment reached the byte/package suffix before its unowned children.");
            _ = fixture.Reference.Common.Capture();
        }
        using (var fixture = new ActorAssignmentFixture())
        {
            fixture.SwallowCaptureReentry = true; fixture.Execute(expectFailure: true);
            Require(fixture.Reference.Common.LastActorPackageAssignment is { Returned: false, PackageMutation: null },
                "A swallowed actual graph capture reentry continued into a package writer.");
        }
        RunActorPackageTargetContracts();
        Console.WriteLine("OPENNV_ACTOR_PACKAGE_ASSIGNMENT_PASS authoredScopeOnly=true sameStatusAndByteOwners=true realCurrentSetter=true " +
            "twoLinkedNullTails=true sharedLoadingBoundary=true partialHighStoresRetained=true targetsOwned=true wholePlayer=UNRETURNED");
    }

    private static void RunActorPackageTargetContracts()
    {
        using var fixture = new ActorAssignmentFixture();
        FalloutPackageRegistrySnapshot saved = fixture.Registry.Capture();
        var reads = new List<FalloutPackageRuntimeTargetRead>();
        fixture.InCommand(invocation =>
        {
            reads.Add(fixture.Registry.ReadCurrentTarget(invocation, fixture.Registry.Require(fixture.Package)));
            reads.Add(fixture.Registry.ReadCurrentTarget(invocation, fixture.Registry.Require(new("PositionFixture.esm", 0x701))));
            reads.Add(fixture.Registry.ReadCurrentTarget(invocation, fixture.Registry.Require(new("PositionFixture.esm", 0x702))));
            reads.Add(fixture.Registry.ReadCurrentTarget(invocation, fixture.Registry.Require(new("PositionFixture.esm", 0x703))));
            reads.Add(fixture.Registry.ReadCurrentTarget(invocation, fixture.Registry.Require(new("PositionFixture.esm", 0x704))));
            reads.Add(fixture.Registry.ReadCurrentTarget(invocation, fixture.Registry.Require(new("PositionFixture.esm", 0x705))));
        });
        Require(reads[0] is { Object: null, Values: null } && reads[1] is { Object: not null, Values.Kind: 2, Values.Payload: 42, Values.DistanceBits: 0x40400000 } &&
            reads[2] is { Values.Kind: 2, Values.Payload: 77, Values.Count: 9, Values.DistanceBits: 0 } &&
            reads[3].Values == reads[2].Values && reads[4] is { Values.Payload: 42, Values.Count: 0, Values.DistanceBits: 0 } &&
            reads[5] is { Object: not null, Values.Kind: 0, Values.Payload: 0, Values.Count: 0, Values.DistanceBits: 0 },
            "Mutable PACK target did not retain real null construction and PTDT target object fields.");
        Require(saved.Packages.Single(package => package.Form.ObjectId == 0x703).Target!.Loads.Select(load => load.SourceExtent).SequenceEqual([16, 12]),
            "Repeated target loads lost actual short extents or reused the previous tail instead of source zero-buffer initialization.");
        var full = saved.Packages.Single(package => package.Form.ObjectId == 0x701).Target!;
        Reject(() => FalloutPackageRuntimeTargetFields.Validate(full with { Loads = [full.Loads[0] with { SourceExtent = 12 }] }));
        using (var oversized = fixture.Reference.ReadOversizedTargetRecordFixture())
            Reject(() => { using var unreturned = new FalloutPackageRuntimeRegistry(oversized,
                fixture.Source.Command.Fields.Controls.Main.EngineSha256, fixture.Reference.Stack); });
        var originalLoads = JsonSerializer.Serialize(saved.Packages.Select(package => package.Target!.Loads));
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            using var cold = new FalloutPackageRuntimeRegistry(fixture.Reference.Records, fixture.Source.Command.Fields.Controls.Main.EngineSha256,
                fixture.Reference.Stack, saved);
            var current = cold.Capture(); FalloutPackageRuntimeRegistry.Validate(current, cold.Executable, cold.Stack);
            FalloutPackageRuntimeRegistry.ValidateTargetReads(reads, current);
            Reject(() => FalloutPackageRuntimeRegistry.ValidateTargetRead(reads[1] with { Registry = current.Factory }, current));
            fixture.InCommand(invocation => FalloutPackageRuntimeRegistry.ValidateTargetReads(
                [cold.ReadCurrentTarget(invocation, cold.Require(new("PositionFixture.esm", 0x701)))], current));
            Require(JsonSerializer.Serialize(current.Packages.Select(package => package.Target!.Loads)) == originalLoads &&
                current.Packages.Single(package => package.Form.ObjectId == 0x701).Target!.Object != saved.Packages.Single(package => package.Form.ObjectId == 0x701).Target!.Object,
                "Cold target replayed original source loads or reused the prior actual managed object.");
            saved = current;
        }
        fixture.InCommand(invocation => fixture.Registry.ObserveUnownedTarget(invocation, fixture.Registry.Require(fixture.Package), "authored-native-target-writer"));
        fixture.InCommand(invocation => Reject(() => fixture.Registry.ReadCurrentTarget(invocation, fixture.Registry.Require(fixture.Package))));
        FalloutPackageRuntimeRegistry.Validate(fixture.Registry.Capture(), fixture.Registry.Executable, fixture.Reference.Stack);
    }

    private static byte[] ActorAssignmentFixtureRows()
    {
        byte[] Row(string signature, uint form, params byte[][] fields)
        { var data = fields.SelectMany(value => value).ToArray(); var result = new byte[24 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)data.Length); BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), form);
            data.CopyTo(result, 24); return result; }
        byte[] Part(string signature, byte[] data)
        { var result = new byte[6 + data.Length]; Encoding.ASCII.GetBytes(signature).CopyTo(result, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), (ushort)data.Length); data.CopyTo(result, 6); return result; }
        var kind = new byte[12]; kind[4] = 6;
        var target = new byte[16]; target[0] = 2; BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(4), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(8), 7); BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(12), 0x40400000);
        var shorter = target[..12]; BinaryPrimitives.WriteUInt32LittleEndian(shorter.AsSpan(4), 77);
        BinaryPrimitives.WriteUInt32LittleEndian(shorter.AsSpan(8), 9);
        return new[] { Row("PACK", 0x700, Part("PKDT", kind)), Row("PACK", 0x701, Part("PKDT", kind), Part("PTDT", target)),
            Row("PACK", 0x702, Part("PKDT", kind), Part("PTDT", shorter)),
            Row("PACK", 0x703, Part("PKDT", kind), Part("PTDT", target), Part("PTDT", shorter)),
            Row("PACK", 0x704, Part("PKDT", kind), Part("PTDT", target[..8])),
            Row("PACK", 0x705, Part("PKDT", kind), Part("PTDT", [])) }.SelectMany(row => row).ToArray();
    }

    private sealed class ActorAssignmentFixture : IFalloutActorPackageAssignmentConsumers, IDisposable
    {
        internal readonly ReferencePositionFixture Reference = new();
        internal readonly FalloutPackageRuntimeRegistry Registry;
        internal readonly FalloutSourceDataHandlerLoadingState Loading;
        internal readonly FalloutFormKey Package = new("PositionFixture.esm", 0x700);
        internal FalloutPackageCommandFieldInvocation? Invocation;
        internal int Hours;
        internal bool SwallowCaptureReentry;
        internal readonly uint HourBits = BitConverter.SingleToUInt32Bits(19.25f);
        private readonly Guid _runtime = Guid.NewGuid();
        private FalloutDataHandlerLoadingInvocation? _loading;
        private bool _entered;
        public FalloutActorPackageAssignmentSource Source { get; }
        public string Stack => Reference.Stack;
        internal ActorAssignmentFixture()
        {
            Source = FalloutActorPackageAssignmentSource.Reviewed(Reference.Fields.Source);
            Registry = new(Reference.Records, Source.Command.Fields.Controls.Main.EngineSha256, Stack);
            Reference.Common.BindSourcePackageRegistry(Registry);
            Reference.Common.BindActorPackageAssignment(Source, Reference.Actor.Reference, 1, actual =>
            { Require(actual.Actor == Reference.Actor && actual.Epoch == 1, "Wrong authored original Common constructor."); });
            Reference.Call(FalloutReferencePositionIngress.ActorPosition,
                current => Reference.Position.InitializeConstructedPlayerFields(current, AnimationSettings("", "")));
            Loading = new(Source, Stack, _runtime, Guid.NewGuid(), actual =>
            { if (actual != _loading) throw new InvalidOperationException("Authored loading owner left its current source scope."); });
        }
        internal void InCommand(Action<FalloutPackageCommandFieldInvocation> operation)
        {
            var process = Reference.Common.ReadSceneQueries(Reference.Actor.Reference, 1);
            Invocation = new(Guid.NewGuid(), Reference.Actor, process.Process, 1, Environment.CurrentManagedThreadId);
            _entered = true;
            try { Reference.InActorAssignmentFixtureCommand(Invocation, () => operation(Invocation)); } finally { _entered = false; }
        }
        internal void Execute(bool expectFailure)
        {
            InCommand(invocation =>
            {
                if (expectFailure) Reject(() => Reference.Common.ExecuteActorPackageAssignment(invocation, Package, false, false, this, _ => { }));
                else _ = Reference.Common.ExecuteActorPackageAssignment(invocation, Package, false, false, this, _ => { });
            });
        }
        public void RequireCurrent(FalloutPackageCommandFieldInvocation invocation)
        {
            if (invocation != Invocation || !_entered) throw new InvalidOperationException("Authored Actor graph lost its actual package command.");
            if (SwallowCaptureReentry) { try { _ = Reference.Common.Capture(); } catch (InvalidOperationException) { } }
        }
        public FalloutProcessSceneQuery? ReadCurrentProcess(FalloutPackageCommandFieldInvocation invocation) => Reference.Common.ReadSceneQueriesForPackageCommand(invocation);
        public int EnsureCurrentProcedure(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package)
        {
            var actual = Registry.Require(package); var value = FalloutPackageAssignmentSource.PureProcedure(actual.Kind)!.Value;
            Registry.PublishProcedure(invocation, actual, value, "authored-selected-pure-procedure"); return value;
        }
        public FalloutReferenceActorYawModeRead ReadAssignmentStatus(FalloutPackageCommandFieldInvocation invocation) =>
            Reference.Call(FalloutReferencePositionIngress.PackageCommand, Reference.Position.ReadActorYawMode);
        public FalloutReferenceActorAssignmentByteRead ReadAssignmentByte(FalloutPackageCommandFieldInvocation invocation) =>
            Reference.Call(FalloutReferencePositionIngress.PackageCommand, Reference.Position.ReadActorAssignmentByte);
        public FalloutReferencePositionCall StoreAssignmentByteZero(FalloutPackageCommandFieldInvocation invocation) =>
            Reference.Call(FalloutReferencePositionIngress.PackageCommand, Reference.Position.StoreActorAssignmentByteZero);
        public FalloutActorPackageExtraRead DeriveCurrentPackageExtra(FalloutPackageCommandFieldInvocation invocation, FalloutFormKey package)
        {
            var target = Registry.ReadCurrentTarget(invocation, Registry.Require(package));
            return new(Source.Identity, package, Registry.Require(package).RecordSha256, target.Values?.Kind, null, true, TargetRead: target);
        }
        public FalloutDataHandlerLoadingRead ReadDataHandlerLoading(FalloutPackageCommandFieldInvocation invocation)
        {
            _loading = new(Guid.NewGuid(), _runtime, invocation.Process, invocation.Actor, invocation.Epoch, invocation.ThreadId,
                FalloutDataHandlerLoadingIngress.PackageCleanup, invocation.SourceCommand);
            try { return Loading.Read(_loading); } finally { _loading = null; }
        }
        public FalloutActorAssignmentBaseRead ReadCurrentBaseForMover(FalloutPackageCommandFieldInvocation invocation) =>
            throw new NotSupportedException("authored-current-Base-and-mover-following-boundary");
        public void ResetActualMover(FalloutPackageCommandFieldInvocation invocation, FalloutActorMoverResetJoin join) => throw new InvalidOperationException("Unowned current Base cannot enter mover.");
        public FalloutActorFollowerExtraRead ReadFollowerExtra(FalloutPackageCommandFieldInvocation invocation) => throw new NotSupportedException("authored-follower-current-membership-unowned");
        public FalloutPackageRuntimeObject? ReadPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation) => null;
        public void ClearPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject expected) => throw new InvalidOperationException("No actual special package.");
        public FalloutPackageGameHourRead ReadGameHour(FalloutPackageCommandFieldInvocation invocation)
        { Hours++; return new(new("PositionFixture.esm", 0x38), HourBits, "authored-actual-shared-global-GameHour"); }
        public Guid ConstructContext(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package) => throw new InvalidOperationException("Kind6 does not allocate a context.");
        public void DestroyContext(FalloutPackageCommandFieldInvocation invocation, Guid context) => throw new InvalidOperationException("Actual context is null.");
        public void DeleteTemporaryPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package) => throw new InvalidOperationException("No temporary package.");
        public void RequireExtra(FalloutPackageCommandFieldInvocation invocation, FalloutPackageExtraReference extra) => throw new InvalidOperationException("Actual target is null.");
        public void Dispose() { Registry.Dispose(); Reference.Dispose(); }
    }

    private sealed partial class ReferencePositionFixture
    {
        internal FalloutPluginStack ReadOversizedTargetRecordFixture()
        {
            const string plugin = "OversizedTargetFixture.esm";
            var kind = new byte[12]; kind[4] = 6;
            File.WriteAllBytes(Path.Combine(_directory, plugin), Join(Row("TES4", 0, Part("HEDR", new byte[12])),
                Row("PACK", 0x700, Part("PKDT", kind), Part("PTDT", new byte[17]))));
            return FalloutPluginStack.Load(_directory, [plugin]);
        }
        internal void InActorAssignmentFixtureCommand(FalloutPackageCommandFieldInvocation invocation, Action operation)
        {
            if (_package is not null) throw new InvalidOperationException("Authored Actor command reentered another package scope.");
            _package = invocation;
            try { operation(); } finally { _package = null; }
        }
    }
}
