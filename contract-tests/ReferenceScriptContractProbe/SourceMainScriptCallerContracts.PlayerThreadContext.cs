using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerThreadContextContracts()
    {
        var immediate = new FalloutImmediateScriptSource(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3));
        var source = FalloutPlayerThreadContextSource.Reviewed(FalloutMainScriptCallerSource.Read(immediate));
        Reject(() => (source with { InitialKind = 0 }).Validate());
        Reject(() => (source with { PlayerKind = 5 }).Validate());
        Reject(() => (source with { InitialModel = 1 }).Validate());
        Reject(() => (source with { InitialReference = 1 }).Validate());
        var process = Guid.NewGuid(); var main = Guid.NewGuid();
        var context = new FalloutPlayerThreadContextState(source, process);
        var actor = new FalloutFormKey("ThreadFixture.esm", 0x14);
        Reject(() => context.ReadPlayerModelCache(actor));
        var before = context.Entry;
        Require(before is null, "Loaded thread template fabricated a returned current Player scope.");
        var entry = context.EnterPlayer(main, 1);
        Require(entry.BeforeKind == 59 && entry.CurrentKind == 52 && entry.Initialized == 1 && entry.InitializerReturned &&
            entry.ScopeEntered && entry.Process == process && entry.Main == main && entry.Thread == Environment.CurrentManagedThreadId,
            "Current-thread initialization conflated its distinct loaded kind, current kind, source scope or calling thread.");
        var cache = context.ReadPlayerModelCache(actor);
        Require(cache is { CachedReference: null, Model: null, Matched: false } && cache.Factory == entry.Factory &&
            cache.Process == entry.Process && cache.Thread == entry.Thread && cache.Actor == actor,
            "Player model cache inferred a current model from kind or lost its independent loader-null fields.");
        Reject(() => context.EnterPlayer(main, 2));
        Reject(() => context.ExitPlayer(Guid.NewGuid(), 1));
        Reject(() => context.ExitPlayer(main, 2));
        var duplicate = new FalloutPlayerThreadContextState(source, process);
        Reject(() => duplicate.EnterPlayer(main, 2));
        Exception? otherThread = null;
        var thread = new Thread(() => { try { context.Retire(); } catch (Exception failure) { otherThread = failure; } });
        thread.Start(); thread.Join();
        Require(otherThread is InvalidOperationException, "A foreign thread retired another source TLS scope.");
        Exception? foreignExit = null;
        var exitThread = new Thread(() => { try { context.ExitPlayer(main, 1); } catch (Exception failure) { foreignExit = failure; } });
        exitThread.Start(); exitThread.Join();
        Require(foreignExit is InvalidOperationException && context.Exit is null,
            "A foreign thread restored another source TLS scope.");
        context.Retire(); duplicate.Retire();
        Reject(() => context.ReadPlayerModelCache(actor));
        Reject(() => context.EnterPlayer(main, 2));
        // Retiring a failed lifetime releases its actual C# current owner,
        // without fabricating the original scope-exit mutation or call return.
        Require(context.Entry == entry, "Retirement replayed/restored a source scope which never returned.");
        var fresh = new FalloutPlayerThreadContextState(source, process);
        var rebound = fresh.EnterPlayer(Guid.NewGuid(), 1);
        Require(rebound.Factory != entry.Factory && rebound.BeforeKind == 59,
            "Fresh typed thread ownership resurrected the retired current field or prior factory.");
        var returned = fresh.ExitPlayer(rebound.Main, 1);
        Require(returned.BeforeKind == 52 && returned.RestoredKind == rebound.BeforeKind &&
            returned.Factory == rebound.Factory && returned.Thread == rebound.Thread && returned.Initialized == 1,
            "Player epilogue changed its actual prior kind, factory or source thread.");
        Reject(() => fresh.ReadPlayerModelCache(actor));
        Reject(() => fresh.ExitPlayer(rebound.Main, 1));
        Reject(() => fresh.EnterPlayer(rebound.Main, 2));
        Reject(() => fresh.EnterPlayer(Guid.NewGuid(), 1));
        var next = fresh.EnterPlayer(Guid.NewGuid(), 2);
        Require(next.Factory == rebound.Factory && next.BeforeKind == returned.RestoredKind &&
            next.Initialized == 1 && fresh.Exit is null,
            "A second Player invocation replayed a receipt or reset its initialized current-thread owner.");
        fresh.ExitPlayer(next.Main, next.MainOrdinal); fresh.Retire();
        Console.WriteLine("OPENNV_PLAYER_THREAD_CONTEXT_PASS loadedKind59=true sourceKind52=true actualCallingThread=true emptyInitializerRange=true duplicateOwnerRefused=true lifetimeRetired=true scopeExit=prior-kind-restored repeatedCalls=true nativeModuleTls=INDEPENDENT");
    }
}
