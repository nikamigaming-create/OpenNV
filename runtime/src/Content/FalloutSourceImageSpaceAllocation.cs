namespace OpenNV.Runtime.Content;

// This is the C# allocation domain, not a native TLS byte or heap address.
// Each scope restores the actual enclosing category, including exceptional
// exits. Factory allocations retain that category for their real lifetime.
internal sealed class FalloutSourceImageSpaceAllocation
{
    private sealed record Scope(Guid Identity, string Category, Scope? Parent);
    [ThreadStatic] private static Scope? _current;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<object, string> _allocations = new(ReferenceEqualityComparer.Instance);
    private int _entered;
    private bool _retired;
    internal int AllocationCount => _allocations.Count;
    internal string? CurrentCategory => _current?.Category;

    internal IDisposable Enter()
    {
        RequireOwner();
        var scope = new Scope(Guid.NewGuid(), "ImageSpace", _current);
        _current = scope;
        ++_entered;
        return new Lease(this, scope);
    }

    internal T Allocate<T>(Func<T> construct) where T : class
    {
        RequireOwner();
        if (_current?.Category != "ImageSpace")
            throw new InvalidOperationException("Image-space factory allocation has no current category scope.");
        var value = construct() ?? throw new InvalidOperationException("Image-space factory returned no resource.");
        if (!_allocations.TryAdd(value, _current.Category))
            throw new InvalidOperationException("Image-space factory reused an already living allocation.");
        return value;
    }

    internal void Release(object resource)
    {
        RequireOwner();
        if (!_allocations.Remove(resource))
            throw new InvalidOperationException("Image-space resource retirement changed its actual allocation owner.");
    }

    internal void Retire()
    {
        RequireOwner();
        if (_allocations.Count != 0 || _entered != 0)
            throw new InvalidOperationException("Image-space allocation domain still has living receiver resources or entered scopes.");
        _retired = true;
    }

    private void RequireOwner()
    {
        if (_retired) throw new ObjectDisposedException(nameof(FalloutSourceImageSpaceAllocation));
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Image-space allocation category changed its actual thread.");
    }

    private sealed class Lease(FalloutSourceImageSpaceAllocation owner, Scope scope) : IDisposable
    {
        private bool _released;
        public void Dispose()
        {
            if (_released) return;
            owner.RequireOwner();
            if (!ReferenceEquals(_current, scope))
                throw new InvalidOperationException("Image-space allocation categories must leave in actual LIFO order.");
            _current = scope.Parent;
            --owner._entered;
            _released = true;
        }
    }
}
