using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPlayerWeaponContinuationSource? _standaloneWeaponSource;
    internal FalloutPlayerWeaponContinuationCall? LastStandalonePlayerWeaponContinuation { get; private set; }

    internal void InitializeStandalonePlayerWeaponActionConstructor()
    {
        RequireStandaloneControlPublication();
        var scene = CampaignStandalonePlayerScene;
        var owned = records.OwnedSource ?? throw new InvalidOperationException("Player weapon construction lost its actual selected source.");
        var source = FalloutExecutableStringTable.ReadPlayerWeaponContinuation(owned.FalloutExecutablePath,
            FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
        if (_standaloneWeaponSource is not null) throw new InvalidOperationException("Player weapon constructor publication was replayed.");
        var actor = ActorProcesses.Read(_enginePlayer);
        if (_processRuntimeRestore is null)
        {
            var constructor = scene.Capture();
            ProcessCommon.InitializePlayerWeaponActionConstructor(_enginePlayer, actor.Epoch, source, constructor.Constructor, () =>
            {
                RequireStandaloneControlPublication(); var current = scene.Capture();
                if (_actorProcessStack != scene.Stack || actor.Source != ActorProcesses.Read(_enginePlayer).Source ||
                    actor.Epoch != ActorProcesses.Read(_enginePlayer).Epoch || actor.Retired || current.Clock is not null ||
                    current.Constructor != constructor.Constructor || current.CapturedProcess != scene.Process)
                    throw new InvalidOperationException("Player weapon byte left its actual fresh source construction lifetime.");
            });
        }
        else _ = ProcessCommon.ReadPlayerWeaponAction(_enginePlayer, actor.Epoch);
        ProcessCommon.RequirePlayerWeaponContinuationSource(_enginePlayer, actor.Epoch, source);
        _standaloneWeaponSource = source;
        scene.BindPlayerWeaponContinuation(source, UpdateStandalonePlayerWeaponContinuation,
            () => LastStandalonePlayerWeaponContinuation);
    }

    internal void ObserveStandalonePlayerWeaponContinuationWriter(Guid observation, string producer,
        FalloutProcessWeaponObservedLane lane)
    {
        RequireStandaloneControlPublication();
        var actor = ActorProcesses.Read(_enginePlayer);
        ProcessCommon.ObservePlayerWeaponContinuationWriter(_enginePlayer, actor.Epoch, observation, producer, lane);
    }

    internal FalloutPlayerWeaponContinuationCall UpdateStandalonePlayerWeaponContinuation(Guid main, long ordinal, Guid invocation)
    {
        RequireStandaloneControlPublication(); var scene = CampaignStandalonePlayerScene;
        var source = _standaloneWeaponSource ?? throw new NotSupportedException("source-Player-following-weapon-actual-constructor-writer-unbound");
        var actor = ActorProcesses.Read(_enginePlayer);
        return FalloutPlayerWeaponContinuation.Execute(source, scene.Stack, scene.Process, main, ordinal, invocation,
            new PlayerWeaponConsumers(this, actor.Epoch), call => LastStandalonePlayerWeaponContinuation = call);
    }
    private sealed class PlayerWeaponConsumers(FalloutReferenceWorld world, long epoch) : IFalloutPlayerWeaponContinuationConsumers
    {
        public void RequireCurrent(Guid main, long ordinal, Guid invocation)
        {
            world.RequireStandaloneControlPublication(); var scene = world.CampaignStandalonePlayerScene;
            scene.RequireWeaponContinuationInvocation(main, ordinal, invocation);
            if (world.ActorProcesses.Read(world._enginePlayer).Epoch != epoch ||
                world._actorProcessStack != scene.Stack)
                throw new InvalidOperationException("Weapon service lost its current actual Player/Main invocation or process epoch.");
        }
        public FalloutProcessWeaponActionRead ReadAction() => world.ProcessCommon.ReadPlayerWeaponAction(world._enginePlayer, epoch);
        public bool ReadSuppression() => throw new NotSupportedException("source-Player-following-weapon-current-independent-suppression-global-unbound");
        public FalloutProcessSceneQuery ReadWeaponEntry() => world.ProcessCommon.ReadSceneQueries(world._enginePlayer, epoch);
        public FalloutProcessWeaponActivityRead ReadActivity() => world.ProcessCommon.ReadPlayerWeaponActivity(world._enginePlayer, epoch);
        public FalloutProcessActorStatusRead ReadStatus() => world.ProcessCommon.ReadPlayerFollowingStatus(world._enginePlayer, epoch,
            () => (world._standaloneIdleActorValue ?? throw new NotSupportedException("source-Player-following-current-Paralysis-pool-owner-unbound"))
                (FalloutPlayerWeaponContinuationSource.ParalysisActorValue));
    }
}
