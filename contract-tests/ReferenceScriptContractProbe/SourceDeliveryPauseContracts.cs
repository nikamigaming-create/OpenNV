using OpenNV.Runtime.Gameplay.State;

internal static class SourceDeliveryPauseContracts
{
    internal static void Run()
    {
        var delivery = new RuntimeSourceDeliveryPause();
        for (var cycle = 0; cycle != 3; ++cycle)
        {
            var call = new TaskCompletionSource();
            using (var paused = delivery.Acquire(call.Task))
            {
                Require(delivery.Paused && !paused.Drained && !call.Task.IsCompleted,
                    "Pause fabricated return or admitted another source call.");
                Reject(() => paused.RequireReturned());
                Reject(() => delivery.Acquire(null));
                call.SetResult();
                paused.RequireReturned();
                Require(paused.Drained && delivery.Paused, "Returned call resumed delivery before save cleanup.");
            }
            Require(!delivery.Paused, "Returned save pause did not resume delivery.");
        }
        var pending = new TaskCompletionSource();
        var cancelled = delivery.Acquire(pending.Task);
        cancelled.Dispose();
        Require(!delivery.Paused && !pending.Task.IsCompleted, "Cancellation completed the entered call.");
        Reject(() => cancelled.RequireReturned());
        pending.SetResult();
        var failure = new InvalidDataException("actual entered-call failure");
        using (var faulted = delivery.Acquire(Task.FromException(failure)))
        {
            try { faulted.RequireReturned(); throw new Exception("Failed source call qualified a save pause."); }
            catch (InvalidDataException error) { Require(ReferenceEquals(error, failure), "Original failure identity was replaced."); }
            Require(delivery.Paused, "Failed call released the save pause before cleanup.");
        }
        using (var returned = delivery.Acquire(null)) returned.RequireReturned();
        Console.WriteLine("OPENNV_SOURCE_DELIVERY_PAUSE_PASS cycles=3 enteredCallPreserved=true failedCallRefused=true cancellationDoesNotComplete=true actualNativeSave=UNEXECUTED");
    }
    private static void Require(bool condition, string error)
    { if (!condition) throw new Exception(error); }
    private static void Reject(Action call)
    {
        try { call(); throw new Exception("Invalid source delivery pause was admitted."); }
        catch (InvalidOperationException) { }
    }
}
