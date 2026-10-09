using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal bool HasStandalonePlayerScene => _processRuntime?.StandaloneMainConstructed == true &&
        FalloutSourceMainFamily.IsFallout3(CampaignMainScriptSource.EngineSha256);
    internal FalloutStandalonePlayerSceneState CampaignStandalonePlayerScene
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!HasStandalonePlayerScene) throw new NotSupportedException("World has no actual selected standalone Main/Player scene.");
            return ProcessRuntime.StandalonePlayerScene;
        }
    }
    internal bool ReadStandalonePlayerSceneMode(FalloutMainPlayerCellInvocation invocation) => ProcessRuntime.ReadStandalonePlayerSceneMode(invocation);
    internal bool ReadStandalonePlayerFirstPerson(FalloutMainPlayerCellInvocation invocation) => ProcessRuntime.ReadStandalonePlayerFirstPerson(invocation);
    internal bool ReadStandaloneMainHold(FalloutMainScriptInvocation invocation) => ProcessRuntime.ReadStandaloneMainHold(invocation);
    internal void StoreStandalonePlayerSceneClock(FalloutMainPlayerCellInvocation invocation) => ProcessRuntime.StoreStandalonePlayerSceneClock(invocation);
    internal uint EnterStandalonePlayerSceneVirtual(FalloutMainPlayerCellInvocation invocation) => ProcessRuntime.EnterStandalonePlayerSceneVirtual(invocation);
    internal void RequireStandalonePlayerSceneRetirement() => ProcessRuntime.RequireStandaloneSceneNativeRetirement();
}
