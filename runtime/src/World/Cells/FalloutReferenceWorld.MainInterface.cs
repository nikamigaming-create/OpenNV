using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal bool HasSourceMainInterface => CampaignMainRuntimeConfigured && CampaignMainScriptSource.HasNewVegasChildren;
    internal FalloutMainInterfaceState SourceMainInterface
    { get { ObjectDisposedException.ThrowIf(_disposed, this); return ProcessRuntime.SourceMainInterface; } }
    internal void RequireSourceMainInterfaceNativeRetirement()
    { ObjectDisposedException.ThrowIf(_disposed, this); ProcessRuntime.RequireMainInterfaceNativeRetirement(); }
}
