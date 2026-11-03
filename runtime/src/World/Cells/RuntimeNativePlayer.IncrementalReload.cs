using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal partial class RuntimeNativePlayer
{
    private enum IncrementalReloadPhase { Opening, Inserting, Closing }
    private sealed class IncrementalReloadOwner(FalloutFormKey weapon, Guid instance, FalloutFormKey ammunition, string group)
    {
        internal FalloutFormKey Weapon { get; } = weapon;
        internal Guid Instance { get; } = instance;
        internal FalloutFormKey Ammunition { get; } = ammunition;
        internal string Group { get; } = group;
        internal IncrementalReloadPhase Phase { get; set; } = IncrementalReloadPhase.Opening;
        internal bool Inserted { get; set; }
        internal bool RepeatPending { get; set; }
    }
    private IncrementalReloadOwner? _incrementalReload;

    private float PlayerReloadSpeed()
    {
        var settings = (_presentationRecords ?? throw new InvalidOperationException("Reload has no owned settings.")).NumericSettings;
        return FalloutWeaponReloadSpeed.Calculate(
            (_combatActorValue ?? throw new NotSupportedException("Reload has no current player actor-value owner."))(10),
            settings.Float("fAgilityReloadBase"), settings.Float("fAgilityReloadModifier"),
            (_combatPerks ?? throw new NotSupportedException("Reload has no current player perk owner."))(),
            _combatCondition ?? throw new NotSupportedException("Reload has no player perk-condition owner."));
    }


    private void BeginIncrementalReload(FalloutWeaponPresentation weapon)
    {
        var owner = PrepareIncrementalReload(weapon);
        PublishIncrementalReloadClip(owner, weapon, opening: true);
        _incrementalReload = owner;
    }

    private IncrementalReloadOwner PrepareIncrementalReload(FalloutWeaponPresentation weapon)
    {
        var first = _firstPerson ?? throw new InvalidOperationException("Reload has no actual first-person actor.");
        var third = _thirdPerson ?? throw new InvalidOperationException("Reload has no actual world actor.");
        var group = weapon.ReloadGroup;
        foreach (var actor in new[] { first, third })
        {
            var opening = actor.PrepareAction(group + "start");
            _ = ActionTimeline(opening, weapon, attack: false);
            var insertion = actor.PrepareAction(group);
            _ = ActionTimeline(insertion, weapon, attack: false);
            var hits = insertion.TextKeys.SelectMany(key => key.Value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(text => text.Trim().Equals("Hit", StringComparison.OrdinalIgnoreCase)).Select(_ => key.Time)).ToArray();
            if (hits.Length != 1 || hits[0] < insertion.Sequence.StartTime || hits[0] >= insertion.Sequence.StopTime)
                throw new NotSupportedException("Incremental reload needs its single source insert event before the closing tail.");
        }
        var instance = _presentationInventory!.SelectedWeapon(weapon.Form).Extra.Instance ??
            throw new InvalidOperationException("Reload has no actual inventory-instance identity.");
        var ammunition = _weaponHandling!.Ammunition(weapon) ??
            throw new InvalidOperationException("Reload has no admitted source ammunition.");
        return new IncrementalReloadOwner(weapon.Form, instance, ammunition, group);
    }

    private void PublishIncrementalReloadClip(IncrementalReloadOwner owner, FalloutWeaponPresentation weapon, bool opening)
    {
        var group = owner.Group + (opening ? "start" : "");
        var first = _firstPerson!.PrepareAction(group);
        _thirdPerson!.PrepareAction(group);
        _weaponActionKeys = ActionTimeline(first, weapon, attack: false);
        _weaponAction = group; _weaponActionClip = first; _weaponActionSeconds = 0; _weaponActionHitCount = 0;
        _weaponActionVariant = null; _weaponActionStartPending = true; _weaponActionLooping = false;
        owner.Phase = opening ? IncrementalReloadPhase.Opening : IncrementalReloadPhase.Inserting;
        owner.Inserted = false; owner.RepeatPending = false;
        _firstPerson.SetAction(group, 0); _thirdPerson.SetAction(group, 0);
        _aiming = false;
    }

    private void CommitIncrementalReloadInsert(FalloutWeaponPresentation weapon)
    {
        var owner = _incrementalReload ?? throw new InvalidOperationException("Reload insert has no current source owner.");
        if (owner.Weapon != weapon.Form || owner.Phase != IncrementalReloadPhase.Inserting || owner.Inserted)
            throw new InvalidDataException("Reload insert does not belong to its current source phase.");
        owner.Inserted = true;
        var inserted = _weaponHandling!.InsertReloadRound(weapon, owner.Instance, owner.Ammunition);
        owner.RepeatPending = inserted && _weaponHandling.CanReload(weapon);
        if (!owner.RepeatPending)
        {
            owner.Phase = IncrementalReloadPhase.Closing;
        }
    }

    private bool InterruptIncrementalReloadForFire(FalloutWeaponPresentation weapon)
    {
        if (_incrementalReload is not { } owner) return false;
        if (owner.Phase == IncrementalReloadPhase.Closing)
        {
            // The logical reload is already complete. Its retained first/third-person
            // closing tails yield to a newly accepted ordinary action.
            CancelWeaponAction();
            return false;
        }
        var loaded = _weaponHandling!.Loaded(weapon.Form);
        if (loaded == 0) return true;
        // The player's explicit stop path completes the in-progress round
        // only below full capacity; it does not dispatch a shot on that edge.
        if (loaded < weapon.ClipSize - 1)
            _weaponHandling.InsertReloadRound(weapon, owner.Instance, owner.Ammunition);
        CancelWeaponAction();
        return true;
    }

    private bool AdvanceIncrementalReloadBoundary(FalloutWeaponPresentation weapon, bool reachedEnd)
    {
        if (_incrementalReload is not { } owner) return false;
        if (owner.RepeatPending || owner.Phase == IncrementalReloadPhase.Opening && reachedEnd)
        {
            PublishIncrementalReloadClip(owner, weapon, opening: false);
            return true;
        }
        if (!reachedEnd) return false;
        if (owner.Phase != IncrementalReloadPhase.Closing)
            throw new InvalidDataException("Incremental reload reached its end without the source insertion event.");
        CancelWeaponAction();
        return true;
    }
}
