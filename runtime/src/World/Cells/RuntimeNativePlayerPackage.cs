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
    private FalloutFormKey? _idle;
    private int _cursor;
    private string? _eventKind;
    private FalloutScriptPackage? _pendingPackage;
    private string? _pendingPackageHash;
    private bool _complete;
    private double _elapsed;
    private double _wait;
    private FalloutNifFile? _skeleton;
    private readonly Dictionary<FalloutFormKey, (FalloutNifAnimatedNodePath Animation, string Hash)> _clips = [];

    internal RuntimeNativePlayerPackage(FalloutPluginStack stack, RuntimeNativePlayer player,
        FalloutScriptSession session, FalloutReferenceWorld world, Func<FalloutFormKey> cell)
    {
        _stack = stack; _player = player; _session = session; _world = world; _cell = cell;
        if (session.PlayerPackage is { } saved) Restore(saved);
    }

    internal object State => new
    {
        package = _package?.Form.ToString(),
        idle = _idle?.ToString(),
        sequence = _animation?.Sequence.Name,
        elapsedSeconds = _elapsed,
        eventKind = _eventKind,
        pendingPackage = _pendingPackage?.Form.ToString(),
        animatedPathNodes = _animation?.AnimatedPathNodes,
        unboundOtherTargets = _animation?.UnboundOtherTargets,
        complete = _complete,
        locationReference = _package?.LocationReference?.ToString(),
        locationRadius = _package?.LocationRadius,
        pendingPlayerMove = _world.PlayerMoves.Pending,
        unbound = new[] { "unreached-destination-traversal", "editor-location-semantics", "nonempty-event-scripts-and-topics",
            "end-animation-and-change-cancellation", "body-animation-targets", "matched-event-timing" },
        parity = "unmeasured"
    };

    internal void Apply(FalloutFormKey? form)
    {
        if (form is null)
        {
            if (_pendingPackage is not null)
                throw new NotSupportedException("Removing a player package during its change animation requires cancellation ownership.");
            _package?.EventPrograms.GetValueOrDefault("POEA")?.RequireEmptyScript();
            if (_package?.Events.GetValueOrDefault("POEA") is not null)
                throw new NotSupportedException("Player package exit animation requires deferred removal ownership.");
            _package = null;
            _packageHash = null;
            _animation = null;
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
                Start(change, "POCA"); Publish();
                GD.Print($"OPENNV_NATIVE_PLAYER_PACKAGE_CHANGE source={_package.Form} next={package.Form} idle={change} owner=source-poca-clock parity=unmeasured");
                return;
            }
        }
        var eventName = _package?.Form == package.Form ? "POCA" : "POBA";
        Assign(package, hash, eventName);
        Publish();
    }

    private void PrepareEvent(FalloutScriptPackage package, string eventName)
    {
        package.EventPrograms.GetValueOrDefault(eventName)?.RequireEmptyScript();
        // Validate the reached event's owned resource before changing assignment.
        if (package.Events.GetValueOrDefault(eventName) is { } first) _ = Clip(first);
    }

    private void Assign(FalloutScriptPackage package, string hash, string eventName)
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
            Start(animation, eventName);
        }
        else { _animation = null; _idle = null; _elapsed = 0; _eventKind = null; }
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
        _eventKind, _pendingPackage?.Form, _pendingPackageHash));

    private void Restore(FalloutPlayerScriptPackageSnapshot saved)
    {
        saved.Validate();
        var record = _stack.GetEffective(saved.Package);
        var package = FalloutScriptPackage.Read(record);
        RequirePackage(package);
        var hash = Convert.ToHexString(SHA256.HashData(record.ReadData()));
        if (!hash.Equals(saved.PackageSha256, StringComparison.OrdinalIgnoreCase) || saved.Cursor > package.Idles.Count ||
            saved.Wait > package.IdleTimer || saved.Complete && !package.DoOnce || saved.Idle is not null && saved.Wait != 0)
            throw new InvalidDataException("Saved player package differs from its winning source.");
        if (saved.Idle is { } idle)
        {
            if (!(saved.Phase is { } phase ? package.Events.GetValueOrDefault(phase) == idle : package.Idles.Contains(idle)))
                throw new InvalidDataException("Saved player idle does not belong to its source package phase.");
            if (saved.Phase is { } eventKind) package.EventPrograms.GetValueOrDefault(eventKind)?.RequireEmptyScript();
            var clip = Clip(idle);
            var sequence = clip.Animation.Sequence;
            if (!clip.Hash.Equals(saved.AnimationSha256, StringComparison.OrdinalIgnoreCase) ||
                saved.Elapsed > (double)(sequence.StopTime - sequence.StartTime) / sequence.Frequency)
                throw new InvalidDataException("Saved player package animation differs from its owned clip.");
            _animation = clip.Animation;
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
        _package = package; _packageHash = hash; _idle = saved.Idle; _cursor = saved.Cursor;
        _eventKind = saved.Phase; _pendingPackage = pending; _pendingPackageHash = saved.PendingPackageSha256;
        _complete = saved.Complete; _elapsed = saved.Elapsed; _wait = saved.Wait;
        if (_animation is not null) ApplySample(_animation.Sequence.StartTime + (float)(_elapsed * _animation.Sequence.Frequency));
    }

    private void NextIdle()
    {
        if (_package is null || _package.Idles.Count == 0 || _complete) { _animation = null; return; }
        RequireLocation();
        if (!_package.RunInSequence && _package.Idles.Count > 1)
            throw new NotSupportedException("Random package idle selection needs the authoritative RNG owner.");
        var cursor = _cursor >= _package.Idles.Count ? 0 : _cursor;
        Start(_package.Idles[cursor], null);
        _cursor = cursor + 1;
    }

    private (FalloutNifAnimatedNodePath Animation, string Hash) Clip(FalloutFormKey idle)
    {
        if (!_clips.TryGetValue(idle, out var clip))
        {
            var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned files are unavailable.");
            FalloutNifFile Read(string path) => source.TryRead(path, null, out var bytes, out _)
                ? FalloutNifFile.Read(bytes) : throw new FileNotFoundException("Source player animation resource is missing.", path);
            var selected = FalloutActorIdleSource.Resolve(_stack, _stack.GetEffective(idle));
            if (selected.Objects.Count != 0) throw new NotSupportedException("Player animation objects require the first-person body owner.");
            // These are engine node/resource identities, not campaign actor or pose constants.
            _skeleton ??= Read("meshes/characters/_1stperson/skeleton.nif");
            if (!source.TryRead(selected.AnimationPath, null, out var bytes, out _))
                throw new FileNotFoundException("Source player animation resource is missing.", selected.AnimationPath);
            clip = (new(_skeleton, FalloutNifFile.Read(bytes), "Camera1st"), Convert.ToHexString(SHA256.HashData(bytes)));
            var sequence = clip.Animation.Sequence;
            if (!float.IsFinite(sequence.Frequency) || sequence.Frequency <= 0 || !float.IsFinite(sequence.StartTime) ||
                !float.IsFinite(sequence.StopTime) || sequence.StopTime <= sequence.StartTime)
                throw new NotSupportedException("Player package animation requires a positive finite source duration.");
            _clips.Add(idle, clip);
        }
        return clip;
    }

    private void Start(FalloutFormKey idle, string? eventKind)
    {
        var animation = Clip(idle).Animation;
        var changed = _idle != idle;
        _animation = animation;
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
        try { AdvanceCore(delta); }
        finally { Publish(); }
    }

    private void AdvanceCore(double delta)
    {
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
            var sequence = _animation.Sequence;
            var duration = (double)(sequence.StopTime - sequence.StartTime) / sequence.Frequency;
            var consumed = Math.Min(delta, Math.Max(0, duration - _elapsed));
            _elapsed += consumed;
            delta -= consumed;
            var ended = _elapsed >= duration;
            ApplySample(ended ? sequence.StopTime : MathF.Min(sequence.StopTime,
                sequence.StartTime + (float)(_elapsed * sequence.Frequency)));
            if (!ended) return;
            _animation = null;
            if (_package is null) return;
            if (_eventKind == "POCA")
            {
                var next = _pendingPackage ?? throw new InvalidDataException("Player change animation lost its pending assignment.");
                var nextHash = _pendingPackageHash!;
                _pendingPackage = null; _pendingPackageHash = null; _eventKind = null;
                if (next.Form == _package.Form) { _cursor = 0; NextIdle(); }
                else Assign(next, nextHash, "POBA");
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

    private void ApplySample(float sourceTime) => _player.ApplyCameraPath(_animation!, sourceTime);
}
