using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.World.Cells;

internal sealed class RuntimeNativePlayerPackage(FalloutPluginStack stack, RuntimeNativePlayer player)
{
    private FalloutScriptPackage? _package;
    private FalloutNifAnimatedNodePath? _animation;
    private FalloutFormKey? _idle;
    private int _cursor;
    private bool _packageEvent;
    private bool _complete;
    private double _elapsed;
    private double _wait;
    private FalloutNifFile? _skeleton;
    private readonly Dictionary<FalloutFormKey, FalloutNifAnimatedNodePath> _clips = [];

    internal object State => new
    {
        package = _package?.Form.ToString(),
        idle = _idle?.ToString(),
        sequence = _animation?.Sequence.Name,
        elapsedSeconds = _elapsed,
        animatedPathNodes = _animation?.AnimatedPathNodes,
        unboundOtherTargets = _animation?.UnboundOtherTargets,
        complete = _complete,
        parity = "unmeasured"
    };

    internal void Apply(string? editorId)
    {
        if (editorId is null)
        {
            if (_package?.Events.GetValueOrDefault("POEA") is not null)
                throw new NotSupportedException("Player package exit animation requires deferred removal ownership.");
            _package = null;
            _animation = null;
            _idle = null;
            player.ReleaseSourceCamera();
            return;
        }
        var package = FalloutScriptPackage.Read(FalloutDialogueTopic.Find(stack, "PACK", editorId));
        if (package.Procedure != 6 || package.LocationType != 3)
            throw new NotSupportedException($"PACK {package.Form} needs travel/location ownership before player animation.");
        var eventName = _package?.Form == package.Form ? "POCA" : "POBA";
        _package = package;
        _cursor = 0;
        _complete = false;
        _wait = 0;
        if (package.Events.GetValueOrDefault(eventName) is { } animation)
        {
            var listIndex = package.Idles.ToList().IndexOf(animation);
            if (listIndex >= 0) _cursor = listIndex + 1;
            Start(animation, true);
        }
        else NextIdle();
        GD.Print($"OPENNV_NATIVE_PLAYER_PACKAGE source={package.Form} event={eventName} idles={package.Idles.Count} owner=source-pack-idle-kf");
    }

    private void NextIdle()
    {
        if (_package is null || _package.Idles.Count == 0 || _complete) { _animation = null; return; }
        if (!_package.RunInSequence && _package.Idles.Count > 1)
            throw new NotSupportedException("Random package idle selection needs the authoritative RNG owner.");
        if (_cursor >= _package.Idles.Count) _cursor = 0;
        Start(_package.Idles[_cursor++], false);
    }

    private void Start(FalloutFormKey idle, bool packageEvent)
    {
        if (!_clips.TryGetValue(idle, out var animation))
        {
            var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned files are unavailable.");
            FalloutNifFile Read(string path) => source.TryRead(path, null, out var bytes, out _)
                ? FalloutNifFile.Read(bytes) : throw new FileNotFoundException("Source player animation resource is missing.", path);
            var selected = FalloutActorIdleSource.Resolve(stack, stack.GetEffective(idle));
            if (selected.Objects.Count != 0) throw new NotSupportedException("Player animation objects require the first-person body owner.");
            // These are engine node/resource identities, not campaign actor or pose constants.
            _skeleton ??= Read("meshes/characters/_1stperson/skeleton.nif");
            animation = new(_skeleton, Read(selected.AnimationPath), "Camera1st");
            _clips.Add(idle, animation);
        }
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
        // Keep the unused part of a frame across clip and idle-wait boundaries.
        // Dropping it on every loop accumulates camera phase drift.
        while (delta > 0)
        {
            if (_animation is null)
            {
                if (_wait <= 0) return;
                var waited = Math.Min(_wait, delta);
                _wait -= waited;
                delta -= waited;
                if (_wait > 0) return;
                NextIdle();
                if (_animation is null) return;
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
            if (_package.DoOnce) { _complete = true; return; }
            _cursor = 0;
            _wait = _package.IdleTimer;
            if (_wait <= 0) NextIdle();
        }
    }

    private void ApplySample(float sourceTime)
    {
        var transform = Transform3D.Identity;
        foreach (var (rest, sample) in _animation!.Sample(sourceTime))
        {
            var translation = sample?.Translation ?? rest.Translation;
            var scale = sample?.Scale ?? rest.Scale;
            var basis = sample?.Rotation is { } rotation
                ? new Basis(new Quaternion(rotation.X, rotation.Z, -rotation.Y, rotation.W).Normalized()).Scaled(Vector3.One * scale)
                : GamebryoCoordinate.ConvertBasis(rest.RotationRowMajor, scale, "source camera parent");
            transform *= new Transform3D(basis, GamebryoCoordinate.ConvertVector(
                new Vector3(translation.X, translation.Y, translation.Z)) * player.UnitsToMeters);
        }
        player.ApplySourceCamera(new Transform3D(transform.Basis.Orthonormalized(), transform.Origin));
    }
}
