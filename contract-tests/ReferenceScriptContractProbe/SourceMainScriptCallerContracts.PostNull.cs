using OpenNV.Runtime.Content;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunStandalonePostNullWordContracts()
    {
        StandalonePostNullWordCurrentAndCold(); StandalonePostNullWordForced(); StandalonePostNullWordEarly();
        Console.WriteLine("OPENNV_FO3_POST_NULL_MAIN_WORD_PASS actualPendingCaller=true sameConstructedMainWord=true " +
            "forcedWriterShortCircuit=true wholePlayerByte=true currentColdNoReplay=true earlyStaleCallerRefused=true " +
            "FnvManagerQuery=UNOWNED nativeGameplay=UNEXECUTED");
    }

    private static FalloutPlayerPendingRequest StoreStandaloneReferenceTransfer(StandaloneFixture fixture)
    {
        var source = FalloutMainPlayerPendingSource.Read(fixture.Owner.MainPlayerCellSource);
        return fixture.Slot.StoreSource(FalloutPlayerPendingKind.ReferenceTravel, null, null,
            "authored-actual-reference-transfer-payload", new(source.Contract, "authored-complete-reference-transfer-factory",
                null, null, new FalloutFormKey("Authored.esm", 0x450), 0x3f800000, 0x40000000, 0x40400000,
                0, 0, 0, 0, null, null, SourceTailWord: 0));
    }

    private static void StandalonePostNullWordCurrentAndCold()
    {
        using var warm = new StandaloneFixture(); var request = StoreStandaloneReferenceTransfer(warm);
        ExecuteStandalonePending(warm);
        var saved = warm.Owner.Capture(); var player = saved.StandaloneMain!.PlayerCell;
        Require(!warm.Slot.Pending && player.Pending.Completion?.Identity == request.Identity &&
            player.LastCall is { Disposition: FalloutMainPlayerCellDisposition.PendingReturned } &&
            player.LastCall.Children.Single(child => child.Step == FalloutMainPlayerCellStep.PendingFlagQueries) is
                { Boolean: true, Returned: not null } && player.PendingConsumers.Flags == 1 && player.PendingConsumers.FlagStores == 1 &&
            (saved.MainFrame.Word & FalloutMainFrameDeclaration.ForcedProcessing) == 0,
            "Actual clear Main word did not reach the source post-null whole Player-byte store.");
        using var cold = new StandaloneFixture(saved, warm.Fade.Capture());
        var next = cold.Owner.Capture();
        Require(next.CapturedProcess != saved.CapturedProcess && next.MainFrame.Word == saved.MainFrame.Word &&
            next.StandaloneMain!.PlayerCell.PendingConsumers.Flags == 1 &&
            next.StandaloneMain.PlayerCell.PendingConsumers.FlagStores == 1 && cold.Host.Log.Count == 0,
            "Cold post-null state replayed a query/write or lost the same mandatory Main/Player fields.");
    }

    private static void StandalonePostNullWordForced()
    {
        using var fixture = new StandaloneFixture();
        const string writer = "authored-actual-source-world-load-forced-writer";
        var actual = fixture.Owner.BeginMain(FalloutMainProcessOperation.SourceWorldLoad, writer);
        StoreStandaloneReferenceTransfer(fixture); ExecuteStandalonePending(fixture);
        var player = fixture.Owner.CaptureMainPlayerCell();
        Require(player.LastCall!.Children.Single(child => child.Step == FalloutMainPlayerCellStep.PendingFlagQueries) is
            { Boolean: false, Returned: not null } && !player.LastCall.Children.Any(child =>
                child.Step == FalloutMainPlayerCellStep.PendingFlagChild) &&
            player.PendingConsumers.Flags == 0 && player.PendingConsumers.FlagStores == 0,
            "Actual forced Main word failed to short-circuit the standalone post-null Player-byte writer.");
        fixture.Owner.CompleteMain(actual, writer);
        Require((fixture.Owner.Capture().MainFrame.Word & FalloutMainFrameDeclaration.ForcedProcessing) == 0,
            "Post-null query changed or replaced the genuine source Main write/return lifetime.");
    }

    private static void StandalonePostNullWordEarly()
    {
        using var fixture = new StandaloneFixture(); var request = StoreStandaloneReferenceTransfer(fixture);
        FalloutMainPlayerCellInvocation? early = null;
        var consumers = new AuthoredPendingTail(fixture.Owner.MainPlayerCellSource, fixture.Owner.MainPlayerPendingConsumers)
        {
            DuringTransfer = invocation =>
            {
                early = invocation;
                // The real source destination is still entered. Constructing
                // another token cannot manufacture a returned null-store.
                fixture.Owner.ReadStandalonePendingPostNullMainWord(new(invocation.Main,
                    FalloutMainPlayerCellStep.PendingFlagQueries));
            }
        };
        using (fixture.Owner.BindMainPlayerCell(consumers, "authored-independent-early-word-negative"))
        using (fixture.Owner.BindMainScriptCaller(fixture.Host, "authored-independent-early-main"))
            Reject(() => fixture.Owner.ExecuteMainScriptCaller(1, .02f).GetAwaiter().GetResult());
        var slot = fixture.Slot.Capture();
        Require(fixture.Slot.Pending && fixture.Slot.Next is null && slot.Pending == request && slot.Completion is null &&
            fixture.Owner.MainPlayerCellSaveBlocker is not null && early is not null,
            "An early post-null query cleared or replayed the still-owned actual transfer prefix.");
        Reject(() => fixture.Owner.ReadStandalonePendingPostNullMainWord(early!));
    }
}
