using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunSourceTaskManagerContracts()
    {
        StandaloneTaskResetCurrentAndCold(); StandaloneTaskResetUnmappedLiveFactory();
        Console.WriteLine("OPENNV_FO3_SOURCE_TASK_MANAGER_PASS authoredOnly=true actualQueuedFactoryCohort=true " +
            "empty37Buckets=true actualThreadRegistration=true mandatoryColdConstructor=true liveUnboundTaskRefused=true " +
            "nativeSchedulerCancelTls=UNOWNED nativeGameplay=UNEXECUTED");
    }

    private static void StoreStandaloneEmptyTransfer(StandaloneFixture fixture)
    {
        var source = FalloutMainPlayerPendingSource.Read(fixture.Owner.MainPlayerCellSource);
        fixture.Slot.StoreSource(FalloutPlayerPendingKind.Empty, null, null, "authored-FO3-empty-pending-allocation",
            new(source.Contract, "authored-complete-raw-empty-payload", null, null, null, 0, 0, 0, 0, 0, 0, 0,
                null, null, SourceTailWord: 0));
    }

    private static void ExecuteStandalonePending(StandaloneFixture fixture)
    {
        var consumers = new AuthoredPendingTail(fixture.Owner.MainPlayerCellSource, fixture.Owner.MainPlayerPendingConsumers);
        using (fixture.Owner.BindMainPlayerCell(consumers, "authored-FO3-current-pending-task-manager"))
        using (fixture.Owner.BindMainScriptCaller(fixture.Host, "authored-FO3-current-main-task-manager"))
            fixture.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult();
    }

    private static void StandaloneTaskResetCurrentAndCold()
    {
        using var warm = new StandaloneFixture(); StoreStandaloneEmptyTransfer(warm);
        ExecuteStandalonePending(warm);
        var saved = warm.Owner.Capture(); var manager = saved.StandaloneMain!.PlayerCell.PendingConsumers.TaskManager!;
        Require(manager.Last is { Complete: true, ExaminedBuckets: 37, Returned: 0, Retirement: false } &&
            manager.Last.Visited.Count == 0 && manager.Last.Main == saved.StandaloneMain.MainCaller.LastCall!.Invocation &&
            manager.Threads.Count == 1 && manager.Threads[0].Slot == 1 &&
            manager.Threads[0].ManagedThread == System.Environment.CurrentManagedThreadId && warm.Slot.Next is null,
            "The genuine empty source factory/map did not return every bucket under the entered Main/thread owner.");
        Require(manager.Source.Bucket(0x100000025UL) == 0 && manager.Source.Bucket(0x100000026UL) == 1,
            "Source task hash consumed the high key word or lost unsigned low-word modulo.");
        Reject(() => (manager.Source with { BucketCount = 36 }).Validate(manager.Player));
        Reject(() => FalloutSourceTaskManager.Validate(manager with
        { Threads = [manager.Threads[0] with { Slot = 0 }] }));
        Reject(() => FalloutMainPlayerPendingState.Validate(saved.StandaloneMain.PlayerCell.PendingConsumers with { TaskManager = null }));
        Reject(() => FalloutMainPlayerPendingState.Validate(saved.StandaloneMain.PlayerCell.PendingConsumers with
        { TaskManager = manager with { Process = Guid.NewGuid() } }));
        using var cold = new StandaloneFixture(saved, warm.Fade.Capture());
        var next = cold.Owner.Capture().StandaloneMain!.PlayerCell.PendingConsumers.TaskManager!;
        Require(next.Process != manager.Process && next.Constructor != manager.Constructor && next.Threads.Count == 0 &&
            next.Last is null && next.Cold?.PreviousProcess == manager.Process && cold.Host.Log.Count == 0,
            "Cold task-manager construction imported old process/thread handles or replayed the reset child.");
    }

    private static void StandaloneTaskResetUnmappedLiveFactory()
    {
        using var fixture = new StandaloneFixture(); var key = new FalloutFormKey("Authored.esm", 0x450);
        var request = fixture.SourceQueue.Request(key, 3,
            new(new(false, "authored-current-Main-forced"), new(true, "authored-current-Main-permit"),
                new(false, "authored-source-base-exclusion"), new(false, "authored-source-reference-queue-flag"),
                new(0u, "authored-source-queue-flags"), "authored-real-queued-source-factory"));
        Require(request is { Disposition: FalloutQueuedReferenceRequestDisposition.Constructed, Identity: not null },
            "Independent live-task negative did not construct the actual source queue object.");
        StoreStandaloneEmptyTransfer(fixture);
        var pending = fixture.Slot.Next!;
        Reject(() => ExecuteStandalonePending(fixture));
        var failed = fixture.Slot.Capture();
        Require(fixture.Slot.Pending && fixture.Slot.Next is null && failed.Pending == pending &&
            failed.Completion is null && failed.Error is not null && fixture.Owner.MainPlayerCellSaveBlocker is not null &&
            fixture.TaskManager!.SaveBlocker!.Contains("membership-and-virtual-cancel-unowned", StringComparison.Ordinal),
            $"Unmapped task reset did not retain its failed payload without replay: pending={fixture.Slot.Pending}, " +
            $"playerBlocker={fixture.Owner.MainPlayerCellSaveBlocker}, managerBlocker={fixture.TaskManager?.SaveBlocker}.");
        Reject(() => fixture.Owner.Capture());
        // Retire only the actual unstarted object through its existing source
        // cancellation/map/caller owner. This does not simulate I/O, an entered
        // original scheduler, a native child, or an opaque cancellation return.
        fixture.SourceQueue.RetireUnstarted(request.Identity!.Value, "authored-unstarted-current-factory-retirement");
    }
}
