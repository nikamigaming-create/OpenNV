using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ParentHistoryContracts
{
    internal static void Run()
    {
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        var source = FalloutReferencePositionSource.Reviewed(FalloutPackageCommandFieldsSource.Reviewed(
            FalloutPlayerMovementControlSource.Reviewed(main)));
        var actor = new FalloutCombatActorIdentity(new("ParentFixture.esm", 0x14), new("ParentFixture.esm", 7),
            "ENGINE_PLAYER", 0, main.EngineSha256, "NPC_", 0, new('5', 64), true);
        var process = Guid.NewGuid(); var factory = Guid.NewGuid(); var constructor = Guid.NewGuid(); var stack = new string('A', 64);
        FalloutReferenceParentCellHistory New() => new(source, stack, process, factory, constructor, actor, 1, null);
        FalloutReferenceParentCellRead Read(long revision, FalloutFormKey? cell) =>
            new(FalloutReferenceParentCellHistory.Source, stack, process, factory, constructor, actor, revision, cell, main.EngineSha256);
        var destination = new FalloutFormKey("ParentFixture.esm", 0x800);
        var original = New(); var initial = original.Capture(1, null);
        new FalloutReferenceParentCellValidation(initial).ValidateRead(Read(1, null));
        var call = Guid.NewGuid();
        original.BeginStore(call, FalloutMainPlayerCellStep.PendingDestination, 1, null, destination);
        Reject(() => original.Capture(1, null));
        Reject(() => original.BeginStore(Guid.NewGuid(), FalloutMainPlayerCellStep.CellAttach, 1, null, destination));
        original.ObserveStore(2, destination); original.EndStore(null);
        // The same actual Main can contain two different original child roles.
        original.BeginStore(call, FalloutMainPlayerCellStep.CellAttach, 2, destination, destination);
        original.ObserveStore(3, destination); original.EndStore(null);
        var saved = RoundTrip(original.Capture(3, destination)); var index = new FalloutReferenceParentCellValidation(saved);
        index.ValidateRead(Read(1, null)); index.ValidateRead(Read(2, destination)); index.ValidateRead(Read(3, destination));
        Reject(() => original.BeginStore(call, FalloutMainPlayerCellStep.CellAttach, 3, destination, destination));
        Reject(() => index.ValidateRead(Read(2, null)));
        Reject(() => index.ValidateRead(Read(4, destination)));
        Reject(() => index.ValidateRead(Read(3, destination) with { Factory = Guid.NewGuid() }));
        Reject(() => new FalloutReferenceParentCellValidation(saved with { Cell = null }));
        Reject(() => new FalloutReferenceParentCellValidation(saved with { Sequence = 2 }));
        var stores = saved.Stores.ToArray(); stores[1] = stores[1] with { Before = destination };
        Reject(() => new FalloutReferenceParentCellValidation(saved with { Stores = stores }));

        var before = New(); before.BeginStore(Guid.NewGuid(), FalloutMainPlayerCellStep.PendingDestination, 1, null, destination);
        before.EndStore(new InvalidOperationException("authored publisher failure before actual field store"));
        var failed = RoundTrip(before.Capture(1, null));
        Require(failed.Stores[^1] is { Stored: false, Returned: false, Failure: not null }, "failed pre-store receipt lost its prefix");
        var failedIndex = new FalloutReferenceParentCellValidation(failed); failedIndex.ValidateRead(Read(1, null));
        Reject(() => failedIndex.ValidateRead(Read(2, destination)));
        Reject(() => before.BeginStore(Guid.NewGuid(), FalloutMainPlayerCellStep.CellAttach, 1, null, destination));

        var after = New(); after.BeginStore(Guid.NewGuid(), FalloutMainPlayerCellStep.PendingDestination, 1, null, destination);
        after.ObserveStore(2, destination); after.EndStore(new InvalidOperationException("authored native callback failure after actual field store"));
        var committed = RoundTrip(after.Capture(2, destination));
        Require(committed.Stores[^1] is { Stored: true, Returned: false, Failure: not null }, "failed post-store receipt discarded actual committed field");
        new FalloutReferenceParentCellValidation(committed).ValidateRead(Read(2, destination));
        after.Retire(); Require(after.Capture(2, destination).Retired, "retirement omitted actual field lifetime");
        Reject(() => after.BeginStore(Guid.NewGuid(), FalloutMainPlayerCellStep.CellAttach, 2, destination, destination));
        Console.WriteLine("OPENNV_PARENT_FIELD_HISTORY_PASS authoredScopeOnly=true existingWorldField=true orderedMainChildren=true independentLineage=true failedPrefixes=true indexedBorrowJoin=true wholeCold=UNACCEPTED");
    }
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action call)
    { try { call(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ObjectDisposedException) { return; } throw new InvalidDataException("Parent history accepted an invalid source prefix."); }
}
