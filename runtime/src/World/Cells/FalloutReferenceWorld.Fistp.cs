using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    internal IDisposable BindCallingThreadFistp(FalloutCallingThreadFistpHost host)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ProcessRuntime.BindCallingThreadFistp(host);
    }
    internal FalloutMainPlayerCellInvocation RequireCurrentPendingConversion(FalloutPlayerPendingRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ProcessRuntime.RequireCurrentPendingConversion(request);
    }
}
