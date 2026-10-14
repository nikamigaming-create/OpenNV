namespace OpenNV.Runtime.Gameplay.State;

// One delivery owner pauses admission while its already-entered call finishes.
// Disposing the lease resumes admission; it never completes or replays a call.
internal sealed class RuntimeSourceDeliveryPause
{
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private Lease? _current;
    internal bool Paused => _current is not null;

    internal Lease Acquire(Task? enteredCall)
    {
        RequireThread();
        if (_current is not null) throw new InvalidOperationException("Source delivery already has a pause lease.");
        return _current = new(this, enteredCall ?? Task.CompletedTask);
    }

    private void RequireThread()
    {
        if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Source delivery pause changed its owner thread.");
    }

    internal sealed class Lease : IDisposable
    {
        private readonly RuntimeSourceDeliveryPause _owner;
        private readonly Task _enteredCall;
        private bool _released;
        internal Lease(RuntimeSourceDeliveryPause owner, Task enteredCall)
        { _owner = owner; _enteredCall = enteredCall; }
        internal bool Drained
        {
            get { Validate(); return _enteredCall.IsCompleted; }
        }
        internal void Validate()
        {
            _owner.RequireThread();
            if (_released || !ReferenceEquals(_owner._current, this))
                throw new InvalidOperationException("Source delivery pause lease is no longer current.");
        }
        internal void RequireReturned()
        {
            Validate();
            if (!_enteredCall.IsCompleted) throw new InvalidOperationException("Entered source delivery has not returned.");
            _enteredCall.GetAwaiter().GetResult();
        }
        public void Dispose()
        {
            _owner.RequireThread();
            if (_released) return;
            Validate();
            _owner._current = null; _released = true;
        }
    }
}
