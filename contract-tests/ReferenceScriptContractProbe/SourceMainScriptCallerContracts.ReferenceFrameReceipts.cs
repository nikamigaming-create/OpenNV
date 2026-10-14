using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunReferenceFrameReceiptContracts()
    {
        using (var fixture = new ReferencePositionFixture())
        {
            fixture.PrepareReferenceAggregate(); fixture.PrepareFrameReceiptData();
            var baseline = fixture.Capture(); var main = Guid.NewGuid();
            FalloutReferencePositionCall? last = null;
            for (var ordinal = 1L; ordinal <= 100_000; ++ordinal)
                last = fixture.FrameReceiptWrite(main, ordinal, new((uint)ordinal, 0x80000000, (uint)(ordinal + 1)));
            var saved = fixture.Capture();
            Require(saved.Position.Calls.Count == baseline.Position.Calls.Count && saved.Changes.Calls.Count == baseline.Changes.Calls.Count &&
                saved.Thread.Calls.Count == baseline.Thread.Calls.Count && saved.Files.Calls.Count == baseline.Files.Calls.Count &&
                saved.Position.FrameRuns is [{ Frames: 100_000, Calls: 100_000, Stores: 300_000, FirstCalls.Count: 1, LastCalls.Count: 1 }] &&
                saved.Changes.FrameRuns is [{ Frames: 100_000, Calls: 100_000, Stores: 100_000 }] &&
                saved.Thread.FrameRuns is [{ Frames: 100_000, Calls: 100_000, Stores: 0 }] &&
                saved.Files.FrameRuns is [{ Frames: 100_000, Calls: 100_000, Stores: 0 }] &&
                saved.Position.Sequence == baseline.Position.Sequence + 500_000 && saved.Changes.Sequence == baseline.Changes.Sequence + 300_000 &&
                saved.Thread.Sequence == baseline.Thread.Sequence + 200_000 && saved.Files.Sequence == baseline.Files.Sequence + 200_000 &&
                saved.Position.Values.Position == last!.After.Position && saved.Changes.Entries.Single().Flags == 2,
                "Returned frame receipts grew archives, lost original revisions or replaced XYZ/mask authority.");
            Reject(() => fixture.FrameReceiptWrite(main, 99_999, new(1, 2, 3)));
            fixture.ExecuteReferenceAggregateCommand();
            var joined = fixture.CaptureReferenceAggregate(); ValidateReferenceAggregate(fixture, joined);
            var package = joined.Owners.Fields.Operations.Last(operation => operation.SavedPositionCall is not null).SavedPositionCall!;
            Require(package.PositionRead?.Sequence == saved.Position.Sequence && package.Position == saved.Position.Values.Position &&
                joined.Owners.Changes.Calls.Last().Mask == 0x800 && joined.Owners.Changes.Entries.Single().Flags == 0x802,
                "Ordinary package save borrowed a collapsed revision or lost its actual mask-800 effect.");
            fixture.FrameReceiptWrite(main, 100_001, new(4, 5, 6));
            var after = fixture.CaptureReferenceAggregate(); ValidateReferenceAggregate(fixture, after);
            Require(after.Owners.Position.FrameRuns!.Count == 2 && after.Owners.Position.FrameRuns[0].Changed == saved.Position.Sequence,
                "A command borrow was overwritten by later frame retention.");
            TestFrameReceiptTampering(fixture, after.Owners);
        }
        using (var original = new ReferencePositionFixture())
        {
            original.PrepareSavedPosition(); original.PrepareFrameReceiptData();
            original.FrameReceiptWrite(Guid.NewGuid(), 1, new(7, 8, 9));
            var saved = original.Capture(); var data = original._frameReceiptData!.Capture();
            var archive = JsonSerializer.Serialize(saved.Position.FrameRuns);
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                using var cold = new ReferencePositionFixture(saved); cold.PrepareFrameReceiptData(data);
                Require(cold.Events.Count == 0 && JsonSerializer.Serialize(cold.Position.Capture().FrameRuns) == archive &&
                    cold.Position.Capture().Sequence == saved.Position.Sequence,
                    "Cold frame retention replayed source stores or rebound historical field receipts.");
                cold.FrameReceiptWrite(Guid.NewGuid(), 1, new((uint)cycle, 8, 9));
                var next = cold.Capture();
                Require(next.Position.FrameRuns!.Count == saved.Position.FrameRuns!.Count + 1 &&
                    next.Position.FrameRuns[^1].FirstFrame.Parent.Frame.PlayerProcess == cold.Position.Process &&
                    next.Position.FrameRuns[^2].LastFrame.Parent.Frame.PlayerProcess == saved.Position.Process,
                    "Cold frame receipt mixed original histories with the fresh actual constructor/process.");
                saved = next; data = cold._frameReceiptData!.Capture(); archive = JsonSerializer.Serialize(saved.Position.FrameRuns);
            }
        }
        foreach (var child in new[] { "change", "reset", "reentry" })
        {
            using var fixture = new ReferencePositionFixture(); fixture.PrepareSavedPosition(); fixture.PrepareFrameReceiptData();
            fixture.FailChild = child;
            Reject(() => fixture.FrameReceiptWrite(Guid.NewGuid(), 1, new(0x3f800000, 2, 3), correction: child != "change"));
            var saved = fixture.Capture(); var failed = saved.Position.Calls.Last();
            Require(!failed.Returned && failed.Invocation.Frame is not null && failed.Stores.Count == 3 &&
                failed.After.Position == new FalloutPackageRawPosition(0x3f800000, 2, 3) && fixture.Position.SaveBlocker is not null,
                "Frame compaction hid a failed prefix or erased its committed paired XYZ stores.");
        }
        Console.WriteLine("OPENNV_REFERENCE_FRAME_RECEIPTS_PASS authoredScopeOnly=true sameSetters=true frameStores=100000 " +
            "boundedIntervals=true originalRevisions=true packageBoundary=true JSONColdCycles=3 failedPrefixes=true ActorBody=UNEXECUTED");
    }

    private static void TestFrameReceiptTampering(ReferencePositionFixture fixture, ReferencePositionFixtureSnapshot saved)
    {
        var position = saved.Position.FrameRuns!.ToArray(); var first = position[0];
        foreach (var bad in new[] { first with { Calls = first.Calls + 1 }, first with { Stores = first.Stores - 1 },
            first with { Changed = first.Changed + 1 }, first with { FirstFrame = first.FirstFrame with
                { Parent = first.FirstFrame.Parent with { Frame = first.FirstFrame.Parent.Frame with { PlayerProcess = Guid.NewGuid() } } } } })
        {
            position[0] = bad;
            Reject(() => FalloutSourceReferencePositionState.Validate(saved.Position with { FrameRuns = position }, fixture.Source, fixture.Stack, fixture.Actor));
        }
        var rows = first.LastCalls.ToArray(); rows[^1] = rows[^1] with { Stores = rows[^1].Stores.Skip(1).ToArray() };
        position[0] = first with { LastCalls = rows };
        Reject(() => FalloutSourceReferencePositionState.Validate(saved.Position with { FrameRuns = position }, fixture.Source, fixture.Stack, fixture.Actor));
        var changes = saved.Changes.FrameRuns!.ToArray(); changes[0] = changes[0] with { Stores = changes[0].Calls + 1 };
        Reject(() => FalloutSourceReferenceChangeManager.Validate(saved.Changes with { FrameRuns = changes }, fixture.Source, fixture.Stack));
    }

    private sealed partial class ReferencePositionFixture
    {
        internal FalloutReferenceModelDataState? _frameReceiptData;
        private FalloutReferenceModelDataInvocation? _frameReceiptDataCall;
        internal void PrepareFrameReceiptData(FalloutReferenceModelDataSnapshot? saved = null)
        {
            var source = FalloutPlayerMoverDeltaSource.Reviewed(FalloutEarlyControllerSource.Reviewed(Source.Fields, Thread.Source));
            _frameReceiptData = new(source, Records, Stack, Position.Process, Fields.Process, Fields.Constructor, Actor, 1,
                inv => { if (inv != _frameReceiptDataCall) throw new InvalidOperationException("Authored retained data left its actual scope."); },
                () => Records.RequireLiveLoadReceipt(Records.LoadReceipt), saved);
            if (saved is not null) return;
            _frameReceiptDataCall = new(Guid.NewGuid(), Actor, Position.Process, Fields.Constructor, Fields.Process, 1,
                Environment.CurrentManagedThreadId, FalloutReferenceModelDataIngress.ReferenceConstructor);
            try { _frameReceiptData.ConstructReferenceField(_frameReceiptDataCall); } finally { _frameReceiptDataCall = null; }
        }
        internal FalloutReferencePositionCall FrameReceiptWrite(Guid main, long ordinal, FalloutPackageRawPosition position, bool correction = false)
        {
            if (_current is not null) throw new InvalidOperationException("Authored frame reference child reentered.");
            var common = Common.ReadSceneQueries(Actor.Reference, 1);
            var frame = new FalloutPlayerLateFrameInvocation(new(main, ordinal, Guid.NewGuid()), Position.Process,
                Fields.Constructor, Guid.NewGuid(), common, Environment.CurrentManagedThreadId, 0);
            var data = _frameReceiptData!.ReadForMover(frame, inv =>
            { if (inv != frame) throw new InvalidOperationException("Authored frame data borrow changed its current parent."); });
            var parent = new FalloutPlayerActorMovementInvocation(Guid.NewGuid(), frame, new(0, 0, 0), 0, data);
            _current = new(Guid.NewGuid(), Actor, Position.Process, Fields.Constructor, Fields.Process, 1,
                Environment.CurrentManagedThreadId, FalloutReferencePositionIngress.ActorPosition,
                Frame: new(parent, correction ? FalloutReferenceFrameOperation.CorrectPosition : FalloutReferenceFrameOperation.BasePosition));
            try { return correction ? Position.StoreActorPosition(_current, position, this) : Position.StoreBasePosition(_current, position, this); }
            finally { _current = null; Events.Clear(); }
        }
    }
}
