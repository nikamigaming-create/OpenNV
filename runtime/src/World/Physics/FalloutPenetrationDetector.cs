namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutPenetrationDetectorSnapshot(IReadOnlyList<byte> States, bool Completed,
    IReadOnlyList<FalloutPenetrationContact> Contacts);

internal sealed class FalloutPenetrationDetector
{
    internal const byte Affected = 1, Descendant = 2;
    internal const uint RayFilterLayer = 29;
    private const uint LayerMask = 0x7f;
    private const uint ExcludedLayer = 8;
    private readonly FalloutPenetrationSkeleton _skeleton;
    private readonly byte[] _states;
    private readonly List<FalloutPenetrationContact> _contacts = [];

    internal FalloutPenetrationDetector(FalloutPenetrationSkeleton skeleton)
    {
        _skeleton = skeleton;
        _states = new byte[skeleton.Bones.Count];
        Reset();
    }

    internal IReadOnlyList<byte> States => _states;
    internal IReadOnlyList<FalloutPenetrationContact> Contacts => _contacts;
    internal bool Completed { get; private set; }

    internal void Reset()
    {
        Array.Fill(_states, Affected); Completed = false;
    }

    internal bool Detect(IReadOnlyList<FalloutPhysicalVector3> pose, IFalloutPenetrationQuery query, ushort group)
    {
        if (pose.Count != _skeleton.Bones.Count)
            throw new InvalidDataException("Current physics pose changed its derived skeleton extent.");
        _contacts.Clear();
        if (Completed) return false;
        var filter = ((uint)group << 16) | RayFilterLayer;
        for (var index = 0; index < _skeleton.Bones.Count; index++)
        {
            var bone = _skeleton.Bones[index];
            if (bone.Parent == -1 || _states[index] == 0) continue;
            var segment = new FalloutPenetrationSegment(pose[bone.Parent], pose[index]);
            query.PublishBounds(Bounds(segment));
            var hit = FirstAccepted(query.CastAll(segment, filter));
            if (hit is null)
            {
                query.SetReverseQueryFlag(!query.ReverseQueryFlag);
                segment = new(segment.End, segment.Start);
                hit = FirstAccepted(query.CastAll(segment, filter));
            }
            if (hit is { } accepted)
                _contacts.Add(new(index, bone.Parent, Interpolate(segment, accepted.FractionBits), accepted.Normal));
        }
        Array.Clear(_states);
        foreach (var contact in _contacts) _states[contact.Bone] = Affected;
        // Parent indices are topological. An ancestor contact always dominates
        // its descendants, independently of contact order. One pass propagates
        // that result after the ordered queries instead of rescanning per hit.
        for (var bone = 0; bone < _states.Length; bone++)
        {
            var parent = _skeleton.Bones[bone].Parent;
            if (parent >= 0 && _states[parent] != 0) _states[bone] = Descendant;
        }
        Completed = _contacts.Count == 0;
        return !Completed;
    }

    internal FalloutPenetrationDetectorSnapshot Capture() =>
        new(Array.AsReadOnly((byte[])_states.Clone()), Completed, _contacts.AsReadOnly().ToArray());

    internal void Restore(FalloutPenetrationDetectorSnapshot saved)
    {
        if (saved is null || saved.States.Count != _states.Length || saved.States.Any(state => state > Descendant) ||
            saved.Contacts is null || saved.Contacts.Any(contact => contact.Bone < 0 || contact.Bone >= _states.Length ||
                contact.Parent != _skeleton.Bones[contact.Bone].Parent || contact.Parent < 0) ||
            (saved.Completed && (saved.States.Any(state => state != 0) || saved.Contacts.Count != 0)))
            throw new InvalidDataException("Saved penetration detector changed its current derived bones or terminal state.");
        for (var index = 0; index < _states.Length; index++) _states[index] = saved.States[index];
        Completed = saved.Completed; _contacts.Clear(); _contacts.AddRange(saved.Contacts);
    }

    private static FalloutPenetrationRayHit? FirstAccepted(IReadOnlyList<FalloutPenetrationRayHit> hits)
    {
        foreach (var hit in hits)
        {
            var layer = hit.CollisionFilter & LayerMask;
            if (hit.RigidCollidable && layer is not ExcludedLayer and not RayFilterLayer) return hit;
        }
        return null;
    }

    private static FalloutPhysicalVector3 Interpolate(FalloutPenetrationSegment segment, uint fractionBits)
    {
        var fraction = BitConverter.UInt32BitsToSingle(fractionBits);
        var complement = FalloutPhysicalFloat32.Subtract(1f, fraction);
        return FalloutPhysicalVector3.FromValues(Component(segment.Start.XValue, segment.End.XValue),
            Component(segment.Start.YValue, segment.End.YValue), Component(segment.Start.ZValue, segment.End.ZValue));
        float Component(float start, float end) => FalloutPhysicalFloat32.Add(
            FalloutPhysicalFloat32.Multiply(complement, start), FalloutPhysicalFloat32.Multiply(fraction, end));
    }

    private static FalloutPenetrationBounds Bounds(FalloutPenetrationSegment segment) => new(
        FalloutPhysicalVector3.FromValues(MathF.Min(segment.Start.XValue, segment.End.XValue),
            MathF.Min(segment.Start.YValue, segment.End.YValue), MathF.Min(segment.Start.ZValue, segment.End.ZValue)),
        FalloutPhysicalVector3.FromValues(MathF.Max(segment.Start.XValue, segment.End.XValue),
            MathF.Max(segment.Start.YValue, segment.End.YValue), MathF.Max(segment.Start.ZValue, segment.End.ZValue)));
}
