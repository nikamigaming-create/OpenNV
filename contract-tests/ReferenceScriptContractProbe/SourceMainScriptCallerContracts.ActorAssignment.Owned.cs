using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    private static object AuditOwnedActorPackageAssignment(FalloutPluginStack records,
        FalloutActorProcessCommonState common, FalloutPlayerMovementControlSource controls)
    {
        var owned = records.OwnedSource ?? throw new InvalidDataException("Actor assignment audit needs actual owned inputs.");
        var fields = FalloutPackageCommandFieldsSource.Reviewed(controls);
        var source = FalloutExecutableStringTable.ReadActorPackageAssignment(owned.FalloutExecutablePath, fields);
        var process = common.ReadSceneQueries(records.RuntimeFormKey(0x14), 1);
        using var registry = new FalloutPackageRuntimeRegistry(records, controls.Main.EngineSha256, owned.StackId);
        var invocation = new FalloutPackageCommandFieldInvocation(Guid.NewGuid(), process.Actor, process.Process, 1, Environment.CurrentManagedThreadId);
        var reads = records.EffectiveRecords("PACK").Select(record => registry.ReadCurrentTarget(invocation, registry.Require(record.FormKey))).ToArray();
        var original = registry.Capture(); FalloutPackageRuntimeRegistry.Validate(original, registry.Executable, registry.Stack);
        FalloutPackageRuntimeRegistry.ValidateTargetReads(reads, original);
        var saved = original;
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            using var cold = new FalloutPackageRuntimeRegistry(records, controls.Main.EngineSha256, owned.StackId, saved);
            var captured = cold.Capture(); FalloutPackageRuntimeRegistry.Validate(captured, cold.Executable, cold.Stack);
            FalloutPackageRuntimeRegistry.ValidateTargetReads(reads, captured);
            var previous = saved.Packages.ToDictionary(package => package.Form, FalloutFormKeyComparer.Instance);
            foreach (var package in captured.Packages)
            {
                var old = previous[package.Form];
                Require(package.Target!.Factory != old.Target!.Factory && package.Target.Constructor == old.Target.Constructor &&
                    package.Target.Values == old.Target.Values && package.Target.Loads.SequenceEqual(old.Target.Loads) &&
                    (package.Target.Object is null ? old.Target.Object is null : package.Target.Object != old.Target.Object),
                    "Owned cold target changed original source fields/load history or reused its managed field/object lifetime.");
            }
            saved = captured;
        }
        return new { source = source.Identity, targetSource = FalloutPackageRuntimeTargetFields.Source, records = reads.Length,
            absentTarget = reads.Count(read => read.Object is null), targetObjects = reads.Count(read => read.Object is not null),
            kinds = reads.Select(read => read.Values?.Kind).Distinct().Order().ToArray(), coldCycles = 3,
            sourceExtents = original.Packages.SelectMany(package => package.Target!.Loads).GroupBy(load => load.SourceExtent)
                .OrderBy(group => group.Key).Select(group => new { bytes = group.Key, rows = group.Count() }).ToArray(),
            currentReferencePayload = "UNOWNED", ActorExtra81 = "UNOWNED", loadingCurrentPrefix = "UNQUALIFIED",
            originalActorAssignment = "UNEXECUTED", wholePlayer = "UNRETURNED" };
    }
}
