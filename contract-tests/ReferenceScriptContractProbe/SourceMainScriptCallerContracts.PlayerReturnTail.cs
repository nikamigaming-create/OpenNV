using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerReturnTailContracts()
    {
        using (var fixture = new PlayerReturnTailFixture())
        {
            var call = fixture.Update();
            Require(call.Returned && call.RawLife?.Value == 0 && call.Delay?.Name == FalloutPlayerReturnTailSource.DeathDelayName &&
                call.Stores is [{ Field: FalloutPlayerReturnTailField.DeathTimer, After: 0xbf800000 },
                    { Field: FalloutPlayerReturnTailField.AnimationBracket, Before: 1, After: 2 },
                    { Field: FalloutPlayerReturnTailField.AnimationBracket, Before: 2, After: 1 }] &&
                call.Animations.Count == 2 && call.Animations.All(row => row.Context is null) && call.ContextUpdates.Count == 0 &&
                call.Queue is { Returned: true, Head: null, Next: null, Delay.Name: FalloutPlayerReturnTailSource.PairDelayName } &&
                call.Extra is { Extra: null, Payload: null } && fixture.Scene.CaptureReturnTailBracketStores().Count == 2,
                "Actual tail prefix lost the living overwrite, SAME Scene bracket or independent null/empty children.");
            var saved = fixture.State.Capture(); FalloutPlayerReturnTailState.Validate(saved);
            foreach (var forged in new[]
            {
                call with { Queue = null }, call with { Extra = null }, call with { Animations = [call.Animations[0]] },
                call with { Stores = call.Stores.Take(2).ToArray() },
                call with { Stores = [call.Stores[0], call.Stores[1] with { SharedBracket = call.Stores[1].SharedBracket! with
                    { SceneProcess = Guid.NewGuid() } }, call.Stores[2]] }
            }) Reject(() => FalloutPlayerReturnTailState.Validate(saved with { LastCall = forged }));
            var cold = fixture.Capture();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                using var next = new PlayerReturnTailFixture(cold);
                Require(next.State.Capture().LastCall!.Factory == cold.Tail.LastCall!.Factory &&
                    next.Fields.Capture().Operations.Count == cold.Fields.Operations.Count &&
                    next.Initializer.Capture().Calls.Count == cold.Fields.ScriptInitialization!.Calls.Count,
                    "Component cold restore replayed source constructor/command/timer work.");
                var returned = next.Update(); Require(returned.Returned, "Fresh actual component cold call did not return.");
                cold = next.Capture();
            }
        }
        using (var fixture = new PlayerReturnTailFixture())
        {
            fixture.State.ObserveUnownedSharedWriter(FalloutPlayerReturnTailField.DeathTimer, "authored-real-writer", () => { });
            var call = fixture.Update();
            Require(call.Stores[0].Before is null && call.Stores[0].After == FalloutPlayerReturnTailSource.InitialDeathTimerBits &&
                fixture.State.Capture().DeathTimerWriter is null,
                "Actual unconditional living timer overwrite borrowed an unknown prior value or left an artificial read guard.");
        }
        using (var fixture = new PlayerReturnTailFixture())
        {
            fixture.Common.ObserveUnownedAnimationPairWriter(fixture.Actor.Reference, 1, "authored-entered-pair-writer", () => { });
            Reject(() => fixture.Update()); var call = fixture.State.LastCall!;
            Require(call.FailedAt == FalloutPlayerReturnTailStep.ProcessAnimationQueue && call.Queue is null && call.Extra is null &&
                call.Stores.Count == 2 && fixture.Scene.CaptureReturnTailBracketStores() is [{ Before: 1, After: 2, Enter: true }],
                "Pair writer failure was swallowed or restored the real shared bracket in cleanup.");
            FalloutPlayerReturnTailState.Validate(fixture.State.Capture());
        }
        using (var fixture = new PlayerReturnTailFixture())
        {
            var context = fixture.ConstructFirstContext();
            var saved = fixture.Fields.Capture(); FalloutActorPackageCommandFields.Validate(saved, fixture.Fields.Source, fixture.Stack, fixture.Actor);
            Require(saved.Values.FirstPersonAnimation == context.Identity && saved.AnimationTail!.LoadWriters is
                [{ Kind: FalloutReferenceAnimationLoadWriterKind.EnteredLoad },
                 { Kind: FalloutReferenceAnimationLoadWriterKind.FirstContextStore, Returned: true }],
                "Actual AnimData constructor did not publish its pointer into the SAME existing reference fields.");
            var pending = (saved.AnimationTail ?? throw new InvalidDataException("First context omitted its load history.")).LoadWriters.ToArray();
            pending[1] = pending[1] with { Construction = null };
            Reject(() => FalloutActorPackageCommandFields.Validate(saved with
                { AnimationTail = saved.AnimationTail with { LoadWriters = pending } }, fixture.Fields.Source, fixture.Stack, fixture.Actor));
            var call = fixture.Update();
            Require(call.Returned && call.ContextUpdates is [{ Returned: true, RootFactory: null }] &&
                call.ContextUpdates[0].Context == context.Identity,
                "A genuine newly constructed context did not execute its qualified root-null body.");
        }
        using (var fixture = new PlayerReturnTailFixture())
        {
            var context = fixture.ConstructFirstContext(); context.ObserveUnownedWriter("actual-entered-model-bind", () => { });
            Reject(() => fixture.Update());
            Require(fixture.Fields.Capture().Values.FirstPersonAnimation == context.Identity &&
                fixture.State.LastCall is { FailedAt: FalloutPlayerReturnTailStep.FirstAnimation, ContextUpdates.Count: 1 } failed &&
                !failed.ContextUpdates[0].Returned && failed.Stores.Count == 2,
                "Failed AnimData work erased its genuine pointer publication or restored a failed bracket.");
        }
        using (var fixture = new PlayerReturnTailFixture())
        {
            fixture.EnterLoad((invocation, guard) => fixture.Common.ObserveEnteredPlayerAnimationLoad(invocation, guard));
            Reject(() => fixture.Common.ReadSceneQueries(fixture.Actor.Reference, 1));
            Require(fixture.State.LastCall is null && fixture.Common.Capture().Current.Single().SceneFields?.AnimationContextWriter is not null,
                "Entered 3D load reused the old Common constructor-null animation pointer.");
        }
        using (var fixture = new PlayerReturnTailFixture())
        {
            fixture.EnterLoad((invocation, guard) => fixture.Fields.ObserveUnownedAnimationNodeMembershipWriter(invocation,
                "authored-actual-list-writer", guard));
            var saved = fixture.Fields.Capture(); var fields = saved.AnimationTail!;
            FalloutActorPackageCommandFields.Validate(saved, fixture.Fields.Source, fixture.Stack, fixture.Actor);
            Reject(() => FalloutActorPackageCommandFields.Validate(saved with { AnimationTail = fields with
                { Membership = fields.Membership! with { Writer = null } } }, fixture.Fields.Source, fixture.Stack, fixture.Actor));
            Reject(() => fixture.Update());
            Require(fixture.State.LastCall is { FailedAt: FalloutPlayerReturnTailStep.ReferenceAnimationExtra, Extra: null } &&
                fixture.Scene.CaptureReturnTailBracketStores() is [{ Enter: true }],
                "Entered same-list model writer reused old Extra41 null membership or restored a failed bracket.");
        }
        Console.WriteLine("OPENNV_PLAYER_RETURN_TAIL_PASS authoredScopeOnly=true deathOverwrite=true sharedSceneBracket=true " +
            "mandatoryNullContexts=true realFirstContextConstructorAndStore=true pairAndExtraWriters=true componentJSONCold3=true wholePlayer=UNRETURNED boundRootUpdate=UNOWNED");
    }

    private sealed record PlayerReturnTailFixtureSnapshot(ReferencePositionFixtureSnapshot Reference,
        FalloutPackageCommandFieldSnapshot Fields, FalloutPlayerReturnTailSnapshot Tail);
    private sealed class PlayerReturnTailFixture : IDisposable
    {
        private readonly ReferencePositionFixture _reference;
        internal readonly FalloutStandalonePlayerSceneState Scene;
        internal readonly FalloutActorPackageCommandFields Fields;
        internal readonly FalloutReferenceScriptExtraInitializer Initializer;
        internal readonly FalloutPlayerReturnTailState State;
        internal FalloutActorProcessCommonState Common => _reference.Common;
        internal FalloutCombatActorIdentity Actor => _reference.Actor;
        internal string Stack => _reference.Stack;
        private readonly FalloutActorProcessRuntimeState _runtime;
        private readonly FalloutSourceFrameTimerState _timer;
        private readonly Dictionary<Guid, FalloutSourceAnimationContextState> _contexts = [];
        private FalloutPlayerLateFrameInvocation? _frame;
        private FalloutReferenceModelDataInvocation? _load;
        private FalloutReferenceScriptExtraInitializationInvocation? _initialization;
        internal PlayerReturnTailFixture(PlayerReturnTailFixtureSnapshot? saved = null,
            FalloutPluginStack? original = null, FalloutPlayerMovementControlSource? controls = null)
        {
            _reference = original is null ? ReferencePositionFixture.CreateReturnTailRecords(saved?.Reference) :
                new ReferencePositionFixture(original: original, controls: controls);
            var assignmentSource = FalloutActorPackageAssignmentSource.Reviewed(_reference.Fields.Source);
            Common.BindActorPackageAssignment(assignmentSource, Actor.Reference, 1, actual =>
            {
                if (actual.Actor != Actor || actual.Epoch != 1 || actual.Process != _reference.Fields.Process ||
                    actual.Level != FalloutDetectionProcessLevel.High)
                    throw new InvalidOperationException("Authored animation load changed the actual initial High constructor.");
            });
            var main = _reference.Source.Fields.Controls.Main;
            Scene = new(FalloutStandalonePlayerSceneSource.Read(main), Stack, Guid.NewGuid());
            Fields = new(_reference.Fields.Source, Stack, Actor, Common.ReadSceneQueries(Actor.Reference, 1).Process,
                1, Scene.Constructor, _ => throw new InvalidOperationException("Tail fixture cannot fabricate a package command."),
                new("authored-independent-reference-constructor", PrecedingWritersClosed: true), saved?.Fields);
            Initializer = new(FalloutReferenceScriptExtraInitializerSource.Reviewed(Fields.Source), _reference.Records,
                Fields, Scene.ThreadContext, invocation =>
                { if (_initialization != invocation) throw new InvalidOperationException("Authored source initializer escaped its actual scope."); });
            if (saved is null)
            {
                _initialization = new(Guid.NewGuid(), Actor, Fields.Process, 1, Fields.Constructor, Environment.CurrentManagedThreadId);
                try { Initializer.Initialize(_initialization); } finally { _initialization = null; }
            }
            var mover = FalloutPlayerMoverDeltaSource.Reviewed(FalloutEarlyControllerSource.Reviewed(Fields.Source, Scene.ThreadContext.Source));
            var source = FalloutPlayerReturnTailSource.Reviewed(FalloutPlayerActorMovementSource.Reviewed(mover,
                FalloutInitialPlacementCellsSource.Reviewed(_reference.Source),
                FalloutPlayerWorldOrientationSource.Reviewed(Fields.Source.Controls)), Scene.Source);
            Fields.BindAnimationNodeExtraMembership(source, Initializer, () => { }, saved?.Fields.AnimationTail?.Membership);
            _runtime = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(main.EngineSha256), Stack, Actor.Reference, _ => Actor);
            _runtime.Construct(Actor.Reference);
            var timerSource = FalloutSourceFrameTimer.Read(new(main.EngineSha256, new('a', 64), new('b', 64), new('c', 64),
                new(FalloutSkillPointOperand.Literal(10), FalloutSkillPointOperand.Literal(1), 0, 2, 1, 10, FalloutSkillPointRounding.Floor),
                new(1, 10, FalloutPermanentIntelligenceInteger.Floor), new("iLevelsPerPerk")));
            _timer = new(timerSource, new ReturnTailCounter(), Scene.Process);
            var owners = new FalloutPlayerReturnTailOwners(_reference.Records, _runtime, Common, Fields, Initializer, _timer,
                (invocation, factory) => Scene.ReadReturnTailAnimationClock(invocation, factory, _timer, RequireFrame),
                (invocation, factory, child, enter) => Scene.StoreReturnTailAnimationBracket(invocation, factory, child, enter, RequireFrame),
                context => _contexts.TryGetValue(context, out var owner) ? owner : throw new InvalidOperationException("No actual AnimData constructor."));
            State = new(source, Stack, Scene.Process, Scene.Constructor, Fields.Process, owners, RequireFrame,
                () => _reference.Records.RequireLiveLoadReceipt(_reference.Records.LoadReceipt), saved?.Tail);
        }
        internal FalloutPlayerReturnTailCall Update()
        {
            var main = Guid.NewGuid(); const long ordinal = 1;
            var argument = Scene.StoreSceneClock(main, ordinal, _timer.ReadScaledDelta); Scene.EnterSceneVirtual(main, ordinal);
            _frame = new(new(main, ordinal, Guid.NewGuid()), Scene.Process, Scene.Constructor, Guid.NewGuid(),
                Common.ReadSceneQueries(Actor.Reference, 1), Environment.CurrentManagedThreadId, argument);
            try { return State.UpdateThroughAnimation(_frame); } finally { _frame = null; }
        }
        internal FalloutSourceAnimationContextState ConstructFirstContext()
        {
            FalloutSourceAnimationContextState? context = null;
            EnterLoad((invocation, guard) =>
            {
                Fields.ObserveEnteredFirstPersonAnimationLoad(invocation, guard);
                context = new(State.Source, invocation, guard, () => _reference.Records.RequireLiveLoadReceipt(_reference.Records.LoadReceipt));
                _contexts.Add(context.Identity, context); Fields.StoreConstructedFirstPersonAnimation(invocation, context, guard);
            });
            return context!;
        }
        internal void EnterLoad(Action<FalloutReferenceModelDataInvocation, Action<FalloutReferenceModelDataInvocation>> operation)
        {
            _load = new(Guid.NewGuid(), Actor, Scene.Process, Scene.Constructor, Fields.Process, 1,
                Environment.CurrentManagedThreadId, FalloutReferenceModelDataIngress.ThreeDLoad);
            try { operation(_load, actual => { if (_load != actual) throw new InvalidOperationException("Authored 3D load left its current source scope."); }); }
            finally { _load = null; }
        }
        private void RequireFrame(FalloutPlayerLateFrameInvocation invocation)
        { if (_frame != invocation) throw new InvalidOperationException("Authored tail child left its current frame scope."); }
        internal PlayerReturnTailFixtureSnapshot Capture() => JsonSerializer.Deserialize<PlayerReturnTailFixtureSnapshot>(JsonSerializer.Serialize(
            new PlayerReturnTailFixtureSnapshot(_reference.Capture(), Fields.Capture(), State.Capture())))!;
        public void Dispose()
        {
            foreach (var context in _contexts.Values) context.Retire();
            State.Retire(); Fields.Retire(); Scene.Retire(); _timer.Dispose(); _runtime.Dispose(); _reference.Dispose();
        }
    }
    private sealed class ReturnTailCounter : IFalloutSourceTickCounter
    {
        public string Owner => "authored-source-tail-counter";
        public uint Read() => 0;
        public void Wait(uint milliseconds) => throw new InvalidOperationException("Tail getter must not poll/wait.");
    }
    private sealed partial class ReferencePositionFixture
    {
        internal static ReferencePositionFixture CreateReturnTailRecords(ReferencePositionFixtureSnapshot? saved = null)
        {
            return new(saved, appendedRows: Join(
                Row("GMST", 0x780, Part("EDID", "fPlayerDeathReloadTime\0"u8.ToArray()), Part("DATA", BitConverter.GetBytes(5f))),
                Row("GMST", 0x781, Part("EDID", "fExplodeLimbRemovalDelay\0"u8.ToArray()), Part("DATA", BitConverter.GetBytes(.25f))),
                Row("GMST", 0x782, Part("EDID", "fExplodeLimbRemovalDelayVATS\0"u8.ToArray()), Part("DATA", BitConverter.GetBytes(.25f)))));
        }
    }
}
