using Godot;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Content;

internal static partial class NativeOwnedSoundPlayback
{
    private const FalloutSoundFlags SupportedTwoDimensionalFlags =
        FalloutSoundFlags.Loop |
        FalloutSoundFlags.MenuSound |
        FalloutSoundFlags.TwoDimensional |
        FalloutSoundFlags.DialogueSound;

    internal static FalloutMenuSoundSelectionCall MenuCall(Node owner, long occurrence, FalloutFormKey sound)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!owner.IsInsideTree()) throw new InvalidOperationException("Menu cue has no actual attached presentation caller.");
        var call = new FalloutMenuSoundSelectionCall("native-menu-caller:" + RuntimeSaveProcessIdentity.Current.Boot + ":" +
            owner.GetInstanceId().ToString(System.Globalization.CultureInfo.InvariantCulture), occurrence, 0, sound);
        call.Validate(); return call;
    }

    internal static AudioStreamPlayer CreateMenu(FalloutSoundRecord descriptor, FalloutPluginStack records,
        FalloutMenuSoundSelectionCall call)
    {
        var source = records.OwnedSource ?? throw new NotSupportedException("Menu cue has no actual selected source.");
        if (!ReferenceEquals(source, RuntimeLiveContentSource.Current))
            throw new InvalidDataException("Menu decode differs from its actual currently selected source graph.");
        var selection = source.RequireMenuSoundSelection(records);
        var prepared = selection.Prepare(descriptor, call);
        var policy = new FalloutMenuCuePlaybackSource(selection.Source.EngineSha256, selection.Source.RuntimeSha256,
            FalloutMenuCuePlaybackSource.CurrentContractSha256);
        var selected = policy.PreparedFile(records, prepared, selection, sourceLoop: true);
        var player = CreateTwoDimensional(selected, records);
        try
        {
            player.SetMeta("opennv_menu_sound_source", descriptor.FormKey.ToString());
            player.SetMeta("opennv_menu_sound_source_flags", (int)descriptor.Flags);
            player.SetMeta("opennv_menu_sound_variant", selected.LogicalPath);
            player.SetMeta("opennv_menu_sound_selection_ordinal", prepared.SelectionOrdinal);
            player.SetMeta("opennv_menu_sound_selection_source", selection.Source.Identity);
            return player;
        }
        catch (Exception original)
        {
            List<Exception> failures = [original];
            try { ((NativeOwnedTwoDimensionalSoundPlayer)player).ReleaseDecodedStream(); } catch (Exception cleanup) { failures.Add(cleanup); }
            try { player.Free(); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count == 1) throw;
            throw new AggregateException("Menu voice retained original metadata and native/resource retirement failures.", failures);
        }
    }

    internal static AudioStreamPlayer CreateTwoDimensional(FalloutSoundRecord descriptor, FalloutPluginStack records)
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
        NativeOwnedTwoDimensionalSoundPlayer? player = null;
        try
        {
            ConfigureLoop(stream, descriptor);
            var menuFeedback = descriptor.Flags.HasFlag(FalloutSoundFlags.MenuSound) && !descriptor.IsLooping;
            // Bare node first: each subsequent setter/registration is covered
            // by this exact created-node and fresh-resource exception owner.
            player = new NativeOwnedTwoDimensionalSoundPlayer();
            player.AdoptDecodedStream(stream);
            player.Name = $"NativeSound_{descriptor.EditorId}";
            player.VolumeDb = -descriptor.StaticAttenuationDb;
            player.PitchScale = descriptor.FixedPitchScale;
            player.ProcessMode = menuFeedback ? Node.ProcessModeEnum.Always : Node.ProcessModeEnum.Inherit;
            var actual = player;
            NativeOwnedSoundVoice.Bind(records, actual, descriptor.FormKey, () => null, "source-2D-or-menu",
                () => actual.Playing, actual.StopFromSourceRegistry, menuFeedback ? () => true : null);
            return actual;
        }
        catch (Exception original)
        {
            List<Exception> failures = [original];
            var adopted = player?.OwnsDecodedStream == true;
            try
            {
                if (adopted) player!.ReleaseDecodedStream();
                else stream.Dispose();
            }
            catch (Exception cleanup) { failures.Add(cleanup); }
            if (player is not null)
                try { player.Free(); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count == 1) throw;
            throw new AggregateException("2D sound construction retained original and created-resource cleanup failures.", failures);
        }
    }

    internal static NativeOwnedSoundPlayer3D CreateThreeDimensional(
        FalloutSoundRecord descriptor,
        FalloutPluginStack records,
        float gameUnitsToMetres,
        Node3D listener,
        uint environmentReverbAreaMask = 0U)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.RandomChancePercent != 0)
            throw FalloutSoundPlaybackContract.Unsupported(descriptor,
                "RNAM chance without the deterministic gameplay-state selection entry point");
        return CreateSelectedThreeDimensional(
            descriptor, records, gameUnitsToMetres, listener, environmentReverbAreaMask);
    }

    internal static bool TryCreateThreeDimensional(
        FalloutSoundRecord descriptor,
        FalloutPluginStack records,
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
            descriptor, records, gameUnitsToMetres, listener, environmentReverbAreaMask);
        return true;
    }

    private static NativeOwnedSoundPlayer3D CreateSelectedThreeDimensional(
        FalloutSoundRecord descriptor,
        FalloutPluginStack records,
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
        var player = new NativeOwnedSoundPlayer3D(
            descriptor, stream, gameUnitsToMetres, listener, environmentReverbAreaMask);
        NativeOwnedSoundVoice.Bind(records, player, descriptor.FormKey, () => NativeOwnedSoundVoice.Reference(records, player),
            "source-3D", () => player.Playing, player.Stop);
        return player;
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
                if (loop)
                {
                    var region = FalloutSoundLoop.Read(descriptor);
                    var frames = Math.Round(wav.GetLength() * wav.MixRate);
                    if (region.Start >= frames || region.End > frames)
                        throw new InvalidDataException("SOUN loop region exceeds the decoded owned WAV.");
                    wav.LoopBegin = checked((int)region.Start);
                    wav.LoopEnd = region.End == 0 ? checked((int)frames) : checked((int)region.End);
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

    internal static AudioStream CreateLoopStream(AudioStream source, FalloutSoundLoop loop)
    {
        if (source is not AudioStreamWav wav)
            throw new NotSupportedException("Source loop/envelope playback requires a decoded WAV sample clock.");
        var frames = Math.Round(wav.GetLength() * wav.MixRate);
        if (loop.Start >= frames || loop.End > frames)
            throw new InvalidDataException("SOUN loop region exceeds the decoded owned WAV.");
        // Each voice owns its loop switch; decoded resource reuse is immutable.
        var result = (AudioStreamWav)wav.Duplicate();
        result.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        result.LoopBegin = checked((int)loop.Start);
        result.LoopEnd = loop.End == 0 ? checked((int)frames) : checked((int)loop.End);
        return result;
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
