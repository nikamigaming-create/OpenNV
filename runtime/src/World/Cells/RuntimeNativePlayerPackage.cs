using Godot;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed class RuntimeNativePlayerPackage
{
    private readonly FalloutPluginStack _stack;
    private readonly RuntimeNativePlayer _player;
    private readonly FalloutScriptSession _session;
    private readonly FalloutReferenceWorld _world;
    private readonly Func<FalloutFormKey> _cell;
    private string? _packageHash;
    private FalloutScriptPackage? _package;
    private FalloutNifAnimatedNodePath? _animation;
    private FalloutIdleAnimationPlayback? _playback;
    private FalloutFormKey? _idle;
    private int _cursor;
    private string? _eventKind;
    private FalloutScriptPackage? _pendingPackage;
    private string? _pendingPackageHash;
    private bool _complete;
    private double _elapsed;
    private double _wait;
    private FalloutNifFile? _skeleton;
    private sealed record CameraClip(FalloutNifAnimatedNodePath Animation, string Hash, string IdleHash,
        FalloutIdleAnimationData Timing);
    private readonly Dictionary<FalloutFormKey, CameraClip> _clips = [];
    private NativeOwnedAnimationSoundPlayer? _sounds;
    private FalloutSoundRandomState? _soundRandom;
    private readonly List<object> _textKeyEvents = [];
    private readonly SortedSet<string> _unboundTextKeys = new(StringComparer.Ordinal);

    internal RuntimeNativePlayerPackage(FalloutPluginStack stack, RuntimeNativePlayer player,
        FalloutScriptSession session, FalloutReferenceWorld world, Func<FalloutFormKey> cell)
    {
        _stack = stack; _player = player; _session = session; _world = world; _cell = cell;
        if (session.PlayerPackage is { } saved) Restore(saved);
    }

    internal FalloutFormKey? CurrentPackage => _package?.Form;

    internal object State => new
    {
        package = _package?.Form.ToString(),
        idle = _idle?.ToString(),
        sequence = _animation?.Sequence.Name,
        elapsedSeconds = _elapsed,
        sourceSeconds = _playback?.SourceSeconds,
        additionalLoops = _playback?.AdditionalLoops,
        completedRepeats = _playback?.CompletedRepeats,
        loopStart = _playback?.LoopStart,
        loopEnd = _playback?.LoopEnd,
        eventKind = _eventKind,
        pendingPackage = _pendingPackage?.Form.ToString(),
        animatedPathNodes = _animation?.AnimatedPathNodes,
        unboundOtherTargets = _animation?.UnboundOtherTargets,
        complete = _complete,
        locationReference = _package?.LocationReference?.ToString(),
        locationRadius = _package?.LocationRadius,
        pendingPlayerMove = _world.PlayerMoves.Pending,
        textKeyEvents = _textKeyEvents,
        unboundTextKeys = _unboundTextKeys,
        sounds = _sounds?.State,
        unbound = new[] { "unreached-destination-traversal", "editor-location-semantics", "nonempty-event-scripts-and-topics",
            "end-animation-and-script-execution", "body-animation-targets", "matched-event-timing" },
        parity = "unmeasured"
    };

    internal void Apply(FalloutFormKey? form)
    {
        if (form is null)
        {
            _package?.EventPrograms.GetValueOrDefault("POEA")?.RequireEmptyScript();
            if (_package?.Events.GetValueOrDefault("POEA") is not null)
                throw new NotSupportedException("Player package exit animation requires deferred removal ownership.");
            // Removal clears the script assignment, including an uncommitted
            // replacement. A completed outgoing clip must not reinstall it.
            // Validate the reached exit behavior before discarding that state.
            _pendingPackage = null; _pendingPackageHash = null;
            _package = null;
            _packageHash = null;
            _animation = null; _playback = null;
            _idle = null;
            _cursor = 0; _eventKind = null; _complete = false; _elapsed = 0; _wait = 0;
            _player.ReleaseSourceCamera();
            _session.PublishPlayerPackage(null);
            return;
        }
        var record = _stack.GetEffective(form.Value);
        var package = FalloutScriptPackage.Read(record);
        RequirePackage(package);
        var hash = Convert.ToHexString(SHA256.HashData(record.ReadData()));
        // A later request replaces the one pending script assignment without
        // restarting the already reached outgoing change event.
        if (_pendingPackage is not null)
        {
            if (_package!.Form != package.Form) PrepareEvent(package, "POBA");
            _pendingPackage = package; _pendingPackageHash = hash; Publish();
            return;
        }
        if (_package is not null)
        {
            _package.EventPrograms.GetValueOrDefault("POCA")?.RequireEmptyScript();
            if (_package.Events.GetValueOrDefault("POCA") is { } change)
            {
                _ = Clip(change);
                if (_package.Form != package.Form) PrepareEvent(package, "POBA");
                _pendingPackage = package; _pendingPackageHash = hash;
                _complete = false; _wait = 0;
                var previous = _package.Form;
                Start(change, "POCA", retainSameIdle: _playback?.Endless == true); Publish();
                // A forever-loop event is a pose, not an assignment barrier.
                // The next package can own that same live IDLE without replaying
                // its intro, travel or sound keys.
                if (_playback!.Endless) { CommitPending(true); Publish(); }
                GD.Print($"OPENNV_NATIVE_PLAYER_PACKAGE_CHANGE source={previous} next={package.Form} idle={change} owner=source-poca-clock parity=unmeasured");
                return;
            }
        }
        var eventName = _package?.Form == package.Form ? "POCA" : "POBA";
        Assign(package, hash, eventName, retainSameIdle: _package?.Form == package.Form && _playback?.Endless == true);
        Publish();
    }

    private void PrepareEvent(FalloutScriptPackage package, string eventName)
    {
        package.EventPrograms.GetValueOrDefault(eventName)?.RequireEmptyScript();
        // Validate the reached event's owned resource before changing assignment.
        if (package.Events.GetValueOrDefault(eventName) is { } first) _ = Clip(first);
    }

    private void Assign(FalloutScriptPackage package, string hash, string eventName, bool retainSameIdle = false)
    {
        PrepareEvent(package, eventName);
        _package = package;
        _packageHash = hash;
        _cursor = 0;
        _complete = false;
        _wait = 0;
        if (package.Events.GetValueOrDefault(eventName) is { } animation)
        {
            var listIndex = package.Idles.ToList().IndexOf(animation);
            if (listIndex >= 0) _cursor = listIndex + 1;
            Start(animation, eventName, retainSameIdle);
        }
        else if (retainSameIdle && _animation is not null && _idle is { } current && package.Idles.Contains(current))
        {
            _cursor = package.Idles.ToList().IndexOf(current) + 1;
            _eventKind = null;
        }
        else { _animation = null; _playback = null; _idle = null; _elapsed = 0; _eventKind = null; }
        GD.Print($"OPENNV_NATIVE_PLAYER_PACKAGE source={package.Form} event={eventName} idles={package.Idles.Count} owner=source-pack-idle-kf");
    }

    private void RequirePackage(FalloutScriptPackage package)
    {
        if (package.Procedure != 6 || package.LocationType is not (0 or 3))
            throw new NotSupportedException($"PACK {package.Form} needs its procedure/location owner before player animation.");
        if (package.LocationReference is { } reference && _stack.RuntimeFormId(reference) != 0x14)
        {
            if (_stack.GetEffective(reference).Signature is not ("REFR" or "ACHR" or "ACRE"))
                throw new InvalidDataException("Player package near-reference location is not a world reference.");
            _ = _world.Placement(reference);
        }
    }

    private void RequireLocation()
    {
        // Retain the existing type-3 presentation boundary. Its editor-origin
        // semantics remain unverified; explicit references use authoritative poses.
        if (_package!.LocationReference is not { } reference || _stack.RuntimeFormId(reference) == 0x14) return;
        var target = _world.Placement(reference);
        var position = _player.GlobalPosition / _player.UnitsToMeters;
        if (!_package.ContainsReferenceLocation(_cell(), [position.X, -position.Z, position.Y], target.Cell, target.Position))
            throw new NotSupportedException($"PACK {_package.Form} has not reached its owned reference location; player package traversal is unbound.");
    }

    private void Publish() => _session.PublishPlayerPackage(_package is null ? null : new(_package.Form, _packageHash!,
        _animation is null ? null : _idle, _animation is null ? null : _clips[_idle!.Value].Hash, _cursor,
        _animation is not null && _eventKind is not null, _complete, _animation is null ? 0 : _elapsed, _wait,
        _eventKind, _pendingPackage?.Form, _pendingPackageHash, _playback?.Capture(),
        _animation is null ? null : _clips[_idle!.Value].IdleHash, _soundRandom?.State));

    internal void Restore(FalloutPlayerScriptPackageSnapshot saved)
    {
        saved.Validate();
        var record = _stack.GetEffective(saved.Package);
        var package = FalloutScriptPackage.Read(record);
        RequirePackage(package);
        var hash = Convert.ToHexString(SHA256.HashData(record.ReadData()));
        if (!hash.Equals(saved.PackageSha256, StringComparison.OrdinalIgnoreCase) || saved.Cursor > package.Idles.Count ||
            saved.Wait > package.IdleTimer || saved.Complete && !package.DoOnce || saved.Idle is not null && saved.Wait != 0)
            throw new InvalidDataException("Saved player package differs from its winning source.");
        FalloutIdleAnimationPlayback? playback = null;
        FalloutNifAnimatedNodePath? animation = null;
        if (saved.Idle is { } idle)
        {
            if (!(saved.Phase is { } phase ? package.Events.GetValueOrDefault(phase) == idle : package.Idles.Contains(idle)))
                throw new InvalidDataException("Saved player idle does not belong to its source package phase.");
            if (saved.Phase is { } eventKind) package.EventPrograms.GetValueOrDefault(eventKind)?.RequireEmptyScript();
            var clip = Clip(idle);
            var sequence = clip.Animation.Sequence;
            if (!clip.Hash.Equals(saved.AnimationSha256, StringComparison.OrdinalIgnoreCase) ||
                saved.IdleSha256 is { } idleHash && !clip.IdleHash.Equals(idleHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved player package animation differs from its owned clip.");
            if (saved.Playback is { } phaseSnapshot)
            {
                if (!clip.Timing.AdmitsAdditionalLoops(phaseSnapshot.SelectedAdditionalLoops))
                    throw new InvalidDataException("Saved player IDLE loop selection differs from its winning timing.");
                playback = Clock(clip, phaseSnapshot.SelectedAdditionalLoops);
                playback.Restore(phaseSnapshot);
                if (Math.Abs(playback.ElapsedSeconds - saved.Elapsed) > Math.Max(1e-8, saved.Elapsed * 1e-10))
                    throw new InvalidDataException("Saved player camera phase differs from its elapsed clock.");
            }
            else
            {
                // Old snapshots did not select random repeat counts. Restore only
                // a deterministic source selection, without rerolling or sound replay.
                if (saved.Elapsed > (double)(sequence.StopTime - sequence.StartTime) / sequence.Frequency)
                    throw new InvalidDataException("Legacy player camera elapsed time exceeds its saved clip.");
                var repeats = clip.Timing.SelectAdditionalLoops(_ =>
                    throw new NotSupportedException("Legacy player camera lacks its selected random IDLE repetitions."));
                playback = Clock(clip, repeats);
                playback.Advance(saved.Elapsed);
            }
            animation = clip.Animation;
            if (saved.Phase == "POCA" && saved.PendingPackage is null && !playback.Endless)
                throw new InvalidDataException("Settled player change pose is not an endless source IDLE.");
        }
        FalloutScriptPackage? pending = null;
        if (saved.PendingPackage is { } next)
        {
            var nextRecord = _stack.GetEffective(next);
            pending = FalloutScriptPackage.Read(nextRecord); RequirePackage(pending);
            if (!Convert.ToHexString(SHA256.HashData(nextRecord.ReadData())).Equals(saved.PendingPackageSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved pending player package differs from its winning source.");
            if (pending.Form != package.Form) PrepareEvent(pending, "POBA");
        }
        var soundRandom = saved.SoundRandomState is { } soundState ? new FalloutSoundRandomState(soundState) : _soundRandom;
        // All saved source, event and clock validation completes before any live
        // assignment changes. An existing sound child retains this RNG object.
        _package = package; _packageHash = hash; _idle = saved.Idle; _cursor = saved.Cursor;
        _animation = animation; _playback = playback;
        _eventKind = saved.Phase; _pendingPackage = pending; _pendingPackageHash = saved.PendingPackageSha256;
        _complete = saved.Complete; _elapsed = saved.Elapsed; _wait = saved.Wait;
        if (_soundRandom is not null && soundRandom is not null) _soundRandom.Restore(soundRandom.State);
        else _soundRandom = soundRandom;
        if (_animation is not null) ApplySample(_playback!.SourceSeconds);
        else _player.ReleaseSourceCamera();
    }

    private void NextIdle()
    {
        if (_package is null || _package.Idles.Count == 0 || _complete) { _animation = null; _playback = null; return; }
        RequireLocation();
        if (!_package.RunInSequence && _package.Idles.Count > 1)
            throw new NotSupportedException("Random package idle selection needs the authoritative RNG owner.");
        var cursor = _cursor >= _package.Idles.Count ? 0 : _cursor;
        Start(_package.Idles[cursor], null);
        _cursor = cursor + 1;
    }

    private CameraClip Clip(FalloutFormKey idle)
    {
        if (!_clips.TryGetValue(idle, out var clip))
        {
            var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned files are unavailable.");
            FalloutNifFile Read(string path) => source.TryRead(path, null, out var bytes, out _)
                ? FalloutNifFile.Read(bytes) : throw new FileNotFoundException("Source player animation resource is missing.", path);
            var record = _stack.GetEffective(idle);
            var selected = FalloutActorIdleSource.Resolve(_stack, record);
            var timing = FalloutIdleAnimationData.Read(record);
            if (selected.Objects.Count != 0) throw new NotSupportedException("Player animation objects require the first-person body owner.");
            // These are engine node/resource identities, not campaign actor or pose constants.
            _skeleton ??= Read("meshes/characters/_1stperson/skeleton.nif");
            if (!source.TryRead(selected.AnimationPath, null, out var bytes, out _))
                throw new FileNotFoundException("Source player animation resource is missing.", selected.AnimationPath);
            clip = new(new(_skeleton, FalloutNifFile.Read(bytes), "Camera1st"), Convert.ToHexString(SHA256.HashData(bytes)),
                Convert.ToHexString(SHA256.HashData(record.ReadData())), timing);
            var sequence = clip.Animation.Sequence;
            if (!float.IsFinite(sequence.Frequency) || sequence.Frequency <= 0 || !float.IsFinite(sequence.StartTime) ||
                !float.IsFinite(sequence.StopTime) || sequence.StopTime <= sequence.StartTime)
                throw new NotSupportedException("Player package animation requires a positive finite source duration.");
            // Validate the whole declared range without consuming live randomness.
            // A malformed repeat interval cannot partially install a package.
            _ = Clock(clip, timing.SelectAdditionalLoops(bound => bound - 1));
            _clips.Add(idle, clip);
        }
        return clip;
    }

    private static FalloutIdleAnimationPlayback Clock(CameraClip clip, byte repeats)
    {
        var sequence = clip.Animation.Sequence;
        return new(sequence.StartTime, sequence.StopTime, sequence.Frequency, sequence.CycleType,
            clip.Animation.TextKeys.Select(key => (key.Time, key.Value)).ToArray(), repeats);
    }

    private void Start(FalloutFormKey idle, string? eventKind, bool retainSameIdle = false)
    {
        if (retainSameIdle && _idle == idle && _playback is { Complete: false }) { _eventKind = eventKind; return; }
        var clip = Clip(idle);
        var animation = clip.Animation;
        var playback = Clock(clip, clip.Timing.SelectAdditionalLoops(_world.ScriptValues.RandomBounded));
        var changed = _idle != idle;
        _animation = animation;
        _playback = playback;
        _idle = idle;
        _elapsed = 0;
        _eventKind = eventKind;
        ApplySample(_animation.Sequence.StartTime);
        if (changed) GD.Print($"OPENNV_NATIVE_PLAYER_CAMERA source={idle} sequence={_animation.Sequence.Name} " +
            $"seconds={_animation.Sequence.StartTime:R}..{_animation.Sequence.StopTime:R} " +
            $"pathChannels={_animation.AnimatedPathNodes} otherTargetsUnbound={_animation.UnboundOtherTargets} parity=unmeasured");
    }

    internal void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (_world.PlayerMoves.Pending) return;
        _textKeyEvents.Clear();
        try { AdvanceCore(delta); }
        finally { Publish(); }
    }

    private void AdvanceCore(double delta)
    {
        // A legacy snapshot can retain the old pending infinite event. Settle
        // that assignment on advancement while preserving its restored pose.
        if (_pendingPackage is not null && _eventKind == "POCA" && _playback?.Endless == true) CommitPending(true);
        // Keep the unused part of a frame across clip and idle-wait boundaries.
        // Dropping it on every loop accumulates camera phase drift.
        while (delta > 0)
        {
            if (_animation is null)
            {
                if (_wait <= 0) { NextIdle(); if (_animation is null) return; }
                else
                {
                    var waited = Math.Min(_wait, delta);
                    _wait -= waited;
                    delta -= waited;
                    if (_wait > 0) return;
                    NextIdle();
                    if (_animation is null) return;
                }
            }
            var playback = _playback ?? throw new InvalidOperationException("Player camera has no source IDLE clock.");
            var remaining = playback.Advance(delta, PublishTextKeys);
            _elapsed += delta - remaining;
            delta = remaining;
            ApplySample(playback.SourceSeconds);
            if (!playback.Complete) return;
            _animation = null; _playback = null;
            if (_package is null) return;
            if (_eventKind == "POCA")
            {
                CommitPending(false);
                continue;
            }
            if (_eventKind is not null) { _eventKind = null; NextIdle(); continue; }
            if (_package.RunInSequence && _cursor < _package.Idles.Count) { NextIdle(); continue; }
            if (_package.DoOnce)
            {
                _package.EventPrograms.GetValueOrDefault("POEA")?.RequireEmptyScript();
                if (_package.Events.GetValueOrDefault("POEA") is not null)
                    throw new NotSupportedException("Completing a player package with an end animation requires its deferred completion owner.");
                _complete = true; return;
            }
            _cursor = 0;
            _wait = _package.IdleTimer;
            if (_wait <= 0) NextIdle();
        }
    }

    private void CommitPending(bool retainCamera)
    {
        var next = _pendingPackage ?? throw new InvalidDataException("Player change animation lost its pending assignment.");
        var nextHash = _pendingPackageHash!;
        _pendingPackage = null; _pendingPackageHash = null; _eventKind = null;
        if (next.Form == _package!.Form)
        {
            _cursor = 0;
            if (retainCamera && _idle is { } idle)
            {
                _eventKind = "POCA";
                var index = next.Idles.ToList().IndexOf(idle);
                if (index >= 0) _cursor = index + 1;
            }
            else { _animation = null; _playback = null; NextIdle(); }
        }
        else Assign(next, nextHash, "POBA", retainCamera);
    }

    private void PublishTextKeys(FalloutIdleAnimationInterval interval)
    {
        foreach (var (key, index) in _animation!.TextKeys.Select((key, index) => (key, index))
            .Where(row => row.key.Time <= interval.To &&
                (row.key.Time > interval.From || interval.IncludeFrom && row.key.Time == interval.From))
            .OrderBy(row => row.key.Time).ThenBy(row => row.index))
        {
            foreach (var value in key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
            {
                var structural = value.Equals("start", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("end", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("StartLoop", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("EndLoop", StringComparison.OrdinalIgnoreCase);
                var disposition = structural ? "source-phase-owner" : "unbound-runtime-event";
                if (!structural && (value.StartsWith("Sound:", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("Enum: StopSounds", StringComparison.OrdinalIgnoreCase)))
                {
                    if (_sounds is null)
                    {
                        _soundRandom ??= new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
                        _sounds = new(_stack, RuntimeLiveContentSource.Current!, _player, _player.UnitsToMeters, _soundRandom);
                        _player.AddChild(_sounds);
                    }
                    disposition = _sounds.Dispatch(new FalloutNifTextKeyEvent(_playback!.CompletedRepeats, index, key.Time, value));
                }
                if (disposition.Contains("unbound", StringComparison.Ordinal)) _unboundTextKeys.Add(value);
                _textKeyEvents.Add(new
                {
                    idle = _idle?.ToString(),
                    sourceSeconds = key.Time,
                    key = value,
                    repeat = _playback!.CompletedRepeats,
                    disposition
                });
            }
        }
    }

    private void ApplySample(float sourceTime) => _player.ApplyCameraPath(_animation!, sourceTime);
}
