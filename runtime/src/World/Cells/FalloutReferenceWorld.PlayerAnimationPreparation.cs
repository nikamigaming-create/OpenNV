using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPlayerAnimationPreparationState? _sourcePlayerAnimationPreparation;
    private FalloutPlayerLateFrameInvocation? _enteredPlayerAnimationPreparation;
    internal void PrepareStandalonePlayerAnimationPreparation()
    {
        RequireStandaloneControlPublication();
        if (_sourcePlayerAnimationPreparation is not null) throw new InvalidOperationException("Player preparation is already constructed.");
        var source = _sourcePlayerPostAnimation ?? throw new NotSupportedException("source-Player-post-animation-source-unbound");
        var scene = CampaignStandalonePlayerScene;
        var common = ProcessCommon.ReadSceneQueries(_enginePlayer, ActorProcesses.Read(_enginePlayer).Epoch);
        var animation = _loadedPlayerAnimation ?? throw new NotSupportedException("source-Player-preparation-actual-context-registry-unbound");
        var owners = new FalloutPlayerAnimationPreparationOwners(ProcessRuntime, ProcessCommon, _sourcePackageFields[_enginePlayer],
            scene, identity => animation.RequireContext(identity));
        _sourcePlayerAnimationPreparation = new(source, scene.Stack, scene.Process, scene.Constructor, common.Process, owners,
            RequireActualPlayerAnimationPreparation, RequirePlayerModelLoadLifetime);
        scene.BindLateAnimationPreparation(_sourcePlayerAnimationPreparation, PrepareActualPlayerAnimation);
    }
    private FalloutPlayerAnimationPreparationCall PrepareActualPlayerAnimation(FalloutPlayerLateFrameInvocation frame)
    {
        if (_enteredPlayerAnimationPreparation is not null) throw new InvalidOperationException("Player preparation reentered its actual frame.");
        _enteredPlayerAnimationPreparation = frame;
        try { RequireActualPlayerAnimationPreparation(frame); return _sourcePlayerAnimationPreparation!.Prepare(frame); }
        finally { _enteredPlayerAnimationPreparation = null; }
    }
    private void RequireActualPlayerAnimationPreparation(FalloutPlayerLateFrameInvocation frame)
    {
        RequireStandaloneControlPublication(); CampaignStandalonePlayerScene.RequirePostAnimationInvocation(frame);
        if (_enteredPlayerAnimationPreparation != frame || _sourcePlayerAnimationPreparation is null)
            throw new InvalidOperationException("Player preparation escaped its actual World/frame scope.");
        var actor = ActorProcesses.Read(_enginePlayer);
        if (actor.Retired || actor.Source != frame.Common.Actor || actor.Epoch != frame.Common.Epoch)
            throw new InvalidOperationException("Player preparation crossed its actual Actor process epoch.");
    }
}
