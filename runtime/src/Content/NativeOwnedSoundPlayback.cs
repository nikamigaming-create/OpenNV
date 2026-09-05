using Godot;

namespace OpenNV.Runtime.Content;

internal static class NativeOwnedSoundPlayback
{
    private const FalloutSoundFlags SupportedTwoDimensionalFlags =
        FalloutSoundFlags.Loop |
        FalloutSoundFlags.MenuSound |
        FalloutSoundFlags.TwoDimensional |
        FalloutSoundFlags.DialogueSound;

    internal static AudioStreamPlayer CreateMenu(FalloutSoundRecord descriptor,
        RuntimeLiveContentSource source, FalloutSoundRandomState random)
    {
        var before = random.State;
        var selected = descriptor.LogicalPath;
        if (!descriptor.HasExactFile)
        {
            var prefix = FalloutBsaArchive.CanonicalPath(descriptor.LogicalPath).TrimEnd('\\') + "\\";
            var variants = source.ResourcePathsUnder(descriptor.LogicalPath)
                .Where(path => !path[prefix.Length..].Contains('\\') &&
                    Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (variants.Length == 0) throw new InvalidDataException($"Menu SOUN {descriptor.FormKey} has no owned WAV variants in {descriptor.LogicalPath}.");
            selected = variants[random.NextBounded((uint)variants.Length)];
        }
        // The menu event is an explicit 2D playback request. Its SOUN still
        // supplies the winning asset, gain, pitch and loop declaration.
        // Environmental/submersion gates belong to positioned sounds. A menu
        // request has no world position or underwater listener relationship.
        var menuFlags = descriptor.Flags & ~(FalloutSoundFlags.EnvironmentIgnored | FalloutSoundFlags.MuteWhenSubmerged);
        var player = CreateTwoDimensional(descriptor with { LogicalPath = selected, Flags = menuFlags | FalloutSoundFlags.MenuSound });
        player.SetMeta("opennv_menu_sound_source", descriptor.FormKey.ToString());
        player.SetMeta("opennv_menu_sound_source_flags", (int)descriptor.Flags);
        player.SetMeta("opennv_menu_sound_variant", selected);
        player.SetMeta("opennv_menu_sound_random_before", before.ToString(System.Globalization.CultureInfo.InvariantCulture));
        player.SetMeta("opennv_menu_sound_random_after", random.State.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return player;
    }

    internal static AudioStreamPlayer CreateTwoDimensional(FalloutSoundRecord descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!descriptor.HasExactFile)
            throw Unsupported(descriptor, "folder-based random variant selection");
        if (!descriptor.IsTwoDimensional)
            throw Unsupported(descriptor, "3D attenuation requires the authored curve runtime");
        if ((descriptor.Flags & ~SupportedTwoDimensionalFlags) != 0 ||
            descriptor.RandomChancePercent != 0 || descriptor.FixedPitchScale <= 0.0f ||
            descriptor.StopTime != 0 || descriptor.StartTime != 0)
            throw Unsupported(descriptor,
                "random frequency, environmental, envelope, timed, or nonpositive pitch behavior");

        var stream = NativeOwnedMediaLoader.LoadAudio(descriptor.LogicalPath);
        ConfigureLoop(stream, descriptor);
        return new AudioStreamPlayer
        {
            Name = $"NativeSound_{descriptor.EditorId}",
            Stream = stream,
            VolumeDb = -descriptor.StaticAttenuationDb,
            PitchScale = descriptor.FixedPitchScale,
        };
    }

    internal static NativeOwnedSoundPlayer3D CreateThreeDimensional(
        FalloutSoundRecord descriptor,
        float gameUnitsToMetres,
        Node3D listener,
        uint environmentReverbAreaMask = 0U)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.RandomChancePercent != 0)
            throw FalloutSoundPlaybackContract.Unsupported(descriptor,
                "RNAM chance without the deterministic gameplay-state selection entry point");
        return CreateSelectedThreeDimensional(
            descriptor, gameUnitsToMetres, listener, environmentReverbAreaMask);
    }

    internal static bool TryCreateThreeDimensional(
        FalloutSoundRecord descriptor,
        FalloutSoundRandomState random,
        float gameUnitsToMetres,
        Node3D listener,
        out NativeOwnedSoundPlayer3D? player,
        uint environmentReverbAreaMask = 0U)
    {
        FalloutSoundPlaybackContract.ValidateThreeDimensional(descriptor);
        if (!FalloutSoundPlaybackContract.PassesRandomChance(descriptor, random))
        {
            player = null;
            return false;
        }
        player = CreateSelectedThreeDimensional(
            descriptor, gameUnitsToMetres, listener, environmentReverbAreaMask);
        return true;
    }

    private static NativeOwnedSoundPlayer3D CreateSelectedThreeDimensional(
        FalloutSoundRecord descriptor,
        float gameUnitsToMetres,
        Node3D listener,
        uint environmentReverbAreaMask)
    {
        FalloutSoundPlaybackContract.ValidateThreeDimensional(descriptor);
        if (!float.IsFinite(gameUnitsToMetres) || gameUnitsToMetres <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(gameUnitsToMetres),
                "SOUN game-unit scale must be finite and positive.");
        ArgumentNullException.ThrowIfNull(listener);
        FalloutSoundPlaybackContract.ValidateEnvironmentReverbAreaMask(
            descriptor, environmentReverbAreaMask);
        var stream = NativeOwnedMediaLoader.LoadAudio(descriptor.LogicalPath);
        ConfigureLoop(stream, descriptor);
        return new NativeOwnedSoundPlayer3D(
            descriptor, stream, gameUnitsToMetres, listener, environmentReverbAreaMask);
    }

    private static void ConfigureLoop(AudioStream stream, FalloutSoundRecord descriptor)
    {
        var loop = descriptor.IsLooping;
        if (!loop && (descriptor.LoopStartSample != 0 || descriptor.LoopEndSample != 0))
            throw Unsupported(descriptor, "loop points on a non-looping sound");
        switch (stream)
        {
            case AudioStreamWav wav:
                wav.LoopMode = loop
                    ? AudioStreamWav.LoopModeEnum.Forward
                    : AudioStreamWav.LoopModeEnum.Disabled;
                if (loop && descriptor.LoopEndSample != 0)
                {
                    if (descriptor.LoopEndSample <= descriptor.LoopStartSample ||
                        descriptor.LoopEndSample > int.MaxValue)
                        throw Unsupported(descriptor, "invalid WAV loop sample bounds");
                    wav.LoopBegin = checked((int)descriptor.LoopStartSample);
                    wav.LoopEnd = checked((int)descriptor.LoopEndSample);
                }
                break;
            case AudioStreamMP3 mp3:
                if (descriptor.LoopStartSample != 0 || descriptor.LoopEndSample != 0)
                    throw Unsupported(descriptor, "sample-indexed looping for MP3");
                mp3.Loop = loop;
                break;
            case AudioStreamOggVorbis ogg:
                if (descriptor.LoopStartSample != 0 || descriptor.LoopEndSample != 0)
                    throw Unsupported(descriptor, "sample-indexed looping for Ogg Vorbis");
                ogg.Loop = loop;
                break;
            default:
                throw new InvalidDataException(
                    $"Unsupported Godot audio stream type: {stream.GetType().Name}");
        }
    }

    private static NotSupportedException Unsupported(FalloutSoundRecord descriptor, string behavior) =>
        FalloutSoundPlaybackContract.Unsupported(descriptor, behavior);
}

internal sealed partial class NativeOwnedSoundPlayer3D : AudioStreamPlayer3D
{
    private readonly FalloutSoundRecord _descriptor;
    private readonly float _gameUnitsToMetres;
    private readonly Node3D _listener;
    private bool _submerged;

    internal NativeOwnedSoundPlayer3D(
        FalloutSoundRecord descriptor,
        AudioStream stream,
        float gameUnitsToMetres,
        Node3D listener,
        uint environmentReverbAreaMask)
    {
        _descriptor = descriptor;
        _gameUnitsToMetres = gameUnitsToMetres;
        _listener = listener;
        Name = $"NativeSound3D_{descriptor.EditorId}";
        Stream = stream;
        PitchScale = descriptor.FixedPitchScale;
        AttenuationModel = AttenuationModelEnum.Disabled;
        MaxDistance = descriptor.MaximumDistanceGameUnits * gameUnitsToMetres;
        AreaMask = environmentReverbAreaMask;
        VolumeDb = -descriptor.StaticAttenuationDb +
            descriptor.AttenuationDbAtDistanceGameUnits(descriptor.MinimumDistanceGameUnits);
    }

    internal bool Submerged
    {
        get => _submerged;
        set
        {
            _submerged = value;
            ApplyListenerPosition(_listener.GlobalPosition);
        }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        ApplyListenerPosition(_listener.GlobalPosition);
    }

    internal void ApplyListenerPosition(Vector3 listenerGlobalPosition)
    {
        if (!listenerGlobalPosition.IsFinite())
            throw new InvalidOperationException("SOUN listener position must be finite.");
        if (_submerged && (_descriptor.Flags & FalloutSoundFlags.MuteWhenSubmerged) != 0)
        {
            VolumeDb = float.NegativeInfinity;
            return;
        }
        var distanceGameUnits = GlobalPosition.DistanceTo(listenerGlobalPosition) / _gameUnitsToMetres;
        VolumeDb = -_descriptor.StaticAttenuationDb +
            _descriptor.AttenuationDbAtDistanceGameUnits(distanceGameUnits);
    }
}
