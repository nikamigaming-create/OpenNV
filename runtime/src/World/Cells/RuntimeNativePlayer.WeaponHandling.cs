using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private FalloutWeaponHandling? _weaponHandling;
    private NativeOwnedAnimationSoundPlayer? _weaponSounds;
    private string? _weaponAction, _weaponActionError;
    private RuntimeNativeNifAnimation? _weaponActionClip;
    private FalloutWeaponAnimationTimeline? _weaponActionKeys;
    private bool _weaponActionLooping, _weaponActionStartPending;
    private double _weaponActionSeconds, _reloadHeld;
    private int _weaponActionHitCount;
    private bool _reloadPressed, _holdHandled;
    private bool _weaponTriggerHeld, _automaticFireStopped;
    private readonly SortedSet<string> _weaponUnboundEvents = new(StringComparer.Ordinal);
    internal FalloutWeaponHandlingSnapshot? CaptureWeaponHandling() => _weaponHandling?.Capture();
    internal NativeHudAmmo? AmmunitionHud
    {
        get
        {
            if (_weaponHandling is not { Drawn: true } || _firstPerson?.Weapon is not { ClipSize: > 0, AmmoUse: > 0 } weapon ||
                !weapon.HasAmmunitionSource) return null;
            var ammo = _weaponHandling.Ammunition(weapon);
            var loaded = _weaponHandling.Loaded(weapon.Form);
            return new(loaded, Math.Max(0, (ammo is { } form ? _presentationInventory!.Item(form)?.Count ?? 0 : 0) - loaded));
        }
    }
    internal string? WeaponActionNotice => _weaponActionError is null ? null :
        _weaponActionError.StartsWith("Firing", StringComparison.Ordinal) ? "Firing is not implemented yet." : "Weapon action unavailable.";
    internal object WeaponHandlingState => new
    {
        state = CaptureWeaponHandling(),
        action = _weaponAction,
        seconds = _weaponActionSeconds,
        error = _weaponActionError,
        sounds = _weaponSounds?.State,
        firing = FiringState,
        unboundEvents = _weaponUnboundEvents.ToArray(),
        unbound = "reload-speed-modifiers,incremental-reload,hold-threshold-retail-match,action-cold-continuation"
    };

    private bool WeaponInput(InputEvent input)
    {
        if (!GetMeta("opennv_source_fighting_enabled", true).AsBool() || _firstPerson?.Weapon is null || _weaponHandling is null) return false;
        var bindings = _configuration.Player.DesktopInput;
        if (input.IsActionPressed(bindings.Reload.Action, allowEcho: false) || input.IsActionReleased(bindings.Reload.Action))
        {
            if (input.IsActionPressed(bindings.Reload.Action, allowEcho: false)) { _reloadPressed = true; _holdHandled = false; _reloadHeld = 0; }
            else
            {
                _reloadPressed = false;
                if (!_holdHandled)
                {
                    if (!_weaponHandling.Drawn) RequestWeaponAction("equip");
                    else if (_firstPerson.Weapon is { ClipSize: > 0, ReloadAnimation: not 255 } weapon) RequestWeaponAction(weapon.ReloadGroup);
                }
            }
            return true;
        }
        if (input.IsActionPressed(bindings.Fire.Action, allowEcho: false) || input.IsActionReleased(bindings.Fire.Action))
        {
            var pressed = input.IsActionPressed(bindings.Fire.Action, allowEcho: false);
            if (pressed == _weaponTriggerHeld) return true;
            _weaponTriggerHeld = pressed;
            _automaticFireStopped = false;
            if (pressed)
            {
                if (!_weaponHandling.Drawn) RequestWeaponAction("equip");
                else RequestWeaponFire();
            }
            return true;
        }
        return false;
    }

    private void RequestWeaponAction(string group)
    {
        if (_weaponAction is not null || _firstPerson?.Weapon is not { } weapon) return;
        _weaponActionError = null;
        try
        {
            if (group.StartsWith("reload", StringComparison.Ordinal))
            {
                if (weapon.ReloadAnimation >= 19) throw new NotSupportedException("Incremental/special reload state is unbound.");
                if (!_weaponHandling!.CanReload(weapon)) return;
            }
            var clip = _firstPerson.PrepareAction(group);
            _thirdPerson?.PrepareAction(group);
            var sequence = clip.Sequence;
            var attack = group.StartsWith("attack", StringComparison.Ordinal);
            _weaponActionKeys = new(clip.TextKeys, sequence.StartTime, sequence.StopTime, sequence.Frequency,
                weaponCadence: attack && weapon.Automatic && weapon.AttackAnimation == 74 && sequence.CycleType == 0);
            _weaponActionHitCount = attack ? _weaponActionKeys.Discharges : 0;
            if (attack && _weaponActionHitCount == 0)
                throw new NotSupportedException("Attack animation has no source Hit, Fire or Release event.");
            if (sequence.CycleType != 2 && !(attack && weapon.Automatic && sequence.CycleType == 0))
                throw new NotSupportedException("A weapon action needs a clamped sequence or an automatic attack loop.");
            _weaponAction = group; _weaponActionClip = clip; _weaponActionSeconds = 0;
            _weaponActionStartPending = true; _weaponActionLooping = false;
            _aiming = false;
            if (group == "equip")
            {
                _weaponHandling!.SetDrawn(true); _firstPerson.SetDrawn(true); _thirdPerson?.SetDrawn(true);
            }
            GD.Print($"OPENNV_WEAPON_ACTION_BEGIN weapon={weapon.Form} group={group}");
        }
        catch (Exception error) { _weaponActionError = error.Message; GD.PushError("OPENNV_WEAPON_ACTION_UNBOUND " + error.Message); }
    }

    private void AdvanceWeaponHandling(double delta)
    {
        if (_reloadPressed && !_holdHandled && (_reloadHeld += delta) >= .5)
        {
            _holdHandled = true;
            RequestWeaponAction(_weaponHandling!.Drawn ? "unequip" : "equip");
        }
        if (_weaponAction is null || _weaponActionClip is null) return;
        try
        {
            EnsureWeaponSounds();
            var weapon = _firstPerson!.Weapon!;
            var attack = _weaponAction.StartsWith("attack", StringComparison.Ordinal);
            var looping = attack && weapon.Automatic && _weaponTriggerHeld && !_automaticFireStopped;
            if (_weaponActionLooping && !looping) _weaponActionSeconds = _weaponActionKeys!.SampleSeconds(_weaponActionSeconds, true);
            _weaponActionLooping = looping;
            var previous = _weaponActionSeconds;
            if (attack && weapon.Automatic)
            {
                var automaticSequence = _weaponActionClip.Sequence;
                var duration = automaticSequence.StopTime - automaticSequence.StartTime;
                if (!float.IsFinite(weapon.AttackShotsPerSecond) || weapon.AttackShotsPerSecond <= 0 ||
                    !float.IsFinite(automaticSequence.Frequency) || automaticSequence.Frequency <= 0 || duration <= 0 || _weaponActionHitCount <= 0)
                    throw new InvalidDataException("Automatic weapon cadence is invalid.");
                _weaponActionSeconds += delta * _weaponActionKeys!.Period * weapon.AttackShotsPerSecond / _weaponActionKeys.Discharges;
            }
            else _weaponActionSeconds += delta * weapon.AnimationMultiplier * (attack ? weapon.AttackMultiplier : 1);
            if (attack && weapon.IsThrownWeapon && _weaponTriggerHeld && _weaponActionKeys!.Hold is { } hold && previous <= hold)
                _weaponActionSeconds = Math.Min(_weaponActionSeconds, hold);
            foreach (var key in _weaponActionKeys!.Crossed(previous, _weaponActionSeconds, _weaponActionStartPending, looping))
                foreach (var text in key.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
                {
                    if (FalloutWeaponAnimationTimeline.DischargesWeapon(text) && attack &&
                        (!weapon.Automatic || _weaponTriggerHeld && !_automaticFireStopped))
                        _pendingShotCount++;
                    else if (text.StartsWith("Sound:", StringComparison.OrdinalIgnoreCase)) _weaponSounds!.Dispatch(key with { Text = text });
                    else if (text.StartsWith("Enum:", StringComparison.OrdinalIgnoreCase) &&
                        weapon.Sounds.TryGetValue(text[5..].Trim().ToLowerInvariant(), out var sound)) _weaponSounds!.DispatchSound(sound);
                    else if (!text.Equals("start", StringComparison.OrdinalIgnoreCase) && !text.Equals("end", StringComparison.OrdinalIgnoreCase) &&
                        !text.StartsWith("Blend:", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("prn:", StringComparison.OrdinalIgnoreCase) &&
                        !text.Equals("Attach", StringComparison.OrdinalIgnoreCase) && !text.Equals("Detach", StringComparison.OrdinalIgnoreCase) &&
                        !text.Equals("Hold", StringComparison.OrdinalIgnoreCase) && !text.Equals("Loop", StringComparison.OrdinalIgnoreCase))
                        _weaponUnboundEvents.Add(text);
                }
            _weaponActionStartPending = false;
            var sequence = _weaponActionClip.Sequence;
            var sourceClock = _weaponActionSeconds * sequence.Frequency;
            var sourceDuration = sequence.StopTime - sequence.StartTime;
            var reachedEnd = !looping && sourceClock >= sourceDuration;
            if (reachedEnd)
            {
                if (_weaponAction.StartsWith("reload", StringComparison.Ordinal)) _weaponHandling!.CompleteReload(weapon);
                else if (_weaponAction == "unequip")
                {
                    _thirdPerson?.SetDrawn(false); _firstPerson.SetDrawn(false); _weaponHandling!.SetDrawn(false);
                }
                var continueAutomaticFire = weapon.Automatic && _weaponTriggerHeld && !_automaticFireStopped &&
                    CanContinueAutomaticFire(weapon) && _weaponAction != "unequip";
                GD.Print($"OPENNV_WEAPON_ACTION_END weapon={weapon.Form} group={_weaponAction}");
                // Normal save actions capture this shared weapon state. A
                // completed animation must not write the campaign every shot.
                CancelWeaponAction();
                if (continueAutomaticFire) RequestWeaponFire();
            }
            else
            {
                var poseSeconds = _weaponActionKeys.SampleSeconds(_weaponActionSeconds, looping);
                _firstPerson.SetAction(_weaponAction, poseSeconds);
                _thirdPerson?.SetAction(_weaponAction, poseSeconds);
            }
            Activity.SetWeaponDrawn(_weaponHandling!.Drawn);
        }
        catch (Exception error)
        {
            CancelWeaponAction(); _weaponActionError = error.Message; GD.PushError("OPENNV_WEAPON_ACTION_UNBOUND " + error.Message);
        }
    }

    private void CancelWeaponAction()
    {
        _weaponAction = null; _weaponActionClip = null; _weaponActionKeys = null; _weaponActionSeconds = 0; _weaponActionHitCount = 0;
        _firstPerson?.SetAction(null, 0); _thirdPerson?.SetAction(null, 0);
    }

    private void EnsureWeaponSounds()
    {
        _weaponSounds ??= new(_presentationRecords!, RuntimeLiveContentSource.Current!, this, UnitsToMeters,
            new FalloutSoundRandomState(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)))));
        if (!_weaponSounds.IsInsideTree()) AddChild(_weaponSounds);
    }
}
