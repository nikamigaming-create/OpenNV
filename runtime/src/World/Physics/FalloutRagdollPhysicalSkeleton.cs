namespace OpenNV.Runtime.World.Physics;

internal sealed record FalloutRagdollPhysicalLink(int Constraint, int Child, int Parent);
internal sealed record FalloutRagdollPhysicalOrder(IReadOnlyList<int> Bodies, IReadOnlyList<int> Parents, IReadOnlyList<int> Constraints)
{
    internal static FalloutRagdollPhysicalOrder Build(IReadOnlyList<int> bodies, IReadOnlyList<FalloutRagdollPhysicalLink> links)
    {
        if (bodies.Count == 0 || bodies.Distinct().Count() != bodies.Count || links.Count != bodies.Count - 1)
            throw new InvalidDataException("Ragdoll physical tree needs one current body root and one parent joint per remaining body.");
        var parents = Enumerable.Repeat(-1, bodies.Count).ToArray();
        var constraints = Enumerable.Repeat(-1, bodies.Count).ToArray();
        var indices = bodies.Select((body, index) => (body, index)).ToDictionary(row => row.body, row => row.index);
        foreach (var link in links)
        {
            if (!indices.TryGetValue(link.Child, out var child) || !indices.TryGetValue(link.Parent, out var parent) ||
                child == parent || parents[child] != -1 || constraints[child] != -1)
                throw new InvalidDataException("Ragdoll constraint leaves its current assembly or repeats a child's physical parent.");
            parents[child] = parent; constraints[child] = link.Constraint;
        }
        if (parents.Count(parent => parent == -1) != 1)
            throw new InvalidDataException("Ragdoll physical tree has no single actual root.");
        var depths = new int[bodies.Count];
        for (var index = 0; index < bodies.Count; ++index)
        {
            var seen = new HashSet<int>();
            for (var parent = parents[index]; parent >= 0; parent = parents[parent])
            {
                if (!seen.Add(parent)) throw new InvalidDataException("Ragdoll physical parent graph cycles.");
                ++depths[index];
            }
        }
        var order = Enumerable.Range(0, bodies.Count).ToArray();
        // The source ordering compares each earlier/later pair and swaps only
        // on greater parent distance. Equal-depth rows retain that algorithm's
        // resulting order; a stable general-purpose sort changes these joins.
        for (var earlier = 0; earlier < order.Length - 1; ++earlier)
            for (var later = earlier + 1; later < order.Length; ++later)
                if (depths[order[earlier]] > depths[order[later]])
                    (order[earlier], order[later]) = (order[later], order[earlier]);
        var sortedIndices = order.Select((index, sorted) => (index, sorted)).ToDictionary(row => row.index, row => row.sorted);
        return new(order.Select(index => bodies[index]).ToArray(),
            order.Select(index => parents[index] < 0 ? -1 : sortedIndices[parents[index]]).ToArray(),
            order.Skip(1).Select(index => constraints[index]).ToArray());
    }
}

internal sealed record FalloutRagdollPhysicalBone(int Body, string Name, int Parent,
    FalloutCharacterControllerProxyTransform ReferencePose, uint Scale);
internal sealed record FalloutRagdollSkeletonDescriptor(Guid Identity, string Key, string Source,
    IReadOnlyList<FalloutRagdollPhysicalBone> Bones, IReadOnlyList<int> Constraints)
{
    [System.Text.Json.Serialization.JsonInclude] public bool RootTranslationLocked { get; private set; } = true;
    [System.Text.Json.Serialization.JsonInclude] public long RootTranslationStores { get; private set; }
    internal void UnlockRootTranslation()
    {
        Validate();
        if (!RootTranslationLocked || RootTranslationStores != 0) throw new InvalidOperationException("Physical skeleton root translation store replayed.");
        RootTranslationLocked = false; RootTranslationStores = 1;
    }
    internal void Validate()
    {
        if (Identity == Guid.Empty || string.IsNullOrWhiteSpace(Key) || Source is not { Length: 64 } || !Source.All(Uri.IsHexDigit) ||
            Bones is not { Count: > 0 } || Constraints is null || Constraints.Count != Bones.Count - 1 ||
            Bones.Select(bone => bone.Body).Distinct().Count() != Bones.Count || Constraints.Distinct().Count() != Constraints.Count ||
            Constraints.Any(index => index < 0) || RootTranslationStores != (RootTranslationLocked ? 0 : 1))
            throw new InvalidDataException("Physical ragdoll skeleton lost its distinct source bodies, joints or descriptor identity.");
        foreach (var (bone, index) in Bones.Select((bone, index) => (bone, index)))
        {
            if (bone.Body < 0 || string.IsNullOrWhiteSpace(bone.Name) || bone.Parent < -1 || bone.Parent >= index ||
                (index == 0) != (bone.Parent == -1) || !float.IsFinite(BitConverter.UInt32BitsToSingle(bone.Scale)) ||
                BitConverter.UInt32BitsToSingle(bone.Scale) <= 0 || !Finite(bone.ReferencePose.Translation) ||
                !Finite(bone.ReferencePose.Rotation.Column0) || !Finite(bone.ReferencePose.Rotation.Column1) || !Finite(bone.ReferencePose.Rotation.Column2))
                throw new InvalidDataException("Physical ragdoll skeleton changed its ordered parent, current name or finite reference pose.");
        }
        static bool Finite(FalloutPhysicalVector3 value) => float.IsFinite(value.XValue) && float.IsFinite(value.YValue) && float.IsFinite(value.ZValue);
    }
}

internal sealed class FalloutRagdollDescriptorCache
{
    private int? _thread;
    private readonly Dictionary<string, FalloutRagdollSkeletonDescriptor> _descriptors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _entered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _failures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FalloutRagdollAnimationDescriptor> _animations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _animationEntered = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FalloutRagdollSkeletonMappings> _mappings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _mappingEntered = new(StringComparer.Ordinal);
    internal FalloutRagdollAnimationDescriptor? ReadExistingAnimation(FalloutRagdollSkeletonDescriptor physical)
    {
        if (_thread != Environment.CurrentManagedThreadId || !_descriptors.TryGetValue(physical.Key, out var current) || !ReferenceEquals(current, physical))
            throw new InvalidOperationException("Ragdoll prior-animation read lost its actual shared physical entry/current thread.");
        if (_animations.TryGetValue(physical.Key, out var value)) return value;
        if (_animationEntered.Contains(physical.Key)) throw new InvalidOperationException("Ragdoll prior-animation read crossed an entered or failed descriptor construction.");
        return null;
    }
    internal (FalloutRagdollSkeletonMappings Mappings, bool Hit) AcquireMappings(FalloutRagdollSkeletonDescriptor physical,
        FalloutRagdollAnimationDescriptor animation, Action requireActual)
    {
        requireActual();
        if (_thread != Environment.CurrentManagedThreadId || !_descriptors.TryGetValue(physical.Key, out var currentPhysical) ||
            !ReferenceEquals(currentPhysical, physical) || !_animations.TryGetValue(physical.Key, out var currentAnimation) || !ReferenceEquals(currentAnimation, animation))
            throw new InvalidOperationException("Ragdoll mapping lost its same actual cache entry and shared descriptors.");
        if (_mappings.TryGetValue(physical.Key, out var current)) { current.Validate(physical, animation); return (current, true); }
        if (!_mappingEntered.Add(physical.Key)) throw new InvalidOperationException("Ragdoll mapper cannot replay entered or failed source construction.");
        animation.UnlockRootTranslation(); requireActual(); physical.UnlockRootTranslation(); requireActual();
        var value = FalloutRagdollSkeletonMappingConstruction.Construct(physical, animation);
        value.Validate(physical, animation); requireActual();
        _mappings.Add(physical.Key, value); return (value, false);
    }
    internal (FalloutRagdollAnimationDescriptor Descriptor, bool Hit) AcquireAnimation(string key, string source,
        Func<FalloutRagdollAnimationDescriptor> construct)
    {
        if (_thread != Environment.CurrentManagedThreadId || !_descriptors.TryGetValue(key, out var physical) || physical.Source != source)
            throw new InvalidOperationException("Ragdoll animation descriptor lost its real physical cache entry/current thread.");
        if (_animations.TryGetValue(key, out var existing)) return (existing, true);
        if (!_animationEntered.Add(key)) throw new InvalidOperationException("Ragdoll animation descriptor cannot replay entered or failed construction.");
        var value = construct(); value.Validate();
        if (value.Key != key || value.Source != source) throw new InvalidDataException("Ragdoll animation factory changed its current cache source/key.");
        _animations.Add(key, value); return (value, false);
    }
    internal (FalloutRagdollSkeletonDescriptor Descriptor, bool Hit) Acquire(string key, string source,
        Func<FalloutRagdollSkeletonDescriptor> construct)
    {
        _thread ??= Environment.CurrentManagedThreadId;
        if (_thread != Environment.CurrentManagedThreadId || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Ragdoll descriptor cache lost its actual current thread/key.");
        if (_descriptors.TryGetValue(key, out var value))
        {
            if (value.Source != source) throw new InvalidDataException("Shared ragdoll descriptor changed its original model bytes.");
            return (value, true);
        }
        if (!_entered.Add(key)) throw new InvalidOperationException("Ragdoll descriptor cannot replay its entered or failed construction:" + _failures.GetValueOrDefault(key));
        try
        {
            value = construct();
            value.Validate();
            if (value.Key != key || value.Source != source || value.Identity == Guid.Empty || value.Bones.Count == 0)
                throw new InvalidDataException("Ragdoll descriptor factory changed its actual key/source/body denominator.");
            _descriptors.Add(key, value); return (value, false);
        }
        catch (Exception error) { _failures[key] = error.Message; throw; }
    }
}
