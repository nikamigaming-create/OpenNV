namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeOwnedPipBoy
{
    internal bool SourceInputSurfacePublished => IsInsideTree() && _menu.IsInsideTree() && _menu.SourceDrawingPublished;
    internal string? SourceInputFailure => _sourcePointerFailure ?? _menu.Error;
}
