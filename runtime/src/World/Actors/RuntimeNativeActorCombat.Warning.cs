using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativeActorCombat
{
    private NativeActorCombatAnimation? _warningIdle;
    private NativeOwnedAnimationSoundPlayer? _warningSounds;
    private float WarningInterval => FalloutGameSettingFloats.Read(_records, "fAITrespassWarningTimer");

    private void ClearRadiusWarning()
    {
        if (_state.AggroWarning is null) return;
        _state.AggroWarning = null;
        Activity.SetAlerted(false);
    }

    private void AdvanceRadiusWarning(RuntimeNativePlayer? player, double delta)
    {
        if (_state.AggroWarning is not { } warning) return;
        RequireFollowCombatPolicy("retained aggro-warning continuation");
        var target = warning.Target == _records.RuntimeFormKey(0x14) ? player :
            CombatActors.SingleOrDefault(actor => actor._state.Reference == warning.Target)?._actor;
        if (target is null) { ClearRadiusWarning(); return; }
        var distance = _actor.GlobalPosition.DistanceSquaredTo(target.GlobalPosition);
        if (distance > _detectRange * _detectRange) { ClearRadiusWarning(); return; }
        var maximum = checked((int)FalloutGameSettingIntegers.Read(_records, "iGuardWarnings"));
        var (next, attack) = warning.Advance(delta, WarningInterval, maximum);
        if (attack || distance <= _detectRange * _detectRange / 4)
        {
            _state.AggroWarning = null;
            _state.Engagement = new(warning.Target);
            return;
        }
        PrepareMovement();
        var directory = _skeletonPath[.._skeletonPath.LastIndexOf('/')];
        _warningIdle ??= Clip(SelectPath(directory, "locomotion/mtidle", "mtidle"), true);
        if (warning.Animation is not null && (!warning.Animation.Equals(_warningIdle.Path, StringComparison.OrdinalIgnoreCase) ||
            !warning.AnimationSha256!.Equals(_warningIdle.Hash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Saved warning idle differs from the winning source.");
        StopPackageMotion();
        Activity.SetAlerted(true); Activity.SetCombat(false); Activity.SetMovement(false, false);
        TurnToward(target.GlobalPosition, delta);
        MoveActor(Vector3.Zero, delta);
        _skeleton.ResetSourceBonePoses();
        _skeleton.Node.SetBonePose(_skeleton.BoneIndex(_warningIdle.Animation.Sequence.TargetName), Transform3D.Identity);
        RuntimeNativeNifAnimation.ApplyLayers([(_warningIdle.Animation, _warningIdle.Time(next.Seconds))]);
        _warningSounds ??= new(_records, _content, _actor, _skeleton.UnitsToMetres, _state.SoundRandom, _state.AnimationSoundEvents);
        if (!_warningSounds.IsInsideTree()) _actor.AddChild(_warningSounds);
        foreach (var key in _warningIdle.Events.Crossed(warning.Seconds, next.Seconds, warning.Seconds == 0))
            _warningSounds.Dispatch(key);
        _state.AggroWarning = next with { Animation = _warningIdle.Path, AnimationSha256 = _warningIdle.Hash };
    }
}
