using OpenNV.Runtime.Gameplay.State;

internal static partial class SourceMainScriptCallerContracts
{
    // Real byte-owner producers/getters under authored component scopes.
    // This validates read persistence, not a returned Main or gameplay save.
    internal static void RunHistoricalInhibitionReadContracts()
    {
        using var actor = new LateActorTimeFixture();
        var source = actor.Capture().Source.Following; var stack = actor.Base.Stack;
        var caller = new FalloutPlayerSourceCaller(Guid.NewGuid(), 1, Guid.NewGuid());
        var process = Guid.NewGuid(); var constructor = Guid.NewGuid();
        var activeConsumer = FalloutPlayerInhibitionConsumer.Movement;
        var owner = new FalloutPlayerInhibitionState(source, stack, process, constructor, Guard);
        try
        {
            var reads = new Dictionary<FalloutPlayerInhibitionConsumer, FalloutPlayerInhibitionRead>();
            foreach (var consumer in Enum.GetValues<FalloutPlayerInhibitionConsumer>())
            {
                var read = Read(consumer, FalloutPlayerInhibitionLane.First); reads.Add(consumer, read);
                var history = Index(); RequireRead(history, read, read);
                Reject(() => RequireRead(history, read with { Source = new('e', 64) }, read));
                Reject(() => RequireRead(history, read with { Stack = stack + "-foreign" }, read));
                Reject(() => RequireRead(history, read with { Process = Guid.NewGuid() }, read));
                Reject(() => RequireRead(history, read with { Factory = Guid.NewGuid() }, read));
                Reject(() => RequireRead(history, read with { Caller = caller with { Invocation = Guid.NewGuid() } }, read));
                Reject(() => RequireRead(history, read with { Consumer = (FalloutPlayerInhibitionConsumer)999 }, read));
                Reject(() => RequireRead(history, read with { Lane = FalloutPlayerInhibitionLane.Second }, read));
                Reject(() => RequireRead(history, read with { Revision = 5 }, read));
                Reject(() => RequireRead(history, read with { Revision = owner.Capture().Sequence + 1 }, read));
                Reject(() => RequireRead(history, read with { Value = 1 }, read));
                Reject(() => history.Require(read, consumer, caller, process, FalloutPlayerInhibitionLane.First, Guid.NewGuid()));
            }
            owner.ObserveUnownedWriter(Guid.NewGuid(), "authored-unowned-first-writer", FalloutPlayerInhibitionLane.First);
            var unavailable = RoundTripPackage(owner.Capture());
            foreach (var read in reads.Values)
            {
                RequireRead(Index(), read, read);
                Reject(() => RequireRead(Index(), read with { Revision = unavailable.Sequence }, read));
            }
            owner.ApplySourceZeroSetter(Guid.NewGuid(), "authored-real-first-zero-setter", FalloutPlayerInhibitionLane.First);
            var restored = Read(FalloutPlayerInhibitionConsumer.Following, FalloutPlayerInhibitionLane.First);
            RequireRead(Index(), restored, restored);
            Reject(() => RequireRead(Index(), restored with { Revision = restored.Revision - 1 }, restored));
            foreach (var read in reads.Values) RequireRead(Index(), read, read);

            // The actual package producer stores one, not any nonzero value.
            var command = new OpenNV.Runtime.World.Cells.FalloutPackageCommandFieldInvocation(Guid.NewGuid(),
                actor.Base.Actor, actor.Base.Common.ReadSceneQueries(actor.Base.Actor.Reference, 1).Process, 1,
                Environment.CurrentManagedThreadId);
            owner.StorePackageAssignmentFourth(command, entered =>
            {
                if (entered != command) throw new InvalidOperationException("Authored package byte writer lost its entered command.");
            });
            var fourth = Read(FalloutPlayerInhibitionConsumer.Following, FalloutPlayerInhibitionLane.Fourth);
            Index().Require(fourth, fourth.Consumer, caller, process, FalloutPlayerInhibitionLane.Fourth, constructor);
            Reject(() => Index().Require(fourth with { Value = 2 }, fourth.Consumer, caller, process, FalloutPlayerInhibitionLane.Fourth, constructor));
            var saved = RoundTripPackage(owner.Capture());
            for (var cycle = 0; cycle < 3; cycle++)
            {
                owner.Retire(); process = Guid.NewGuid(); constructor = Guid.NewGuid();
                owner = new(source, stack, process, constructor, Guard, saved);
                var cold = Index();
                foreach (var read in reads.Values) RequireRead(cold, read, read);
                cold.Require(fourth, fourth.Consumer, caller, fourth.Process, fourth.Lane);
                var inherited = Read(FalloutPlayerInhibitionConsumer.LateFrame, FalloutPlayerInhibitionLane.First);
                RequireRead(cold, inherited, inherited);
                // A real current-process store ends every older publication's
                // readable interval, while inherited old reads remain valid.
                owner.ApplySourceZeroSetter(Guid.NewGuid(), "authored-real-cold-second-setter", FalloutPlayerInhibitionLane.Second);
                var written = Index();
                foreach (var read in reads.Values)
                {
                    RequireRead(written, read, read);
                    Reject(() => RequireRead(written, read with { Revision = owner.Capture().Sequence }, read));
                }
                var lateSecond = Read(FalloutPlayerInhibitionConsumer.LateFrame, FalloutPlayerInhibitionLane.Second);
                written.Require(lateSecond, lateSecond.Consumer, caller, process, lateSecond.Lane, constructor);
                saved = RoundTripPackage(owner.Capture());
            }
            Console.WriteLine("OPENNV_INHIBITION_HISTORICAL_READ_PASS movementFollowingLate=true sourceAndCaller=true " +
                "actualHistoricalWriter=true constructorAndRetirementIntervals=true noWriteCold=true coldCycles=3 " +
                "wholeMain=UNEXECUTED saveQuitColdContinue=UNEXECUTED");
        }
        finally { owner.Retire(); }

        FalloutPlayerInhibitionReadHistory Index() => new(RoundTripPackage(owner.Capture()), source, stack);
        FalloutPlayerInhibitionRead Read(FalloutPlayerInhibitionConsumer consumer, FalloutPlayerInhibitionLane lane)
        {
            activeConsumer = consumer;
            return consumer == FalloutPlayerInhibitionConsumer.LateFrame ? owner.ReadLateFrameLane(caller, lane) :
                owner.ReadCurrent(consumer, caller).Reads.Single(read => read.Lane == lane);
        }
        void Guard(FalloutPlayerInhibitionConsumer consumer, FalloutPlayerSourceCaller entered)
        {
            if (consumer != activeConsumer || entered != caller)
                throw new InvalidOperationException("Authored inhibition getter lost its entered consumer/caller.");
        }
        void RequireRead(FalloutPlayerInhibitionReadHistory history, FalloutPlayerInhibitionRead read,
            FalloutPlayerInhibitionRead expected) =>
            history.Require(read, expected.Consumer, caller, expected.Process, expected.Lane);
    }
}
