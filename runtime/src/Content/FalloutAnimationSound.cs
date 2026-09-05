namespace OpenNV.Runtime.Content;

internal sealed record FalloutAnimationSoundSelection(
    FalloutSoundRecord Source, bool Play, string? Path, float PitchScale,
    ulong RandomBefore, ulong RandomAfter, IReadOnlyList<string> Unbound)
{
    internal float GainDb => -Source.StaticAttenuationDb;
}

/// <summary>One explicit KF sound request, resolved against winning SOUN data.</summary>
internal static class FalloutAnimationSound
{
    private const FalloutSoundFlags SupportedFlags = FalloutSoundFlags.RandomFrequencyShift |
        FalloutSoundFlags.EnvironmentIgnored | FalloutSoundFlags.MenuSound | FalloutSoundFlags.TwoDimensional |
        FalloutSoundFlags.DialogueSound | FalloutSoundFlags.MuteWhenSubmerged;

    internal static string? EditorId(string textKey)
    {
        var key = textKey.Trim();
        if (!key.StartsWith("Sound:", StringComparison.OrdinalIgnoreCase)) return null;
        var name = key["Sound:".Length..].Trim();
        if (name.Length == 0 || name.Any(char.IsWhiteSpace))
            throw new NotSupportedException("KF sound event has an empty or compound sound identifier.");
        return name;
    }

    internal static IReadOnlyList<string> Variants(FalloutSoundRecord source, IEnumerable<string> resources)
    {
        if (source.HasExactFile) return [source.LogicalPath];
        var prefix = FalloutBsaArchive.CanonicalPath(source.LogicalPath).TrimEnd('\\') + "\\";
        var paths = resources.Select(FalloutBsaArchive.CanonicalPath)
            .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                !path[prefix.Length..].Contains('\\') &&
                Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) throw new FileNotFoundException($"SOUN {source.FormKey} has no owned WAV variants in {source.LogicalPath}.");
        return paths;
    }

    internal static FalloutAnimationSoundSelection Select(FalloutSoundRecord source,
        IReadOnlyList<string> variants, FalloutSoundRandomState random)
    {
        var unsupported = source.Flags & ~SupportedFlags;
        if (unsupported != 0)
            throw FalloutSoundPlaybackContract.Unsupported(source, $"KF sound flags 0x{(ushort)unsupported:x4}, including any loop stop/envelope owner");
        if (source.StopTime != source.StartTime)
            throw FalloutSoundPlaybackContract.Unsupported(source, "sound scheduling against the authoritative world clock");
        if (source.LoopStartSample != 0 || source.LoopEndSample != 0)
            throw FalloutSoundPlaybackContract.Unsupported(source, "KF loop points without a loop stop owner");
        if (variants.Count == 0) throw new InvalidDataException("A KF sound selection has no source variants.");
        if (!source.IsTwoDimensional)
        {
            source.ValidateAttenuationCurve();
            _ = source.AttenuationDbAtDistanceGameUnits(source.MinimumDistanceGameUnits);
        }
        var unbound = new List<string>();
        if ((source.Flags & (FalloutSoundFlags.MenuSound | FalloutSoundFlags.EnvironmentIgnored)) == 0 && source.ReverbAttenuation != 100)
            unbound.Add("source-environment-reverb-send");
        if ((source.Flags & FalloutSoundFlags.MuteWhenSubmerged) != 0)
            unbound.Add("authoritative-listener-submersion");
        var before = random.State;
        if (!FalloutSoundPlaybackContract.PassesRandomChance(source, random))
            return new(source, false, null, 1, before, random.State, unbound);
        var path = variants.Count == 1 ? variants[0] : variants[(int)random.NextBounded((uint)variants.Count)];
        var pitch = (source.Flags & FalloutSoundFlags.RandomFrequencyShift) != 0
            ? 1 + (random.NextUnitFloat() * 2 - 1) * Math.Abs((int)source.FrequencyAdjustment) / 100f
            : source.FixedPitchScale;
        if (pitch <= 0 || !float.IsFinite(pitch))
            throw FalloutSoundPlaybackContract.Unsupported(source, "a nonpositive selected pitch");
        return new(source, true, path, pitch, before, random.State, unbound);
    }
}
