using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerPackageAnimationInitializationContracts()
    {
        // Omitting Y/world proves the selected empty-X arm cannot read them.
        var empty = AnimationSettings("", "");
        using (var fields = new PackageFieldFixture(admitted: false))
        {
            var receipt = fields.Call(invocation => fields.Fields.InitializeConstructedPlayerAnimation(invocation, empty));
            var snapshot = Roundtrip(fields.Fields.Capture());
            Require(receipt is { Returned: true, ConfiguredCell: "", ConfiguredGridX: "", OverrideContext: null, FirstPersonContext: null } &&
                fields.Fields.HasConstructedPlayerAnimationInitialization && fields.Fields.LastAnimationInitialization == receipt &&
                snapshot.Operations.Single().Stores.Count == 0 && snapshot.Values == FalloutPackageCommandFieldValues.Constructed &&
                snapshot.Availability.Where(value => value.Available).Select(value => value.Lane)
                    .SequenceEqual(new[] { FalloutPackageCommandFieldLane.Animation }),
                "Independent animation admission repeated constructor stores or opened unrelated position/Extra/physical lanes.");
            var viewCalls = 0; var commonCalls = 0;
            var read = fields.Call(invocation => fields.Fields.ReadAnimationContext(invocation,
                () => { commonCalls++; return fields.ReadCommon(); },
                () => { viewCalls++; return fields.ReadView() with { View = 1 }; }));
            Require(read is { Returned: true, Selection: FalloutPackageAnimationSelection.Process, Context: null, ViewRead.View: 1 } &&
                viewCalls == 1 && commonCalls == 1 && read.ViewRead.Process != fields.Process,
                "Animation did not borrow the current independent scene view before Common fallback.");
            Reject(() => fields.Call(invocation => fields.Fields.InitializeConstructedPlayerAnimation(invocation, empty)));
            Require(fields.Fields.Capture().Operations.Count == 2,
                "Repeated animation initialization entered another deferred constructor/setting receipt.");
            var saved = Roundtrip(fields.Fields.Capture());
            var common = fields.Actual.Common.Capture();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                using var cold = new PackageFieldFixture(saved, common);
                Require(cold.Fields.HasConstructedPlayerAnimationInitialization && cold.Fields.LastAnimationInitialization == receipt &&
                    cold.Fields.Capture().Operations.Count == saved.Operations.Count &&
                    cold.Fields.LastAnimationInitialization!.Invocation.Process != cold.Process,
                    "Cold animation fields replayed initialization or rebound its historical Common identity.");
                _ = cold.Call(invocation => cold.Fields.ReadAnimationContext(invocation, cold.ReadCommon, cold.ReadView));
                saved = Roundtrip(cold.Fields.Capture());
                common = cold.Actual.Common.Capture();
            }
            var changed = snapshot.Operations.Single() with { AnimationInitialization = receipt with { ConfiguredGridX = "1" } };
            Reject(() => FalloutActorPackageCommandFields.Validate(snapshot with { Operations = [changed] }, fields.Source, fields.Stack, fields.Actor));
            var readSnapshot = fields.Fields.Capture();
            var badRead = readSnapshot.Operations.Last() with
            { AnimationRead = read with { ViewRead = read.ViewRead! with { Constructor = Guid.Empty } } };
            Reject(() => FalloutActorPackageCommandFields.Validate(readSnapshot with
            { Operations = [readSnapshot.Operations[0], badRead] }, fields.Source, fields.Stack, fields.Actor));
        }
        foreach (var (cell, x) in new[] { ("SelectedCell", ""), ("", "1") })
        {
            using var fields = new PackageFieldFixture(admitted: false);
            Reject(() => fields.Call(invocation => fields.Fields.InitializeConstructedPlayerAnimation(invocation, AnimationSettings(cell, x))));
            var failed = fields.Fields.LastAnimationInitialization;
            Require(failed is { Returned: false, Failure: FalloutActorPackageCommandFields.ConfiguredAnimationWriterBoundary } &&
                failed.ConfiguredCell == cell && (cell.Length == 0 ? failed.ConfiguredGridX == x : failed.ConfiguredGridX is null) &&
                fields.Fields.Capture().Availability.All(value => !value.Available) && fields.Fields.SaveBlocker is not null,
                "Configured CELL/grid publication was replaced by constructor-null animation or lost its failed prefix.");
            Reject(() => fields.Call(invocation => fields.Fields.InitializeConstructedPlayerAnimation(invocation, empty)));
            Require(fields.Fields.Capture().Operations.Count == 1, "Failed animation initialization was retried in the same source epoch.");
        }
        using (var fields = new PackageFieldFixture(admitted: false))
        {
            fields.Call(invocation => fields.Fields.ObserveUnownedWriter(invocation, FalloutPackageCommandFieldLane.Animation,
                "authored-real-first-person-animation-publication"));
            Reject(() => fields.Call(invocation => fields.Fields.InitializeConstructedPlayerAnimation(invocation, empty)));
            Require(fields.Fields.LastAnimationInitialization is { Returned: false, ConfiguredCell: null },
                "Animation constructor admission erased a real preceding publication before settings were read.");
            _ = fields.Fields.Capture();
        }
        using (var fields = new PackageFieldFixture(admitted: false))
        {
            _ = fields.Call(invocation => fields.Fields.InitializeConstructedPlayerAnimation(invocation, empty));
            fields.Call(invocation => fields.Fields.ObserveUnownedWriter(invocation, FalloutPackageCommandFieldLane.Animation,
                "authored-later-override-or-3D-context-writer"));
            var viewCalled = false;
            Reject(() => fields.Call(invocation => fields.Fields.ReadAnimationContext(invocation, fields.ReadCommon,
                () => { viewCalled = true; return fields.ReadView(); })));
            Require(!viewCalled && fields.Fields.LastAnimationInitialization is { Returned: true } && fields.Fields.SaveBlocker is not null,
                "Later animation publication was hidden by a successful historical constructor prefix.");
            _ = fields.Fields.Capture();
        }
        Console.WriteLine("OPENNV_PACKAGE_ANIMATION_INITIALIZATION_PASS authoredScopeOnly=true " +
            "independentLane=true actualViewBorrowed=true configuredWriterRefused=true failedPrefixRetained=true coldCycles=3 originalCommand=UNEXECUTED");
    }

    private static FalloutInstallationSettings AnimationSettings(string cell, string x) =>
        FalloutInstallationSettings.ReadLayers([], [new("General", "SStartingCell", cell), new("General", "SStartingCellX", x)]);
}
