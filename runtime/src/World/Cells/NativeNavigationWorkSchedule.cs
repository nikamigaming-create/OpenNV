using System.Collections;

namespace OpenNV.Runtime.World.Cells;

internal sealed record NativeNavigationWorkSnapshot(long Search, string State, long Attempts, long Grants,
    long DeniedBudget, long DeniedTurn, long SkippedInactiveTurns, long IteratorSteps, long ExpandedNodes,
    long GuidedSamples, long SmoothingSteps, double WorkMilliseconds, double MaximumSliceMilliseconds,
    double? NearestTargetDistance, ulong? LastPhysicsFrame, ulong? LastProcessFrame, int QueuePosition);

// Main-thread queries retain their own cursor. The global budget limits work,
// while this queue keeps callback order from deciding who can ever progress.
internal sealed class NativeNavigationWorkSchedule
{
    internal const double BudgetMilliseconds = 2;
    internal const int MaximumIteratorSteps = 16;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly LinkedList<Registration> _queue = new();
    private long _generation;
    private ulong? _physicsFrame, _processFrame;
    private Registration? _running;
    internal double UsedMilliseconds { get; private set; }
    internal int Count => _queue.Count;

    internal void RequireThread()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Native navigation work must stay on its owning thread.");
    }

    internal Registration Register()
    {
        RequireThread();
        var registration = new Registration(this, checked(++_generation));
        registration.Node = _queue.AddLast(registration);
        return registration;
    }

    internal bool TryBegin(Registration registration, ulong physicsFrame, ulong processFrame)
    {
        RequireThread();
        RequireRegistration(registration);
        if (_running is not null) throw new InvalidOperationException("Native navigation work cannot overlap or reenter.");
        if (_physicsFrame > physicsFrame || _processFrame > processFrame)
            throw new InvalidOperationException("Native navigation engine phases moved backwards.");
        if (_physicsFrame != physicsFrame) { _physicsFrame = physicsFrame; UsedMilliseconds = 0; }
        _processFrame = processFrame;
        registration.Attempts++;
        registration.LastPhysicsFrame = physicsFrame;
        registration.LastProcessFrame = processFrame;
        if (UsedMilliseconds >= BudgetMilliseconds) { registration.DeniedBudget++; return false; }
        // A bot is polled in Process after actor PhysicsProcess callbacks.
        // Preserve its reserved turn across physics ticks of that same process
        // phase. An owner absent for a whole process phase yields its turn,
        // without cancelling or replacing its source/native query.
        while (_queue.First!.Value != registration)
        {
            var head = _queue.First.Value;
            if (head.LastProcessFrame is { } requested && processFrame - requested <= 1)
            { registration.DeniedTurn++; return false; }
            _queue.RemoveFirst();
            head.Node = _queue.AddLast(head);
            head.SkippedInactiveTurns++;
        }
        registration.Grants++;
        _running = registration;
        return true;
    }

    internal void Finish(Registration registration, double elapsedMilliseconds)
    {
        RequireThread();
        if (_running != registration || !double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0)
            throw new InvalidOperationException("Native navigation slice completion has no matching finite work lease.");
        UsedMilliseconds += elapsedMilliseconds;
        registration.WorkMilliseconds += elapsedMilliseconds;
        registration.MaximumSliceMilliseconds = Math.Max(registration.MaximumSliceMilliseconds, elapsedMilliseconds);
        _running = null;
        // A single collision call can exceed the budget. Charge its actual
        // duration and rotate even then; never grant another call in that frame.
        if (registration.Node is { } node)
        { _queue.Remove(node); registration.Node = _queue.AddLast(registration); }
    }

    internal void RequireQuery(Registration registration)
    {
        RequireThread();
        RequireRegistration(registration);
        if (_running is not null && _running != registration)
            throw new InvalidOperationException("Native navigation queries cannot overlap another owner's slice.");
    }

    private void RequireRegistration(Registration registration)
    {
        if (registration.Owner != this || registration.Node is null)
            throw new InvalidOperationException("Native navigation work registration has retired.");
    }

    internal sealed class Registration(NativeNavigationWorkSchedule owner, long generation)
    {
        internal NativeNavigationWorkSchedule Owner { get; } = owner;
        internal LinkedListNode<Registration>? Node;
        internal long Attempts, Grants, DeniedBudget, DeniedTurn, SkippedInactiveTurns, IteratorSteps;
        internal long ExpandedNodes, GuidedSamples, SmoothingSteps;
        internal double WorkMilliseconds, MaximumSliceMilliseconds;
        internal double? NearestTargetDistance;
        internal ulong? LastPhysicsFrame, LastProcessFrame;
        internal string State = "registered";
        internal void Retire(string state)
        {
            Owner.RequireThread();
            if (Node is null) return;
            Owner._queue.Remove(Node); Node = null; State = state;
        }
        internal void ObserveNode(double distance)
        {
            if (!double.IsFinite(distance) || distance < 0) throw new InvalidDataException("Native navigation distance is invalid.");
            ExpandedNodes++;
            NearestTargetDistance = Math.Min(NearestTargetDistance ?? distance, distance);
        }
        internal NativeNavigationWorkSnapshot Snapshot
        {
            get
            {
                Owner.RequireThread();
                var position = -1;
                if (Node is not null)
                {
                    position = 0;
                    for (var node = Owner._queue.First; node != Node; node = node!.Next) position++;
                }
                return new(generation, State, Attempts, Grants, DeniedBudget, DeniedTurn, SkippedInactiveTurns,
                    IteratorSteps, ExpandedNodes, GuidedSamples, SmoothingSteps, WorkMilliseconds,
                    MaximumSliceMilliseconds, NearestTargetDistance, LastPhysicsFrame, LastProcessFrame, position);
            }
        }
    }
}

internal sealed class NativeNavigationScheduledEnumerable<T>(NativeNavigationWorkSchedule schedule,
    Func<NativeNavigationWorkSchedule.Registration, IEnumerator<T>> create, Func<bool> ownerValid,
    Func<T, bool> terminal, Func<Action, Action>? bindRetirement = null) : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => new NativeNavigationScheduledSearch<T>(schedule, create, ownerValid, terminal, bindRetirement);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class NativeNavigationScheduledSearch<T> : IEnumerator<T>
{
    private readonly NativeNavigationWorkSchedule _schedule;
    private readonly NativeNavigationWorkSchedule.Registration _registration;
    private readonly IEnumerator<T> _inner;
    private readonly Func<bool> _ownerValid;
    private readonly Func<T, bool> _terminal;
    private readonly Action? _unbind;
    private bool _disposed, _moving;
    private T _current = default!;

    internal NativeNavigationScheduledSearch(NativeNavigationWorkSchedule schedule,
        Func<NativeNavigationWorkSchedule.Registration, IEnumerator<T>> create, Func<bool> ownerValid,
        Func<T, bool> terminal, Func<Action, Action>? bindRetirement = null)
    {
        _schedule = schedule; _ownerValid = ownerValid; _terminal = terminal;
        _registration = schedule.Register();
        IEnumerator<T>? created = null;
        try
        {
            _inner = created = create(_registration);
            _unbind = bindRetirement?.Invoke(InvalidateOwner);
        }
        catch
        {
            try { created?.Dispose(); }
            finally { _registration.Retire("creation-failed"); }
            throw;
        }
    }

    internal NativeNavigationWorkSnapshot State => _registration.Snapshot;
    internal bool TryBegin(ulong physicsFrame, ulong processFrame)
    {
        RequireOwner();
        return _schedule.TryBegin(_registration, physicsFrame, processFrame);
    }
    internal void Finish(double elapsedMilliseconds) => _schedule.Finish(_registration, elapsedMilliseconds);
    public T Current => _current;
    object? IEnumerator.Current => Current;

    public bool MoveNext()
    {
        RequireOwner();
        _schedule.RequireQuery(_registration);
        if (_moving) throw new InvalidOperationException("Native navigation cursor cannot reenter.");
        _moving = true;
        try
        {
            _registration.IteratorSteps++;
            if (!_inner.MoveNext()) { Retire("exhausted"); return false; }
            _current = _inner.Current;
            if (_terminal(_current)) Retire("completed");
            return true;
        }
        catch { Retire("faulted"); throw; }
        finally { _moving = false; }
    }

    private void RequireOwner()
    {
        _schedule.RequireThread();
        if (!_disposed)
        {
            bool valid;
            try { valid = _ownerValid(); }
            catch { Retire("owner-failed"); throw; }
            if (!valid) InvalidateOwner();
        }
        if (_disposed) throw new InvalidOperationException($"Native navigation cursor has retired: {_registration.State}.");
    }
    private void InvalidateOwner() => Retire("owner-retired");
    private void Retire(string state)
    {
        _schedule.RequireThread();
        if (_disposed) return;
        _disposed = true;
        _registration.Retire(state);
        try { _inner.Dispose(); }
        finally { _unbind?.Invoke(); }
    }
    public void Dispose() => Retire("cancelled");
    public void Reset() => throw new NotSupportedException("Native navigation cursors cannot rewind source/native work.");
}
