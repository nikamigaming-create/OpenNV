using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal partial class Fo1TacticalSession
{
    internal bool FireFirstPerson(Vector3 origin, Vector3 direction)
    {
        if (!_firstPersonModeActive)
            return false;
        SetEquippedWeapon(melee: false);
        if (_fpsShotCooldownSeconds > 0.0)
            return false;
        var weapon = _playerProfile.RangedWeapon;
        if (_magazineRounds < weapon.RoundsPerAttack)
        {
            _combatPresentation?.PresentDryFire(origin);
            _status = $"{weapon.Name} is empty • press R to reload ({_reserveRounds} reserve)";
            RefreshHud();
            return false;
        }
        _fpsShotCooldownSeconds = _runtimeProfile.Gameplay.FirstPersonShotCooldownSeconds;
        direction = direction.Normalized();
        _fpsShots++;
        _rangedAttacks++;
        _combatSequence++;
        _magazineRounds -= weapon.RoundsPerAttack;
        var maximumRange = FirstPersonMaximumRangeMeters;
        var target = FindFirstPersonTarget(origin, direction, maximumRange);
        if (target is null)
        {
            var endpoint = FirstPersonEnvironmentEndpoint(origin, direction, maximumRange);
            PresentFirstPersonRanged(origin, direction, endpoint, hit: false);
            _status = $"{weapon.Name} fired • MISS • {_magazineRounds}/{weapon.AmmunitionCapacity}";
            RefreshHud();
            Save();
            return false;
        }

        SelectMob(target.Mob);
        target.Mob.Alert();
        var rolled = RollDamage(weapon, target.Mob.Serial, melee: false);
        var applied = ApplyDamage(target.Mob, rolled, firstPerson: true);
        _fpsHits++;
        _rangedHits++;
        PresentFirstPersonRanged(
            origin,
            direction,
            target.Mob.GlobalPosition +
                Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters,
            hit: true);
        if (!target.Mob.Alive)
        {
            _status = $"FPS {weapon.Name} hit for {applied} • {target.Mob.DisplayName} down";
        }
        else
            _status = $"FPS {weapon.Name} hit for {applied} • " +
                $"{target.Mob.HitPoints}/{target.Mob.MaximumHitPoints} HP";
        RefreshHud();
        Save();
        return true;
    }

    internal Vector3 FindClearFirstPersonDirection(Vector3 origin)
    {
        Vector3? bestDirection = null;
        var bestDistance = float.PositiveInfinity;
        for (var sample = 0; sample < Fo1HexMath.Width; sample++)
        {
            var angle = Mathf.Tau * sample / Fo1HexMath.Width;
            var direction = new Vector3(MathF.Sin(angle), 0.0f, MathF.Cos(angle));
            if (FindFirstPersonTarget(origin, direction, FirstPersonMaximumRangeMeters) is not null)
                continue;
            var endpoint = FirstPersonEnvironmentEndpoint(
                origin,
                direction,
                FirstPersonMaximumRangeMeters);
            var distance = endpoint.DistanceTo(origin);
            if (distance >= bestDistance)
                continue;
            bestDistance = distance;
            bestDirection = direction;
        }
        return bestDirection ?? throw new InvalidOperationException(
            "Fallout FPS could not find a clear source-walk-mask miss direction.");
    }

    private FirstPersonTarget? FindFirstPersonTarget(
        Vector3 origin,
        Vector3 direction,
        float maximumRange)
    {
        return _mobs
            .Where(mob => mob.Alive)
            .Select(mob =>
            {
                var targetPoint = mob.GlobalPosition +
                    Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters;
                var offset = targetPoint - origin;
                var along = offset.Dot(direction);
                var perpendicular = (offset - direction * along).Length();
                return new FirstPersonTarget(mob, along, perpendicular);
            })
            .Where(candidate =>
                candidate.Along > _runtimeProfile.Gameplay.FirstPersonMinimumForwardMeters &&
                candidate.Along <= maximumRange &&
                candidate.Perpendicular <= _runtimeProfile.Gameplay.FirstPersonHitRadiusMeters)
            .OrderBy(candidate => candidate.Along)
            .ThenBy(candidate => candidate.Mob.Serial)
            .FirstOrDefault();
    }

    private Vector3 FirstPersonEnvironmentEndpoint(
        Vector3 origin,
        Vector3 direction,
        float maximumRange)
    {
        var spacing = _runtimeProfile.Gameplay.FirstPersonMaximumSubstepMeters;
        var steps = Math.Max(1, (int)MathF.Ceiling(maximumRange / spacing));
        for (var step = 1; step <= steps; step++)
        {
            var distance = MathF.Min(maximumRange, step * spacing);
            var point = origin + direction * distance;
            if (!CanWalk(Fo1HexMath.NearestTile(point)))
                return point;
        }
        return origin + direction * maximumRange;
    }

    internal bool MeleeFirstPerson(Vector3 origin, Vector3 direction)
    {
        if (!_firstPersonModeActive || _fpsMeleeCooldownSeconds > 0.0)
            return false;
        _fpsMeleeCooldownSeconds = _runtimeProfile.Gameplay.FirstPersonMeleeCooldownSeconds;
        SetEquippedWeapon(melee: true);
        PlayPlayerCombatAnimation(_playerMeleeAttackAnimation);
        direction = direction.Normalized();
        _meleeAttacks++;
        _combatSequence++;
        var target = _mobs
            .Where(mob => mob.Alive)
            .Select(mob =>
            {
                var targetPoint = mob.GlobalPosition +
                    Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters;
                var offset = targetPoint - origin;
                var along = offset.Dot(direction);
                var perpendicular = (offset - direction * along).Length();
                return new { Mob = mob, Along = along, Perpendicular = perpendicular };
            })
            .Where(candidate =>
                candidate.Along > 0.0f &&
                candidate.Along <= _runtimeProfile.Gameplay.FirstPersonMeleeReachMeters &&
                candidate.Perpendicular <= _runtimeProfile.Gameplay.FirstPersonMeleeHitRadiusMeters)
            .OrderBy(candidate => candidate.Along)
            .ThenBy(candidate => candidate.Mob.Serial)
            .FirstOrDefault();
        if (target is null)
        {
            _combatPresentation?.PresentMelee(
                origin,
                origin + direction * _runtimeProfile.Gameplay.FirstPersonMeleeReachMeters,
                hit: false);
            _status = $"FPS {_playerProfile.MeleeWeapon.Name} swing • MISS";
            RefreshHud();
            Save();
            return false;
        }

        SelectMob(target.Mob);
        target.Mob.Alert();
        var damage = RollDamage(_playerProfile.MeleeWeapon, target.Mob.Serial, melee: true);
        var applied = ApplyDamage(target.Mob, damage, firstPerson: true);
        _meleeHits++;
        _combatPresentation?.PresentMelee(
            origin,
            target.Mob.GlobalPosition +
                Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters,
            hit: true);
        _status = target.Mob.Alive
            ? $"FPS {_playerProfile.MeleeWeapon.Name} struck for {applied} • " +
                $"{target.Mob.HitPoints}/{target.Mob.MaximumHitPoints} HP"
            : $"FPS {_playerProfile.MeleeWeapon.Name} struck for {applied} • " +
                $"{target.Mob.DisplayName} down";
        RefreshHud();
        Save();
        return true;
    }

    private void PresentTacticalRanged(Fo1Mob target, bool hit)
    {
        if (_combatPresentation is null)
            return;
        var origin = _ownedPlayerWeapon is null ||
            _ownedPlayerWeapon.Value.MuzzleMarker is null
            ? _playerToken.GlobalPosition + Vector3.Up
            : _ownedPlayerWeapon.Value.Root.ToGlobal(
                _ownedPlayerWeapon.Value.MuzzlePositionGodotUnits);
        var casingOrigin = _ownedPlayerWeapon is null ||
            _ownedPlayerWeapon.Value.ShellMarker is null
            ? origin
            : _ownedPlayerWeapon.Value.Root.ToGlobal(
                _ownedPlayerWeapon.Value.ShellPositionGodotUnits);
        var right = _ownedPlayerWeapon?.Root.GlobalBasis.X.Normalized() ??
            _playerToken.GlobalBasis.X.Normalized();
        var endpoint = target.GlobalPosition +
            Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters;
        if (!hit)
            endpoint += right * _runtimeProfile.CombatPresentation.TacticalMissOffsetMeters;
        _combatPresentation.PresentRanged(origin, endpoint, hit, casingOrigin, right);
    }

    private void PresentTacticalMelee(Fo1Mob target, bool hit)
    {
        if (_combatPresentation is null)
            return;
        var origin = _ownedPlayerMeleeWeapon?.Root.GlobalPosition ??
            _playerToken.GlobalPosition + Vector3.Up;
        var endpoint = target.GlobalPosition +
            Vector3.Up * _runtimeProfile.Gameplay.FirstPersonTargetHeightMeters;
        _combatPresentation.PresentMelee(origin, endpoint, hit);
    }

    private void PresentFirstPersonRanged(
        Vector3 origin,
        Vector3 direction,
        Vector3 endpoint,
        bool hit)
    {
        if (_combatPresentation is null)
            return;
        var right = direction.Cross(Vector3.Up).Normalized();
        var casingOrigin = origin +
            right * _runtimeProfile.CombatPresentation.FpsCasingRightMeters +
            Vector3.Down * _runtimeProfile.CombatPresentation.FpsCasingDownMeters +
            direction * _runtimeProfile.CombatPresentation.FpsCasingForwardMeters;
        _combatPresentation.PresentRanged(origin, endpoint, hit, casingOrigin, right);
    }

    private CombatResult RejectCombat(string kind, string mode, string status)
    {
        _status = status;
        RefreshHud();
        Save();
        return new CombatResult(false, kind, mode, false, 0, false, 0, 0);
    }

    private int TacticalHitChance(WeaponProfile weapon, Fo1Mob target, int distance)
    {
        if (!_playerProfile.Skills.TryGetValue(weapon.Skill, out var skill))
            throw new InvalidOperationException(
                $"Fallout character has no transported combat skill: {weapon.Skill}");
        var strengthPenalty = Math.Max(0, weapon.MinimumStrength - _playerProfile.Strength) *
            _runtimeProfile.Gameplay.StrengthPenaltyPerPointPercent;
        var rangePenalty = weapon.Melee
            ? 0
            : Math.Max(
                0,
                distance - _playerProfile.Perception *
                    _runtimeProfile.Gameplay.RangedPerceptionRangeMultiplier) *
                _runtimeProfile.Gameplay.RangedPenaltyPerExcessHexPercent;
        return Math.Clamp(
            skill - target.ArmorClass - strengthPenalty - rangePenalty,
            _runtimeProfile.Gameplay.TacticalMinimumHitChancePercent,
            _runtimeProfile.Gameplay.TacticalMaximumHitChancePercent);
    }

    private int RollDamage(WeaponProfile weapon, int targetSerial, bool melee)
    {
        var span = weapon.MaximumDamage - weapon.MinimumDamage + 1;
        var rolled = weapon.MinimumDamage +
            (int)(DeterministicUInt($"{(melee ? "melee" : "ranged")}-damage", targetSerial) %
                (uint)span);
        return rolled + (melee ? _playerProfile.MeleeDamage : 0);
    }

    private int DeterministicPercent(string purpose, int targetSerial) =>
        (int)(DeterministicUInt(purpose, targetSerial) % Fo1TacticalSessionNumericContracts.PresentationUint100U) + 1;

    private uint DeterministicUInt(string purpose, int targetSerial)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"{_sceneSha256}|{_turn}|{_combatSequence}|{purpose}|{_playerTile}|{targetSerial}");
        var hash = SHA256.HashData(payload);
        return (uint)(hash[0] << Fo1TacticalSessionNumericContracts.PresentationInt24 | hash[1] << Fo1TacticalSessionNumericContracts.PresentationInt16 | hash[2] << Fo1TacticalSessionNumericContracts.PresentationInt8 | hash[3]);
    }

    private int ApplyDamage(Fo1Mob target, int damage, bool firstPerson)
    {
        var applied = target.TakeDamage(damage);
        if (target.Alive)
            return applied;
        _mobsByTile.Remove(target.Tile);
        _walkable[target.Tile] = true;
        _kills++;
        if (firstPerson)
            _fpsKills++;
        _targetReticle.Visible = false;
        return applied;
    }

    private void AddInventoryObjects(string symbol, int objects)
    {
        if (string.IsNullOrWhiteSpace(symbol) || objects <= 0)
            throw new InvalidOperationException("Fallout inventory stack is invalid.");
        _inventoryObjects[symbol] = InventoryObjects(symbol) + objects;
    }

    private int InventoryObjects(string symbol) => _inventoryObjects.GetValueOrDefault(symbol);

    internal IReadOnlyDictionary<string, int> InventorySnapshot() =>
        _inventoryObjects.OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);

    internal MapInventoryPickup PickupAdjacentMapInventoryHost(int serial)
    {
        if (!_mapInventoryHosts.TryGetValue(serial, out var host))
            throw new InvalidOperationException(
                $"Fallout MAP inventory host is absent from this source scene: {serial}");
        if (_lootedMapInventoryHostSerials.Contains(serial))
            throw new InvalidOperationException(
                $"Fallout MAP inventory host was already collected: {serial}");
        if (!Fo1HexMath.AreNeighbors(_playerTile, host.Tile))
            throw new InvalidOperationException(
                $"Fallout MAP inventory host requires a source-adjacent player hex: {serial}");
        foreach (var item in host.Items)
            AddInventoryObjects(item.Symbol, item.Objects);
        _lootedMapInventoryHostSerials.Add(serial);
        _status = $"Collected source MAP inventory host {serial}";
        RefreshHud();
        Save();
        return new MapInventoryPickup(host, InventorySnapshot());
    }

    internal bool EquipLootedMapInventoryWeaponForHeadlessProof(int hostSerial, string symbol)
    {
        if (!_mapInventoryHosts.TryGetValue(hostSerial, out var host) ||
            !_lootedMapInventoryHostSerials.Contains(hostSerial) ||
            !host.Items.Any(item => item.Symbol == symbol && item.SubtypeName == "weapon"))
            throw new InvalidOperationException(
                "Fallout headless proof cannot equip a weapon that was not collected from the source MAP host.");
        return EquipInventoryWeaponCore(symbol);
    }
}
