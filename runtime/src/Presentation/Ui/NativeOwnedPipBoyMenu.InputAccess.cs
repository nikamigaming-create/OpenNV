namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoyMenu
{
    // This is completed source-tile/drawing validation, not a final-pixel receipt.
    internal bool SourceDrawingPublished => _tiles is not null && Error is null;
}
