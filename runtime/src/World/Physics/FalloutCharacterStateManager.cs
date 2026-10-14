using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Physics;

// These actual shared state objects own their constructor fields. Their later
// transition/update methods are distinct source consumers; construction calls
// none of them and cannot qualify them with a default successful delegate.
internal abstract class FalloutCharacterState(byte kind, bool? enabled, uint? scalarBits)
{
    internal FalloutCharacterStateSnapshot Snapshot { get; } = new(kind, enabled, scalarBits);
    internal void Update() => throw new NotSupportedException("source-character-state-" + Snapshot.Kind + "-transition-and-motion-consumer-unbound");
}
internal sealed class FalloutCharacterState0() : FalloutCharacterState(0, true, null) { }
internal sealed class FalloutCharacterState1() : FalloutCharacterState(1, null, null) { }
internal sealed class FalloutCharacterState2() : FalloutCharacterState(2, null, FalloutEarlyControllerSource.State2ScalarBits) { }
internal sealed class FalloutCharacterState3() : FalloutCharacterState(3, null, null) { }
internal sealed class FalloutCharacterState4() : FalloutCharacterState(4, null, null) { }
internal sealed class FalloutCharacterState5() : FalloutCharacterState(5, null, null) { }
internal sealed class FalloutCharacterState6() : FalloutCharacterState(6, null, null) { }

internal sealed class FalloutCharacterStateManager
{
    private readonly FalloutCharacterState?[] _slots = new FalloutCharacterState?[FalloutEarlyControllerSource.ManagerSlots];
    private readonly List<byte> _order = [];
    private readonly HashSet<FalloutCharacterStateContext> _contexts = [];
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private bool _retired;
    private FalloutCharacterStateManagerSnapshot? _retiredSnapshot;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal FalloutCharacterStateManager()
    {
        Register(new FalloutCharacterState0()); Register(new FalloutCharacterState5()); Register(new FalloutCharacterState1());
        Register(new FalloutCharacterState2()); Register(new FalloutCharacterState3()); Register(new FalloutCharacterState4()); Register(new FalloutCharacterState6());
    }
    private void Register(FalloutCharacterState value)
    {
        RequireCurrent(); var kind = value.Snapshot.Kind;
        if (_slots[kind] is not null) throw new InvalidOperationException("Shared source state factory was registered twice.");
        _slots[kind] = value; _order.Add(kind);
    }
    internal FalloutCharacterStateContext ConstructContext(byte initialState)
    {
        RequireCurrent();
        var context = new FalloutCharacterStateContext(this, initialState); _contexts.Add(context); return context;
    }
    internal FalloutCharacterStateContext RebindContext(FalloutCharacterStateContextSnapshot saved)
    {
        RequireCurrent(); var context = new FalloutCharacterStateContext(this, saved); _contexts.Add(context); return context;
    }
    internal FalloutCharacterState Read(byte kind)
    {
        RequireCurrent(); return kind < _slots.Length ? _slots[kind] ?? throw new NotSupportedException("source-character-unregistered-state-factory") :
        throw new InvalidDataException("Character state is outside the original manager registry.");
    }
    internal void RequireContext(FalloutCharacterStateContext context)
    { RequireCurrent(); if (!_contexts.Contains(context)) throw new InvalidOperationException("Actual state context lost its shared manager lease."); }
    internal void Release(FalloutCharacterStateContext context)
    { RequireContext(context); _contexts.Remove(context); }
    internal FalloutCharacterStateManagerSnapshot Capture()
    {
        if (_retiredSnapshot is { } retired) return retired;
        RequireCurrent(); return new(Identity, _slots.Select(value => value?.Snapshot).ToArray(), _order.ToArray(), _contexts.Count);
    }
    internal void Retire()
    {
        if (_retired) return; RequireCurrent(); if (_contexts.Count != 0) throw new InvalidOperationException("Shared state manager must outlive its actual contexts.");
        _retiredSnapshot = Capture(); Array.Clear(_slots); _retired = true;
    }
    private void RequireCurrent()
    {
        ObjectDisposedException.ThrowIf(_retired, this); if (_thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Shared character state manager escaped its actual physics thread.");
    }
}

internal sealed class FalloutCharacterStateContext : IDisposable
{
    private readonly FalloutCharacterStateManager _manager;
    private bool _retired;
    internal FalloutCharacterStateContextSnapshot Snapshot { get; }
    internal FalloutCharacterStateContext(FalloutCharacterStateManager manager, byte state)
    {
        _manager = manager; Snapshot = new(manager.Identity, state, state, true, FalloutEarlyControllerSource.ContextScalarABits,
        FalloutEarlyControllerSource.ContextScalarBBits, FalloutEarlyControllerSource.PositiveOneBits);
    }
    internal FalloutCharacterStateContext(FalloutCharacterStateManager manager, FalloutCharacterStateContextSnapshot saved)
    { _manager = manager; Snapshot = saved with { Manager = manager.Identity }; }
    internal void RequireCurrent() { ObjectDisposedException.ThrowIf(_retired, this); _manager.RequireContext(this); }
    public void Dispose() { if (_retired) return; _manager.Release(this); _retired = true; }
}
