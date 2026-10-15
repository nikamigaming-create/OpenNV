using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutLoadedAnimationSequenceSnapshot(Guid Identity, FalloutLoadedAnimationResourceReceipt Resource,
    FalloutLoadedAnimationClockSnapshot Clock, int TransformChannels, int AbsentTargets, FalloutActorMoverVector Motion, byte Priority);
internal sealed record FalloutLoadedAnimationManagerSnapshot(string Source, Guid Factory, Guid ModelFactory, Guid LogicalLifetime,
    long Sequence, int SequenceCapacity, int NameBuckets, int PaletteBuckets,
    IReadOnlyList<FalloutLoadedAnimationSequenceSnapshot> Sequences, IReadOnlyDictionary<ushort, Guid> SingleGroups,
    IReadOnlyDictionary<ushort, IReadOnlyList<Guid>> MultipleGroups, string? Failure, bool Retired, bool Registered, FalloutLoadedAnimationRootUpdate? LastUpdate);
internal sealed record FalloutLoadedAnimationRootUpdate(Guid Factory, long Entered, long Changed,
    uint AbsoluteBits, int ActiveSequences, IReadOnlyList<Guid> Updated, bool Returned, string? Failure);

// A real source controller manager owns independently cloned sequence clocks.
// The existing sampler publishes C# poses into the SAME logical graph. Native
// presentation independently projects that authoritative graph afterwards.
internal sealed class FalloutLoadedAnimationManagerState
{
    private sealed class Sequence(FalloutLoadedAnimationResource resource, FalloutLoadedAnimationSequenceBinding binding)
    {
        internal readonly Guid Identity = Guid.NewGuid();
        internal readonly FalloutLoadedAnimationResource Resource = resource;
        internal readonly FalloutLoadedAnimationSequenceBinding Binding = binding;
        internal readonly FalloutLoadedAnimationSequenceClock Clock = new(resource.Sequence);
        internal byte Priority;
        internal FalloutLoadedAnimationSequenceSnapshot Capture() => new(Identity, Resource.Receipt, Clock.Capture(),
            Binding.TransformChannelCount, Binding.AbsentSourceTargets.Count, Resource.Motion, Priority);
    }
    private readonly FalloutLoadedAnimationSource _source;
    private readonly FalloutActorLogicalScene _scene;
    private readonly FalloutLoadedAnimationResources _resources;
    private readonly Action _requireActual;
    private readonly Dictionary<Guid, Sequence> _sequences = [];
    private readonly Dictionary<ushort, Guid> _single = [];
    private readonly Dictionary<ushort, List<Guid>> _multiple = [];
    private sealed class Bucket { internal Guid Identity { get; } = Guid.NewGuid(); }
    private readonly Dictionary<ushort, Bucket> _buckets = [];
    private readonly HashSet<Guid> _active = [];
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private long _sequence;
    private bool _busy, _retired, _registered;
    private string? _failure;
    internal Guid Factory { get; } = Guid.NewGuid();
    internal FalloutNifSceneObject? AccumulationRoot { get; private set; }
    internal string? SaveBlocker => _retired ? "source-loaded-animation-manager-retired" :
        _busy ? "source-loaded-animation-manager-entered" : _failure;
    internal FalloutLoadedAnimationRootUpdate? LastUpdate { get; private set; }

    internal FalloutLoadedAnimationManagerState(FalloutLoadedAnimationSource source, FalloutLoadedAnimationResources resources,
        FalloutActorLogicalScene scene, Action requireActual)
    {
        source.Validate(); ArgumentNullException.ThrowIfNull(resources); ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(requireActual);
        _source = source; _resources = resources; _scene = scene; _requireActual = requireActual; Require();
        if (resources.Source != source || resources.Model.Factory != scene.Factory ||
            resources.Model.Skeleton.Source.Sha256 != scene.SkeletonSha256)
            throw new InvalidDataException("Animation manager changed its actual source model, root palette or native adapter.");
    }
    internal void RegisterResources()
    {
        Require();
        if (_registered || _sequences.Count != 0) throw new InvalidOperationException("Source animation resources were already imported or failed part way.");
        var source = _source; var resources = _resources; var scene = _scene;
        var names = new Dictionary<string, FalloutNifSceneObject>(StringComparer.Ordinal);
        foreach (var node in scene.Objects.Where(node => node.IsNode)) names.TryAdd(node.Name, node);
        try
        {
            foreach (var resource in resources.Resources)
            {
                Require();
                var binding = new FalloutLoadedAnimationSequenceBinding(resource, scene);
                var sequence = new Sequence(resource, binding); _sequences.Add(sequence.Identity, sequence);
                var group = source.Groups[resource.Receipt.Group & 0xff];
                if (!_buckets.ContainsKey(resource.Receipt.Group)) _buckets.Add(resource.Receipt.Group, new());
                if (group.Multiple)
                {
                    if (!_multiple.TryGetValue(resource.Receipt.Group, out var values)) _multiple.Add(resource.Receipt.Group, values = []);
                    values.Add(sequence.Identity);
                }
                else _single[resource.Receipt.Group] = sequence.Identity;
                AccumulationRoot ??= names.GetValueOrDefault(resource.Sequence.TargetName);
                // The registration child samples the current accumulation target
                // at both source endpoints and stores this group's real velocity.
                // These are sampler reads; they do not publish a pose or activate it.
                if (!group.Multiple && group.Index is not (1 or 2) && group.Category is not (3 or 4) && AccumulationRoot is { } accumulation)
                {
                    var link = resource.Sequence.ControlledBlocks.FirstOrDefault(link => link.NodeName == accumulation.Name);
                    if (link is not null)
                    {
                        var sampler = new FalloutNifAnimationSampler(resource.File, link.Interpolator);
                        var start = sampler.Sample(resource.Sequence.StartTime).Translation;
                        var end = sampler.Sample(resource.Sequence.StopTime).Translation;
                        if (start is { } first && end is { } last)
                        {
                            var factor = 1f / (resource.Sequence.StopTime - resource.Sequence.StartTime);
                            resource.Motion = new(Bits((last.X - first.X) * factor), Bits((last.Y - first.Y) * factor), Bits((last.Z - first.Z) * factor));
                        }
                    }
                }
                Next();
            }
            _registered = true; Next(); Require();
        }
        catch (Exception error) { _failure ??= error.Message; throw; }
    }
    internal IReadOnlyDictionary<ushort, Guid> ReadGroupIdentities()
    { Require(); return _buckets.ToDictionary(pair => pair.Key, pair => pair.Value.Identity); }

    internal bool ApplyInitialIdle()
    {
        Require();
        if (!_registered) throw new InvalidOperationException("Initial pose preceded the actual sequence imports.");
        if (!_single.TryGetValue(0, out var identity)) return false;
        var sequence = _sequences[identity];
        sequence.Priority = FalloutLoadedAnimationSource.InitialIdlePriority;
        if (!sequence.Clock.Activate(0x3f800000, restart: true)) throw new InvalidOperationException("New idle sequence was already active.");
        _active.Add(identity); Next();
        Update(0, updateWorldAndBounds: true);
        if (!sequence.Clock.DeactivateImmediately()) throw new InvalidOperationException("Initial idle lost its actual activation before deactivation.");
        _active.Remove(identity); Next(); return true;
    }
    internal FalloutLoadedAnimationRootUpdate Update(uint absoluteBits, bool updateWorldAndBounds = false)
    {
        Require(); var entered = Next(); _busy = true; var updated = new List<Guid>(); var activeCount = _active.Count; string? failure = null;
        try
        {
            foreach (var identity in _active)
            {
                var sequence = _sequences[identity];
                RequireCurrentUpdate();
                if (!sequence.Clock.Active) continue;
                if (sequence.Binding.UnboundChannels.Count != 0)
                    throw new NotSupportedException("source-loaded-animation-active-controller-channel-unowned:" + sequence.Resource.Receipt.Path + ":" +
                        string.Join(";", sequence.Binding.UnboundChannels.Select(row => row.Source.NodeName + "/" + row.Source.ControllerType + "/" + row.Reason)));
                var time = sequence.Clock.Update(absoluteBits);
                sequence.Binding.ApplySourceTime(time); updated.Add(sequence.Identity); Next(); RequireCurrentUpdate();
            }
            // The binding's explicit root update includes world/bounds. The
            // ordinary AnimData helper updates controllers only; its caller
            // keeps the later scene transform/bound stores independent.
            if (updateWorldAndBounds) _scene.UpdateWorldAndBounds();
            RequireCurrentUpdate();
        }
        catch (Exception error) { failure = error.Message; _failure ??= failure; throw; }
        finally
        {
            LastUpdate = new(Factory, entered, Next(), absoluteBits, activeCount, updated.ToArray(), failure is null, failure);
            _busy = false;
        }
        return LastUpdate!;
    }
    internal FalloutLoadedAnimationManagerSnapshot Capture()
    {
        if (_busy) throw new InvalidOperationException("An entered animation manager cannot be captured.");
        return new(_source.Identity, Factory, _scene.Factory, _scene.Lifetime, _sequence,
            FalloutLoadedAnimationSource.ManagerSequenceCapacity, FalloutLoadedAnimationSource.ManagerNameBuckets,
            FalloutLoadedAnimationSource.PaletteNameBuckets, _sequences.Values.Select(row => row.Capture()).ToArray(),
            new Dictionary<ushort, Guid>(_single), _multiple.ToDictionary(row => row.Key, row => (IReadOnlyList<Guid>)row.Value.ToArray()),
            _failure, _retired, _registered, LastUpdate);
    }
    internal void Retire()
    {
        if (_retired) return;
        if (_busy || _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Animation manager retirement left its idle source owner/thread.");
        foreach (var identity in _active) _sequences[identity].Clock.DeactivateImmediately();
        _active.Clear();
        _retired = true; Next();
    }
    private void Require(bool allowFailure = false)
    {
        if (_busy) throw new InvalidOperationException("Loaded animation manager reentered its actual update.");
        ObjectDisposedException.ThrowIf(_retired, this); _requireActual(); _resources.RequireCurrent();
        if (_thread != Environment.CurrentManagedThreadId || _scene.SaveBlocker is not null && !allowFailure)
            throw new InvalidOperationException("Animation manager lost its actual model/source/current thread.");
        if (!allowFailure && _failure is { } failure) throw new NotSupportedException(failure);
    }
    private void RequireCurrentUpdate()
    {
        if (!_busy || _retired || _thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Sequence callback left its actual manager update.");
        _requireActual(); _resources.RequireCurrent();
    }
    private long Next() => checked(++_sequence);
    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);
}
