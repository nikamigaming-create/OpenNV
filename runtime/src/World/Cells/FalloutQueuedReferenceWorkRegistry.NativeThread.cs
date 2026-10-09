namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutQueuedReferenceWorkRegistry
{
    private object? _nativePresentationOwner;
    internal void BindNativePresentationThread(object actualOwner)
    {
        ArgumentNullException.ThrowIfNull(actualOwner);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_busy)
        {
            _reentry = checked(_reentry + 1);
            throw new InvalidOperationException("An entered queued source consumer cannot change its native owning thread.");
        }
        if (_nativePresentationOwner is not null)
        {
            if (!ReferenceEquals(actualOwner, _nativePresentationOwner))
                throw new InvalidOperationException("Source native caller belongs to another current presentation epoch.");
            RequireThread(); return;
        }
        if (_work.Count != 0)
            throw new NotSupportedException("Native caller thread publication cannot move entered source/native work from its original owner.");
        // The source world can be indexed on a background worker. Its actual
        // Godot caller publishes this thread before the first native read.
        // This is distinct from the retail inline OS-thread/TLS declaration.
        _thread = Environment.CurrentManagedThreadId;
        _nativePresentationOwner = actualOwner;
    }
}
