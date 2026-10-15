using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutAnimationResourceSlot(Guid? Resource, Guid? Reference, Guid? Payload);
internal sealed record FalloutAnimationResourceCleanup(FalloutReferenceModelDataInvocation Invocation,
    Guid Factory, long Entered, long Changed, int Visited, int Cleared, bool Returned, string? Failure);
internal sealed record FalloutActorAnimationResourceSnapshot(string Source, Guid Factory,
    FalloutReferenceModelDataInvocation Construction, FalloutLoadedAnimationRole Role, Guid? ModelFactory,
    long Sequence, IReadOnlyList<FalloutAnimationResourceSlot> Primary,
    IReadOnlyList<FalloutAnimationResourceSlot> Secondary, string? Writer,
    FalloutAnimationResourceCleanup? LastCleanup, FalloutNifSceneKey? Root,
    IReadOnlyList<FalloutNifSceneKey?> NodeBindings);

// The independent source model-resource object selected by Player +1E8. These
// two arrays are constructed by its real source allocation; neither is an
// AnimData sequence map or a presentation clip cache.
internal sealed class FalloutActorAnimationResourceState
{
    internal const int SlotCount = 20;
    private readonly FalloutAnimationResourceSlot[] _primary = Enumerable.Range(0, SlotCount).Select(_ => new FalloutAnimationResourceSlot(null, null, null)).ToArray();
    private readonly FalloutAnimationResourceSlot[] _secondary = Enumerable.Range(0, SlotCount).Select(_ => new FalloutAnimationResourceSlot(null, null, null)).ToArray();
    private readonly Action _requireLifetime;
    private readonly FalloutLoadedAnimationSource _source;
    private readonly FalloutReferenceModelDataInvocation _construction;
    private readonly FalloutLoadedAnimationRole _role;
    private readonly Guid? _modelFactory;
    private readonly FalloutNifSceneKey? _root;
    private readonly FalloutNifSceneKey?[] _nodes;
    private long _sequence;
    private bool _busy;
    private string? _writer;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutAnimationResourceCleanup? LastCleanup { get; private set; }
    internal FalloutActorAnimationResourceState(FalloutLoadedAnimationSource source, FalloutReferenceModelDataInvocation invocation,
        FalloutLoadedAnimationRole role, FalloutActorLogicalScene? model, Action<FalloutReferenceModelDataInvocation> requireActual,
        Action requireLifetime)
    {
        source.Validate(); requireActual(invocation); requireLifetime();
        if (!Enum.IsDefined(role) || invocation.Ingress != FalloutReferenceModelDataIngress.ThreeDLoad ||
            invocation.Call == Guid.Empty || invocation.ThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Animation resource allocation changed its actual source model construction.");
        _source = source; _construction = invocation; _role = role; _modelFactory = model?.Factory; _requireLifetime = requireLifetime;
        var names = new Dictionary<string, FalloutNifSceneObject>(StringComparer.Ordinal);
        if (model is not null) foreach (var node in model.Objects.Where(node => node.IsNode)) names.TryAdd(node.Name, node);
        _root = names.GetValueOrDefault(source.ResourceRootName)?.Key;
        _nodes = source.ResourceNodeNames.Select(name => names.GetValueOrDefault(name)?.Key).ToArray();
    }
    internal FalloutAnimationResourceCleanup ClearForAnimationLoad(FalloutReferenceModelDataInvocation invocation,
        Action<FalloutReferenceModelDataInvocation> requireActual)
    {
        _requireLifetime(); requireActual(invocation);
        if (_busy || invocation != _construction || _role != FalloutLoadedAnimationRole.Common)
            throw new InvalidOperationException("Resource cleanup changed the actual inherited-loader owner.");
        if (_writer is { } writer) throw new NotSupportedException(writer);
        _busy = true; var entered = ++_sequence; var visited = 0; var cleared = 0; string? failure = null;
        try
        {
            for (var index = 0; index < SlotCount; index++)
            {
                requireActual(invocation); ++visited;
                if (_primary[index].Payload is not null || _secondary[index].Payload is not null)
                    throw new NotSupportedException("source-animation-resource-current-process-node-release-detach-and-deferred-destruction-unowned");
                _primary[index] = _primary[index] with { Resource = null, Reference = null }; _sequence += 2; cleared += 2;
                _secondary[index] = _secondary[index] with { Resource = null, Reference = null }; _sequence += 2; cleared += 2;
                requireActual(invocation);
            }
        }
        catch (Exception error) { failure = error.Message; throw; }
        finally { LastCleanup = new(invocation, Factory, entered, ++_sequence, visited, cleared, failure is null, failure); _busy = false; }
        return LastCleanup!;
    }
    internal void ObserveEnteredWriter(string producer, Action requireActual)
    {
        requireActual(); _requireLifetime(); if (_busy) throw new InvalidOperationException("Animation resource writer reentered cleanup.");
        ArgumentException.ThrowIfNullOrWhiteSpace(producer); _writer = "source-animation-resource-current-writer-unowned:" + producer; ++_sequence;
    }
    internal FalloutActorAnimationResourceSnapshot Capture() => new(_source.Identity, Factory, _construction, _role,
        _modelFactory, _sequence, _primary.ToArray(), _secondary.ToArray(), _writer, LastCleanup, _root, _nodes);
}
