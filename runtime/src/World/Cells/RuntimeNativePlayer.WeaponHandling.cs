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
    private FalloutNifTextKeyTimeline? _weaponActionKeys;
    private double _weaponActionSeconds, _reloadHeld;
    private bool _reloadPressed, _holdHandled;
    private readonly SortedSet<string> _weaponUnboundEvents = new(StringComparer.Ordinal);
    internal FalloutWeaponHandlingSnapshot? CaptureWeaponHandling() => _weaponHandling?.Capture();
    internal NativeHudAmmo? AmmunitionHud
    {
        get
        {
            if (_weaponHandling is not { Drawn: true } || _firstPerson?.Weapon is not { ClipSize: > 0 } weapon) return null;
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
        if (input is InputEventKey { PhysicalKeycode: Key.R, Echo: false } key)
        {
            if (key.Pressed) { _reloadPressed = true; _holdHandled = false; _reloadHeld = 0; }
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
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            if (!_weaponHandling.Drawn) RequestWeaponAction("equip");
            else RequestWeaponFire();
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
            if (sequence.CycleType != 2) throw new NotSupportedException("A weapon handling action needs a clamped source sequence.");
            _weaponActionKeys = new(clip.TextKeys, sequence.StartTime, sequence.StopTime, sequence.CycleType, sequence.Frequency);
            _weaponAction = group; _weaponActionClip = clip; _weaponActionSeconds = 0;
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
            var previous = _weaponActionSeconds;
            var weapon = _firstPerson!.Weapon!;
            _weaponActionSeconds += delta * weapon.AnimationMultiplier * (_weaponAction.StartsWith("attack", StringComparison.Ordinal) ? weapon.AttackMultiplier : 1);
            foreach (var key in _weaponActionKeys!.Crossed(previous, _weaponActionSeconds, previous == 0))
                foreach (var text in key.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(value => value.Trim()))
                {
                    if (text.Equals("Hit", StringComparison.OrdinalIgnoreCase) && _weaponAction.StartsWith("attack", StringComparison.Ordinal) && !_shotEmitted)
                    { _shotEmitted = true; _shotPending = true; }
                    else if (text.StartsWith("Sound:", StringComparison.OrdinalIgnoreCase)) _weaponSounds!.Dispatch(key with { Text = text });
                    else if (text.StartsWith("Enum:", StringComparison.OrdinalIgnoreCase) &&
                        weapon.Sounds.TryGetValue(text[5..].Trim().ToLowerInvariant(), out var sound)) _weaponSounds!.DispatchSound(sound);
                    else if (!text.Equals("start", StringComparison.OrdinalIgnoreCase) && !text.Equals("end", StringComparison.OrdinalIgnoreCase) &&
                        !text.StartsWith("Blend:", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("prn:", StringComparison.OrdinalIgnoreCase) &&
                        !text.Equals("Attach", StringComparison.OrdinalIgnoreCase) && !text.Equals("Detach", StringComparison.OrdinalIgnoreCase))
                        _weaponUnboundEvents.Add(text);
                }
            var sequence = _weaponActionClip.Sequence;
            if (_weaponActionSeconds * sequence.Frequency >= sequence.StopTime - sequence.StartTime)
            {
                if (_weaponAction.StartsWith("reload", StringComparison.Ordinal)) _weaponHandling!.CompleteReload(weapon);
                else if (_weaponAction == "unequip")
                {
                    _thirdPerson?.SetDrawn(false); _firstPerson.SetDrawn(false); _weaponHandling!.SetDrawn(false);
                }
                GD.Print($"OPENNV_WEAPON_ACTION_END weapon={weapon.Form} group={_weaponAction}");
                CancelWeaponAction(); SaveGame?.Invoke();
            }
            else
            {
                _firstPerson.SetAction(_weaponAction, _weaponActionSeconds);
                _thirdPerson?.SetAction(_weaponAction, _weaponActionSeconds);
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
        _weaponAction = null; _weaponActionClip = null; _weaponActionKeys = null; _weaponActionSeconds = 0;
        _firstPerson?.SetAction(null, 0); _thirdPerson?.SetAction(null, 0);
    }

    private void EnsureWeaponSounds()
    {
        _weaponSounds ??= new(_presentationRecords!, RuntimeLiveContentSource.Current!, this, UnitsToMeters,
            new FalloutSoundRandomState(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong)))));
        if (!_weaponSounds.IsInsideTree()) AddChild(_weaponSounds);
    }
}
