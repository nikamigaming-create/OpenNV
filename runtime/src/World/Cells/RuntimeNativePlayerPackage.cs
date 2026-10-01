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
    private bool _packageEvent;
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
        animatedPathNodes = _animation?.AnimatedPathNodes,
        unboundOtherTargets = _animation?.UnboundOtherTargets,
        complete = _complete,
        locationReference = _package?.LocationReference?.ToString(),
        locationRadius = _package?.LocationRadius,
        pendingPlayerMove = _world.PlayerMoves.Pending,
        unbound = new[] { "unreached-destination-traversal", "editor-location-semantics", "body-animation-targets", "matched-event-timing" },
        parity = "unmeasured"
    };

    internal void Apply(FalloutFormKey? form)
    {
        if (form is null)
        {
            _package?.EventPrograms.GetValueOrDefault("POEA")?.RequireEmptyScript();
            if (_package?.Events.GetValueOrDefault("POEA") is not null)
                throw new NotSupportedException("Player package exit animation requires deferred removal ownership.");
            _package = null;
            _packageHash = null;
            _animation = null;
            _idle = null;
            _cursor = 0; _packageEvent = false; _complete = false; _elapsed = 0; _wait = 0;
            _player.ReleaseSourceCamera();
            _session.PublishPlayerPackage(null);
            return;
        }
        var record = _stack.GetEffective(form.Value);
        var package = FalloutScriptPackage.Read(record);
        RequirePackage(package);
        if (_package is not null)
        {
            _package.EventPrograms.GetValueOrDefault("POCA")?.RequireEmptyScript();
            if (_package.Events.GetValueOrDefault("POCA") is not null)
                throw new NotSupportedException("Replacing a player package with a change animation requires its deferred switch owner.");
        }
        var eventName = _package?.Form == package.Form ? "POCA" : "POBA";
        package.EventPrograms.GetValueOrDefault(eventName)?.RequireEmptyScript();
        // Validate the reached event's owned resource before changing assignment.
        if (package.Events.GetValueOrDefault(eventName) is { } first) _ = Clip(first);
        _package = package;
        _packageHash = Convert.ToHexString(SHA256.HashData(record.ReadData()));
        _cursor = 0;
        _complete = false;
        _wait = 0;
        if (package.Events.GetValueOrDefault(eventName) is { } animation)
        {
            var listIndex = package.Idles.ToList().IndexOf(animation);
            if (listIndex >= 0) _cursor = listIndex + 1;
            Start(animation, true);
        }
        else { _animation = null; _idle = null; _elapsed = 0; _packageEvent = false; }
        Publish();
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
        _animation is not null && _packageEvent, _complete, _animation is null ? 0 : _elapsed, _wait));

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
            if (!(saved.PackageEvent ? package.Events.GetValueOrDefault("POBA") == idle : package.Idles.Contains(idle)))
                throw new InvalidDataException("Saved player idle does not belong to its source package phase.");
            var clip = Clip(idle);
            var sequence = clip.Animation.Sequence;
            if (!clip.Hash.Equals(saved.AnimationSha256, StringComparison.OrdinalIgnoreCase) ||
                saved.Elapsed > (double)(sequence.StopTime - sequence.StartTime) / sequence.Frequency)
                throw new InvalidDataException("Saved player package animation differs from its owned clip.");
            _animation = clip.Animation;
        }
        _package = package; _packageHash = hash; _idle = saved.Idle; _cursor = saved.Cursor;
        _packageEvent = saved.PackageEvent; _complete = saved.Complete; _elapsed = saved.Elapsed; _wait = saved.Wait;
        if (_animation is not null) ApplySample(_animation.Sequence.StartTime + (float)(_elapsed * _animation.Sequence.Frequency));
    }

    private void NextIdle()
    {
        if (_package is null || _package.Idles.Count == 0 || _complete) { _animation = null; return; }
        RequireLocation();
        if (!_package.RunInSequence && _package.Idles.Count > 1)
            throw new NotSupportedException("Random package idle selection needs the authoritative RNG owner.");
        var cursor = _cursor >= _package.Idles.Count ? 0 : _cursor;
        Start(_package.Idles[cursor], false);
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

    private void Start(FalloutFormKey idle, bool packageEvent)
    {
        var animation = Clip(idle).Animation;
        var changed = _idle != idle;
        _animation = animation;
        _idle = idle;
        _elapsed = 0;
        _packageEvent = packageEvent;
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
            if (_packageEvent) { _packageEvent = false; NextIdle(); continue; }
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
