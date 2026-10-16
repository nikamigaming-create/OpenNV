using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerFirstModelCall(FalloutReferenceModelDataInvocation Load,
    Guid Factory, long Entered, long Changed, FalloutSourceSceneNode? Before, FalloutSourceSceneNode? After,
    FalloutPlayerLoadedRootFlag? Flag, bool PointerStored, bool Retained,
    IReadOnlyList<FalloutPlayerModelGlobalStore> Globals, bool Updated, uint? Height,
    bool IdentityRotationStored, bool Returned, string? Failure);
internal sealed record FalloutPlayerFirstModelSnapshot(string Source, Guid Process, Guid Constructor,
    Guid Factory, long Sequence, FalloutSourceSceneNode? Current, uint? Height,
    FalloutPlayerFirstModelCall? LastCall, bool Retired, string? Writer, bool FieldRetained = false,
    IReadOnlyList<FalloutSourceSceneNode>? PendingBindings = null, string? RetirementFailure = null, bool RetirementStarted = false);

// This is the original independent Player first-model field. It is neither
// the reference-data model pointer nor a Scene/camera/TLS query cache.
internal sealed class FalloutPlayerFirstModelState
{
    private readonly FalloutPlayerModelLoadContinuationSource _source;
    private readonly Guid _process, _constructor;
    private readonly FalloutPlayerModelLoadGlobals _globals;
    private readonly Action<FalloutReferenceModelDataInvocation> _requireActual;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly List<FalloutReferenceModelDataNode> _pendingBindingRetirement = [];
    private FalloutReferenceModelDataNode? _current;
    private FalloutReferenceModelDataInvocation? _load;
    private uint? _height;
    private long _sequence, _reentry;
    private bool _busy, _retired, _currentRetained, _retirementStarted;
    private string? _writer, _retirementFailure;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal string? SaveBlocker => _retired ? "source-Player-first-model-retired" : _busy ? "source-Player-first-model-entered" :
        _retirementStarted ? _retirementFailure ?? "source-Player-first-model-retirement-incomplete" :
        _writer ?? LastCall?.Failure ?? (_pendingBindingRetirement.Count != 0 ? "source-Player-first-model-native-binding-retirement-pending" : null);
    internal FalloutPlayerFirstModelCall? LastCall { get; private set; }
    internal FalloutPlayerFirstModelState(FalloutPlayerModelLoadContinuationSource source, Guid process,
        Guid constructor, FalloutPlayerModelLoadGlobals globals, Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(globals); ArgumentNullException.ThrowIfNull(requireActual);
        if (process == Guid.Empty || constructor == Guid.Empty) throw new InvalidDataException("First-model field has no actual Player constructor.");
        if (globals.SourceIdentity != source.Identity) throw new InvalidDataException("First-model globals changed their actual loader source.");
        _source = source; _process = process; _constructor = constructor; _globals = globals; _requireActual = requireActual;
    }
    internal FalloutReferenceModelDataNode? ReadForLoader(FalloutReferenceModelDataInvocation invocation)
    {
        if (_busy && _load != invocation) { ++_reentry; throw new InvalidOperationException("First-model query changed its entered loader."); }
        Require(invocation); return _current;
    }
    internal FalloutSourceSceneNode? ReadCurrent()
    {
        if (_busy) { ++_reentry; throw new InvalidOperationException("First-model field reentered its actual loader."); }
        ObjectDisposedException.ThrowIf(_retired || _retirementStarted, this);
        if (_writer is { } writer) throw new NotSupportedException(writer);
        _current?.RequireCurrent(); return _current?.Node;
    }
    internal FalloutPlayerFirstModelCall PublishAndInitialize(FalloutReferenceModelDataInvocation load,
        FalloutReferenceModelDataNode returned, Action<int, string> reportMissingNode)
    {
        if (_busy) { ++_reentry; throw new InvalidOperationException("First-model publication reentered its actual source field."); }
        Require(load); returned.RequireCurrent();
        if (returned.Node.Process != _process) throw new InvalidDataException("First-model factory belongs to another actual Player process.");
        var before = _current?.Node; var fault = _reentry; _busy = true; _load = load;
        var call = new FalloutPlayerFirstModelCall(load, Factory, Next(), _sequence, before, before,
            null, false, false, [], false, null, false, false, null);
        var globalStores = new List<FalloutPlayerModelGlobalStore>();
        try
        {
            Check(); call = call with { Flag = returned.LogicalScene.StorePlayerFirstModelFlag(load, _requireActual), Changed = Next() };
            if (!ReferenceEquals(_current, returned))
            {
                var old = _current;
                if (old is not null)
                {
                    if (!_currentRetained) throw new InvalidOperationException("Prior first-model pointer has no committed source retain.");
                    if (old.RetirementState == FalloutReferenceModelBindingRetirementState.SourceReleaseFailed)
                        throw new InvalidOperationException("Prior first-model source release failed and cannot replay.");
                    old.Release(() => { _current = null; _currentRetained = false; QueuePending(old); Next(); });
                }
                _current = returned; _currentRetained = false; call = call with { After = returned.Node, PointerStored = true, Changed = Next() };
                returned.Retain(() => { _currentRetained = true; call = call with { Retained = true, Changed = Next() }; });
            }
            var graph = returned.LogicalScene; var nodes = new FalloutNifSceneObject?[3];
            for (var index = 0; index < nodes.Length; ++index)
            {
                Check(); var name = _source.FirstNodeNames[index];
                nodes[index] = graph.ReadPlayerModelNamedObject(name, load, _requireActual);
                globalStores.Add(_globals.StoreFirstNamedNode(load, graph, index, nodes[index]));
            }
            // Source diagnostics run only for the second and third result.
            for (var index = 1; index < nodes.Length; ++index)
                if (nodes[index] is null) { reportMissingNode(index, _source.FirstNodeNames[index]); Check(); }
            graph.UpdateInitialPlayerModel(load, _requireActual); call = call with { Updated = true, Changed = Next() };
            if (nodes[1] is not { } camera)
                throw new InvalidDataException("Source first Camera1st diagnostic returned before its mandatory current-node height dereference.");
            _height = BitConverter.SingleToUInt32Bits(camera.World.Translation.Z);
            call = call with { Height = _height, Changed = Next() };
            graph.StoreInitialPlayerIdentityRotation(load, _requireActual);
            call = call with { IdentityRotationStored = true, Changed = Next() }; Check();
            call = call with { Globals = globalStores.ToArray(), Returned = true, Changed = Next() }; return call;
        }
        catch (Exception error) { call = call with { Failure = error.Message, Changed = Next() }; throw; }
        finally { LastCall = call with { Globals = globalStores.ToArray() }; _load = null; _busy = false; }
        void Check()
        {
            Require(load);
            if (_reentry != fault) throw new InvalidOperationException("First-model callback swallowed actual field reentry.");
        }
    }
    internal void ObserveEnteredWriter(string producer, Action requireActual)
    {
        if (_busy) { ++_reentry; throw new InvalidOperationException("First-model writer reentered an actual source publication."); }
        requireActual(); ArgumentException.ThrowIfNullOrWhiteSpace(producer); _writer ??= producer; Next();
    }
    internal FalloutPlayerFirstModelSnapshot Capture()
    {
        if (_busy) { ++_reentry; throw new InvalidOperationException("First-model capture reentered its actual source lifecycle."); }
        return new(_source.Identity, _process, _constructor, Factory, _sequence, _current?.Node, _height,
            LastCall, _retired, _writer, _currentRetained, _pendingBindingRetirement.Select(node => node.Node).ToArray(),
            _retirementFailure, _retirementStarted);
    }
    // Native preservation depends on committed field/binding ownership even
    // after gameplay or another independent loader child has failed.
    internal bool RetainsModel(Guid factory) => factory != Guid.Empty &&
        (_current is { } current && current.Node.Factory == factory ||
            _pendingBindingRetirement.Any(node => node.Node.Factory == factory &&
                node.RetirementState != FalloutReferenceModelBindingRetirementState.Retired));

    internal bool CleanupUnpublished(FalloutReferenceModelDataNode node)
    {
        ArgumentNullException.ThrowIfNull(node); RequireRetirementEntry();
        if (RetainsModel(node.Node.Factory)) return false;
        if (node.RetirementState == FalloutReferenceModelBindingRetirementState.Retired) return true;
        QueuePending(node); _busy = true; var fault = _reentry;
        try
        {
            RetirePending(node, unpublished: true);
            if (_reentry != fault) throw new InvalidOperationException("Unpublished first-model cleanup swallowed lifecycle reentry.");
            return true;
        }
        catch (Exception error) { _retirementFailure ??= error.Message; throw; }
        finally { _pendingBindingRetirement.RemoveAll(value => value.RetirementState == FalloutReferenceModelBindingRetirementState.Retired); _busy = false; }
    }
    internal void Retire()
    {
        if (_retired) return;
        RequireRetirementEntry(); _retirementStarted = true; _busy = true; var fault = _reentry;
        // A binding first made pending by this release is retried on the next
        // retirement call. Other independent pending bindings still drain now.
        var pending = _pendingBindingRetirement.ToArray();
        var actions = new List<Action>();
        if (_current is { } current)
            actions.Add(() =>
            {
                if (current.RetirementState == FalloutReferenceModelBindingRetirementState.SourceReleaseFailed)
                    throw new InvalidOperationException("First-model field retains a failed source release; that callback cannot replay.");
                if (_currentRetained)
                    current.Release(() => { _current = null; _currentRetained = false; QueuePending(current); Next(); });
                else
                {
                    // A pointer store may precede a failed retain. No source
                    // reference was acquired, so no release may be invented.
                    _current = null; QueuePending(current); Next(); RetirePending(current, unpublished: true);
                }
            });
        actions.AddRange(pending.Select(node => (Action)(() => RetirePending(node, unpublished: false))));
        try
        {
            NativeNifSceneRetirement.Drain("Player independent first-model bindings", actions);
            if (_reentry != fault) throw new InvalidOperationException("First-model retirement swallowed source lifecycle reentry.");
            if (_current is not null || _pendingBindingRetirement.Any(node => node.RetirementState != FalloutReferenceModelBindingRetirementState.Retired))
                throw new InvalidOperationException("First-model retirement retains an independent source owner or native binding.");
            _retired = true;
        }
        catch (Exception error) { _retirementFailure ??= error.Message; throw; }
        finally
        {
            _pendingBindingRetirement.RemoveAll(node => node.RetirementState == FalloutReferenceModelBindingRetirementState.Retired);
            _busy = false;
        }
    }
    private void QueuePending(FalloutReferenceModelDataNode node)
    {
        if (!_pendingBindingRetirement.Contains(node)) _pendingBindingRetirement.Add(node);
    }
    private void RetirePending(FalloutReferenceModelDataNode node, bool unpublished)
    {
        switch (node.RetirementState)
        {
            case FalloutReferenceModelBindingRetirementState.Retired:
                return;
            case FalloutReferenceModelBindingRetirementState.BindingPending:
                node.RetireBinding(); return;
            case FalloutReferenceModelBindingRetirementState.Retained when unpublished:
                // This is the first retirement of an unadopted factory. The
                // node itself proves zero source references before the callback.
                node.RetireBinding(); return;
            default:
                throw new InvalidOperationException("First-model binding is still source-retained, releasing or failed; only pending zero-reference binding retirement can retry.");
        }
    }
    private void RequireRetirementEntry()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_busy) { ++_reentry; throw new InvalidOperationException("First-model lifecycle reentered."); }
        if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("First-model retirement changed its actual source thread.");
    }
    private void Require(FalloutReferenceModelDataInvocation invocation)
    {
        ObjectDisposedException.ThrowIf(_retired || _retirementStarted, this); _requireActual(invocation);
        if (_writer is { } writer) throw new NotSupportedException(writer);
        if (invocation.ReferenceProcess != _process || invocation.ReferenceConstructor != _constructor ||
            invocation.ThreadId != _thread || _thread != Environment.CurrentManagedThreadId ||
            invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad)
            throw new InvalidOperationException("First-model child left its actual entered Player constructor/load.");
    }
    private long Next() => _sequence = checked(_sequence + 1);
}
