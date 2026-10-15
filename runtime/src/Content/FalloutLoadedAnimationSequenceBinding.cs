using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

// The source sampler writes C# local transforms. A native skeleton may project
// this same graph afterwards, but supplies no source poses, clocks or targets.
internal sealed class FalloutLoadedAnimationSequenceBinding
{
    private readonly FalloutActorLogicalScene _scene;
    private readonly List<(FalloutNifSceneKey Target, FalloutNifAnimationSampler Sampler)> _transforms = [];
    private readonly List<RuntimeNifUnboundAnimationChannel> _unbound = [];
    private readonly List<FalloutNifControllerLink> _absent = [];
    internal IReadOnlyList<RuntimeNifUnboundAnimationChannel> UnboundChannels => _unbound;
    internal IReadOnlyList<FalloutNifControllerLink> AbsentSourceTargets => _absent;
    internal int TransformChannelCount => _transforms.Count;
    internal FalloutLoadedAnimationSequenceBinding(FalloutLoadedAnimationResource resource, FalloutActorLogicalScene scene)
    {
        _scene = scene; var palette = new Dictionary<string, FalloutNifSceneObject>(StringComparer.Ordinal);
        foreach (var node in scene.Objects) palette.TryAdd(node.Name, node);
        var selected = new HashSet<FalloutNifSceneKey>();
        foreach (var link in resource.Sequence.ControlledBlocks)
        {
            try
            {
                if (!palette.TryGetValue(link.NodeName, out var target))
                { if (link.Interpolator >= 0) _ = resource.File.ReadObject(link.Interpolator); _absent.Add(link); continue; }
                if (link.ControllerType != "NiTransformController" || link.PropertyType.Length != 0 ||
                    link.Variable1.Length != 0 || link.Variable2.Length != 0)
                { _unbound.Add(new(link, "The current source channel needs its independent non-transform field owner.")); continue; }
                if (!selected.Add(target.Key))
                    throw new NotSupportedException("Multiple transform links on one target need the actual blend child.");
                _transforms.Add((target.Key, new(resource.File, link.Interpolator)));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { _unbound.Add(new(link, error.Message)); }
        }
    }
    internal void ApplySourceTime(float sourceTime)
    {
        if (!float.IsFinite(sourceTime) || _unbound.Count != 0)
            throw new NotSupportedException("The active loaded sequence has unresolved source time or channel fields.");
        foreach (var (target, sampler) in _transforms)
            _scene.PublishAnimationSample(target, sampler.Sample(sourceTime));
    }
}
