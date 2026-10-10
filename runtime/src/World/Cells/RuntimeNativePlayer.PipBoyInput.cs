using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    // Set only around the actual ApplySourceControls publication. The source
    // state remains committed separately if a native consumer fails partway.
    private bool _sourceControlsPublished;

    internal FalloutPipBoyInputBlocker SourcePipBoyInputAccess(bool loading, bool equipped) =>
        !_sourceControlsPublished ? FalloutPipBoyInputBlocker.Unbound :
            FalloutPipBoyInputPolicy.Read(SourceControls, _furniturePhase != 0, loading, equipped);
}
