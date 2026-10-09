using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class ProcessQueueContracts
{
    private static void ActualNativeCallerThread(FalloutActorProcessQueueDeclaration declaration)
    {
        // Source indexing constructs the actual empty registry on a worker.
        // Its first real native caller publishes a sticky scene-thread epoch,
        // including the early-bootstrap failure path with no queued reads.
        using var queue = new FalloutQueuedReferences(declaration, Stack, Queued);
        var registry = Task.Run(() => new FalloutQueuedReferenceWorkRegistry(queue)).GetAwaiter().GetResult();
        Reject(() => registry.StopAndReadTasks());
        var nativeEpoch = new object();
        registry.BindNativePresentationThread(nativeEpoch);
        registry.BindNativePresentationThread(nativeEpoch);
        Reject(() => registry.BindNativePresentationThread(new object()));
        Task.Run(() => Reject(() => registry.BindNativePresentationThread(nativeEpoch))).GetAwaiter().GetResult();
        Require(registry.StopAndReadTasks().Count == 0, "Empty bootstrap cleanup invented work or kept the indexing thread.");
        registry.RetireReturnedWork(); registry.Dispose();

        using var enteredQueue = new FalloutQueuedReferences(declaration, Stack, Queued);
        var entered = new FalloutQueuedReferenceWorkRegistry(enteredQueue);
        var identity = enteredQueue.Request(Npc, 0, Inputs()).Identity!.Value;
        var returned = 0;
        var read = entered.Begin(identity, "authored-entered-native-thread-negative", _ => Task.FromResult(73),
            () => returned++, default);
        Reject(() => entered.BindNativePresentationThread(new object()));
        Require(read.ReadTask.GetAwaiter().GetResult() == 73 && !read.Retired && returned == 0,
            "Native thread rebinding retired an entered actual read or native owner.");
        _ = entered.StopAndReadTasks(); entered.RetireReturnedWork(); entered.Dispose();
        Require(returned == 1 && read.Retired, "The exact constructing thread lost its native retirement after refused rebinding.");
        Console.WriteLine("OPENNV_NATIVE_QUEUED_CALLER_THREAD_CONTRACT_PASS workerIndexing=true emptyBootstrapCleanup=true stickyNativeEpoch=true foreignThreadRefused=true enteredWorkCannotMove=true actualNativeReturn=true originalTLS=unowned");
    }
}
