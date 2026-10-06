using System.Security.Cryptography;
using Godot;

namespace OpenNV.Runtime.Content;

internal sealed class NativeOwnedFiniteSoundSaveDrain : IFalloutFiniteSoundSaveDrainLease
{
    private readonly FalloutPluginStack _records;
    private readonly FalloutAnimationSoundEvents _events;
    private readonly FalloutAnimationSoundEvent _entry;
    private readonly int _sourceGenerations;
    private readonly Node _node;
    private readonly AudioStream _stream;
    private readonly FalloutFiniteSoundCompletionWait _completion;
    private readonly Func<bool> _bound;
    private readonly Action _release;
    private readonly ulong _nodeId, _playbackId, _streamId;
    private readonly Node.ProcessModeEnum _processMode;
    private readonly bool _streamPaused, _preparedPaused;
    private bool _active, _finished, _disposed;
    private Exception? _finishedError;
    internal FalloutFiniteSoundVoice? PendingVoice => ObserveFinished() ? null : Voice;
    public FalloutFiniteSoundVoice Voice { get; }

    internal NativeOwnedFiniteSoundSaveDrain(FalloutPluginStack records, FalloutAnimationSoundEvents events,
        FalloutFiniteSoundVoice voice, Node node, FalloutFiniteSoundCompletionWait completion,
        Func<bool> bound, Action release)
    {
        voice.Validate();
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() ||
            !bound() || events.Reference != voice.Reference || !events.CanAwaitNativeCompletion)
            throw new NotSupportedException("Save preparation lacks its already proven finite native owner.");
        _records = records; _events = events; Voice = voice; _node = node; _completion = completion;
        _bound = bound; _release = release;
        _entry = events.Events.Single(entry => entry.Generation == voice.Generation);
        if (_entry is not { End: FalloutAnimationSoundEnd.Active, Error: null } ||
            _entry.Sound != voice.Sound || _entry.SoundSha256 != voice.SoundSha256 ||
            _entry.Path != voice.Path || _entry.MediaSha256 != voice.MediaSha256)
            throw new InvalidDataException("Finite save preparation has no exact active source/media generation.");
        _sourceGenerations = events.Events.Count;
        _nodeId = node.GetInstanceId();
        _stream = Stream(node) ?? throw new NotSupportedException("Finite save preparation has no actual native stream.");
        _streamId = _stream.GetInstanceId();
        _playbackId = Playback(node)?.GetInstanceId() ??
            throw new NotSupportedException("Finite save preparation has no original native playback.");
        _processMode = node.ProcessMode; _streamPaused = Paused(node); _preparedPaused = node.GetTree().Paused;
        Node? owner = node;
        while (owner is not null && owner.ProcessMode == Node.ProcessModeEnum.Inherit) owner = owner.GetParent();
        if (owner?.ProcessMode == Node.ProcessModeEnum.Disabled)
            throw new NotSupportedException("A disabled finite audio owner cannot acquire a save-drain lease.");
        _ = ObserveFinished();
    }

    public bool ObserveFinished()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_finishedError is { } error)
            throw new FalloutFiniteSoundSaveDrainInvalidatedException(error.Message);
        ValidateSource(validateMedia: !_finished);
        if (_finished)
        {
            if (_events.Events.Single(entry => entry.Generation == Voice.Generation) !=
                (_entry with { End = FalloutAnimationSoundEnd.NativeFinished }))
                throw new FalloutFiniteSoundSaveDrainInvalidatedException("The genuine finite Finished receipt changed during save preparation.");
            return true;
        }
        var entry = _events.Events.Single(entry => entry.Generation == Voice.Generation);
        if (entry.End != FalloutAnimationSoundEnd.Active)
            throw new NotSupportedException($"Finite save preparation lost native Finished: end={entry.End} error={entry.Error}.");
        if (entry != _entry || !_bound() || !GodotObject.IsInstanceValid(_node) || !_node.IsInsideTree() ||
            _node.IsQueuedForDeletion() || _node.GetInstanceId() != _nodeId || Stream(_node) != _stream)
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("Finite save preparation changed its original source/native binding.");
        if (_node.ProcessMode != (_active ? Node.ProcessModeEnum.Always : _processMode) ||
            (_active ? !_node.GetTree().Paused || Paused(_node) || !_node.CanProcess() :
                _node.GetTree().Paused == _preparedPaused && Paused(_node) != _streamPaused))
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("Finite save preparation lost its exact pause/playback lease.");
        var playback = Playback(_node);
        if (playback is null || !GodotObject.IsInstanceValid(playback))
            throw new NotSupportedException("Finite native Finished was not delivered for the original playback.");
        if (playback.GetInstanceId() != _playbackId)
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("Finite save preparation replaced its original native playback.");
        if (_completion.ObserveSavePreparation(Voice, _nodeId, _playbackId, _streamId, Playing(_node), Paused(_node),
            _node is AudioStreamPlayer3D ? Engine.GetPhysicsFrames() : Engine.GetProcessFrames(), Time.GetTicksMsec()) != Voice)
            throw new NotSupportedException(_completion.Error ?? "Finite save preparation never observed its original playback playing.");
        return false;
    }

    private void ValidateSource(bool validateMedia = true)
    {
        if (_events.Events.Count != _sourceGenerations)
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("A source producer emitted another generation while saving.");
        var source = _records.GetEffective(Voice.Sound);
        if (source.Signature != "SOUN" ||
            !Convert.ToHexString(SHA256.HashData(source.ReadData())).Equals(Voice.SoundSha256, StringComparison.OrdinalIgnoreCase) ||
            FalloutSoundLoop.Read(FalloutSoundRecordReader.Read(source)).Mode != FalloutSoundLoopMode.None ||
            validateMedia && (!GodotObject.IsInstanceValid(_stream) || _stream.GetInstanceId() != _streamId ||
                !double.IsFinite(_stream.GetLength()) || _stream.GetLength() <= 0 ||
                _stream is AudioStreamWav { LoopMode: not AudioStreamWav.LoopModeEnum.Disabled } ||
                !_stream.GetMeta("opennv_owned_media_sha256", "").AsString().Equals(Voice.MediaSha256, StringComparison.OrdinalIgnoreCase)))
            throw new FalloutFiniteSoundSaveDrainInvalidatedException("Finite save preparation changed its winning source or actual owned finite media.");
    }

    // Called only by the original native Finished handler, before it releases
    // that node. Playing=false, retirement and authored Stop never call this.
    internal void NativeFinished()
    {
        if (_disposed) return;
        try
        {
            ValidateSource();
            if (_finished || !_bound() || !GodotObject.IsInstanceValid(_node) || _node.GetInstanceId() != _nodeId ||
                Stream(_node) != _stream || _events.Events.Single(entry => entry.Generation == Voice.Generation) != _entry)
                throw new FalloutFiniteSoundSaveDrainInvalidatedException("Native Finished no longer belongs to the prepared source/native generation.");
            _finished = true;
        }
        catch (Exception error) { _finishedError = error; }
    }

    public void Activate()
    {
        if (_active) throw new InvalidOperationException("Finite native drain lease was activated twice.");
        if (!_node.GetTree().Paused) throw new InvalidOperationException("Finite audio drain requires the authoritative world to stay paused.");
        if (ObserveFinished()) throw new InvalidOperationException("Finite native playback finished before lease activation.");
        _node.ProcessMode = Node.ProcessModeEnum.Always;
        SetPaused(_node, false);
        _active = true;
        _ = ObserveFinished();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (GodotObject.IsInstanceValid(_node) && _node.GetInstanceId() == _nodeId)
            {
                try { _node.ProcessMode = _processMode; }
                finally { SetPaused(_node, _streamPaused); }
            }
        }
        finally { _release(); }
    }

    private static AudioStream? Stream(Node node) => node is AudioStreamPlayer3D spatial
        ? spatial.Stream : (node as AudioStreamPlayer)?.Stream;
    private static AudioStreamPlayback? Playback(Node node) => node is AudioStreamPlayer3D spatial
        ? spatial.HasStreamPlayback() ? spatial.GetStreamPlayback() : null
        : node is AudioStreamPlayer flat && flat.HasStreamPlayback() ? flat.GetStreamPlayback() : null;
    private static bool Playing(Node node) => node is AudioStreamPlayer3D spatial ? spatial.Playing : ((AudioStreamPlayer)node).Playing;
    private static bool Paused(Node node) => node is AudioStreamPlayer3D spatial ? spatial.StreamPaused : ((AudioStreamPlayer)node).StreamPaused;
    private static void SetPaused(Node node, bool paused)
    {
        if (node is AudioStreamPlayer3D spatial) spatial.StreamPaused = paused;
        else ((AudioStreamPlayer)node).StreamPaused = paused;
    }
}
