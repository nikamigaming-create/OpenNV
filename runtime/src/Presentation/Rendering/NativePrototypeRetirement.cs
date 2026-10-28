namespace OpenNV.Runtime.Presentation.Rendering;

// Cache removal is immediate; the detached native owner survives until its
// scheduled release succeeds. A re-created cache entry is a separate owner.
internal sealed class NativePrototypeRetirement<T> where T : class
{
    private readonly Queue<(string Source, T Owner)> _pending = [];
    internal int Count => _pending.Count;
    internal string? NextSource => _pending.TryPeek(out var pending) ? pending.Source : null;

    internal void EnqueueCold(Dictionary<string, T> cache, IReadOnlySet<string> warm)
    {
        foreach (var path in cache.Keys.Where(path => !warm.Contains(path)).ToArray())
        {
            var owner = cache[path];
            cache.Remove(path);
            _pending.Enqueue((path, owner));
        }
    }

    internal bool RetireNext(Action<T> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (_pending.Count == 0) return false;
        release(_pending.Peek().Owner);
        _pending.Dequeue();
        return true;
    }

    internal void Drain(Action<T> release)
    {
        while (RetireNext(release)) { }
    }
}
