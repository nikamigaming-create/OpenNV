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
        var timer = _cachedSourceTimer ?? throw new InvalidOperationException("Scene clock lost its actual cached Main timer.");
        var readOwner = new FalloutSourceFrameDeltaReadOwner(FalloutSourceFrameDeltaReader.SceneClock,
            StandalonePlayerScene.Constructor, main.Invocation, main.Invocation, main.Ordinal, _process);
        _ = StandalonePlayerScene.StoreSceneClock(main.Invocation, main.Ordinal, () => timer.ReadScaledDelta(readOwner));
    }
    internal uint EnterStandalonePlayerSceneVirtual(FalloutMainPlayerCellInvocation invocation)
    {
        RequireMainPlayerCellChild(invocation, FalloutMainPlayerCellStep.SceneChild);
        return StandalonePlayerScene.EnterSceneVirtual(invocation.Main.Identity, invocation.Main.Ordinal);
    }
    internal float ReadStandalonePlayerScaledSeconds(FalloutMainPlayerCellInvocation invocation)
    {
        RequireMainPlayerCellChild(invocation, FalloutMainPlayerCellStep.SceneChild);
        var timer = _cachedSourceTimer ?? throw new InvalidOperationException("Player scalar update lost its actual Main timer.");
        // Player runs before the current Main timer/renderer suffix. This is a
        // read of the existing source scalar, including constructor zero; it
        // cannot require a later publication or borrow the delivered host delta.
        var current = timer.ReadScaledDelta();
        if (current.Process != _process || current.Source != MainScriptCallerSource().EngineSha256)
            throw new InvalidOperationException("Player scalar update changed its actual source timer constructor.");
        return BitConverter.UInt32BitsToSingle(current.ScaledSecondsBits);
    }
    internal void RequireStandaloneSceneNativeRetirement() => RequireMainScriptClosureBoundary(retiring: true);
    internal void RequireStandaloneSceneReplacement()
    {
        if (_scriptCallerManagedThread is { } thread && thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Player scene replacement changed its actual Main constructor thread.");
        RequireNotBusy();
        RequireMainScriptClosureBoundary(retiring: false);
        StandalonePlayerScene.RequireFirstPersonReplacement();
    }
    private void RetireStandalonePlayerScene() => _standalonePlayerScene?.Retire();
}
