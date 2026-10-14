namespace OpenNV.Runtime.Content;

internal sealed partial class RuntimeLiveContentSource
{
    private readonly object _resourceLifetime = new();
    private readonly object _resourceRetirement = new();
    private readonly Dictionary<int, int> _resourceReadThreads = [];
    private int _activeResourceReads;
    private volatile bool _resourcesRetired;
    private bool _resourcesDisposed;
    private Exception? _resourceRetirementFailure;
    internal bool ResourceReadsClosed => _resourcesRetired;

    private ResourceReadLease EnterResourceRead()
    {
        var thread = Environment.CurrentManagedThreadId;
        lock (_resourceLifetime)
        {
            var entered = _resourceReadThreads.GetValueOrDefault(thread);
            // Already-entered synchronous readers may finish their nested
            // resource dependencies while retirement drains them.
            ObjectDisposedException.ThrowIf(_resourcesRetired && entered == 0, this);
            _resourceReadThreads[thread] = checked(entered + 1);
            _activeResourceReads = checked(_activeResourceReads + 1);
            return new(this, thread);
        }
    }

    private void RequireNotInsideResourceRead()
    {
        lock (_resourceLifetime)
            if (_resourceReadThreads.ContainsKey(Environment.CurrentManagedThreadId))
                throw new InvalidOperationException("Owned content cannot retire inside its entered resource read.");
    }

    private void StopAndDrainResourceReads()
    {
        lock (_resourceLifetime)
        {
            _resourcesRetired = true;
            while (_activeResourceReads != 0) Monitor.Wait(_resourceLifetime);
        }
    }

    private readonly struct ResourceReadLease(RuntimeLiveContentSource owner, int thread) : IDisposable
    {
        public void Dispose()
        {
            if (thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("A synchronous owned resource read changed its entered thread.");
            lock (owner._resourceLifetime)
            {
                var remaining = owner._resourceReadThreads[thread] - 1;
                if (remaining == 0) owner._resourceReadThreads.Remove(thread);
                else owner._resourceReadThreads[thread] = remaining;
                if (--owner._activeResourceReads == 0) Monitor.PulseAll(owner._resourceLifetime);
            }
        }
    }
}
