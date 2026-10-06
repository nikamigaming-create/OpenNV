namespace OpenNV.Runtime.Gameplay.State;

// Preparation changes no authoritative inventory or presentation. Reversible
// commits follow both inventory publications; native teardown follows all commits.
internal sealed class FalloutInventoryChangeLease(Action commit, Action rollback, Action? complete = null) : IDisposable
{
    private bool _started, _completed;

    internal void Commit()
    {
        if (_started || _completed) throw new InvalidOperationException("Inventory change lease was already consumed.");
        _started = true;
        commit();
    }

    internal void Complete()
    {
        if (!_started || _completed) throw new InvalidOperationException("Inventory change lease has no committed change.");
        _completed = true;
        complete?.Invoke();
    }

    public void Dispose()
    {
        if (!_started || _completed) return;
        rollback();
        _started = false;
    }
}
