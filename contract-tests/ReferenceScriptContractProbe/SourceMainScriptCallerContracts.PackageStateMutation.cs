using System.Buffers.Binary;
using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPackageStateMutationContracts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennv-package-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "WeaponFixture.esm"), PackageStateFixturePlugin());
            using var records = FalloutPluginStack.Load(directory, ["WeaponFixture.esm"]);
            using var warm = new PackageMutationFixture(records);
            var current = warm.Common.ReadSourcePackageSlot(warm.Player, 1, FalloutProcessPackageSlot.Current);
            var primary = warm.Common.ReadSourcePackageSlot(warm.Player, 1, FalloutProcessPackageSlot.Primary);
            var first = warm.Set(FalloutProcessPackageSlot.Current, 0x100);
            Require(first.Step == FalloutPackageStateStep.Returned && first.After is { Context: null, Index: 0, Extra: null, Flags: 0 } &&
                first.After.TimeBits == warm.HourBits && warm.Registry.Require(Key(0x100)).LifetimeCount == 1 && warm.HourReads == 1 &&
                warm.Common.ReadSourcePackageSlot(warm.Player, 1, FalloutProcessPackageSlot.Primary).Fields.Fields == FalloutProcessPackageWords.Constructed,
                "Real Common current setter lost ordered state, command-time hour or slot independence.");
            var shared = warm.Set(FalloutProcessPackageSlot.Primary, 0x100);
            var registry = warm.Registry.Require(Key(0x100));
            Require(shared.RetainedCount == 2 && registry.LifetimeCount == 2 && warm.HourReads == 2,
                "Independent slots did not retain the same mutable winning PACK object.");
            warm.HourBits = BitConverter.SingleToUInt32Bits(19.5f);
            var same = warm.Set(FalloutProcessPackageSlot.Current, 0x100, new(Key(0x14), FalloutPackageExtraKind.Reference));
            Require(same.Equal && same.Stores.Count == 0 && same.After == first.After && registry.LifetimeCount == 2 && warm.HourReads == 2,
                "Pointer equality read time, adjusted a count or rewrote derived Extra.");
            var beforeClear = warm.Common.Capture();
            warm.Special = registry;
            var cleared = warm.Clear(FalloutProcessPackageSlot.Current);
            Require(cleared.Step == FalloutPackageStateStep.Returned && cleared.After == FalloutProcessPackageWords.Constructed &&
                cleared.ReleasedCount == 1 && warm.Special is null && warm.SpecialClears == 1 && registry.LifetimeCount == 1 &&
                cleared.Stores.Select(store => store.Step).SequenceEqual([FalloutPackageStateStep.PackagePointerClear,
                    FalloutPackageStateStep.TimeReset, FalloutPackageStateStep.IndexReset, FalloutPackageStateStep.FlagsReset, FalloutPackageStateStep.ExtraReset]),
                "Source clear omitted special-pointer/count effects or reordered the four reset stores.");
            var clearNull = warm.Clear(FalloutProcessPackageSlot.Current);
            Require(clearNull.Stores.Count == 4 && warm.SpecialReads == 1 && clearNull.ReleasedCount is null,
                "Constructor-null clear fabricated pointer stores or entered a skipped release child.");
            var saved = RoundTripPackage(warm.Common.Capture());
            FalloutActorProcessCommonState.Validate(saved);
            FalloutProcessPackageState.ValidateRead(current, warm.Executable, warm.Stack, saved);
            FalloutProcessPackageState.ValidateRead(primary, warm.Executable, warm.Stack, saved);
            var savedText = JsonSerializer.Serialize(saved);
            var historical = first;
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                using var cold = new PackageMutationFixture(records, saved);
                var restored = cold.Common.Capture();
                FalloutActorProcessCommonState.Validate(restored);
                Require(cold.HourReads == 0 && cold.SpecialReads == 0 && cold.Registry.Factory != saved.PackageRegistry!.Factory &&
                    cold.Registry.Require(Key(0x100)).LifetimeCount == 1 && cold.Registry.Require(Key(0x100)).Procedure == -1 &&
                    restored.Current.Single().Packages!.Primary!.Fields.TimeBits == shared.After.TimeBits &&
                    restored.Current.Single().Packages!.Current.Mutations!.Any(call => FalloutProcessPackageState.MutationEquivalent(call, historical)),
                    "Cold publication replayed effects/clock/count or lost older exact field history.");
                FalloutProcessPackageState.ValidateRead(current, warm.Executable, warm.Stack, restored);
                Require(JsonSerializer.Serialize(saved) == savedText, "Fresh cold objects mutated their saved source input.");
                saved = RoundTripPackage(restored); savedText = JsonSerializer.Serialize(saved);
            }
            Reject(() => FalloutActorProcessCommonState.Validate(beforeClear with
            { PackageRegistry = beforeClear.PackageRegistry! with { Packages = beforeClear.PackageRegistry!.Packages.Select(item => item.Form == Key(0x100) ? item with { LifetimeCount = 7 } : item).ToArray() } }));
            Reject(() => FalloutActorProcessCommonState.Validate(beforeClear with
            { PackageRegistry = beforeClear.PackageRegistry! with { Packages = beforeClear.PackageRegistry!.Packages.Select(item => item.Form == Key(0x101) ? item with { Flags = 1 } : item).ToArray() } }));
            Reject(() => FalloutActorProcessCommonState.Validate(beforeClear with
            { PackageRegistry = beforeClear.PackageRegistry! with { Packages = beforeClear.PackageRegistry!.Packages.Select(item => item.Form == Key(0x100) ? item with { SourceFlags = 1 } : item).ToArray() } }));
            var mutation = beforeClear.Current.Single().Packages!.Current.Mutations![0];
            RejectMutation(beforeClear, mutation with { Invocation = mutation.Invocation with { Actor = mutation.Invocation.Actor with { Base = Key(8) } } });
            RejectMutation(beforeClear, mutation with { Hour = mutation.Hour! with { Bits = 0 } });
            RejectMutation(beforeClear, mutation with { RetainedCount = 7 });
            RejectMutation(beforeClear, mutation with { Stores = mutation.Stores.Reverse().ToArray() });
            FailedClock(records); FailedContext(records); FailedTemporary(records); ObservedWriter(records); UnknownSharedCount(records); ReentryAndCaller(records);
            SignedDefaults(records); TwoActorFields(records); ActualIdlePackageGetter(records); RetainedPackageObservation(records);
            Console.WriteLine("OPENNV_PACKAGE_STATE_MUTATION_PASS actualCommon=true sharedRegistry=true typedExtra=true pointerEqualSkip=true " +
                "atomicCounts=true orderedStores=true failedPrefixes=true coldCycles=3 noEffectReplay=true authoredTwoActors=true originalAddScriptPackage=UNEXECUTED");
        }
        finally { File.Delete(Path.Combine(directory, "WeaponFixture.esm")); Directory.Delete(directory); }

        static FalloutFormKey Key(uint id) => new("WeaponFixture.esm", id);
    }

    private static void RetainedPackageObservation(FalloutPluginStack records)
    {
        using var fixture = new PackageMutationFixture(records);
        fixture.Set(FalloutProcessPackageSlot.Current, 0x100);
        var before = fixture.Common.ObserveSourcePackageFields(fixture.Player, 1);
        fixture.Common.RetainUnownedMutation(fixture.Player, "authored-actual-Common-child-failure");
        Reject(() => fixture.Common.ReadSourcePackageSlot(fixture.Player, 1, FalloutProcessPackageSlot.Current));
        var after = fixture.Common.ObserveSourcePackageFields(fixture.Player, 1);
        Require(after.Current == before.Current && after.Primary == before.Primary &&
            after.CurrentAvailable == before.CurrentAvailable && after.PrimaryAvailable == before.PrimaryAvailable &&
            after.Process == before.Process && after.Actor == before.Actor && after.Changed > before.Changed &&
            after.Failure == "authored-actual-Common-child-failure",
            "A failed process lost its exact diagnostic fields or allowed a fresh gameplay getter.");
    }

    private static void FailedClock(FalloutPluginStack records)
    {
        using var failed = new PackageMutationFixture(records); failed.FailHour = true;
        Reject(() => failed.Set(FalloutProcessPackageSlot.Current, 0x100));
        var saved = RoundTripPackage(failed.Common.Capture()); FalloutActorProcessCommonState.Validate(saved);
        var fields = saved.Current.Single().Packages!.Current;
        Require(fields is { Available: false, Fields.Package: null } && fields.Fields.TimeBits == FalloutPlayerFollowingContinuationSource.PackageUnsetTimeBits &&
            fields.Mutations![^1] is { FailedAt: FalloutPackageStateStep.GameHour, RetainedCount: 1 } &&
            failed.Registry.Require(new("WeaponFixture.esm", 0x100)).LifetimeCount == 1 && failed.Common.SaveBlocker is not null,
            "Failed hour rolled back the already committed retain or manufactured a package pointer.");
        using var cold = new PackageMutationFixture(records, saved);
        Require(cold.Registry.Require(new("WeaponFixture.esm", 0x100)).LifetimeCount == 1 && cold.HourReads == 0,
            "Cold failed prefix inferred count from null slot or re-read time.");
        Reject(() => cold.Set(FalloutProcessPackageSlot.Current, 0x100));
        var observation = failed.Common.ObserveSourcePackageFields(failed.Player, 1);
        Require(observation.Failure is not null && observation.Current == fields.Fields &&
            observation.Process == fields.Publication.Process && observation.Actor.Reference == failed.Player,
            "Diagnostic package observation changed or erased the actual failed committed fields.");
        Reject(() => failed.Common.ReadSourcePackageSlot(failed.Player, 1, FalloutProcessPackageSlot.Current));
        Reject(() => failed.Common.ObserveSourcePackageFields(failed.Player, 2));
    }
    private static void FailedContext(FalloutPluginStack records)
    {
        using var failed = new PackageMutationFixture(records);
        Reject(() => failed.Set(FalloutProcessPackageSlot.Current, 0x102));
        var saved = failed.Common.Capture(); FalloutActorProcessCommonState.Validate(saved);
        var fields = saved.Current.Single().Packages!.Current;
        Require(fields is { Available: false, Fields.Context: null, Fields.Index: -1 } && fields.Fields.Package == new FalloutFormKey("WeaponFixture.esm", 0x102) &&
            fields.Fields.TimeBits == failed.HourBits && fields.Mutations![^1].FailedAt == FalloutPackageStateStep.ContextFactory && failed.ContextCalls == 1,
            "Unowned context allocation bypassed its real child or discarded time/package stores.");
    }
    private static void FailedTemporary(FalloutPluginStack records)
    {
        using var failed = new PackageMutationFixture(records); failed.Set(FalloutProcessPackageSlot.Current, 0x103);
        Reject(() => failed.Clear(FalloutProcessPackageSlot.Current));
        var saved = failed.Common.Capture(); FalloutActorProcessCommonState.Validate(saved);
        var call = saved.Current.Single().Packages!.Current.Mutations![^1];
        Require(call is { FailedAt: FalloutPackageStateStep.TemporaryDeletion, ReleasedCount: 0, After.Package: null, After.Index: 0 } &&
            call.Stores.Count == 1 && call.Stores[0].Step == FalloutPackageStateStep.TemporaryPackagePointerClear && failed.DeleteCalls == 1 &&
            failed.Registry.Require(new("WeaponFixture.esm", 0x103)).LifetimeCount == 0,
            "Temporary deletion failure rolled back its prior pointer clear/count or performed later resets.");
    }
    private static void ObservedWriter(FalloutPluginStack records)
    {
        using var fixture = new PackageMutationFixture(records);
        fixture.Common.ObserveSourcePackageAssignment(fixture.Player, 1, Guid.NewGuid(), "authored-entered-unowned-writer", null,
            new("WeaponFixture.esm", 0x100), false, FalloutProcessPackageWriterTarget.Current);
        var saved = JsonSerializer.Serialize(fixture.Common.Capture());
        Reject(() => fixture.Set(FalloutProcessPackageSlot.Current, 0x100));
        Require(JsonSerializer.Serialize(fixture.Common.Capture()) == saved && fixture.HourReads == 0,
            "Known setter repaired an unavailable old pointer/counter state.");
        var invocation = fixture.Invocation(); var package = fixture.Registry.Require(new("WeaponFixture.esm", 0x100));
        fixture.Registry.RetainUnownedWriter(invocation, package, true, "authored-actual-procedure-writer-unbound");
        Reject(() => fixture.Registry.PublishProcedure(invocation, package, 0, "authored-calculation"));
        FalloutActorProcessCommonState.Validate(fixture.Common.Capture());
    }
    private static void ReentryAndCaller(FalloutPluginStack records)
    {
        using var fixture = new PackageMutationFixture(records);
        var before = JsonSerializer.Serialize(fixture.Common.Capture()); var invocation = fixture.Invocation();
        Reject(() => fixture.Common.SetSourcePackageState(invocation with { Process = Guid.NewGuid() }, FalloutProcessPackageSlot.Current,
            new("WeaponFixture.esm", 0x100), null, fixture));
        Task.Run(() => Reject(() => fixture.Common.SetSourcePackageState(invocation, FalloutProcessPackageSlot.Current,
            new("WeaponFixture.esm", 0x100), null, fixture))).GetAwaiter().GetResult();
        Require(JsonSerializer.Serialize(fixture.Common.Capture()) == before, "Foreign caller wrote source package fields/count.");
        fixture.DuringHour = () => { Reject(() => fixture.Common.ReadSourcePackageSlot(fixture.Player, 1, FalloutProcessPackageSlot.Current)); };
        Reject(() => fixture.Set(FalloutProcessPackageSlot.Current, 0x100));
        var retained = fixture.Common.Capture(); FalloutActorProcessCommonState.Validate(retained);
        Require(retained.Current.Single().Packages!.Current.Mutations![^1].FailedAt == FalloutPackageStateStep.GameHour &&
            fixture.Registry.Require(new("WeaponFixture.esm", 0x100)).LifetimeCount == 1,
            "Caught callback reentry returned a fake hour or lost its earlier retain.");
    }
    private static void UnknownSharedCount(FalloutPluginStack records)
    {
        using var fixture = new PackageMutationFixture(records);
        fixture.Common.ObserveSourcePackageAssignment(fixture.Player, 1, Guid.NewGuid(), "authored-unknown-current-assignment", null,
            new("WeaponFixture.esm", 0x100), false, FalloutProcessPackageWriterTarget.Current);
        var local = fixture.Common.Capture(); FalloutActorProcessCommonState.Validate(local);
        Require(local.PackageRegistry!.CountAuthorityAvailable && !fixture.Registry.Require(new("WeaponFixture.esm", 0x100)).CountAvailable &&
            fixture.Registry.Require(new("WeaponFixture.esm", 0x101)).CountAvailable,
            "Known requested count loss altered procedure/flags or hid unrelated count availability.");
        Reject(() => fixture.Set(FalloutProcessPackageSlot.Primary, 0x100));
        var failed = fixture.Common.Capture(); FalloutActorProcessCommonState.Validate(failed);
        Require(failed.Current.Single().Packages!.Primary!.Mutations![^1] is { FailedAt: FalloutPackageStateStep.Retain, RetainedCount: null } && fixture.HourReads == 0,
            "Another slot retained against an unavailable shared package count.");
        fixture.Common.ObserveSourcePackageAssignment(fixture.Player, 1, Guid.NewGuid(), "authored-second-unknown-pointer-assignment", null,
            new("WeaponFixture.esm", 0x101), false, FalloutProcessPackageWriterTarget.Current);
        var global = fixture.Common.Capture(); FalloutActorProcessCommonState.Validate(global);
        Require(!global.PackageRegistry!.CountAuthorityAvailable && global.PackageRegistry.CountAuthorityObservations.Count == 1 &&
            fixture.Registry.Require(new("WeaponFixture.esm", 0x104)).CountAvailable,
            "Unknown old pointer scanned/mutated unrelated objects or left global count authority usable.");
        Reject(() => fixture.Registry.Retain(fixture.Invocation(), fixture.Registry.Require(new("WeaponFixture.esm", 0x104))));
        using var cold = new PackageMutationFixture(records, RoundTripPackage(global));
        Require(!cold.Registry.Capture().CountAuthorityAvailable && cold.Registry.SaveBlocker is not null,
            "Fresh cold registry repaired unknown counter authority.");
        Reject(() => FalloutActorProcessCommonState.Validate(global with
        {
            PackageRegistry = global.PackageRegistry with
            { CountAuthorityAvailable = true, CountAuthorityObservations = [] }
        }));
        using var unbound = new WeaponContinuationFixture();
        unbound.Common.ObserveSourcePackageAssignment(unbound.Player, 1, Guid.NewGuid(), "authored-prebind-unowned-assignment", null,
            new("WeaponFixture.esm", 0x100), false, FalloutProcessPackageWriterTarget.Current);
        using var fresh = new FalloutPackageRuntimeRegistry(records, fixture.Executable, fixture.Stack);
        Reject(() => unbound.Common.BindSourcePackageRegistry(fresh));
    }
    private static void SignedDefaults(FalloutPluginStack records)
    {
        foreach (var id in new uint[] { 0x100, 0x101, 0x104 })
        {
            using var fixture = new PackageMutationFixture(records);
            var extra = new FalloutPackageExtraReference(new("WeaponFixture.esm", 0x14), FalloutPackageExtraKind.Reference);
            var call = fixture.Set(FalloutProcessPackageSlot.Current, id, extra);
            Require(call.After.Context is null && call.After.Extra == extra && fixture.ContextCalls == 0 && fixture.ExtraReads == 1,
                "Default signed kind acquired a fabricated context or lost its typed Extra.");
            FalloutActorProcessCommonState.Validate(fixture.Common.Capture());
        }
    }
    private static void TwoActorFields(FalloutPluginStack records)
    {
        using var fixture = new PackageMutationFixture(records);
        fixture.Set(FalloutProcessPackageSlot.Current, 0x100);
        var actor = new FalloutCombatActorIdentity(new("WeaponFixture.esm", 0x200), new("WeaponFixture.esm", 0x201), "ACHR", 0,
            new('7', 64), "NPC_", 0, new('8', 64), false);
        var process = new FalloutProcessSceneQuery(Guid.NewGuid(), fixture.Stack, fixture.Executable, actor, 2, FalloutDetectionProcessLevel.High,
            Guid.NewGuid(), 1, null, null);
        var fields = FalloutProcessPackageState.Construct(fixture.Executable, process.Process, 2, FalloutDetectionProcessLevel.High,
            FalloutProcessPackageConstructionKind.IndependentProcessConstructor).Current;
        long sequence = 1; var invocation = new FalloutPackageCommandFieldInvocation(Guid.NewGuid(), actor, process.Process, 2, Environment.CurrentManagedThreadId);
        var call = FalloutProcessPackageState.Mutate(invocation, process, fields, FalloutPackageStateOperation.Set, fixture.Registry,
            fixture.Registry.Require(new("WeaponFixture.esm", 0x100)), null, new IndependentPackageServices(), () => ++sequence,
            (words, receipt) => fields = fields with
            {
                Fields = words,
                Available = receipt.Step == FalloutPackageStateStep.Returned,
                Changed = receipt.Changed,
                Mutations = [receipt]
            });
        Require(call.Step == FalloutPackageStateStep.Returned && fields.Available && fields.Fields == call.After && call.RetainedCount == 2 &&
            fixture.Registry.Require(new("WeaponFixture.esm", 0x100)).LifetimeCount == 2,
            "Two authored independent Actor field owners acquired duplicate package counts.");
        FalloutPackageRuntimeRegistry.Validate(fixture.Registry.Capture(), fixture.Executable, fixture.Stack);
    }

    private static void RejectMutation(FalloutProcessCommonSnapshot saved, FalloutPackageStateMutation call)
    {
        var entry = saved.Current.Single(); var slots = entry.Packages!; var fields = slots.Current;
        Reject(() => FalloutActorProcessCommonState.Validate(saved with
        {
            Current = [entry with
        { Packages = slots with { Current = fields with { Mutations = fields.Mutations!.Select(item => item.Entered == call.Entered ? call : item).ToArray() } } }]
        }));
    }
    private static T RoundTripPackage<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    private sealed class PackageMutationFixture : IFalloutPackageStateServices, IDisposable
    {
        private readonly WeaponContinuationFixture _actual;
        internal FalloutActorProcessCommonState Common => _actual.Common;
        internal FalloutFormKey Player => _actual.Player;
        internal string Stack => _actual.Stack;
        internal string Executable => _actual.Source.Controls.Main.EngineSha256;
        internal readonly FalloutPackageRuntimeRegistry Registry;
        private FalloutPackageCommandFieldInvocation? _entered;
        internal uint HourBits = BitConverter.SingleToUInt32Bits(8.25f);
        internal int HourReads, SpecialReads, SpecialClears, ContextCalls, DeleteCalls, ExtraReads;
        internal bool FailHour;
        internal Action? DuringHour;
        internal FalloutPackageRuntimeObject? Special;
        internal PackageMutationFixture(FalloutPluginStack records, FalloutProcessCommonSnapshot? saved = null)
        {
            _actual = new(saved); Registry = new(records, Executable, Stack, saved?.PackageRegistry);
            Common.BindSourcePackageRegistry(Registry);
        }
        internal FalloutPackageCommandFieldInvocation Invocation()
        {
            var process = Common.ReadSceneQueries(Player, 1);
            return new(Guid.NewGuid(), process.Actor, process.Process, 1, Environment.CurrentManagedThreadId);
        }
        internal FalloutPackageStateMutation Set(FalloutProcessPackageSlot slot, uint? id, FalloutPackageExtraReference? extra = null) =>
            Enter(invocation => Common.SetSourcePackageState(invocation, slot, id is { } value ? new("WeaponFixture.esm", value) : null, extra, this));
        internal FalloutPackageStateMutation Clear(FalloutProcessPackageSlot slot) => Enter(invocation => Common.ClearSourcePackageState(invocation, slot, this));
        private FalloutPackageStateMutation Enter(Func<FalloutPackageCommandFieldInvocation, FalloutPackageStateMutation> call)
        {
            _entered = Invocation();
            try { return call(_entered); } finally { _entered = null; }
        }
        public void RequireCurrent(FalloutPackageCommandFieldInvocation invocation)
        {
            if (invocation != _entered) throw new InvalidOperationException("Authored package child lost its entered command.");
            _ = Common.ReadSceneQueriesForPackageCommand(invocation); Registry.RequireCurrent();
        }
        public FalloutPackageRuntimeObject? ReadPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation) { ++SpecialReads; return Special; }
        public void ClearPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject expected)
        { if (!ReferenceEquals(Special, expected)) throw new InvalidOperationException("Actual special pointer changed before clear."); ++SpecialClears; Special = null; }
        public FalloutPackageGameHourRead ReadGameHour(FalloutPackageCommandFieldInvocation invocation)
        {
            ++HourReads; DuringHour?.Invoke(); if (FailHour) throw new NotSupportedException("authored-retained-GameHour-unavailable");
            return new(new("WeaponFixture.esm", 0x38), HourBits, "authored-current-retained-GameHour");
        }
        public Guid ConstructContext(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package)
        { ++ContextCalls; throw new NotSupportedException("authored-actual-context-constructor-unowned"); }
        public void DestroyContext(FalloutPackageCommandFieldInvocation invocation, Guid context) => throw new NotSupportedException("authored-context-destructor-unowned");
        public void DeleteTemporaryPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package)
        { ++DeleteCalls; throw new NotSupportedException("authored-temporary-package-deletion-unowned"); }
        public void RequireExtra(FalloutPackageCommandFieldInvocation invocation, FalloutPackageExtraReference extra) { extra.Validate(); ++ExtraReads; }
        public void Dispose() { _actual.Dispose(); Registry.Dispose(); }
    }
    private sealed class IndependentPackageServices : IFalloutPackageStateServices
    {
        public void RequireCurrent(FalloutPackageCommandFieldInvocation invocation) => FalloutPackageRuntimeRegistry.RequireInvocation(invocation);
        public FalloutPackageRuntimeObject? ReadPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation) => throw new InvalidDataException("New field owner entered skipped release.");
        public void ClearPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject expected) => throw new InvalidDataException("New field owner entered skipped special clear.");
        public FalloutPackageGameHourRead ReadGameHour(FalloutPackageCommandFieldInvocation invocation) => new(null, FalloutPackageRuntimeRegistry.NullGameHourBits, "authored-genuine-null-hour-pointer");
        public Guid ConstructContext(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package) => throw new InvalidDataException("Default context allocated.");
        public void DestroyContext(FalloutPackageCommandFieldInvocation invocation, Guid context) => throw new InvalidDataException("Null context destroyed.");
        public void DeleteTemporaryPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package) => throw new InvalidDataException("New package deleted.");
        public void RequireExtra(FalloutPackageCommandFieldInvocation invocation, FalloutPackageExtraReference extra) => extra.Validate();
    }
    private static byte[] PackageStateFixturePlugin()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        var header = new byte[18]; "HEDR"u8.CopyTo(header); BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 12);
        Record("TES4", 0, header);
        foreach (var (id, kind, flags) in new (uint, byte, uint)[] { (0x100, 6, 0x1004), (0x101, 255, 0), (0x102, 2, 0), (0x103, 6, 0x800), (0x104, 17, 0), (0x105, 6, 0) })
        {
            var data = new byte[14]; "PKDT"u8.CopyTo(data); BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 8);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(6), flags); data[10] = kind;
            if (id == 0x105)
            {
                // An explicit empty collection has a nonnull source pointer.
                var collection = new byte[7]; "IDLC"u8.CopyTo(collection); collection[4] = 1;
                data = data.Concat(collection).ToArray();
            }
            Record("PACK", id, data);
        }
        return stream.ToArray();
        void Record(string name, uint id, byte[] body)
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes(name)); writer.Write(body.Length); writer.Write(0u); writer.Write(id);
            writer.Write(0u); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(body);
        }
    }
}
