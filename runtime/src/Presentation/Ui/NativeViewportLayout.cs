using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>A layout subscription tied to the viewport used for this tree entry.</summary>
internal sealed class NativeViewportLayout : IDisposable
{
    private readonly Viewport _viewport;
    private readonly Action _layout;
    private bool _disposed;
    internal NativeViewportLayout(Control owner, Action layout)
    {
        _viewport = owner.GetViewport(); _layout = layout;
        _viewport.SizeChanged += _layout;
        Callable.From(() => { if (!_disposed && owner.IsInsideTree()) _layout(); }).CallDeferred();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _viewport.SizeChanged -= _layout;
    }
}
