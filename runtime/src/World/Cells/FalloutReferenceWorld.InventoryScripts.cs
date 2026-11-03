namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private int _activeInventoryScriptFrames;
    internal bool InventoryScriptFrameActive => _activeInventoryScriptFrames != 0;
    internal readonly struct InventoryScriptFrameScope(FalloutReferenceWorld owner) : IDisposable
    {
        public void Dispose() => --owner._activeInventoryScriptFrames;
    }
    internal InventoryScriptFrameScope EnterInventoryScriptFrame()
    {
        _activeInventoryScriptFrames = checked(_activeInventoryScriptFrames + 1);
        return new(this);
    }
}
