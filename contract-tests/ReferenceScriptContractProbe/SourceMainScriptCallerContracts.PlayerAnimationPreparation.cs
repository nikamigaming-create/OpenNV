using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerAnimationPreparationContracts()
    {
        using (var fixture = new AnimationPreparationFixture())
        {
            fixture.InFrame((frame, guard) =>
            {
                var scene = fixture.Host.Scene;
                var baseline = scene.Capture();
                var initial = scene.ReadTailView(fixture.Source, frame, guard);
                var bracketConsumer = Guid.NewGuid();
                scene.StoreReturnTailAnimationBracket(frame, bracketConsumer, FalloutPlayerAnimationBracketChild.Updater, true, guard);
                scene.StoreReturnTailAnimationBracket(frame, bracketConsumer, FalloutPlayerAnimationBracketChild.Updater, false, guard);
                var current = scene.ReadTailView(fixture.Source, frame, guard);
                var toggle = scene.StoreFirstTailViewToggle(fixture.Source, frame, fixture.Preparation.Factory, initial, current, guard);
                var preparation = fixture.Preparation.Prepare(frame);
                Require(initial.Value == 0 && toggle.After == 1 && preparation.View is { Value: 1 } && preparation.Returned,
                    "Post-updater preparation borrowed the view from before its mandatory first toggle.");
                Require(current.Sequence > initial.Sequence, "Authored independent view reads lost their intervening Scene observations.");
                var viewFields = scene.CaptureViewFields();
                var viewSnapshot = baseline with { PlayerView = viewFields.Value, Sequence = viewFields.Sequence, ViewFields = viewFields };
                FalloutStandalonePlayerSceneState.ValidateViewFields(viewSnapshot);
                var forgedToggle = toggle with { Read = current with { Value = 1 }, After = 0 };
                Reject(() => FalloutStandalonePlayerSceneState.ValidateViewFields(viewSnapshot with
                { PlayerView = 0, ViewFields = viewFields with { Value = 0, LastWrite = forgedToggle,
                    Tail = viewFields.Tail! with { First = forgedToggle } } }));
                const string failure = "authored-unimplemented-status-child";
                var prefix = new FalloutPlayerPostAnimationPrefixCall(fixture.Source.Identity, frame, fixture.Preparation.Factory,
                    FalloutPlayerPostAnimationPrefixStep.Failed, FalloutPlayerPostAnimationPrefixStep.Status,
                    initial, current, toggle, preparation, failure);
                var parent = new FalloutPlayerLateFrameCall("authored-component-parent", fixture.Host.Stack, frame, 1, 2,
                    FalloutPlayerLateFrameStep.Failed, FalloutPlayerLateFrameStep.AnimationPreparation, Failure: failure, PostAnimation: prefix);
                void Check(FalloutPlayerPostAnimationPrefixCall actual) =>
                    FalloutStandalonePlayerSceneState.ValidatePostAnimationPrefix(actual, parent, fixture.Source.Tail, fixture.Host.Stack);
                Check(JsonSerializer.Deserialize<FalloutPlayerPostAnimationPrefixCall>(JsonSerializer.Serialize(prefix))!);
                Reject(() => Check(prefix with { Preparation = null }));
                Reject(() => Check(prefix with { FirstToggle = toggle with { After = 0 } }));
                Reject(() => Check(prefix with { CurrentView = current with { Process = Guid.NewGuid() } }));
                Reject(() => Check(prefix with { Preparation = preparation with { Frame = frame with { Prelude = Guid.NewGuid() } } }));
            });
        }
        using (var fixture = new AnimationPreparationFixture())
        {
            fixture.InFrame((frame, guard) =>
            {
                var call = fixture.Preparation.Prepare(frame);
                Require(call.Returned && call.Life is { Result: false } && call.Activity is { Value: 0 } && call.View is { Value: 0 } &&
                    call.Selected is null && call.Controller is null && fixture.ResolverCalls == 0,
                    "Preparation did not take its actual Player view-zero early return before AnimData/controller getters.");
                Validate(RoundTrip(call), fixture);
                Reject(() => Validate(call with { View = null }, fixture));
                Reject(() => fixture.Preparation.Prepare(frame));
            });
        }
        using (var fixture = new AnimationPreparationFixture())
        {
            var first = fixture.Host.ConstructFirstContext();
            fixture.InFrame((frame, guard) =>
            {
                var before = fixture.Host.Scene.ReadTailView(fixture.Source, frame, guard);
                fixture.Host.Scene.StoreFirstTailViewToggle(fixture.Source, frame, Guid.NewGuid(), before, before, guard);
                var operations = fixture.Host.Fields.Capture().Operations.Count;
                var call = fixture.Preparation.Prepare(frame);
                Require(call.Returned && call.View is { Value: 1 } &&
                    call.Selected is { FirstRead: false, First: null, Selection: FalloutPackageAnimationSelection.Process, Context: null, Common: not null } &&
                    call.Controller is { Controller: null, Context.Context: null } && fixture.ResolverCalls == 0 &&
                    fixture.Host.Fields.Capture().Values.FirstPersonAnimation == first.Identity,
                    "View1 borrowed First, omitted the mandatory controller getter, or dereferenced an unselected real context.");
                Validate(RoundTrip(call), fixture);
                Reject(() => Validate(call with { Controller = null }, fixture));
                Reject(() => Validate(call with { Selected = call.Selected! with { FirstRead = true, First = first.Identity } }, fixture));
                for (var index = 0; index < 1_000; index++)
                    fixture.Host.Fields.ReadAnimationForPreparation(fixture.Source, frame,
                        () => fixture.Host.Scene.ReadTailView(fixture.Source, frame, guard),
                        () => fixture.Host.Common.ReadAnimationContextForReturnTail(frame, guard), guard);
                Require(fixture.Host.Fields.Capture().Operations.Count == operations,
                    "An ordinary pure animation getter appended unbounded command history per frame.");
            });
        }
        using (var fixture = new AnimationPreparationFixture())
        {
            var first = fixture.Host.ConstructFirstContext();
            fixture.InFrame((frame, guard) =>
            {
                var selected = fixture.Host.Fields.ReadAnimationForPreparation(fixture.Source, frame,
                    () => fixture.Host.Scene.ReadTailView(fixture.Source, frame, guard),
                    () => throw new InvalidOperationException("Selected nonnull First must skip Common."), guard);
                Require(selected.Context == first.Identity && selected.FirstRead && selected.First == first.Identity &&
                    selected.Selection == FalloutPackageAnimationSelection.FirstPerson && selected.Common is null,
                    "The real nonnull First pointer getter was replaced by a nullable guard or another context.");
                var slot = first.ReadPreparationSlot(fixture.Source, frame, guard);
                var group = first.ReadPreparationGroup(fixture.Source, frame, guard);
                var savedSlot = JsonSerializer.Deserialize<FalloutAnimationPreparationSlotRead>(JsonSerializer.Serialize(slot))!;
                var savedGroup = JsonSerializer.Deserialize<FalloutAnimationPreparationGroupRead>(JsonSerializer.Serialize(group))!;
                Require(savedSlot == slot && savedGroup == group && group.Group == ushort.MaxValue && slot.Selected is null &&
                    group.Context == first.Identity && group.Factory == first.Factory && group.Construction == first.Construction,
                    "Preparation did not read the actual constructor-created slot/group or lost structural JSON identity.");
                Reject(() => first.ReadPreparationGroup(fixture.Source, frame with { PlayerConstructor = Guid.NewGuid() }, guard));
                first.ObserveUnownedWriter("authored-entered-sequence-writer", () => { });
                Reject(() => first.ReadPreparationSlot(fixture.Source, frame, guard));
            });
        }
        using (var fixture = new AnimationPreparationFixture())
        {
            fixture.Host.EnterLoad((load, guard) => fixture.Host.Fields.ObserveEnteredFirstPersonAnimationLoad(load, guard));
            fixture.InFrame((frame, guard) =>
            {
                var before = fixture.Host.Scene.ReadTailView(fixture.Source, frame, guard); var consumer = Guid.NewGuid();
                fixture.Host.Scene.StoreFirstTailViewToggle(fixture.Source, frame, consumer, before, before, guard);
                var selected = fixture.Host.Fields.ReadAnimationForPreparation(fixture.Source, frame,
                    () => fixture.Host.Scene.ReadTailView(fixture.Source, frame, guard),
                    () => fixture.Host.Common.ReadAnimationContextForReturnTail(frame, guard), guard);
                Require(selected.Selection == FalloutPackageAnimationSelection.Process && !selected.FirstRead &&
                    fixture.Host.Fields.Capture().AnimationTail!.AnimationWriter is not null,
                    "A skipped unavailable First lane blocked the independent valid Common getter or erased its real writer.");
                var current = fixture.Host.Scene.ReadTailView(fixture.Source, frame, guard);
                fixture.Host.Scene.StoreSecondTailViewToggle(fixture.Source, frame, consumer, current, guard);
                Reject(() => fixture.Host.Fields.ReadAnimationForPreparation(fixture.Source, frame,
                    () => fixture.Host.Scene.ReadTailView(fixture.Source, frame, guard),
                    () => throw new InvalidOperationException("Unknown First cannot manufacture its null fallback."), guard));
            });
        }
        using (var fixture = new AnimationPreparationFixture())
        {
            fixture.Runtime.RetainUnownedLifeTransition(fixture.Host.Actor.Reference, "authored-real-life-writer");
            fixture.InFrame((frame, guard) =>
            {
                Reject(() => fixture.Preparation.Prepare(frame));
                var call = fixture.Preparation.Capture()!;
                Require(!call.Returned && call.FailedAt == FalloutPlayerAnimationPreparationStep.Life &&
                    call.Life is { Raw.Value: null, Result: null } && call.Activity is null && call.View is null && call.Controller is null,
                    "Failed actual Life read erased its prefix or invented later preparation effects.");
                Validate(RoundTrip(call), fixture);
                Reject(() => Validate(call with { Step = FalloutPlayerAnimationPreparationStep.Returned, Returned = true, FailedAt = null, Failure = null }, fixture));
                Reject(() => Validate(call with { FailedAt = (FalloutPlayerAnimationPreparationStep)999 }, fixture));
                Reject(() => Validate(call with { View = fixture.Host.Scene.ReadTailView(fixture.Source, frame, guard) }, fixture));
            });
        }
        Console.WriteLine("OPENNV_PLAYER_ANIMATION_PREPARATION_PASS authoredScopeOnly=true realEarlyReturns=true " +
            "mandatoryControllerGetter=true realNonnullFirstGetter=true skippedUnknownFirstPreserved=true boundedReadHistory=true " +
            "sameContextSlotAndGroup=true independentJSON=true failedPrefix=true nonnullActionSelection=UNOWNED completeTail=UNRETURNED coldGame=UNEXECUTED");

        static FalloutPlayerAnimationPreparationCall RoundTrip(FalloutPlayerAnimationPreparationCall call) =>
            JsonSerializer.Deserialize<FalloutPlayerAnimationPreparationCall>(JsonSerializer.Serialize(call))!;
        static void Validate(FalloutPlayerAnimationPreparationCall call, AnimationPreparationFixture fixture) =>
            FalloutPlayerAnimationPreparationState.Validate(call, fixture.Source, fixture.Host.Stack);
    }

    private sealed class AnimationPreparationFixture : IDisposable
    {
        internal readonly PlayerReturnTailFixture Host = new();
        internal readonly FalloutPlayerPostAnimationSource Source;
        internal readonly FalloutActorProcessRuntimeState Runtime;
        internal readonly FalloutPlayerAnimationPreparationState Preparation;
        private FalloutPlayerLateFrameInvocation? _frame;
        private bool _alive = true;
        internal int ResolverCalls;
        internal AnimationPreparationFixture()
        {
            Source = FalloutPlayerPostAnimationSource.Reviewed(Host.State.Source);
            Runtime = new(FalloutActorProcessRuntimeDeclaration.ForExecutable(Source.Tail.Scene.Main.EngineSha256),
                Host.Stack, Host.Actor.Reference, _ => Host.Actor);
            Runtime.Construct(Host.Actor.Reference);
            Preparation = new(Source, Host.Stack, Host.Scene.Process, Host.Scene.Constructor,
                Host.Common.ReadSceneQueries(Host.Actor.Reference, 1).Process,
                new(Runtime, Host.Common, Host.Fields, Host.Scene,
                    _ => { ++ResolverCalls; throw new InvalidOperationException("Real early-return contracts cannot invent a nonnull preparation body."); }),
                Guard, Lifetime);
        }
        internal void InFrame(Action<FalloutPlayerLateFrameInvocation, Action<FalloutPlayerLateFrameInvocation>> action)
        {
            var main = Guid.NewGuid();
            var argument = Host.Scene.StoreSceneClock(main, 1, () => AuthoredSceneCachedDelta(Host.Scene, 0x3c800000));
            Host.Scene.EnterSceneVirtual(main, 1);
            _frame = new(new(main, 1, Guid.NewGuid()), Host.Scene.Process, Host.Scene.Constructor, Guid.NewGuid(),
                Host.Common.ReadSceneQueries(Host.Actor.Reference, 1), Environment.CurrentManagedThreadId, argument);
            try { action(_frame, Guard); } finally { _frame = null; }
        }
        private void Guard(FalloutPlayerLateFrameInvocation frame)
        { Lifetime(); if (frame != _frame) throw new InvalidOperationException("Authored preparation escaped its actual current component frame."); }
        private void Lifetime() => ObjectDisposedException.ThrowIf(!_alive, this);
        public void Dispose()
        {
            Preparation.Retire(); Runtime.Dispose(); Host.Dispose(); _alive = false;
        }
    }
}
