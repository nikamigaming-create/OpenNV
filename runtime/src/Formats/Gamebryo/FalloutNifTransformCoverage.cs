namespace OpenNV.Runtime.Formats.Gamebryo;

internal readonly record struct FalloutNifTransformCoverageChannel(int Bone, byte Priority, float Weight,
    FalloutNifTransformComponents Components);

internal static class FalloutNifTransformCoverage
{
    internal static FalloutNifTransformComponents[] Resolve(int boneCount,
        IReadOnlyList<FalloutNifTransformCoverageChannel> channels)
    {
        if (boneCount < 0) throw new ArgumentOutOfRangeException(nameof(boneCount));
        var coverage = new FalloutNifTransformComponents[boneCount];
        var priorities = new byte[boneCount]; var active = new bool[boneCount];
        foreach (var channel in channels)
        {
            if (channel.Bone < 0 || channel.Bone >= boneCount || !float.IsFinite(channel.Weight) || channel.Weight < 0 ||
                (channel.Components & ~FalloutNifTransformComponents.All) != 0)
                throw new InvalidDataException("Source transform coverage has an invalid bone, weight or component.");
            if (!active[channel.Bone] || channel.Priority > priorities[channel.Bone])
            { active[channel.Bone] = true; priorities[channel.Bone] = channel.Priority; }
        }
        // Priority selection precedes weight admission, exactly as ApplyLayers.
        // A winning zero-weight channel never revives a lower-priority pose.
        foreach (var channel in channels)
            if (channel.Priority == priorities[channel.Bone] && channel.Weight > 0)
                coverage[channel.Bone] |= channel.Components;
        return coverage;
    }
}
