using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPlayerFollowingContinuationSource? _standaloneFollowingSource;

    internal FalloutOriginalInputSource ReadStandaloneOriginalInputSource()
    {
        var scene = CampaignStandalonePlayerScene;
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-Player-original-input-owned-executable-unbound");
        return FalloutExecutableStringTable.ReadOriginalInputQueries(owned.FalloutExecutablePath,
            FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
    }

    internal void PrepareStandalonePlayerFollowingContinuation()
    {
        RequireStandaloneControlPublication();
        if (_standaloneFollowingSource is not null)
            throw new InvalidOperationException("Following Player services already have their actual source owner.");
        var scene = CampaignStandalonePlayerScene;
        var owned = records.OwnedSource ?? throw new NotSupportedException("source-Player-following-owned-executable-unbound");
        var source = FalloutExecutableStringTable.ReadPlayerFollowingContinuation(owned.FalloutExecutablePath,
            FalloutPlayerMovementControlSource.Reviewed(scene.Source.Main));
        _standaloneFollowingSource = source;
        scene.BindPlayerFollowingContinuation(source, (caller, observe) =>
        {
            var actor = ActorProcesses.Read(_enginePlayer);
            FalloutPlayerFollowingContinuation.Execute(source, scene.Stack, scene.Process, caller,
                new PlayerFollowingConsumers(this, actor.Epoch), observe);
        });
    }

    private sealed class PlayerFollowingConsumers(FalloutReferenceWorld world, long epoch)
        : IFalloutPlayerFollowingContinuationConsumers, IFalloutPlayerReadyItemConsumers
    {
        public void RequireCurrent(FalloutPlayerSourceCaller caller)
        {
            world.RequireStandaloneControlPublication();
            var scene = world.CampaignStandalonePlayerScene;
            scene.RequireFollowingContinuationInvocation(caller);
            if (world.ActorProcesses.Read(world._enginePlayer).Epoch != epoch || world._actorProcessStack != scene.Stack)
                throw new InvalidOperationException("Following Player service lost its actual Actor/Main epoch or source.");
        }
        public FalloutPlayerInhibitionPredicate ReadInhibition(FalloutPlayerSourceCaller caller, Action<FalloutPlayerInhibitionRead> observe) =>
            world.CampaignStandalonePlayerScene.ReadFollowingInhibition(caller, observe);
        public FalloutPlayerFollowingLifeRead ReadLife(bool argument)
        {
            var process = world.ProcessCommon.ReadSceneQueries(world._enginePlayer, epoch);
            var raw = world.ProcessRuntime.Life(world._enginePlayer);
            var value = raw.Require();
            return new(process, raw, argument, FalloutPlayerFollowingContinuation.LifePredicate(value, argument));
        }
        public FalloutProcessPackageRead ReadCurrentPackage() =>
            world.ProcessCommon.ReadSourcePackageSlot(world._enginePlayer, epoch, FalloutProcessPackageSlot.Current);
        public FalloutProcessPackageRead ReadPrimaryPackage() =>
            world.ProcessCommon.ReadSourcePackageSlot(world._enginePlayer, epoch, FalloutProcessPackageSlot.Primary);
        public FalloutPlayerFollowingTimerFlagRead ReadTimerByte(FalloutPlayerSourceCaller caller) =>
            world.CampaignStandalonePlayerScene.ReadFollowingTimerByte(caller);
        public FalloutOriginalInputQuery ReadReadyItem(FalloutPlayerSourceCaller caller, int mode,
            Action<FalloutOriginalInputQuery> observe) =>
            world.CampaignStandalonePlayerScene.ReadFollowingSourceControl(caller,
                FalloutPlayerFollowingContinuationSource.ReadyItemControl, mode, observe);
    }
}
