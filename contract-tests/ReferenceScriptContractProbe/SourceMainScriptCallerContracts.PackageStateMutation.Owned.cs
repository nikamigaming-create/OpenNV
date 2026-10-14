using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static object AuditOwnedPackageState(FalloutPluginStack records, FalloutActorProcessCommonState originalCommon,
        FalloutPlayerMovementControlSource controls)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("Package state audit lost its selected original source.");
        var source = FalloutExecutableStringTable.ReadPackageState(owned.FalloutExecutablePath, controls);
        var before = SHA256.HashData(File.ReadAllBytes(owned.FalloutExecutablePath));
        var inputs = records.EffectiveRecords("PACK").Select(record => (Record: record, Data: FalloutPackageData.Read(record))).ToArray();
        var hashes = inputs.ToDictionary(row => row.Record.FormKey, row => SHA256.HashData(row.Record.ReadData()), FalloutFormKeyComparer.Instance);
        var selected = inputs.FirstOrDefault(row => row.Data.Procedure == 6 && (row.Data.Flags & FalloutPackageRuntimeRegistry.TemporaryFlag) == 0 &&
            !row.Record.ReadSubrecords().Any(field => field.Signature == "PTDT"));
        if (selected.Record is null) throw new NotSupportedException("Owned package audit has no genuine target-null default-context input.");
        var player = records.RuntimeFormKey(0x14);
        var original = originalCommon.ReadSceneQueries(player, 1);
        var declaration = FalloutActorProcessRuntimeDeclaration.ForExecutable(controls.Main.EngineSha256);
        using var common = new FalloutActorProcessCommonState(declaration, owned.StackId,
            actor => actor == player ? original.Actor : throw new InvalidDataException("Owned isolated package state has no other Actor constructor."),
            _ => throw new InvalidDataException("Package field setter queried unrelated gameplay pools."),
            _ => throw new InvalidDataException("Package field setter queried native/rendered body state."));
        common.Construct(player, 1, FalloutDetectionProcessLevel.High);
        using var registry = new FalloutPackageRuntimeRegistry(records, controls.Main.EngineSha256, owned.StackId);
        common.BindSourcePackageRegistry(registry);
        Require(registry.Capture().Packages.Count == inputs.Length && inputs.All(input => registry.Require(input.Record.FormKey) is { } package &&
            package.Kind == unchecked((sbyte)input.Data.Procedure) && package.SourceFlags == input.Data.Flags && package.Flags == input.Data.Flags &&
            package.Procedure == -1 && package.LifetimeCount == 0),
            "Owned registry omitted a winning PACK constructor or confused declaration kind with mutable procedure.");
        var globals = FalloutGlobalState.Read(records); var forms = FalloutGameTimeBindings.Read(records);
        var time = new FalloutGameTime(globals, forms, FalloutCalendar.Read(owned.FalloutExecutablePath));
        var services = new OwnedPackageServices(common, registry, time, forms.Hour);
        var call = services.Set(selected.Record.FormKey);
        var equal = services.Set(selected.Record.FormKey);
        var warm = RoundTripPackage(common.Capture()); FalloutActorProcessCommonState.Validate(warm);
        Require(call is { Step: FalloutPackageStateStep.Returned, RetainedCount: 1, After.Context: null, After.Extra: null, After.Index: 0 } &&
            call.Hour?.Global == forms.Hour && call.After.TimeBits == BitConverter.SingleToUInt32Bits(time.Hour) && equal.Equal && equal.Stores.Count == 0 && services.HourReads == 1,
            "Owned six-field setter did not consume its actual retained hour/default context or pointer-equal skip.");
        using var coldCommon = new FalloutActorProcessCommonState(declaration, owned.StackId,
            actor => actor == player ? original.Actor : throw new InvalidDataException("Cold owned package Actor changed."),
            _ => throw new InvalidDataException("Cold package fields replayed gameplay."), _ => throw new InvalidDataException("Cold package fields created a body."), warm);
        using var coldRegistry = new FalloutPackageRuntimeRegistry(records, controls.Main.EngineSha256, owned.StackId, warm.PackageRegistry);
        coldCommon.BindSourcePackageRegistry(coldRegistry);
        var cold = coldCommon.Capture(); FalloutActorProcessCommonState.Validate(cold);
        Require(cold.CapturedProcess != warm.CapturedProcess && coldRegistry.Factory != registry.Factory &&
            coldRegistry.Require(selected.Record.FormKey).LifetimeCount == 1 &&
            cold.Current.Single().Packages!.Current.Fields == warm.Current.Single().Packages!.Current.Fields &&
            hashes.All(pair => SHA256.HashData(records.GetEffective(pair.Key).ReadData()).AsSpan().SequenceEqual(pair.Value)) &&
            SHA256.HashData(File.ReadAllBytes(owned.FalloutExecutablePath)).AsSpan().SequenceEqual(before),
            "Owned cold objects replayed counts/time/source state or modified original files.");
        return new { source, winningPackages = inputs.Length, selected = selected.Record.FormKey, sourceReadOnly = true,
            actualMutableRegistry = true, actualCommonFields = call.After, commandHour = call.Hour,
            pointerEquality = equal.Equal, freshColdCount = coldRegistry.Require(selected.Record.FormKey).LifetimeCount,
            originalAddScriptPackage = "UNEXECUTED", campaign = "UNEXECUTED", rendering = "UNEXECUTED" };
    }

    private sealed class OwnedPackageServices(FalloutActorProcessCommonState common, FalloutPackageRuntimeRegistry registry,
        FalloutGameTime time, FalloutFormKey hour) : IFalloutPackageStateServices
    {
        private FalloutPackageCommandFieldInvocation? _entered;
        internal int HourReads;
        internal FalloutPackageStateMutation Set(FalloutFormKey package)
        {
            var actor = common.Capture().Current.Single(); var process = common.ReadSceneQueries(actor.Source.Reference, actor.Epoch);
            _entered = new(Guid.NewGuid(), actor.Source, process.Process, actor.Epoch, Environment.CurrentManagedThreadId);
            try { return common.SetSourcePackageState(_entered, FalloutProcessPackageSlot.Current, package, null, this); }
            finally { _entered = null; }
        }
        public void RequireCurrent(FalloutPackageCommandFieldInvocation invocation)
        {
            if (invocation != _entered) throw new InvalidOperationException("Owned field child lost its isolated actual invocation.");
            _ = common.ReadSceneQueriesForPackageCommand(invocation); registry.RequireCurrent();
        }
        public FalloutPackageGameHourRead ReadGameHour(FalloutPackageCommandFieldInvocation invocation)
        { ++HourReads; return new(hour, BitConverter.SingleToUInt32Bits(time.Hour), "actual-winning-retained-GameHour-component-read"); }
        public FalloutPackageRuntimeObject? ReadPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation) => throw Missing();
        public void ClearPlayerSpecialPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject expected) => throw Missing();
        public Guid ConstructContext(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package) => throw Missing();
        public void DestroyContext(FalloutPackageCommandFieldInvocation invocation, Guid context) => throw Missing();
        public void DeleteTemporaryPackage(FalloutPackageCommandFieldInvocation invocation, FalloutPackageRuntimeObject package) => throw Missing();
        public void RequireExtra(FalloutPackageCommandFieldInvocation invocation, FalloutPackageExtraReference extra) => throw Missing();
        private static NotSupportedException Missing() => new("Owned isolated six-field proof cannot enter an unowned special/context/target/deletion child.");
    }
}
