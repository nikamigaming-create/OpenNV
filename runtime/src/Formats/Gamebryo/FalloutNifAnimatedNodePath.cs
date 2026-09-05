namespace OpenNV.Runtime.Formats.Gamebryo;

// Resolves one node through its complete source hierarchy. Other clip targets
// are deliberately not presented as animated actors by this camera path owner.
internal sealed class FalloutNifAnimatedNodePath
{
    private readonly List<(FalloutNifNode Node, FalloutNifAnimationSampler? Track)> _path = [];
    internal int AnimatedPathNodes => _path.Count(item => item.Track is not null);
    internal int UnboundOtherTargets { get; }
    internal FalloutNifControllerSequence Sequence { get; }

    internal FalloutNifAnimatedNodePath(FalloutNifFile skeleton, FalloutNifFile clip, string target)
    {
        if (clip.Roots.Count != 1) throw new InvalidDataException("An explicitly selected KF needs one sequence.");
        Sequence = clip.ReadControllerSequence(clip.Roots[0]);
        if (Sequence.Weight != 1 || Sequence.Frequency <= 0 || !float.IsFinite(Sequence.Frequency) ||
            Sequence.StopTime <= Sequence.StartTime || Sequence.CycleType is not (0 or 2))
            throw new NotSupportedException("KF camera timing/weight needs another animation owner.");
        var nodes = new Dictionary<int, FalloutNifNode>();
        var parents = new Dictionary<int, int>();
        void Visit(int index, int parent)
        {
            if (skeleton.Blocks[index].TypeName is not ("NiNode" or "NiBone" or "BSFadeNode")) return;
            if (!parents.TryAdd(index, parent)) throw new InvalidDataException("Source camera hierarchy is cyclic or multiply owned.");
            var node = skeleton.ReadNode(index);
            nodes.Add(index, node);
            foreach (var child in node.Children.Where(child => child >= 0)) Visit(child, index);
        }
        foreach (var root in skeleton.Roots) Visit(root, -1);
        var candidates = nodes.Values.Where(node => node.Name == target).ToArray();
        if (candidates.Length != 1) throw new InvalidDataException($"Source skeleton needs one camera node '{target}'.");
        var chain = new List<FalloutNifNode>();
        for (var index = candidates[0].Block.Index; index >= 0; index = parents[index]) chain.Add(nodes[index]);
        chain.Reverse();
        foreach (var node in chain)
        {
            var links = Sequence.ControlledBlocks.Where(link => link.NodeName == node.Name).ToArray();
            if (links.Length > 1 || links.Any(link => link.ControllerType != "NiTransformController" ||
                link.PropertyType.Length != 0 || link.Variable1.Length != 0 || link.Variable2.Length != 0))
                throw new NotSupportedException($"Camera path target {node.Name} needs channel blending or another controller owner.");
            _path.Add((node, links.Length == 0 ? null : new FalloutNifAnimationSampler(clip, links[0].Interpolator)));
        }
        UnboundOtherTargets = Sequence.ControlledBlocks.Length - AnimatedPathNodes;
        if (_path[^1].Track is null) throw new InvalidDataException("Selected KF has no authored camera channel.");
    }

    internal IReadOnlyList<(FalloutNifTransform Bind, FalloutNifAnimationSample? Sample)> Sample(float sourceTime)
    {
        if (!float.IsFinite(sourceTime) || sourceTime < Sequence.StartTime || sourceTime > Sequence.StopTime)
            throw new ArgumentOutOfRangeException(nameof(sourceTime));
        return _path.Select(item => (item.Node.Transform, item.Track?.Sample(sourceTime))).ToArray();
    }
}
