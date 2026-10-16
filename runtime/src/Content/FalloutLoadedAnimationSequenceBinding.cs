using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

// The source sampler writes C# local transforms. A native skeleton may project
// this same graph afterwards, but supplies no source poses, clocks or targets.
internal sealed class FalloutLoadedAnimationSequenceBinding
{
    private readonly FalloutActorLogicalScene _scene;
    private readonly List<Action<float>> _channels = [];
    private int _transformChannels, _floatChannels, _visibilityChannels, _morphChannels;
    private readonly List<RuntimeNifUnboundAnimationChannel> _unbound = [];
    private readonly List<FalloutNifControllerLink> _absent = [];
    internal IReadOnlyList<RuntimeNifUnboundAnimationChannel> UnboundChannels => _unbound;
    internal IReadOnlyList<FalloutNifControllerLink> AbsentSourceTargets => _absent;
    internal int TransformChannelCount => _transformChannels;
    internal int FloatExtraDataChannelCount => _floatChannels;
    internal int VisibilityChannelCount => _visibilityChannels;
    internal int MorphChannelCount => _morphChannels;
    internal FalloutLoadedAnimationSequenceBinding(FalloutLoadedAnimationResource resource, FalloutActorLogicalScene scene)
    {
        _scene = scene; var palette = new Dictionary<string, FalloutNifSceneObject>(StringComparer.Ordinal);
        foreach (var node in scene.Objects) palette.TryAdd(node.Name, node);
        var selected = new HashSet<FalloutNifSceneKey>(); var selectedFloats = new HashSet<FalloutNifSceneKey>();
        var selectedVisibility = new HashSet<FalloutNifSceneKey>();
        var selectedMorphs = new HashSet<(FalloutNifSceneKey Target, int Index)>();
        foreach (var link in resource.Sequence.ControlledBlocks)
        {
            try
            {
                if (!palette.TryGetValue(link.NodeName, out var target))
                { if (link.Interpolator >= 0) _ = resource.File.ReadObject(link.Interpolator); _absent.Add(link); continue; }
                if (link.ControllerType == "NiFloatExtraDataController")
                {
                    var field = scene.RequireFloatExtraDataKey(target.Key, link.Variable1);
                    var apply = scene.BindFloatExtraDataAnimation(target.Key, resource.File, link);
                    if (!selectedFloats.Add(field))
                        throw new NotSupportedException("Multiple float links on one source field need the actual blend child.");
                    _channels.Add(apply); ++_floatChannels; continue;
                }
                if (link.ControllerType == "NiVisController")
                {
                    var apply = scene.BindVisibilityAnimation(target.Key, resource.File, link);
                    if (!selectedVisibility.Add(target.Key))
                        throw new NotSupportedException("Multiple visibility links on one source field need the actual blend child.");
                    _channels.Add(apply); ++_visibilityChannels; continue;
                }
                if (link.ControllerType == "NiGeomMorpherController")
                {
                    var field = scene.RequireMorphField(target.Key); var index = field.RequireIndex(link.Variable2);
                    var apply = scene.BindMorphAnimation(target.Key, resource.File, link);
                    if (!selectedMorphs.Add((target.Key, index)))
                        throw new NotSupportedException("Multiple morph links on one source slot need the actual blend child.");
                    _channels.Add(apply); ++_morphChannels; continue;
                }
                if (link.ControllerType != "NiTransformController" || link.PropertyType.Length != 0 ||
                    link.Variable1.Length != 0 || link.Variable2.Length != 0)
                { _unbound.Add(new(link, "The current source channel needs its independent non-transform field owner.")); continue; }
                if (!selected.Add(target.Key))
                    throw new NotSupportedException("Multiple transform links on one target need the actual blend child.");
                var sampler = new FalloutNifAnimationSampler(resource.File, link.Interpolator);
                _channels.Add(time => _scene.PublishAnimationSample(target.Key, sampler.Sample(time))); ++_transformChannels;
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException)
            { _unbound.Add(new(link, error.Message)); }
        }
    }
    internal void ApplySourceTime(float sourceTime)
    {
        if (!float.IsFinite(sourceTime) || _unbound.Count != 0)
            throw new NotSupportedException("The active loaded sequence has unresolved source time or channel fields.");
        foreach (var apply in _channels) apply(sourceTime);
    }
}
