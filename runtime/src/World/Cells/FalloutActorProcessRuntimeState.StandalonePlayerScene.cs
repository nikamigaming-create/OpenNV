using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutStandalonePlayerSceneState? _standalonePlayerScene;
    internal FalloutStandalonePlayerSceneState StandalonePlayerScene => _standalonePlayerScene ??
        throw new NotSupportedException("Selected Main/Player has no actual standalone scene constructor.");
    private void ConstructStandalonePlayerScene(FalloutMainScriptCallerSource source, FalloutStandalonePlayerSceneSnapshot? restore)
    {
        if (_standalonePlayerScene is not null) throw new InvalidOperationException("Player scene cannot replace its selected current source lifetime.");
        _standalonePlayerScene = new(FalloutStandalonePlayerSceneSource.Read(source), _stack, _process, restore);
    }
    internal bool ReadStandalonePlayerSceneMode(FalloutMainPlayerCellInvocation invocation)
    {
        RequireMainPlayerCellChild(invocation, FalloutMainPlayerCellStep.SceneMode);
        return StandalonePlayerScene.ReadSceneMode();
    }
    internal bool ReadStandalonePlayerFirstPerson(FalloutMainPlayerCellInvocation invocation)
    {
        RequireMainPlayerCellChild(invocation, FalloutMainPlayerCellStep.SceneRead);
        return StandalonePlayerScene.ReadFirstPersonSelected();
    }
    internal bool ReadStandaloneMainHold(FalloutMainScriptInvocation invocation)
    {
        RequireMainScriptChild(invocation, FalloutMainScriptCallerStep.MainHold);
        return StandalonePlayerScene.ReadMainHold();
    }
    internal void StoreStandalonePlayerSceneClock(FalloutMainPlayerCellInvocation invocation)
    {
        RequireMainPlayerCellChild(invocation, FalloutMainPlayerCellStep.SceneClock);
        var main = _scriptCallerLast ?? throw new InvalidOperationException("Scene clock lost its actual Main delivery.");
        if (main.Invocation != invocation.Main.Identity || main.Ordinal != invocation.Main.Ordinal ||
            main.SourceProcess != _process) throw new InvalidOperationException("Player scene changed its retained Main source argument.");
        _ = StandalonePlayerScene.StoreSceneClock(main.Invocation, main.Ordinal, main.DeliveredSecondsBits);
    }
    internal uint EnterStandalonePlayerSceneVirtual(FalloutMainPlayerCellInvocation invocation)
    {
        RequireMainPlayerCellChild(invocation, FalloutMainPlayerCellStep.SceneChild);
        return StandalonePlayerScene.EnterSceneVirtual(invocation.Main.Identity, invocation.Main.Ordinal);
    }
    internal void RequireStandaloneSceneNativeRetirement() => RequireMainScriptClosureBoundary(retiring: true);
    private void RetireStandalonePlayerScene() => _standalonePlayerScene?.Retire();
}
