using System.Text.Json;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

internal static partial class SourceMainScriptCallerContracts
{
    internal static void RunPlayerViewOwnerContracts()
    {
        using (var fixture = new PlayerReturnTailFixture())
        {
            var source = FalloutPlayerPostAnimationSource.Reviewed(fixture.State.Source);
            var scene = fixture.Scene;
            fixture.EnterLoad((load, guard) =>
            {
                var entry = scene.EnterAnimationLoadView(source, load, guard);
                var failedPrefix = RoundTrip(scene.Capture());
                Require(entry.Read.Value == 0 && entry.After == 1 && failedPrefix.PlayerView == 1 &&
                    failedPrefix.ViewFields?.LastWrite == entry && scene.SaveBlocker == "source-Player-animation-load-temporary-view-unrestored",
                    "Actual temporary loader view store was omitted or aliased to presentation.");
                FalloutStandalonePlayerSceneState.Validate(failedPrefix);
                Reject(() => FalloutStandalonePlayerSceneState.Validate(failedPrefix with { ViewFields = null }));
                Reject(() => FalloutStandalonePlayerSceneState.Validate(failedPrefix with { PlayerView = 0 }));
                Reject(() => scene.RestoreAnimationLoadView(source, load, entry with { Consumer = Guid.NewGuid() }, guard));
                var restored = scene.RestoreAnimationLoadView(source, load, entry, guard);
                Require(restored.After == entry.Read.Value && scene.Capture().PlayerView == 0 && scene.SaveBlocker is null,
                    "Actual loader did not restore exactly its retained prior view byte.");
                Reject(() => scene.RestoreAnimationLoadView(source, load, entry, guard));
            });

            var saved = RoundTrip(scene.Capture());
            for (var cycle = 0; cycle < 3; cycle++)
            {
                var cold = new FalloutStandalonePlayerSceneState(scene.Source, fixture.Stack, Guid.NewGuid(), saved);
                try
                {
                    var inherited = cold.Capture();
                    Require(inherited.ViewFields!.Publications.Count == saved.ViewFields!.Publications.Count + 1 &&
                        inherited.ViewFields.Load == saved.ViewFields.Load && inherited.ViewFields.LastWrite == saved.ViewFields.LastWrite &&
                        inherited.ViewFields.Value == saved.PlayerView,
                        "Scene-only cold view reconstruction replayed a loader store or erased original process lineage.");
                    var historical = inherited.ViewFields.Load!;
                    var futureStore = historical.Second! with { Changed = inherited.ViewFields.Publications[^1].Published };
                    Reject(() => FalloutStandalonePlayerSceneState.Validate(inherited with { ViewFields = inherited.ViewFields with
                        { Load = historical with { Second = futureStore }, LastWrite = futureStore } }));
                    var load = new FalloutReferenceModelDataInvocation(Guid.NewGuid(), fixture.Actor, cold.Process,
                        cold.Constructor, fixture.Common.ReadSceneQueries(fixture.Actor.Reference, 1).Process, 1,
                        Environment.CurrentManagedThreadId, FalloutReferenceModelDataIngress.ThreeDLoad);
                    var active = true;
                    void Guard(FalloutReferenceModelDataInvocation actual)
                    { if (!active || actual != load) throw new InvalidOperationException("Authored cold load escaped its real component scope."); }
                    var entry = cold.EnterAnimationLoadView(source, load, Guard);
                    cold.RestoreAnimationLoadView(source, load, entry, Guard); active = false;
                    saved = RoundTrip(cold.Capture());
                    var fields = saved.ViewFields!;
                    var publications = fields.Publications.ToArray();
                    publications[^1] = publications[^1] with { PreviousConstructor = Guid.NewGuid() };
                    Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ViewFields = fields with { Publications = publications } }));
                    var original = fields.Load!;
                    Reject(() => FalloutStandalonePlayerSceneState.Validate(saved with { ViewFields = fields with
                        { Load = original with { Second = original.Second! with { After = 1 } } } }));
                }
                finally { cold.Retire(); }
            }
        }
        using (var fixture = new PlayerReturnTailFixture())
        {
            var source = FalloutPlayerPostAnimationSource.Reviewed(fixture.State.Source);
            var baseline = fixture.Scene.Capture();
            var main = Guid.NewGuid();
            var argument = fixture.Scene.StoreSceneClock(main, 1, () => AuthoredSceneCachedDelta(fixture.Scene, 0x3c800000));
            fixture.Scene.EnterSceneVirtual(main, 1);
            var frame = new FalloutPlayerLateFrameInvocation(new(main, 1, Guid.NewGuid()), fixture.Scene.Process,
                fixture.Scene.Constructor, Guid.NewGuid(), fixture.Common.ReadSceneQueries(fixture.Actor.Reference, 1),
                Environment.CurrentManagedThreadId, argument);
            var active = true;
            void Guard(FalloutPlayerLateFrameInvocation actual)
            { if (!active || actual != frame) throw new InvalidOperationException("Authored view child escaped its actual entered component frame."); }
            var before = fixture.Scene.ReadTailView(source, frame, Guard); var consumer = Guid.NewGuid();
            var first = fixture.Scene.StoreFirstTailViewToggle(source, frame, consumer, before, before, Guard);
            Require(first.Read.Value == 0 && first.After == 1 && fixture.Scene.SaveBlocker == "source-Player-tail-second-view-toggle-unreached",
                "First toggle lost its actual mutable Scene field or claimed the unfinished suffix returned.");
            var failed = fixture.Scene.CaptureViewFields();
            var authoredOuter = baseline with { PlayerView = failed.Value, Sequence = failed.Sequence, ViewFields = failed };
            FalloutStandalonePlayerSceneState.ValidateViewFields(RoundTrip(authoredOuter));
            Reject(() => fixture.Scene.StoreFirstTailViewToggle(source, frame, consumer, before, before, Guard));
            var fresh = fixture.Scene.ReadTailView(source, frame, Guard);
            Reject(() => fixture.Scene.StoreSecondTailViewToggle(source, frame, Guid.NewGuid(), fresh, Guard));
            var second = fixture.Scene.StoreSecondTailViewToggle(source, frame, consumer, fresh, Guard);
            var returnedStores = fixture.Scene.CaptureViewFields();
            Require(second.After == 0 && returnedStores.Tail!.First == first && returnedStores.Tail.Second == second &&
                returnedStores.LastWrite == second && returnedStores.Load is null,
                "The second source toggle did not reuse the original view field and fresh current read.");
            var authoredReturnedOuter = baseline with { PlayerView = returnedStores.Value, Sequence = returnedStores.Sequence, ViewFields = returnedStores };
            FalloutStandalonePlayerSceneState.ValidateViewFields(RoundTrip(authoredReturnedOuter));
            var tail = returnedStores.Tail!;
            foreach (var forged in new[]
            {
                returnedStores with { LastWrite = first },
                returnedStores with { Tail = tail with { First = first with { After = 0 } } },
                returnedStores with { Tail = tail with { Second = second with { Read = fresh with { Process = Guid.NewGuid() } } } },
                returnedStores with { Tail = tail with { Second = second with { After = 1 } } }
            }) Reject(() => FalloutStandalonePlayerSceneState.ValidateViewFields(authoredReturnedOuter with { ViewFields = forged }));
            active = false;
            Reject(() => fixture.Scene.ReadTailView(source, frame, Guard));
        }
        Console.WriteLine("OPENNV_PLAYER_VIEW_OWNER_PASS authoredScopeOnly=true sameSceneField=true temporaryAndExactRestore=true " +
            "twoDistinctToggles=true failedPrefix=true boundedLatest=true sceneOnlyJSONCold3=true wholePlayer=UNRETURNED wholeSaveContinue=UNEXECUTED");
        static FalloutStandalonePlayerSceneSnapshot RoundTrip(FalloutStandalonePlayerSceneSnapshot saved) =>
            JsonSerializer.Deserialize<FalloutStandalonePlayerSceneSnapshot>(JsonSerializer.Serialize(saved))!;
    }

    internal static void AuditOwnedPlayerPostAnimation(string executable, FalloutPlayerReturnTailSource tail)
    {
        var actual = FalloutExecutableStringTable.ReadPlayerPostAnimation(executable, tail);
        Require(actual == FalloutPlayerPostAnimationSource.Reviewed(tail), "Actual selected post-animation source changed its declaration.");
        Console.WriteLine("OPENNV_PLAYER_POST_ANIMATION_SOURCE_PASS ownedSelectedBytes=true loaderAndTailSameView=true " +
            "actualPreparationOrder=true completePreparation=UNIMPLEMENTED completeTail=UNRETURNED gameplay=UNEXECUTED");
    }
}
