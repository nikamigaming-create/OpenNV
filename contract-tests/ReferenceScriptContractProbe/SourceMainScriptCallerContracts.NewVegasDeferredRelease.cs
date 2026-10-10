using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunNewVegasDeferredReleaseContracts()
    {
        using var warm = new Fixture(); var slot = new FalloutPlayerPendingSlot();
        var player = FalloutMainPlayerCellSource.Read(warm.Host.Source);
        warm.Owner.ConstructMainPlayerCell(player, slot, null);
        var pending = warm.Owner.MainPlayerPendingConsumers;
        var manager = pending.CurrentNewVegasDeferredRelease;
        var initial = manager.Capture();
        Require(initial.SkipMask == 0 && initial.Callers.Count == 0 && initial.Last is null &&
            initial.Buffers.Count == 11 && initial.Buffers.Count(buffer => buffer.Capacity == 70) == 2 &&
            initial.Buffers.All(buffer => buffer.Objects.Count == 0),
            "Original FNV array, embedded-allocation, lock and compiler-thread constructors were not retained separately.");
        var request = slot.StoreSource(FalloutPlayerPendingKind.Empty, null, null, "authored-NV-empty-source-allocation",
            AuthoredPendingPayload(FalloutMainPlayerPendingSource.Read(player)));
        var consumer = new AuthoredPendingTail(player, pending);
        warm.Host.AwaitPlayer = warm.Owner.ExecuteMainPlayerCell;
        using (warm.Owner.BindMainPlayerCell(consumer, "authored-actual-NV-pending-deferred-owner"))
        using (warm.Bind()) warm.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult();
        var saved = manager.Capture(); var cell = warm.Owner.CaptureMainPlayerCell();
        Require(saved.Last is { Complete: true, PrimaryQueues: 6, SecondaryQueues: 5, CallerBefore: 1 } &&
            saved.Last.Main == cell.LastCall!.MainInvocation && saved.Callers.Count == 1 && saved.Callers[0].Enabled == 1 &&
            !slot.Pending && slot.Capture().Completion?.Identity == request.Identity &&
            cell.LastCall!.Children.Single(child => child.Step == FalloutMainPlayerCellStep.PendingDeferredDestruction).Returned is not null,
            "Actual FNV drain did not restore the same caller byte and return both queue cohorts before the payload null store.");
        Reject(() => FalloutMainPlayerPendingState.Validate(cell.PendingConsumers with { NewVegasDeferredRelease = null }));
        Reject(() => FalloutNewVegasDeferredRelease.Validate(saved with { SkipMask = 1 }));
        Reject(() => FalloutNewVegasDeferredRelease.Validate(saved with { Buffers = saved.Buffers.Skip(1).ToArray() }));
        Reject(() => FalloutNewVegasDeferredRelease.Validate(saved with
        { Callers = [saved.Callers[0] with { Enabled = 0 }] }));
        NewVegasDeferredReceiptCorruption(saved);
        var cold = new FalloutNewVegasDeferredRelease(player, saved.Stack, Guid.NewGuid(), saved);
        var next = cold.Capture();
        Require(next.Process != saved.Process && next.Constructor != saved.Constructor && next.Callers.Count == 0 && next.Last is null,
            "Cold FNV release imported a previous native lock, compiler thread or completed child.");
        cold.Retire(() => { }); // Authored already-closed native Main boundary, not a product producer.
        Console.WriteLine("OPENNV_FNV_SOURCE_DEFERRED_CONSTRUCTOR_PASS actualPendingMain=true primary6=true secondary5=true " +
            "sourceTlsInitializerOne=true sameCallerRestore=true embeddedAllocation70=true mandatoryFreshCold=true corruptReceiptRefused=true " +
            "actualNonemptyNativeFactories=UNOWNED nativeGameplay=UNEXECUTED");
    }

    private static void NewVegasDeferredReceiptCorruption(FalloutNewVegasDeferredReleaseSnapshot saved)
    {
        var returned = saved.Last!;
        // A complete bit alone must never certify the pending child's actual
        // caller, queue prefix or eligibility for a fresh cold constructor.
        foreach (var changed in new[]
        {
            returned with { Main = null }, returned with { Main = Guid.Empty },
            returned with { ManagedThread = 0 }, returned with { ManagedThread = returned.ManagedThread + 1 },
            returned with { TryLock = false }, returned with { Complete = false },
            returned with { Error = "authored-unreturned-child" }, returned with { CallerBefore = null },
            returned with { CallerBefore = 0 }, returned with { PrimaryQueues = 5 },
            returned with { PrimaryQueues = 7 }, returned with { SecondaryQueues = 4 },
            returned with { SecondaryQueues = 6 }, returned with { BusyReturn = true },
            returned with { LockReturn = true },
        }) Refuse(saved with { Last = changed });
        Refuse(saved with { Last = null });
        Refuse(saved with { Sequence = 0 });
        Refuse(saved with { Callers = [] });
        Refuse(saved with { Callers = [saved.Callers[0], saved.Callers[0]] });
        Refuse(saved with { Sequence = 2, Callers = [saved.Callers[0],
            saved.Callers[0] with { ManagedThread = returned.ManagedThread + 1 }] });
        Refuse(saved with { Callers = [null!] });
        Refuse(saved with { Buffers = saved.Buffers.Select((buffer, index) => index == 0 ? null! : buffer).ToArray() });

        // These are independently authored source-exit shapes, not claims that
        // another product thread acquired the current Main child token.
        var early = returned with { BusyReturn = true, CallerBefore = null, PrimaryQueues = 0, SecondaryQueues = 0 };
        FalloutNewVegasDeferredRelease.Validate(saved with { Last = early });
        FalloutNewVegasDeferredRelease.Validate(saved with { Last = early with { BusyReturn = false, LockReturn = true } });
        Refuse(saved with { Last = early with { LockReturn = true } });
        Refuse(saved with { Last = early with { CallerBefore = 1 } });
        Refuse(saved with { Last = early with { PrimaryQueues = 1 } });
        Refuse(saved with { Last = early with { SecondaryQueues = 1 } });

        static void Refuse(FalloutNewVegasDeferredReleaseSnapshot corrupt)
        {
            Reject(() => FalloutNewVegasDeferredRelease.Validate(corrupt));
            Reject(() => _ = new FalloutNewVegasDeferredRelease(corrupt.Player, corrupt.Stack, Guid.NewGuid(), corrupt));
        }
    }
}
