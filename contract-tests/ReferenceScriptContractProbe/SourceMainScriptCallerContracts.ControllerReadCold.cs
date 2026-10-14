using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Physics;

internal static partial class SourceMainScriptCallerContracts
{
    private static void ControllerReadColdLineage(FalloutStandalonePlayerSceneSnapshot saved)
    {
        var call = saved.PostProcessContext!; var query = call.Queries[0]; var thread = saved.ThreadContextEntry!;
        var source = FalloutEarlyControllerSource.Reviewed(
            FalloutPackageCommandFieldsSource.Reviewed(FalloutPlayerMovementControlSource.Reviewed(saved.Source.Main)),
            FalloutPlayerThreadContextSource.Reviewed(saved.Source.Main));
        var entered = new FalloutControllerPostProcessInvocation(call.Main, call.MainOrdinal, thread.Process,
            Guid.NewGuid(), saved.Constructor, thread.Thread, query, 0);
        var lineage = new FalloutEarlyControllerLineage(source.Identity, saved.Stack, Guid.NewGuid(),
            query.Process.Process, entered.RuntimeProcess, thread.Process, thread.Factory,
            query.Process.Actor, query.Process.Epoch, query.Process.Level, entered.Constructor, thread.Thread);
        Check(saved, entered, lineage);
        Reject(() => Check(saved, entered, lineage with { Source = new('0', 64) }));
        Reject(() => Check(saved, entered, lineage with { ThreadFactory = Guid.NewGuid() }));
        Reject(() => Check(saved, entered with { Constructor = Guid.NewGuid() }, lineage));
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            var previous = saved;
            var cold = new FalloutStandalonePlayerSceneState(saved.Source, saved.Stack, Guid.NewGuid(),
                JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(saved))!);
            try { saved = cold.Capture(); }
            finally { cold.Retire(); }
            Require(JsonSerializer.Serialize(saved.PostProcessContext) == JsonSerializer.Serialize(previous.PostProcessContext) &&
                saved.ThreadContextEntry == previous.ThreadContextEntry &&
                saved.PointOfView!.Lifetimes.Count == previous.PointOfView!.Lifetimes.Count + 1,
                "Cold scene changed its actual retained controller scope or replayed its source child.");
            Check(saved, entered, lineage);
            var latest = saved.PointOfView!.Lifetimes[^1];
            Reject(() => Check(saved,
                entered with { PlayerProcess = latest.Process, Constructor = latest.Constructor },
                lineage with { ThreadProcess = latest.Process, Constructor = latest.Constructor }));
        }

        void Check(FalloutStandalonePlayerSceneSnapshot parent, FalloutControllerPostProcessInvocation invocation,
            FalloutEarlyControllerLineage actual) => FalloutStandalonePlayerSceneState.ValidateControllerReadLineage(
                parent, invocation, actual, query.Context!.Value, query, call.Main, call.MainOrdinal,
                call.Entered, call.Changed, 0, FalloutControllerPlayerSourceScope.PostProcessContext);
    }
}
