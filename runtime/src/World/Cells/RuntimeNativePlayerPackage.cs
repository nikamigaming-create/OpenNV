using Godot;
using System.Security.Cryptography;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class RuntimeNativePlayerPackage
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
    private readonly FalloutAnimationSoundEvents _soundEvents;
    private readonly List<object> _textKeyEvents = [];
    private readonly SortedSet<string> _unboundTextKeys = new(StringComparer.Ordinal);

    internal RuntimeNativePlayerPackage(FalloutPluginStack stack, RuntimeNativePlayer player,
        FalloutScriptSession session, FalloutReferenceWorld world, Func<FalloutFormKey> cell,
        FalloutPlayerPackageAudioSnapshot? audio = null,
        Action<FalloutPackageEvent, Action<FalloutScriptResultReceipt>>? executeResult = null)
    {
        _stack = stack; _player = player; _session = session; _world = world; _cell = cell;
        _executeResult = executeResult;
        // The engine player reference exists independently of placed ACHR
        // records. Its audio history outlives every individual package.
        _soundEvents = new(stack.RuntimeFormKey(0x14));
        if (session.PlayerPackage is { } saved) Restore(saved);
        if (audio is not null)
        {
            if (_soundRandom is not null && _soundRandom.State != audio.RandomState)
                throw new InvalidDataException("Player package and audio random state disagree.");
            _soundRandom = new(audio.RandomState);
            _soundEvents.Restore(audio.Events, stack);
            if (_soundEvents.Events.Count != 0 || _soundEvents.OwnedLanes.Any()) EnsureSounds();
        }
    }

    internal FalloutFormKey? CurrentPackage => _package?.Form;
    internal FalloutPlayerPackageAudioSnapshot CaptureAudio()
    {
        RequireHealthy();
        if (_sounds is not null && !_sounds.CanCaptureSilent)
            throw new NotSupportedException("Player package audio requires its native playback continuation.");
        _soundRandom ??= new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
        return new(_soundEvents.Capture(), _soundRandom.State);
    }

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
        exitAction = _exitAction,
        resultReceipts = _results.Values,
        failure = _failure,
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
        unbound = new[] { "unreached-destination-traversal", "editor-location-semantics",
            "player-topic-voice", "body-animation-targets", "matched-event-timing" },
        parity = "unmeasured"
    };

    internal void Apply(FalloutFormKey? form)
    {
        RequireHealthy();
        try { ApplyCore(form); }
        catch (Exception error)
        {
            _failure = error.Message;
            throw;
        }
    }

    private void ApplyCore(FalloutFormKey? form)
    {
        if (form is null)
        {
            if (_package is null) return;
            if (_exitAction == "remove") return;
            BeginExit("remove");
            Publish();
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
            PrepareEvent(_package, "POCA");
            if (_package.Events.GetValueOrDefault("POCA") is { } change)
            {
                _ = Clip(change);
                if (_package.Form != package.Form) PrepareEvent(package, "POBA");
                _pendingPackage = package; _pendingPackageHash = hash;
                _complete = false; _wait = 0; _exitAction = null;
                var previous = _package.Form;
                var revision = _assignmentRevision;
                DispatchResult(_package, "POCA");
                if (_assignmentRevision != revision || _pendingPackage is null) { Publish(); return; }
                Start(change, "POCA", retainSameIdle: _playback?.Endless == true); Publish();
                // A forever-loop event is a pose, not an assignment barrier.
                // The next package can own that same live IDLE without replaying
                // its intro, travel or sound keys.
                if (_playback!.Endless) { CommitPending(true); Publish(); }
                GD.Print($"OPENNV_NATIVE_PLAYER_PACKAGE_CHANGE source={previous} next={package.Form} idle={change} owner=source-poca-clock parity=unmeasured");
                return;
            }
            if (_package.Form != package.Form)
            {
                var previous = _package;
                var revision = _assignmentRevision;
                DispatchResult(previous, "POCA");
                if (_assignmentRevision != revision) { Publish(); return; }
            }
        }
        var eventName = _package?.Form == package.Form ? "POCA" : "POBA";
        Assign(package, hash, eventName, retainSameIdle: _package?.Form == package.Form && _playback?.Endless == true);
        Publish();
    }

    private void PrepareEvent(FalloutScriptPackage package, string eventName)
    {
        if (package.EventPrograms.GetValueOrDefault(eventName) is { } program)
        {
            program.ValidateScript();
            if (program.CompiledProgram is { } compiled)
            {
                if (compiled.LocalCount != 0) throw new NotSupportedException("Player package embedded locals have no event-list owner.");
                _ = FalloutCompiledControlFlow.Read(compiled.ResultInstructions());
            }
            else _ = FalloutGameModeProgram.Read("begin Result\n" + program.Source + "\nend", "Result");
            if (_executeResult is null) throw new NotSupportedException("Player package result has no shared execution owner.");
        }
        // Validate the reached event's owned resource before changing assignment.
        if (package.Events.GetValueOrDefault(eventName) is { } first) _ = Clip(first);
    }

    private void Assign(FalloutScriptPackage package, string hash, string eventName, bool retainSameIdle = false)
    {
        PrepareEvent(package, eventName);
        if (_package?.Form != package.Form) _results.Clear();
        _package = package;
        _packageHash = hash;
        var revision = checked(++_assignmentRevision);
        _cursor = 0;
        _complete = false;
        _wait = 0;
        _exitAction = null;
        DispatchResult(package, eventName);
        if (_assignmentRevision != revision) return;
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
        // Compare the actual body with the source marker's forward projection.
        // Dividing the body back into game units loses a float bit even when
        // an authored MoveTo installed exactly this marker. Zero-radius source
        // locations retain exact equality; no arrival tolerance is invented.
        var position = _player.GlobalPosition;
        var projected = target.Position.Select(value => value * _player.UnitsToMeters).ToArray();
        if (!_package.ContainsReferenceLocation(_cell(), [position.X, -position.Z, position.Y], target.Cell, projected, _player.UnitsToMeters))
            throw new NotSupportedException($"PACK {_package.Form} has not reached its owned reference location; player package traversal is unbound.");
    }

    private void Publish()
    {
        if (_failure is not null) return;
        _session.PublishPlayerPackage(_package is null ? null : new(_package.Form, _packageHash!,
            _animation is null ? null : _idle, _animation is null ? null : _clips[_idle!.Value].Hash, _cursor,
            _animation is not null && _eventKind is not null, _complete, _animation is null ? 0 : _elapsed, _wait,
            _eventKind, _pendingPackage?.Form, _pendingPackageHash, _playback?.Capture(),
            _animation is null ? null : _clips[_idle!.Value].IdleHash, _soundRandom?.State,
            _results.Values.OrderBy(result => result.Kind, StringComparer.Ordinal).ToArray(), _exitAction));
    }

    internal void Restore(FalloutPlayerScriptPackageSnapshot saved)
    {
        saved.Validate();
        var record = _stack.GetEffective(saved.Package);
        var package = FalloutScriptPackage.Read(record);
        RequirePackage(package);
        var results = RestoreResults(package, saved.EventResults!);
        RequireSavedEvent(package, "POBA", results);
        RequireSavedEvent(package, saved.Phase, results);
        if (saved.Complete) RequireSavedEvent(package, "POEA", results);
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
            var clip = Clip(idle);
            if (!clip.Hash.Equals(saved.AnimationSha256, StringComparison.OrdinalIgnoreCase) ||
                !clip.IdleHash.Equals(saved.IdleSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Saved player package animation differs from its owned clip.");
            var phaseSnapshot = saved.Playback ?? throw new InvalidDataException("Player package animation has no exact saved clock.");
            if (!clip.Timing.AdmitsAdditionalLoops(phaseSnapshot.SelectedAdditionalLoops))
                throw new InvalidDataException("Saved player IDLE loop selection differs from its winning timing.");
            playback = Clock(clip, phaseSnapshot.SelectedAdditionalLoops);
            playback.Restore(phaseSnapshot);
            if (Math.Abs(playback.ElapsedSeconds - saved.Elapsed) > Math.Max(1e-8, saved.Elapsed * 1e-10))
                throw new InvalidDataException("Saved player camera phase differs from its elapsed clock.");
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
        _exitAction = saved.ExitAction;
        _results.Clear();
        foreach (var result in results) _results.Add(result.Kind, result);
        _complete = saved.Complete; _elapsed = saved.Elapsed; _wait = saved.Wait;
        if (_soundRandom is not null && soundRandom is not null) _soundRandom.Restore(soundRandom.State);
        else _soundRandom = soundRandom;
        if (_animation is not null) ApplySample(_playback!.SourceSeconds);
        else _player.ReleaseSourceCamera();
    }

    private void NextIdle()
    {
        if (_package is null || _complete) { _animation = null; _playback = null; return; }
        RequireLocation();
        if (_package.Idles.Count == 0)
        {
            _animation = null; _playback = null; _idle = null;
            if (_package.DoOnce) BeginExit("complete");
            return;
        }
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
        RequireHealthy();
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (_world.PlayerMoves.Pending) return;
        _textKeyEvents.Clear();
        try { AdvanceCore(delta); }
        catch (Exception error) { _failure = error.Message; throw; }
        finally { Publish(); }
    }

    private void AdvanceCore(double delta)
    {
        // A restored outgoing pose settles the pending assignment without
        // replaying its already committed result or starting another clock.
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
            if (_eventKind == "POEA")
            {
                FinishExit();
                return;
            }
            if (_eventKind is not null) { _eventKind = null; NextIdle(); continue; }
            if (_package.RunInSequence && _cursor < _package.Idles.Count) { NextIdle(); continue; }
            if (_package.DoOnce)
            {
                BeginExit("complete");
                if (_animation is not null) continue;
                return;
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
                    disposition = EnsureSounds().Dispatch(new FalloutNifTextKeyEvent(_playback!.CompletedRepeats, index, key.Time, value));
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

    private NativeOwnedAnimationSoundPlayer EnsureSounds()
    {
        if (_sounds is not null) return _sounds;
        _soundRandom ??= new(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
        _sounds = new(_stack, RuntimeLiveContentSource.Current!, _player, _player.UnitsToMeters,
            _soundRandom, _soundEvents);
        _player.AddChild(_sounds);
        _sounds.RequirePcmRestored();
        return _sounds;
    }
}
