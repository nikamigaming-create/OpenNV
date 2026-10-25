using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

// Static ANIO geometry follows its owning KF. Independent clocks/physics remain
// with their own owners instead of being reset by an actor animation loop.
internal sealed class NativeActorIdleObjects : IDisposable
{
    private readonly List<Node3D> _roots = [];
    private readonly Node3D[] _nodes;
    private readonly FalloutNifFile _animation;
    private readonly float _units;
    internal IReadOnlyList<FalloutActorIdleObjectBinding> Bindings { get; }
    internal IReadOnlySet<string> Targets { get; }

    internal NativeActorIdleObjects(FalloutPluginStack records, RuntimeLiveContentSource content,
        RuntimeNativeNifSkeleton skeleton, FalloutActorIdleSource idle,
        IReadOnlyList<FalloutActorIdleObjectBinding>? saved = null)
    {
        _units = skeleton.UnitsToMetres;
        _animation = NativeAnimationResources.For(content).Read(idle.AnimationPath);
        var sequences = _animation.Roots.Select(_animation.ReadObject).OfType<FalloutNifControllerSequence>().ToArray();
        if (sequences.Length != 1) throw new NotSupportedException("ANIO owner requires one source IDLE sequence.");
        var controlled = sequences[0].ControlledBlocks.Where(link => link.ControllerType is "NiTransformController" or "NiVisController" &&
            link.PropertyType.Length == 0 && link.Variable1.Length == 0 && link.Variable2.Length == 0)
            .Select(link => link.NodeName).ToHashSet(StringComparer.Ordinal);
        var bindings = new List<FalloutActorIdleObjectBinding>();
        try
        {
            foreach (var item in idle.Objects)
            {
                if (!content.TryRead(item.ModelPath, null, out var bytes, out _))
                    throw new FileNotFoundException("Source ANIO is absent: " + item.ModelPath);
                var source = FalloutNifFile.Read(bytes);
                var binding = new FalloutActorIdleObjectBinding(item.Form,
                    FalloutActorFurnitureContinuation.RecordHash(records.GetEffective(item.Form)), item.ModelPath, source.Sha256);
                binding.ValidateModel(source);
                if (saved is not null) saved[bindings.Count].ValidateModel(source);
                bindings.Add(binding);
                var part = NativeNifMeshBuilder.AddActorPart(source, skeleton, externalTransformTargets: controlled);
                part.Root.Visible = false;
                part.Root.SetMeta("opennv_source_model", item.ModelPath);
                part.Root.SetMeta("opennv_animation_object_form", item.Form.ToString());
                _roots.Add(part.Root);
            }
            _nodes = _roots.SelectMany(root => root.FindChildren("*", "", true, false).OfType<Node3D>().Prepend(root)).ToArray();
            Targets = _nodes.Select(node => node.Name.ToString()).ToHashSet(StringComparer.Ordinal);
            Bindings = bindings.ToArray();
        }
        catch { Dispose(); throw; }
    }

    internal Action<float>? Bind(FalloutNifControllerLink link)
    {
        if (link.ControllerType is not ("NiTransformController" or "NiVisController") || link.PropertyType.Length != 0 ||
            link.Variable1.Length != 0 || link.Variable2.Length != 0) return null;
        var matches = _nodes.Where(node => node.Name.ToString() == link.NodeName).ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length != 1) throw new NotSupportedException("ANIO channel target is ambiguous: " + link.NodeName);
        if (link.ControllerType == "NiVisController")
        {
            var visibility = new FalloutNifBoolAnimation(_animation, link.Interpolator);
            return time => matches[0].Visible = visibility.Sample(time);
        }
        var sampler = new FalloutNifAnimationSampler(_animation, link.Interpolator);
        return time =>
        {
            var sample = sampler.Sample(time);
            if (sample.Translation is { } p) matches[0].Position = GamebryoCoordinate.ConvertVector(new(p.X, p.Y, p.Z)) * _units;
            if (sample.Rotation is { } q) matches[0].Quaternion = new Quaternion(q.X, q.Z, -q.Y, q.W).Normalized();
            if (sample.Scale is { } scale) matches[0].Scale = Vector3.One * scale;
        };
    }

    internal void SetVisible(bool visible) { foreach (var root in _roots) root.Visible = visible; }
    public void Dispose() { foreach (var root in _roots) if (GodotObject.IsInstanceValid(root)) root.Free(); _roots.Clear(); }
}
