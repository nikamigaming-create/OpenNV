using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static void ActualIdlePackageGetter(FalloutPluginStack records)
    {
        using var warm = new PackageMutationFixture(records);
        warm.Set(FalloutProcessPackageSlot.Current, 0x105);
        var package = warm.Registry.Require(new("WeaponFixture.esm", 0x105));
        warm.Registry.PublishProcedure(warm.Invocation(), package, 0x1c, "authored-independent-runtime-procedure");
        var calls = new List<FalloutProcessPackageSlot>();
        var current = Execute(warm);
        Require(current is { Result: { Kind: 6, HasExtra: true, Source: { Slot: FalloutProcessPackageSlot.Current } } } &&
            current.ProcessPackage!.Fields.Fields.Package == package.Form && current.RegistryFactory == warm.Registry.Factory &&
            current.Factory != warm.Registry.Factory && current.Result.Source!.Factory == current.Factory &&
            calls.SequenceEqual([FalloutProcessPackageSlot.Primary, FalloutProcessPackageSlot.Current]) && package.Procedure == 0x1c,
            "Current idle selection rejected a real package, aliased its registry/slot factories or read mutable procedure as PKDT kind.");
        var captured = warm.Common.Capture();
        FalloutPackageRuntimeRegistry.ValidateIdlePackageRead(current, captured, records);
        var currentObject = current.Result!; var source = currentObject.Source!;
        Reject(() => warm.Registry.RequireObject(new FalloutPackageRuntimeObject(package.Record)));
        Reject(() => FalloutIdlePackageSelection.From(package, source with { Sha256 = new string('0', 64) }));
        Reject(() => warm.Common.ReadSourcePackageForIdle(warm.Common.ReadSceneQueries(warm.Player, 1), FalloutProcessPackageSlot.Current));
        Reject(() => FalloutPackageRuntimeRegistry.ValidateIdlePackageRead(current with { Process = Guid.NewGuid() }, captured, records));
        Reject(() => FalloutPackageRuntimeRegistry.ValidateIdlePackageRead(current with { RegistryFactory = Guid.NewGuid() }, captured, records));
        Reject(() => FalloutPackageRuntimeRegistry.ValidateIdlePackageRead(current with
        { Result = currentObject with { Source = source with { Sha256 = new string('0', 64) } } }, captured, records));
        Reject(() => FalloutPackageRuntimeRegistry.ValidateIdlePackageRead(current with
        { Result = currentObject with { HasExtra = false } }, captured, records));
        Reject(() => FalloutPackageRuntimeRegistry.ValidateIdlePackageRead(current with
        { Result = currentObject with { Kind = 0x1c } }, captured, records));
        Reject(() => FalloutPackageRuntimeRegistry.ValidateIdlePackageRead(current with
        { ProcessPackage = current.ProcessPackage! with { Slot = FalloutProcessPackageSlot.Primary } }, captured, records));
        var registry = captured.PackageRegistry!;
        Reject(() => FalloutPackageRuntimeRegistry.ValidateIdlePackageRead(current, captured with
        {
            PackageRegistry = registry with
            {
                Packages = registry.Packages.Select(value => value.Form == package.Form
            ? value with { RecordSha256 = new string('0', 64) } : value).ToArray()
            }
        }, records));

        calls.Clear(); warm.Set(FalloutProcessPackageSlot.Current, 0x101);
        var signed = Execute(warm);
        Require(signed.Result is { Kind: 255, HasExtra: false } &&
            calls.SequenceEqual([FalloutProcessPackageSlot.Primary, FalloutProcessPackageSlot.Current]),
            "Signed PKDT kind byte was clamped or an absent collection was invented.");
        calls.Clear(); warm.Set(FalloutProcessPackageSlot.Primary, 0x105);
        var primary = Execute(warm);
        Require(primary.Result is { Kind: 6, HasExtra: true, Source.Slot: FalloutProcessPackageSlot.Primary } &&
            calls.SequenceEqual([FalloutProcessPackageSlot.Primary, FalloutProcessPackageSlot.Primary]),
            "Idle nonnull Primary did not reenter its actual second getter or read the unrelated Current slot.");
        var saved = RoundTripPackage(warm.Common.Capture());
        var read = current;
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            read = RoundTripPackage(read);
            using var cold = new PackageMutationFixture(records, saved);
            var restored = cold.Common.Capture();
            FalloutPackageRuntimeRegistry.ValidateIdlePackageRead(read, restored, records);
            Require(cold.HourReads == 0 && cold.ContextCalls == 0 && cold.Registry.Factory != saved.PackageRegistry!.Factory &&
                cold.Registry.Require(package.Form).HasIdleCollection && cold.Registry.Require(package.Form).Procedure == 0x1c,
                "Cold idle metadata replayed an effect/clock or changed the genuine winning collection/kind owner.");
            var currentCold = Execute(cold);
            FalloutPackageRuntimeRegistry.ValidateIdlePackageRead(currentCold, cold.Common.Capture(), records);
            Require(currentCold.RegistryFactory == cold.Registry.Factory && currentCold.Result is { Kind: 6, HasExtra: true },
                "Fresh cold idle getter borrowed an old registry or fabricated the current package.");
            saved = RoundTripPackage(cold.Common.Capture());
        }
        Console.WriteLine("OPENNV_SOURCE_IDLE_PACKAGE_REGISTRY_PASS actualCurrentSetter=true actualIdleScope=true " +
            "primaryReread=true rawKindNotRuntimeProcedure=true emptyCollectionNonnull=true wrongOwnerAndRecordRejected=true " +
            "jsonCycles=3 noAssignmentOrClockReplay=true gameplay=UNEXECUTED");

        FalloutIdlePackageRead Execute(PackageMutationFixture fixture)
        {
            var before = fixture.Common.ReadSceneQueries(fixture.Player, 1);
            var consumer = new AuthoredIdleConsumers { Animation = null };
            consumer.SelectedObjectOverride = () => FalloutIdlePackageSelection.Read(
                () => Select(FalloutProcessPackageSlot.Primary), () => Select(FalloutProcessPackageSlot.Current));
            var beforeCounts = fixture.Registry.Capture(); var hourReads = fixture.HourReads;
            var result = fixture.Common.UpdateIdleAnimation(fixture.Player, 1, consumer);
            Require(result.Trace.Returned && result.Trace.SelectedObjectReturned && consumer.PackageRead is not null &&
                JsonSerializer.Serialize(beforeCounts) == JsonSerializer.Serialize(fixture.Registry.Capture()) && fixture.HourReads == hourReads,
                "Actual idle getter replayed assignment/count/clock or failed to return its selected package.");
            return consumer.PackageRead!;
            FalloutIdleSelectedObject? Select(FalloutProcessPackageSlot slot)
            {
                calls.Add(slot); consumer.PackageRead = fixture.Common.ReadSourcePackageForIdle(before, slot);
                return consumer.PackageRead.Result;
            }
        }
    }
}
