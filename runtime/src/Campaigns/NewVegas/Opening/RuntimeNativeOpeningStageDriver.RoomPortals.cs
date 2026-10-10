using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private Func<IReadOnlyList<FalloutRoomPortalSnapshot>>? _captureRoomPortals;
    internal void BindSourceRoomPortalCapture(Func<IReadOnlyList<FalloutRoomPortalSnapshot>> capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (_captureRoomPortals is not null) throw new InvalidOperationException("Source room/portal capture was already bound.");
        _captureRoomPortals = capture;
    }
    internal IReadOnlyList<FalloutRoomPortalSnapshot> CaptureSourceRoomPortals() =>
        (_captureRoomPortals ?? throw new NotSupportedException("Current CELL graph lacks its genuine source room/portal capture owner."))();
    internal void RetainSourceRoomPortalFailure(Exception error) => RetainDriverFailure(error);
}
