using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static class ParentColdContracts
{
    internal static void Run()
    {
        var main = FalloutMainScriptCallerSource.Read(new(FalloutSourceMainFamily.Fallout3, new('1', 64),
            FalloutImmediateScriptSource.ContractForEngine(FalloutSourceMainFamily.Fallout3)));
        var source = FalloutReferencePositionSource.Reviewed(FalloutPackageCommandFieldsSource.Reviewed(
            FalloutPlayerMovementControlSource.Reviewed(main)));
        var actor = new FalloutCombatActorIdentity(new("ParentColdFixture.esm", 0x14), new("ParentColdFixture.esm", 7),
            "ENGINE_PLAYER", 0, main.EngineSha256, "NPC_", 0, new('5', 64), true);
        var stack = new string('B', 64);
        var process = Guid.NewGuid(); var factory = Guid.NewGuid(); var constructor = Guid.NewGuid();
        var field = new FalloutReferenceParentCellHistory(source, stack, process, factory, constructor, actor, 1, null);
        var saved = RoundTrip(field.Capture(1, null));
        var reads = new List<FalloutReferenceParentCellRead> { Read(saved) };
        var history = JsonSerializer.Serialize(saved.Stores);
        for (var cycle = 0; cycle < 3; ++cycle)
        {
            process = Guid.NewGuid(); factory = Guid.NewGuid(); constructor = Guid.NewGuid();
            field = new(source, stack, process, factory, constructor, actor, saved.Sequence, saved.Cell, saved);
            var rebound = RoundTrip(field.Capture(saved.Sequence, saved.Cell));
            Require(JsonSerializer.Serialize(rebound.Stores) == history && rebound.Sequence == saved.Sequence &&
                rebound.Publications.Count == saved.Publications.Count + 1 &&
                rebound.Publications[^1].PublishedRevision == saved.Sequence,
                "Cold Parent binding replayed a writer or replaced the retained field revision.");
            var validator = new FalloutReferenceParentCellValidation(rebound);
            foreach (var read in reads) validator.ValidateRead(read);
            reads.Add(Read(rebound)); validator.ValidateRead(reads[^1]);

            var destination = new FalloutFormKey("ParentColdFixture.esm", checked((uint)(0x800 + cycle)));
            var mainInvocation = Guid.NewGuid();
            field.BeginStore(mainInvocation, FalloutMainPlayerCellStep.CellAttach, saved.Sequence, saved.Cell, destination);
            field.ObserveStore(saved.Sequence + 1, destination); field.EndStore(null);
            saved = RoundTrip(field.Capture(saved.Sequence + 1, destination));
            history = JsonSerializer.Serialize(saved.Stores);
            Require(saved.Stores.Count(value => value.ConstructorStore) == 1 &&
                saved.Stores[^1].Process == process && saved.Stores[^1].MainInvocation == mainInvocation,
                "Cold Parent writer lost actual fresh Main lineage or repeated source construction.");
            reads.Add(Read(saved));
        }
        var actual = new FalloutReferenceParentCellValidation(saved);
        foreach (var read in reads) actual.ValidateRead(read);
        Reject(() => actual.ValidateRead(Read(saved) with { Sequence = saved.Sequence + 1 }));
        Reject(() => actual.ValidateRead(Read(saved) with { Cell = null }));
        Reject(() => actual.ValidateRead(Read(saved) with { Constructor = reads[0].Constructor }));
        var publications = saved.Publications.ToArray();
        publications[^1] = publications[^1] with { PublishedRevision = saved.Sequence };
        Reject(() => new FalloutReferenceParentCellValidation(saved with { Publications = publications }));
        publications = saved.Publications.ToArray();
        publications[^1] = publications[^1] with { PreviousFactory = Guid.NewGuid() };
        Reject(() => new FalloutReferenceParentCellValidation(saved with { Publications = publications }));
        var stores = saved.Stores.ToArray(); (stores[1], stores[2]) = (stores[2], stores[1]);
        Reject(() => new FalloutReferenceParentCellValidation(saved with { Stores = stores }));
        Reject(() => new FalloutReferenceParentCellHistory(source, stack, process, Guid.NewGuid(), Guid.NewGuid(),
            actor, saved.Sequence, saved.Cell, saved));
        Reject(() => new FalloutReferenceParentCellHistory(source, stack, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            actor, saved.Sequence + 1, saved.Cell, saved));

        var before = NewFresh();
        before.BeginStore(Guid.NewGuid(), FalloutMainPlayerCellStep.PendingDestination, 1, null, new("ParentColdFixture.esm", 0x900));
        before.EndStore(new InvalidOperationException("authored callback failed before actual Parent store"));
        var failed = RoundTrip(before.Capture(1, null));
        var failedCold = new FalloutReferenceParentCellHistory(source, stack, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            actor, failed.Sequence, failed.Cell, failed);
        var failedSaved = RoundTrip(failedCold.Capture(1, null));
        Require(failedSaved.Stores[^1] is { Stored: false, Returned: false, Failure: not null },
            "Cold Parent binding erased an actual pre-store failed prefix.");
        Reject(() => failedCold.BeginStore(Guid.NewGuid(), FalloutMainPlayerCellStep.CellAttach, 1, null,
            new("ParentColdFixture.esm", 0x901)));
        Console.WriteLine("OPENNV_PARENT_COLD_FIELD_PASS authoredScopeOnly=true cycles=3 constructorOnce=true unchangedRevisions=true " +
            "freshMainWriters=true historicalBorrows=true tamperRejected=true failedPrefixRetained=true wholeWorldNative=UNACCEPTED");

        FalloutReferenceParentCellHistory NewFresh() => new(source, stack, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), actor, 1, null);
        FalloutReferenceParentCellRead Read(FalloutReferenceParentCellSnapshot snapshot) =>
            new(snapshot.Source, snapshot.Stack, snapshot.Process, snapshot.Factory, snapshot.Constructor, snapshot.Actor,
                snapshot.Sequence, snapshot.Cell, snapshot.ExecutableSha256);
    }
    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    private static void Require(bool value, string error) { if (!value) throw new InvalidDataException(error); }
    private static void Reject(Action action)
    { try { action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ObjectDisposedException) { return; } throw new InvalidDataException("Cold Parent accepted an invalid field/history association."); }
}
