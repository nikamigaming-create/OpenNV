using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.Presentation.Ui;

// One resident campaign host for the original indexed producer. Pause menus
// may disappear while a finite voice continues. Completion is the player's
// real Finished signal; deletion is separately observed through a boolean
// native query, which cannot add a C# RefCounted binding to the observed ID.
internal sealed partial class NativeOwnedIndexedInterfaceSounds : Node
{
    private sealed class Voice(NativeOwnedTwoDimensionalSoundPlayer player, long ordinal)
    {
        internal NativeOwnedTwoDimensionalSoundPlayer Player { get; } = player;
        internal long Ordinal { get; } = ordinal;
        internal ulong PlayerId { get; } = player.GetInstanceId();
        internal ulong StreamId { get; } = player.OwnedStreamId;
        internal bool Closing, Queued;
        internal Action? FinishedHandler, ExitHandler, SourceStopHandler;
    }
    private readonly Dictionary<long, Voice> _voices = [];
    private FalloutIndexedInterfaceSounds? _owner;
    private Action<Exception>? _failed;
    private Expression? _validity;
    private Guid _lease;
    private bool _retiring;
    internal FalloutIndexedInterfaceSounds SoundState => _owner ?? throw new InvalidOperationException("Indexed audio host has no actual state owner.");
    internal string? SaveBlocker => SoundState.SaveBlocker ?? (_voices.Count != 0 ? "indexed-interface-native-retirement" : null);

    internal static NativeOwnedIndexedInterfaceSounds Attach(Node actualCampaignHost,
        FalloutIndexedInterfaceSounds owner, Action<Exception> failed)
    {
        ArgumentNullException.ThrowIfNull(actualCampaignHost); ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(failed);
        var records = owner.Source.Records;
        if (!GodotObject.IsInstanceValid(actualCampaignHost) || !actualCampaignHost.IsInsideTree() || actualCampaignHost.IsQueuedForDeletion() ||
            records.OwnedSource is null || !ReferenceEquals(RuntimeLiveContentSource.Current, records.OwnedSource))
            throw new NotSupportedException("Indexed interface sounds require the actual campaign/tree/selected media owner.");
        var host = new NativeOwnedIndexedInterfaceSounds();
        try
        {
            host._owner = owner; host._failed = failed;
            host.Name = "NativeOriginalIndexedInterfaceSounds"; host.ProcessMode = ProcessModeEnum.Always;
            host._validity = new Expression();
            if (host._validity.Parse("is_instance_id_valid(id)", ["id"]) != Error.Ok || host.Alive(0))
                throw new InvalidOperationException("Native indexed audio cannot observe actual ObjectDB closure.");
            actualCampaignHost.AddChild(host);
            if (host.GetParent() != actualCampaignHost || !host.IsInsideTree())
                throw new InvalidOperationException("Indexed sound host did not attach to its actual campaign.");
            host._lease = owner.BindNative(host.PlayActual);
            return host;
        }
        catch (Exception original)
        {
            List<Exception> errors = [original];
            try { host.RetireForSession(); } catch (Exception cleanup) when (Ordinary(cleanup)) { errors.Add(cleanup); }
            try { host.Free(); } catch (Exception cleanup) when (Ordinary(cleanup)) { errors.Add(cleanup); }
            if (errors.Count == 1) throw;
            throw new AggregateException("Indexed audio retained original attachment and cleanup failures.", errors);
        }
    }

    internal FalloutInterfaceSoundVoice Play(FalloutInterfaceSoundCall call) => SoundState.Play(call);

    private long PlayActual(FalloutInterfaceSoundCall call)
    {
        if (_retiring || _lease == Guid.Empty || !IsInsideTree())
            throw new NotSupportedException("Indexed cue has no current actual resident native host.");
        var entered = SoundState.Enter(_lease, call);
        Voice? voice = null;
        NativeOwnedTwoDimensionalSoundPlayer? allocated = null;
        try
        {
            var source = SoundState.Resolve(_lease, entered.Ordinal);
            if (source.Descriptor is null) return entered.Ordinal;
            var prepared = NativeOwnedSoundPlayback.CreateTwoDimensional(source.Descriptor, SoundState.Source.Records);
            if (prepared is not NativeOwnedTwoDimensionalSoundPlayer player)
            {
                prepared.Free();
                throw new InvalidOperationException("Original indexed playback lacks its decoded-resource lifetime owner.");
            }
            allocated = player;
            var actualVoice = new Voice(player, entered.Ordinal);
            voice = actualVoice; _voices.Add(voice.Ordinal, voice);
            SoundState.Allocated(_lease, voice.Ordinal, voice.PlayerId, voice.StreamId);
            var media = player.Stream ?? throw new InvalidDataException("Original indexed player has no decoded stream.");
            var sha = media.GetMeta("opennv_owned_media_sha256").AsString();
            SoundState.BoundMedia(_lease, voice.Ordinal, voice.PlayerId, sha);
            player.ProcessMode = ProcessModeEnum.Always;
            player.SetMeta("opennv_indexed_sound_call", call.Owner);
            player.SetMeta("opennv_indexed_sound_index", call.Index);
            player.SetMeta("opennv_indexed_sound_ordinal", voice.Ordinal);
            player.SetMeta("opennv_indexed_sound_source", SoundState.Source.Identity);
            player.SetMeta("opennv_menu_sound_source_flags", (int)source.OriginalFlags!.Value);
            voice.FinishedHandler = () => Finished(actualVoice);
            voice.ExitHandler = () => Exited(actualVoice);
            voice.SourceStopHandler = () => SourceStopped(actualVoice);
            player.Finished += voice.FinishedHandler; player.TreeExiting += voice.ExitHandler;
            player.SourceRegistryStopped += voice.SourceStopHandler;
            AddChild(player);
            if (player.GetParent() != this || !player.IsInsideTree())
                throw new InvalidOperationException("Indexed cue did not attach its original native player.");
            player.Play();
            if (!player.Playing) throw new InvalidOperationException("Original indexed cue did not enter actual playback.");
            SoundState.Started(_lease, voice.Ordinal, voice.PlayerId);
            GD.Print($"OPENNV_INDEXED_INTERFACE_PLAY ordinal={voice.Ordinal} index={call.Index} sound={source.Sound} player={voice.PlayerId} stream={voice.StreamId}");
            return voice.Ordinal;
        }
        catch (Exception original) when (Ordinary(original))
        {
            List<Exception> errors = [original];
            try { Fail(entered.Ordinal, original); } catch (Exception retained) when (Ordinary(retained)) { errors.Add(retained); }
            if (voice is not null)
                try { Close(voice, immediate: true); } catch (Exception cleanup) when (Ordinary(cleanup)) { errors.Add(cleanup); }
            else if (allocated is not null)
                try { allocated.ReleaseDecodedStream(); allocated.Free(); }
                catch (Exception cleanup) when (Ordinary(cleanup)) { errors.Add(cleanup); }
            if (errors.Count == 1) throw;
            throw new AggregateException("Original indexed cue retained its attempted prefix and native cleanup failures.", errors);
        }
    }

    private void Finished(Voice voice)
    {
        try
        {
            RequireVoice(voice); SoundState.Finished(_lease, voice.Ordinal, voice.PlayerId);
            Close(voice, immediate: false);
        }
        catch (Exception error) when (Ordinary(error)) { Fail(voice.Ordinal, error); }
    }

    private void Exited(Voice voice)
    {
        if (voice.Closing) return;
        try
        {
            RequireVoice(voice);
            if (!_retiring && SoundState.Voice(voice.Ordinal).Phase is not (FalloutInterfaceVoicePhase.Finished or FalloutInterfaceVoicePhase.Failed))
                Fail(voice.Ordinal, new InvalidOperationException("Indexed player exited without original Finished or explicit source/session stop."));
            Close(voice, immediate: false);
        }
        catch (Exception error) when (Ordinary(error)) { Fail(voice.Ordinal, error); }
    }

    private void SourceStopped(Voice voice)
    {
        try { RequireVoice(voice); SoundState.Stopped(_lease, voice.Ordinal, voice.PlayerId); Close(voice, immediate: false); }
        catch (Exception error) when (Ordinary(error)) { Fail(voice.Ordinal, error); throw; }
    }

    private void Close(Voice voice, bool immediate)
    {
        RequireVoice(voice); voice.Closing = true;
        var player = voice.Player;
        if (Alive(voice.PlayerId))
        {
            player.Stop();
            if (player.Playing) throw new InvalidOperationException("Original indexed native voice did not stop.");
            SoundState.Stopped(_lease, voice.Ordinal, voice.PlayerId);
            player.ReleaseDecodedStream();
            if (!player.OwnedStreamReleased) throw new InvalidOperationException("Indexed voice retained its original decoded resource.");
            SoundState.ReleasedStream(_lease, voice.Ordinal, voice.PlayerId, voice.StreamId);
            if (voice.FinishedHandler is { } finished) { player.Finished -= finished; voice.FinishedHandler = null; }
            if (voice.ExitHandler is { } exited) { player.TreeExiting -= exited; voice.ExitHandler = null; }
            if (voice.SourceStopHandler is { } sourceStop) { player.SourceRegistryStopped -= sourceStop; voice.SourceStopHandler = null; }
            if (immediate) player.Free();
            else if (!voice.Queued) { player.QueueFree(); voice.Queued = true; }
        }
        ObserveClosure(voice, required: immediate);
    }

    private void ObserveClosure(Voice voice, bool required)
    {
        var playerGone = !Alive(voice.PlayerId); var streamGone = !Alive(voice.StreamId);
        if (!playerGone || !streamGone)
        {
            if (required) throw new InvalidOperationException($"Indexed voice retains native objects: playerLive={!playerGone} streamLive={!streamGone}.");
            return;
        }
        SoundState.Destroyed(_lease, voice.Ordinal, voice.PlayerId, voice.StreamId, playerGone, streamGone);
        _voices.Remove(voice.Ordinal);
        GD.Print($"OPENNV_INDEXED_INTERFACE_CLOSED ordinal={voice.Ordinal} player={voice.PlayerId} stream={voice.StreamId} finished={SoundState.Voice(voice.Ordinal).Phase == FalloutInterfaceVoicePhase.Finished}");
    }

    public override void _Process(double delta)
    {
        _ = delta;
        foreach (var voice in _voices.Values.Where(voice => voice.Closing).ToArray())
            try { ObserveClosure(voice, required: false); }
            catch (Exception error) when (Ordinary(error)) { Fail(voice.Ordinal, error); }
    }

    private bool Alive(ulong id)
    {
        var query = _validity ?? throw new InvalidOperationException("Indexed native closure query retired too early.");
        using var args = new Godot.Collections.Array { unchecked((long)id) };
        using var result = query.Execute(args, showError: false);
        if (query.HasExecuteFailed() || result.VariantType != Variant.Type.Bool)
            throw new InvalidOperationException("Indexed native closure observation failed; no deleted-object receipt is emitted.");
        return result.AsBool();
    }
    private void RequireVoice(Voice voice)
    {
        if (!_voices.TryGetValue(voice.Ordinal, out var actual) || !ReferenceEquals(actual, voice))
            throw new InvalidOperationException("Indexed native callback has another original voice generation.");
    }
    private void Fail(long ordinal, Exception error)
    {
        SoundState.Failed(_lease, ordinal, error);
        GD.PushError("OPENNV_INDEXED_INTERFACE_SOUND_FAIL " + error.GetType().Name + ": " + error.Message);
        _failed?.Invoke(error);
    }

    internal void RetireForSession()
    {
        if (_retiring && _lease == Guid.Empty) return;
        _retiring = true; List<Exception> errors = [];
        foreach (var voice in _voices.Values.ToArray())
            try { Close(voice, immediate: true); }
            catch (Exception error) when (Ordinary(error))
            {
                errors.Add(error);
                try { Fail(voice.Ordinal, error); } catch (Exception retained) when (Ordinary(retained)) { errors.Add(retained); }
            }
        if (_lease != Guid.Empty && _voices.Count == 0)
            try { SoundState.RetireNative(_lease); _lease = Guid.Empty; }
            catch (Exception error) when (Ordinary(error)) { errors.Add(error); }
        if (_lease == Guid.Empty) { _validity?.Dispose(); _validity = null; }
        if (errors.Count != 0) throw new AggregateException("Indexed native retirement retained original voice/resource failures.", errors);
    }
    public override void _ExitTree()
    {
        try { RetireForSession(); }
        catch (Exception error) when (Ordinary(error))
        { GD.PushError("OPENNV_INDEXED_INTERFACE_RETIRE_FAIL " + error.GetType().Name + ": " + error.Message); _failed?.Invoke(error); }
    }
    private static bool Ordinary(Exception error) => FalloutPlayerPhysicalActivity.Ordinary(error);
}
