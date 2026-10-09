using Godot;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Content;

internal enum RuntimeNativeLandscapeConstructionPhase
{
    Entered, Textures, Mesh, Material, Geometry, Root, Collision, Returned, Retiring, Retired
}

// This is the actual constructor allocation owner, not a source-graph or draw
// completion receipt. Native IDs remain process-local and are never persisted.
internal sealed class RuntimeNativeLandscapeConstruction
{
    private sealed class Allocation(GodotObject value, ulong identity, bool node)
    {
        internal GodotObject Value { get; } = value;
        internal ulong Identity { get; } = identity;
        internal bool Node { get; } = node;
        internal bool DisposeEntered { get; set; }
    }
    private readonly List<Allocation> _allocations = [];
    private readonly Action<RuntimeNativeLandscapeConstruction>? _observe;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private readonly List<Exception> _retirementErrors = [];
    private readonly List<(ulong Identity, string Receiver)> _resourceTransfers = [];
    private ulong? _returnedRoot;
    private ulong? _publishedParent;
    internal Guid Identity { get; } = Guid.NewGuid();
    internal FalloutLandscapeTransport Source { get; }
    internal string TransportSha256 { get; }
    internal RuntimeNativeLandscapeConstructionPhase Phase { get; private set; }
    internal Exception? OriginalFailure { get; private set; }
    internal IReadOnlyList<Exception> RetirementErrors => _retirementErrors.ToArray();
    internal IReadOnlyList<ulong> NodeIdentities => _allocations.Where(value => value.Node).Select(value => value.Identity).ToArray();
    internal IReadOnlyList<ulong> ResourceIdentities => _allocations.Where(value => !value.Node).Select(value => value.Identity).ToArray();
    internal IReadOnlyList<(ulong Identity, string Receiver)> ResourceTransfers => _resourceTransfers.ToArray();
    internal IReadOnlyList<ulong> StillOwnedNodes => NodeIdentities.Where(Alive).ToArray();
    internal bool NativeNodesDestroyed => NodeIdentities.All(identity => !Alive(identity));

    internal RuntimeNativeLandscapeConstruction(FalloutLandscapeTransport source,
        Action<RuntimeNativeLandscapeConstruction>? observe = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        TransportSha256 = FalloutCellNativeSource.TransportDigest(source);
        _observe = observe;
    }
    internal T Own<T>(T value) where T : GodotObject
    {
        RequireThread(); ArgumentNullException.ThrowIfNull(value);
        if (!GodotObject.IsInstanceValid(value) || value is not (Node or Resource))
            throw new InvalidDataException("LAND constructor returned no admitted native Node/Resource allocation.");
        var identity = value.GetInstanceId();
        if (identity == 0 || _allocations.Any(allocation => allocation.Identity == identity))
            throw new InvalidDataException("LAND construction repeated or borrowed its actual native allocation.");
        if (Phase is RuntimeNativeLandscapeConstructionPhase.Returned or RuntimeNativeLandscapeConstructionPhase.Retiring or RuntimeNativeLandscapeConstructionPhase.Retired)
            throw new InvalidOperationException("LAND construction allocated after return/retirement.");
        _allocations.Add(new(value, identity, value is Node)); return value;
    }
    internal void TransferResourceToCache(Resource resource, string receiver, Func<bool> actualCacheOwnsResource)
    {
        RequireThread(); ArgumentException.ThrowIfNullOrWhiteSpace(receiver); ArgumentNullException.ThrowIfNull(actualCacheOwnsResource);
        var identity = resource.GetInstanceId();
        var allocation = _allocations.SingleOrDefault(value => value.Identity == identity && !value.Node);
        if (allocation is null || !Alive(identity) || OriginalFailure is not null ||
            Phase is RuntimeNativeLandscapeConstructionPhase.Returned or RuntimeNativeLandscapeConstructionPhase.Retiring or RuntimeNativeLandscapeConstructionPhase.Retired ||
            !actualCacheOwnsResource())
            throw new InvalidDataException("LAND cache transfer has no actual resource/receiver ownership.");
        _resourceTransfers.Add((identity, receiver)); _allocations.Remove(allocation);
    }
    internal void Advance(RuntimeNativeLandscapeConstructionPhase phase)
    {
        RequireThread();
        if (phase is RuntimeNativeLandscapeConstructionPhase.Returned or RuntimeNativeLandscapeConstructionPhase.Retiring or RuntimeNativeLandscapeConstructionPhase.Retired ||
            OriginalFailure is not null || Phase is RuntimeNativeLandscapeConstructionPhase.Returned or RuntimeNativeLandscapeConstructionPhase.Retiring or RuntimeNativeLandscapeConstructionPhase.Retired)
            throw new InvalidOperationException("LAND constructor crossed its returned or failed lifetime.");
        Phase = phase; _observe?.Invoke(this);
    }
    internal void FactoryReturned(RuntimeNativeLandscapeTransport root)
    {
        RequireThread();
        if (OriginalFailure is not null || !GodotObject.IsInstanceValid(root) || root.IsQueuedForDeletion() ||
            root.GetParent() is not null || !NodeIdentities.Contains(root.GetInstanceId()) || root.Construction != this ||
            root.Source != Source || FalloutCellNativeSource.TransportDigest(root.Source) != TransportSha256 ||
            _allocations.Where(value => value.Node).Any(value => !Alive(value.Identity) ||
                value.Value is not Node node || node != root && !root.IsAncestorOf(node)))
            throw new InvalidDataException("LAND factory return lost its complete actual allocation/source hierarchy.");
        _returnedRoot = root.GetInstanceId(); Phase = RuntimeNativeLandscapeConstructionPhase.Returned;
        _observe?.Invoke(this);
    }
    internal void BindActualParent(Node3D parent)
    {
        RequireThread(); RequireReturned();
        if (!GodotObject.IsInstanceValid(parent) || parent.IsQueuedForDeletion() ||
            GodotObject.InstanceFromId(_returnedRoot!.Value) is not Node root || root.GetParent() != parent ||
            _publishedParent is { } bound && bound != parent.GetInstanceId())
            throw new InvalidDataException("LAND constructor publication borrowed a different actual CELL root.");
        _publishedParent = parent.GetInstanceId();
    }
    internal void EnterActualParentPublication(Node3D parent)
    {
        RequireThread(); RequireReturned();
        if (!GodotObject.IsInstanceValid(parent) || parent.IsQueuedForDeletion() ||
            _publishedParent is { } bound && bound != parent.GetInstanceId())
            throw new InvalidDataException("LAND publication entry lost its actual retained native CELL parent.");
        // This records the actual caller's intended parent, not completion.
        // BindActualParent still requires the constructor root's real parent.
        _publishedParent = parent.GetInstanceId();
    }
    internal void RequireReturned()
    {
        RequireThread();
        if (Phase != RuntimeNativeLandscapeConstructionPhase.Returned || OriginalFailure is not null ||
            _returnedRoot is not { } root || !Alive(root) || _allocations.Any(value => !Alive(value.Identity)))
            throw new NotSupportedException("LAND construction has no healthy living actual factory-return lease.");
    }
    internal void MarkCallerFailure(Exception original)
    { RequireThread(); OriginalFailure ??= original ?? throw new ArgumentNullException(nameof(original)); }

    internal void UnwindBeforeReturn(Exception original)
    {
        RequireThread(); MarkCallerFailure(original);
        RetireFailedAllocations();
    }
    internal void RetireFailedAllocations()
    {
        RequireThread();
        if (OriginalFailure is null) throw new InvalidOperationException("Failed LAND unwind has no original constructor/caller failure.");
        if (Phase == RuntimeNativeLandscapeConstructionPhase.Retired) return;
        Phase = RuntimeNativeLandscapeConstructionPhase.Retiring;
        var errors = new List<Exception>();
        foreach (var allocation in _allocations.Where(value => value.Node).Reverse())
        {
            if (!Alive(allocation.Identity)) continue;
            try
            {
                if (GodotObject.InstanceFromId(allocation.Identity) is not Node node ||
                    node.GetParent() is { } parent && !_allocations.Any(value => value.Node && value.Identity == parent.GetInstanceId()) &&
                        (_returnedRoot != allocation.Identity || _publishedParent != parent.GetInstanceId()))
                    throw new InvalidDataException("Failed LAND allocation moved into an unrelated native lifetime; retirement is refused.");
                node.Free();
            }
            catch (Exception error) { errors.Add(error); }
        }
        if (!NativeNodesDestroyed)
            errors.Add(new InvalidOperationException("Failed LAND constructor still owns native nodes; detach/QueueFree is not destruction."));
        if (NativeNodesDestroyed)
            try { RetireResources(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) RetainRetirementFailure(errors);
    }
    internal void ObserveNativeDestructionAndRetireResources()
    {
        RequireThread();
        if (Phase == RuntimeNativeLandscapeConstructionPhase.Retired) return;
        if (!NativeNodesDestroyed)
            throw new InvalidOperationException("LAND resource retirement precedes actual native node destruction.");
        Phase = RuntimeNativeLandscapeConstructionPhase.Retiring; RetireResources();
    }
    private void RetireResources()
    {
        var errors = new List<Exception>();
        // The retained managed binding itself owns one native reference. Keep
        // it alive while a mesh, material or foreign native consumer borrows
        // the resource; releasing that binding early can cause Godot to create
        // another binding during the consumer's later unreference callback.
        // A successful release can make another owned dependency releasable.
        bool progressed;
        do
        {
            progressed = false;
            foreach (var allocation in _allocations.Where(value => !value.Node))
            {
                if (!Alive(allocation.Identity) || allocation.DisposeEntered) continue;
                try
                {
                    if (allocation.Value is not Resource resource || !GodotObject.IsInstanceValid(resource))
                        throw new InvalidOperationException("A living LAND resource lost its retained managed binding before release.");
                    var references = resource.GetReferenceCount();
                    if (references < 1) throw new InvalidDataException("Living LAND resource has no native reference owner.");
                    if (references != 1) continue;
                    allocation.DisposeEntered = true; progressed = true;
                    resource.Dispose();
                }
                catch (Exception error) { errors.Add(error); }
            }
        } while (progressed);
        // Failed or externally borrowed resources retain their real ownership.
        // Retry never repeats a release through an already disposed wrapper.
        var retained = _allocations.Where(value => Alive(value.Identity)).ToArray();
        if (retained.Length != 0)
            errors.Add(new InvalidOperationException("LAND retirement retains native allocations or external resource borrowers: " +
                string.Join(", ", retained.Select(value => $"{value.Value.GetType().Name} id={value.Identity} node={value.Node} managedReleaseEntered={value.DisposeEntered}")) + "."));
        if (errors.Count != 0) RetainRetirementFailure(errors);
        Phase = RuntimeNativeLandscapeConstructionPhase.Retired;
    }
    private void RetainRetirementFailure(IReadOnlyList<Exception> errors)
    {
        _retirementErrors.AddRange(errors);
        IEnumerable<Exception> all = OriginalFailure is { } original ? new[] { original }.Concat(errors) : errors;
        throw new AggregateException("LAND construction/retirement retains its original failure and actual still-owned allocations.", all);
    }
    // Query the native ID without creating a new RefCounted managed binding.
    private static bool Alive(ulong identity) => GodotObject.IsInstanceIdValid(identity);
    private void RequireThread()
    {
        if (System.Environment.CurrentManagedThreadId != _thread)
            throw new InvalidOperationException("Actual LAND allocation retirement left its native presentation-thread lifetime.");
    }
}
