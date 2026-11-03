using Godot;

namespace OpenNV.Runtime.Content;

internal sealed partial class NativeOwnedAnimationSoundPlayer
{
    private readonly Func<string?, NativeOwnedSoundSourceScene>? _sourceScenes;
    private readonly bool _sourcePoseBoundary;
    private bool _sourcePcmPublished;
    private NativeOwnedSoundSourceScene? _dispatchScene;
    private sealed record PendingSourceSound(string? Text, FalloutFormKey? Sound, Node3D? Emitter,
        Action? Completed, NativeOwnedSoundSourceScene? Scene, bool SourceEmitter, Action<string>? Admitted = null);
    private readonly Queue<PendingSourceSound> _pendingSourceSounds = [];
    private readonly Dictionary<Node3D, NativeOwnedSoundSourceScene> _emitterScenes = [];
    private readonly Dictionary<ulong, Dictionary<string, Node3D>> _sceneEmitters = [];
    private bool SourcePosePending => _sourcePoseBoundary && (!_sourcePcmPublished || _pendingSourceSounds.Count != 0);
    internal bool HasPendingEmitterSounds(Node3D emitter) => _pendingSourceSounds.Any(pending =>
        pending.Emitter is { } queued && (queued == emitter ||
            GodotObject.IsInstanceValid(queued) && emitter.IsAncestorOf(queued)));

    private NativeOwnedSoundSourceScene CurrentSourceScene()
    {
        var scene = _dispatchScene ?? (_sourceScenes?.Invoke(null) ?? new(null, _actor, _actor));
        if (_sourceScenes is not null && (string.IsNullOrWhiteSpace(scene.Identity) || scene.Identity.Length > 128 || scene.Identity.Any(char.IsControl)))
            throw new InvalidDataException("Sound source-scene owner has no valid persistent identity.");
        if (!GodotObject.IsInstanceValid(scene.Root) || !scene.Root.IsInsideTree())
            throw new NotSupportedException("Sound source-scene owner is not resident at key admission.");
        return scene;
    }

    private Dictionary<string, Node3D> SceneEmitterCache(NativeOwnedSoundSourceScene scene)
    {
        if (_sourceScenes is null) return _emitters;
        var key = scene.Root.GetInstanceId();
        if (!_sceneEmitters.TryGetValue(key, out var cache))
        {
            _sceneEmitters.Add(key, cache = new(StringComparer.Ordinal));
            scene.Root.TreeExiting += () =>
            {
                _sceneEmitters.Remove(key);
                foreach (var emitter in _emitterScenes.Where(pair => pair.Value.Root == scene.Root).Select(pair => pair.Key).ToArray())
                    _emitterScenes.Remove(emitter);
            };
        }
        return cache;
    }

    private string DispatchSourceText(string text)
    {
        if (!_sourcePoseBoundary) return DispatchSegments(text);
        if (_retired) throw new InvalidOperationException("Retired player sound owner cannot admit a source key.");
        _pendingSourceSounds.Enqueue(new(text, null, null, null, CurrentSourceScene(), true));
        return "source-sound-awaiting-pose-publication";
    }

    private string DispatchSourceSound(FalloutFormKey sound, Node3D? emitter, Action? completed)
    {
        if (!_sourcePoseBoundary) return Dispatch("SOUN:" + sound, emitter ?? this, ownsLoopStop: true, sound, completed);
        if (_retired) throw new InvalidOperationException("Retired player sound owner cannot admit a source sound.");
        if (emitter is not null && (!GodotObject.IsInstanceValid(emitter) || !emitter.IsInsideTree()))
            throw new NotSupportedException("Queued sound lost its actual source emitter before admission.");
        // An explicit real emitter already resolved from a source scene retains
        // that owner even when current POV changed before this request.
        var scene = emitter is not null && _emitterScenes.TryGetValue(emitter, out var boundScene)
            ? boundScene : CurrentSourceScene();
        var sourceEmitter = emitter is null || emitter == scene.Root || scene.Root.IsAncestorOf(emitter);
        if (!sourceEmitter) return DispatchWorldSound(sound, emitter!, completed);
        _pendingSourceSounds.Enqueue(new(null, sound, emitter, completed, scene, sourceEmitter));
        return "source-sound-awaiting-pose-publication";
    }

    // World effects already own their real published transform. They share the
    // sound history and FIFO with actor keys, but not the actor-palette pose.
    // The callback reports actual dispatch, never queue admission as playback.
    internal string DispatchWorldSound(FalloutFormKey sound, Node3D emitter, Action? completed = null,
        Action<string>? admitted = null)
    {
        if (_retired) throw new InvalidOperationException("Retired sound owner cannot admit a world sound.");
        if (!GodotObject.IsInstanceValid(emitter) || !emitter.IsInsideTree() ||
            emitter.GetViewport() != GetViewport() || !emitter.GlobalTransform.IsFinite() || _emitterScenes.ContainsKey(emitter))
            throw new NotSupportedException("World sound requires its actual resident world-space emitter.");
        if (!_sourcePoseBoundary || _sourcePcmPublished && _pendingSourceSounds.Count == 0)
        {
            RequirePcmRestored();
            var disposition = Dispatch("SOUN:" + sound, emitter, ownsLoopStop: true, sound, completed);
            admitted?.Invoke(disposition);
            return disposition;
        }
        _pendingSourceSounds.Enqueue(new(null, sound, emitter, completed, null, false, admitted));
        return "source-sound-awaiting-pose-publication";
    }

    // Called by the real player pose/camera publication owner. Playback, random
    // draws, source generations and emitter lookup occur only at this boundary.
    internal void PublishSourcePose()
    {
        if (!_sourcePoseBoundary) throw new InvalidOperationException("Sound owner has no deferred source-pose producer.");
        if (_retired) throw new InvalidOperationException("Retired player sound owner cannot publish a pose.");
        if (!_sourcePcmPublished)
        {
            _sourcePcmPublished = true;
            RestoreOwnedPcm();
        }
        RequirePcmRestored();
        while (_pendingSourceSounds.TryPeek(out var pending))
        {
            _dispatchScene = pending.Scene;
            try
            {
                if (pending.SourceEmitter)
                {
                    // The classification belongs to admission while both real
                    // nodes were live. Invoke the captured owner before touching
                    // a root that may have retired while the key was pending.
                    var scene = pending.Scene ?? throw new InvalidDataException("Queued source sound has no actual source scene.");
                    scene.RequirePublishedPose?.Invoke();
                    scene.RequireEmitter(pending.Emitter ?? scene.Root);
                }
                if (pending.Text is { } text) DispatchSegments(text);
                else
                {
                    var emitter = pending.Emitter ?? pending.Scene!.Root;
                    if (pending.SourceEmitter) _emitterScenes[emitter] = pending.Scene!;
                    var disposition = Dispatch("SOUN:" + pending.Sound, emitter, ownsLoopStop: true,
                        pending.Sound, pending.Completed);
                    // Retire the queue entry before notifying its consumer: a
                    // consumer failure must not dispatch this generation twice.
                    _pendingSourceSounds.Dequeue();
                    pending.Admitted?.Invoke(disposition);
                    continue;
                }
                _pendingSourceSounds.Dequeue();
            }
            finally { _dispatchScene = null; }
        }
    }

    private void PublishMappedVoicePosition(Node voice, Voice state)
    {
        if (state.SourceScene is not { } scene || voice is not AudioStreamPlayer3D spatial) return;
        spatial.GlobalPosition = scene.Position(state.Emitter);
    }
}
