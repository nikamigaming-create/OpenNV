using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutPlayerReturnTailState? _sourcePlayerReturnTail;
    private FalloutPlayerLateFrameInvocation? _enteredPlayerReturnTail;
    internal void PrepareStandalonePlayerReturnTail()
    {
        RequireStandaloneControlPublication();
        if (_sourcePlayerReturnTail is not null) throw new InvalidOperationException("Player return updater is already constructed.");
        var source = (_loadedPlayerAnimation ?? throw new NotSupportedException("source-return-updater-loaded-animation-owner-unbound")).Source.Tail;
        var scene = CampaignStandalonePlayerScene; var common = ProcessCommon.ReadSceneQueries(_enginePlayer, ActorProcesses.Read(_enginePlayer).Epoch);
        var extras = _sourceReferenceScriptInitializer ?? throw new NotSupportedException("source-return-updater-reference-extra-owner-unbound");
        _sourcePackageFields[_enginePlayer].BindAnimationNodeExtraMembership(source, extras, () =>
        {
            RequireStandaloneControlPublication(); extras.RequireAnimationNodeConstructorPrefix();
        });
        var timer = ProcessRuntime.RequirePlayerControlTimer();
        var owners = new FalloutPlayerReturnTailOwners(records, ProcessRuntime, ProcessCommon, _sourcePackageFields[_enginePlayer],
            extras, timer,
            (frame, factory) => scene.ReadReturnTailAnimationClock(frame, factory, timer, RequireActualPlayerReturnTail),
            (frame, factory, child, enter) => scene.StoreReturnTailAnimationBracket(frame, factory, child, enter, RequireActualPlayerReturnTail),
            identity => _loadedPlayerAnimation.RequireContext(identity));
        _sourcePlayerReturnTail = new(source, scene.Stack, scene.Process, scene.Constructor, common.Process, owners,
            RequireActualPlayerReturnTail, RequirePlayerModelLoadLifetime, scene.Capture().ReturnTail);
        scene.BindLateReturnTail(_sourcePlayerReturnTail, UpdateStandalonePlayerReturnTail);
    }
    private FalloutPlayerReturnTailCall UpdateStandalonePlayerReturnTail(FalloutPlayerLateFrameInvocation frame)
    {
        if (_enteredPlayerReturnTail is not null) throw new InvalidOperationException("Player return updater reentered its actual frame.");
        _enteredPlayerReturnTail = frame;
        try { RequireActualPlayerReturnTail(frame); return _sourcePlayerReturnTail!.UpdateThroughAnimation(frame); }
        finally { _enteredPlayerReturnTail = null; }
    }
    private void RequireActualPlayerReturnTail(FalloutPlayerLateFrameInvocation frame)
    {
        RequireStandaloneControlPublication(); CampaignStandalonePlayerScene.RequireReturnTailInvocation(frame);
        if (_enteredPlayerReturnTail != frame || _sourcePlayerReturnTail is null ||
            frame.Common.Process != _sourcePlayerReturnTail.CommonProcess)
            throw new InvalidOperationException("Player return updater changed its actual entered Common process.");
        var actor = ActorProcesses.Read(_enginePlayer);
        if (actor.Retired || actor.Source != frame.Common.Actor || actor.Epoch != frame.Common.Epoch)
            throw new InvalidOperationException("Player return updater crossed its actual Actor process epoch.");
    }
}
