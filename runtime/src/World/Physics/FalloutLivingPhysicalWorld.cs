namespace OpenNV.Runtime.World.Physics;

// Entity IDs and master/slave memberships are produced by actual admissions.
// Authored link order and source block IDs never fill these current arrays.
internal sealed partial class FalloutLivingPhysicalWorld : IDisposable
{
    internal const byte FixedMotion = 7;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly IFalloutLivingPhysicsBackend _backend;
    private readonly HashSet<FalloutLivingPhysicalBody> _bodies = [];
    private readonly HashSet<FalloutLivingPhysicalConstraint> _constraints = [];
    private readonly List<FalloutLivingPhysicalBody> _bodyAdmission = [];
    private readonly List<FalloutLivingPhysicalConstraint> _constraintAdmission = [];
    private readonly List<FalloutLivingPhysicalBody> _pendingBodies = [];
    private readonly List<FalloutLivingPhysicalConstraint> _pendingConstraints = [];
    private readonly FalloutLivingPhysicalIsland _fixedIsland = new(true);
    private uint _nextEntityId;
    private int _criticalDepth;
    private bool _retired;
    private string? _unknownState;

    internal FalloutLivingPhysicalWorld(IFalloutLivingPhysicsBackend backend, uint nextEntityId = 0)
    { ArgumentNullException.ThrowIfNull(backend); backend.RequireCurrent(); _backend = backend; _nextEntityId = nextEntityId; }
    internal string Key => _backend.WorldKey;
    internal uint NextEntityId { get { RequireCurrent(); return _nextEntityId; } }
    internal IReadOnlyCollection<FalloutLivingPhysicalBody> Bodies { get { RequireCurrent(); return _bodies; } }
    internal IReadOnlyCollection<FalloutLivingPhysicalConstraint> Constraints { get { RequireCurrent(); return _constraints; } }
    internal IReadOnlyList<FalloutLivingPhysicalBody> PendingBodies => _pendingBodies.AsReadOnly();
    internal IReadOnlyList<FalloutLivingPhysicalConstraint> PendingConstraints => _pendingConstraints.AsReadOnly();
    internal string? UnknownState => _unknownState;

    internal void RequireCurrent()
    {
        if (Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Living physical world requires its owning thread.");
        ObjectDisposedException.ThrowIf(_retired, this); _backend.RequireCurrent();
    }

    internal void EnterCriticalOperations() { RequireCurrent(); _criticalDepth = checked(_criticalDepth + 1); }
    internal void LeaveCriticalOperations()
    {
        RequireCurrent();
        if (_criticalDepth == 0) throw new InvalidOperationException("Physical critical bracket is unbalanced.");
        _criticalDepth--;
        if (_criticalDepth == 0 && (_pendingBodies.Count != 0 || _pendingConstraints.Count != 0))
            RefuseUnknown("Living physical pending-operation drain and source queue interleave are unowned.");
    }

    internal void AddBody(FalloutLivingPhysicalBody body)
    {
        RequireCurrent(); body.RequireCurrent();
        if (body.CurrentWorld == this) return;
        if (body.CurrentWorld is not null)
            RefuseUnknown("Living body world transfer requires the source inverse island/contact producer.");
        if (body.PendingWorld is not null) throw new InvalidOperationException("Living physical body admission is already pending.");
        if (_criticalDepth != 0)
        { body.PendingWorld = this; _pendingBodies.Add(body); return; }
        var id = unchecked(_nextEntityId + 1);
        _backend.AddBody(body.Backend);
        if (!body.Backend.IsWorldAttached)
            throw new InvalidDataException("Underlying body admission did not publish real physics residency.");
        _nextEntityId = id; body.EntityId = id; body.CurrentWorld = this;
        body.Island = body.Motion == FixedMotion ? _fixedIsland : new(false);
        _bodies.Add(body); _bodyAdmission.Add(body);
    }

    internal void AddConstraint(FalloutLivingPhysicalConstraint constraint)
    {
        RequireCurrent(); constraint.RequireCurrent();
        if (constraint.CurrentWorld is not null || constraint.PendingWorld is not null)
            throw new InvalidOperationException("Living constraint is already attached or pending.");
        if (_criticalDepth != 0)
        { constraint.PendingWorld = this; _pendingConstraints.Add(constraint); return; }
        var first = constraint.FirstBody; var second = constraint.SecondBody;
        if (first.CurrentWorld != this || second.CurrentWorld != this || first.Island is null || second.Island is null ||
            first.EntityId is null || second.EntityId is null)
            throw new InvalidDataException("Constraint endpoints require actual current world/island admissions.");
        var master = first.Motion == FixedMotion ? second : first;
        var slave = ReferenceEquals(master, first) ? second : first;
        if (master.Motion == FixedMotion)
            RefuseUnknown("Living fixed-to-fixed constraint admission is unowned.");
        // Find after all equal keys. Binary lookup bounds comparison work; the
        // actual source array insertion necessarily shifts following entries.
        var low = 0; var high = master.MasterMemberships.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            var existing = (FalloutLivingPhysicalConstraint)master.MasterMemberships[middle];
            if (Compare(existing, constraint) <= 0) low = middle + 1; else high = middle;
        }
        _backend.AddConstraint(constraint.Backend);
        if (!constraint.Backend.IsWorldAttached)
            throw new InvalidDataException("Constraint admission did not publish its actual underlying joint.");
        if (!first.Island.Find().Fixed && !second.Island.Find().Fixed)
            FalloutLivingPhysicalIsland.Join(first.Island, second.Island);
        master.MasterMemberships.Insert(low, constraint);
        for (var index = low; index < master.MasterMemberships.Count; index++)
            ((FalloutLivingPhysicalConstraint)master.MasterMemberships[index]).MasterIndex = index;
        constraint.SlaveIndex = slave.SlaveMemberships.Count; slave.SlaveMemberships.Add(constraint);
        constraint.Master = master; constraint.Slave = slave; constraint.CurrentWorld = this;
        _constraints.Add(constraint); _constraintAdmission.Add(constraint);
    }

    private static int Compare(FalloutLivingPhysicalConstraint left, FalloutLivingPhysicalConstraint right)
    {
        var priority = left.Source.Header.Priority.CompareTo(right.Source.Header.Priority);
        if (priority != 0) return priority;
        var first = left.FirstBody.EntityId!.Value.CompareTo(right.FirstBody.EntityId!.Value);
        return first != 0 ? first : left.SecondBody.EntityId!.Value.CompareTo(right.SecondBody.EntityId!.Value);
    }

    // Full current-model retirement has an inverse owner. Removing a single
    // body/constraint from a surviving island needs separate source split and
    // dynamic contact semantics, and is intentionally not exposed as success.
    internal void RetireModel(FalloutLivingActorModel model)
    {
        RequireCurrent(); model.RequireCurrent();
        var bodies = _bodyAdmission.Where(body => ReferenceEquals(body.Model, model)).ToArray();
        var selected = bodies.ToHashSet();
        if (_criticalDepth != 0 || bodies.Any(body => body.PendingWorld is not null) ||
            _pendingBodies.Any(body => ReferenceEquals(body.Model, model)) ||
            _pendingConstraints.Any(value => selected.Contains(value.FirstBody) || selected.Contains(value.SecondBody)))
            throw new NotSupportedException("Living queued/critical model retirement is unowned.");
        var constraints = _constraintAdmission.Where(value => selected.Contains(value.FirstBody) || selected.Contains(value.SecondBody)).ToArray();
        if (constraints.Any(value => !selected.Contains(value.FirstBody) || !selected.Contains(value.SecondBody)))
            throw new NotSupportedException("Living model retirement has surviving cross-model physical constraints.");
        foreach (var constraint in constraints)
        {
            _backend.RemoveConstraint(constraint.Backend);
            constraint.Master!.MasterMemberships.Remove(constraint); constraint.Slave!.SlaveMemberships.Remove(constraint);
            constraint.CurrentWorld = null; constraint.Master = constraint.Slave = null;
            constraint.MasterIndex = constraint.SlaveIndex = null; _constraints.Remove(constraint); _constraintAdmission.Remove(constraint);
        }
        foreach (var body in bodies)
        {
            _backend.RemoveBody(body.Backend);
            body.CurrentWorld = null; body.Island = null; body.EntityId = null;
            body.MasterMemberships.Clear(); body.SlaveMemberships.Clear(); _bodies.Remove(body); _bodyAdmission.Remove(body);
        }
    }

    internal void RequireSourceContactProduction() => RefuseUnknown(
        "Living source contact-constraint publication, collector ordering and solver island parity are unowned.");

    private void RefuseUnknown(string message)
    { _unknownState ??= message; throw new NotSupportedException(message); }

    public void Dispose()
    {
        if (_retired) return; RequireCurrent();
        if (_criticalDepth != 0 || _pendingBodies.Count != 0 || _pendingConstraints.Count != 0)
            throw new NotSupportedException("Living physical pending world retirement is unowned.");
        foreach (var model in _bodies.Select(body => body.Model).Distinct().ToArray()) RetireModel(model);
        _retired = true;
    }
}

// The C# structural simulation cohorts are real admission owners. Backend
// solver-island equivalence remains a separate visible parity boundary.
internal sealed class FalloutLivingPhysicalIsland
{
    private FalloutLivingPhysicalIsland? _parent;
    private int _rank;
    internal FalloutLivingPhysicalIsland(bool fixedIsland) => Fixed = fixedIsland;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal bool Fixed { get; }
    internal FalloutLivingPhysicalIsland Find()
    {
        var root = this;
        while (root._parent is not null) root = root._parent;
        var current = this;
        while (current._parent is { } parent) { current._parent = root; current = parent; }
        return root;
    }
    internal static void Join(FalloutLivingPhysicalIsland first, FalloutLivingPhysicalIsland second)
    {
        first = first.Find(); second = second.Find(); if (ReferenceEquals(first, second)) return;
        if (first.Fixed || second.Fixed) throw new InvalidOperationException("Fixed island cannot merge into a moving cohort.");
        if (first._rank < second._rank) first._parent = second;
        else { second._parent = first; if (first._rank == second._rank) first._rank++; }
    }
}
