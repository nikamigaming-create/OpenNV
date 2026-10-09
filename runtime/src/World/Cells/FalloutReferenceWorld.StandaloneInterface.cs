using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal bool HasStandaloneInterface => CampaignMainRuntimeConfigured && FalloutSourceMainFamily.IsFallout3(CampaignMainScriptSource.EngineSha256);
    internal FalloutStandaloneInterfaceState StandaloneInterface
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return ProcessRuntime.StandaloneInterface; }
    }
    internal bool ReadStandalonePlayerHeldInterface(FalloutMainPlayerCellInvocation invocation)
    {
        RequireCampaignMainPlayerInvocation(invocation, FalloutMainPlayerCellStep.HeldInterfaceQuery);
        return ProcessRuntime.ReadStandalonePlayerHeldInterface(invocation);
    }
    internal void RequireStandaloneInterfaceNativeRetirement()
    {
        ObjectDisposedException.ThrowIf(_disposed, this); ProcessRuntime.RequireStandaloneInterfaceNativeRetirement();
    }
}
