using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime;

internal partial class Fo1TacticalSession : Node
{
    private const string SaveSchema = "opennv-fo1-hex-save/v1";
    private readonly Queue<int> _movement = new();
    private bool[] _walkable = [];
    private int[] _floorIds = [];
    private IReadOnlyDictionary<int, string> _floorNames = new Dictionary<int, string>();
    private string _sceneSha256 = "";
    private string _savePath = "";
    private int _maximumActionPoints;
    private int _doorTile;
    private int _entryTile;
    private int _hoveredTile = -1;
    private int _selectedTile = -1;
    private int _turn = 1;
    private int _actionPoints;
    private int _playerTile;
    private Node3D _playerToken = null!;
    private Sprite3D _playerSourceSprite = null!;
    private ActorModelSlice.LoadedActor? _ownedPlayer;
    private Fo1ThirdPersonWeapon.LoadedWeapon? _ownedPlayerWeapon;
    private Fo1ThirdPersonWeapon.LoadedWeapon? _ownedPlayerMeleeWeapon;
    private AnimationPlayer? _playerAnimationPlayer;
    private string _playerIdleAnimation = "";
    private string _playerMoveAnimation = "";
    private string _playerRangedAttackAnimation = "";
    private string _playerMeleeAttackAnimation = "";
    private string _playerReloadAnimation = "";
    private bool _meleeWeaponEquipped;
    private int _playerMoveAnimationPlaybacks;
    private MeshInstance3D _hoverMarker = null!;
    private MultiMeshInstance3D _pathMarkers = null!;
    private Label _turnLabel = null!;
    private Label _hexLabel = null!;
    private Label _statusLabel = null!;
    private Label _controlsLabel = null!;
    private Control _debugHudRoot = null!;
    private Fo1ClassicHud? _classicHud;
    private Control _targetReticle = null!;
    private Label _targetReticleLabel = null!;
    private Control _fpsCrosshair = null!;
    private Camera3D? _camera;
    private string _status = "Select a highlighted floor hex to move";
    private PlayerProfile _playerProfile;
    private Fo1CharacterProfile? _characterProfile;
    private IReadOnlyList<Fo1Mob> _mobs = [];
    private readonly Dictionary<int, Fo1Mob> _mobsByTile = [];
    private Fo1Mob? _selectedMob;
    private int _playerHitPoints;
    private int _attacks;
    private int _kills;
    private bool _gridVisible;
    private bool _sourceOverlayVisible;
    private bool _blockout3dVisible;
    private bool _worldGuidesVisible = true;
    private int _ratActivationDistanceHexes;
    private int _lastRatActors;
    private bool _firstPersonModeActive;
    private bool _firstPersonMoving;
    private int _fpsShots;
    private int _fpsHits;
    private int _fpsKills;
    private double _fpsShotCooldownSeconds;
    private double _fpsMeleeCooldownSeconds;
    private int _rangedAttacks;
    private int _rangedHits;
    private int _meleeAttacks;
    private int _meleeHits;
    private int _reloads;
    private int _combatSequence;
    private int _magazineRounds;
    private int _reserveRounds;
    private bool _tagInventoryApplied;
    private readonly Dictionary<string, int> _inventoryObjects = new(StringComparer.Ordinal);
    private Fo1PipBoy2000? _pipBoy;
    private Fo1CombatPresentation? _combatPresentation;
    private Fo1RuntimeProfile _runtimeProfile = null!;

    internal int PlayerTile => _playerTile;
    internal int DoorTile => _doorTile;
    internal int HoveredTile => _hoveredTile;
    internal int ActionPoints => _actionPoints;
    internal int Turn => _turn;
    internal Node3D PlayerToken => _playerToken;
    internal Sprite3D PlayerSourceSprite => _playerSourceSprite;
    internal ActorModelSlice.LoadedActor? OwnedPlayer => _ownedPlayer;
    internal Fo1ThirdPersonWeapon.LoadedWeapon? OwnedPlayerWeapon => _ownedPlayerWeapon;
    internal Fo1ThirdPersonWeapon.LoadedWeapon? OwnedPlayerMeleeWeapon => _ownedPlayerMeleeWeapon;
    internal CanvasLayer Hud { get; private set; } = null!;
    internal bool CanWalk(int tile) => tile >= 0 && tile < _walkable.Length && _walkable[tile];
    internal IReadOnlyList<Fo1Mob> Mobs => _mobs;
    internal Fo1Mob? SelectedMob => _selectedMob;
    internal int PlayerHitPoints => _playerHitPoints;
    internal int Attacks => _attacks;
    internal int Kills => _kills;
    internal int WeaponActionPointCost => _playerProfile.WeaponActionPointCost;
    internal int MeleeActionPointCost => _playerProfile.MeleeWeapon.ActionPointCost;
    internal string EquippedWeaponSymbol => _meleeWeaponEquipped
        ? _playerProfile.Inventory.EquippedMeleeSymbol
        : _playerProfile.Inventory.EquippedRangedSymbol;
    internal string EquippedWeaponName => _meleeWeaponEquipped
        ? _playerProfile.MeleeWeapon.Name
        : _playerProfile.RangedWeapon.Name;
    internal int EquippedWeaponActionPointCost => _meleeWeaponEquipped
        ? _playerProfile.MeleeWeapon.ActionPointCost
        : _playerProfile.RangedWeapon.ActionPointCost;
    internal int HudWeaponArtSwitches => _classicHud?.WeaponArtSwitches ?? 0;
    internal int MagazineRounds => _magazineRounds;
    internal int ReserveRounds => _reserveRounds;
    internal int RangedAttacks => _rangedAttacks;
    internal int RangedHits => _rangedHits;
    internal int MeleeAttacks => _meleeAttacks;
    internal int MeleeHits => _meleeHits;
    internal int Reloads => _reloads;
    internal double FirstPersonMeleeCooldownSeconds => _fpsMeleeCooldownSeconds;
    internal string Status => _status;
    internal float FirstPersonMaximumRangeMeters => MathF.Max(
        _runtimeProfile.Gameplay.FirstPersonMinimumRangeMeters,
        _playerProfile.RangedWeapon.RangeHexes *
            _runtimeProfile.Gameplay.FirstPersonMetersPerWeaponRangeHex);
    internal int QueuedMovementSteps => _movement.Count;
    internal int PlayerMoveAnimationPlaybacks => _playerMoveAnimationPlaybacks;
    internal Fo1CharacterProfile? CharacterProfile => _characterProfile;
    internal bool GridVisible => _gridVisible;
    internal int AlertedMobs => _mobs.Count(mob => mob.Alive && mob.Alerted);
    internal int LastRatActors => _lastRatActors;
    internal int RatActivationDistanceHexes => _ratActivationDistanceHexes;
    internal int VisibleHostileMarkers => _mobs.Count(mob => mob.HostileMarkerVisible);
    internal int VisibleHostileBeacons => _mobs.Count(mob => mob.HostileBeaconVisible);
    internal int VisibleHostileLabels => _mobs.Count(mob => mob.HostileLabelVisible);
    internal float PlayerHexCenterErrorMeters =>
        _playerToken.Position.DistanceTo(
            Fo1HexMath.Center(_playerTile) +
            Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters);
    internal bool FirstPersonModeActive => _firstPersonModeActive;
    internal int FpsShots => _fpsShots;
    internal int FpsHits => _fpsHits;
    internal int FpsKills => _fpsKills;
    internal Fo1PipBoy2000? PipBoy => _pipBoy;
    internal bool ClassicInterfaceAttached => _classicHud is not null;
    internal Fo1CombatPresentation? CombatPresentation => _combatPresentation;

    internal void Configure(
        string sceneSha256,
        bool[] walkable,
        int[] floorIds,
        IReadOnlyDictionary<int, string> floorNames,
        int entryTile,
        int doorTile,
        int actionPoints,
        int ratActivationDistanceHexes,
        PlayerProfile playerProfile,
        IReadOnlyList<Fo1Mob> mobs,
        string? savePath,
        Fo1RuntimeProfile runtimeProfile)
    {
        if (walkable.Length != Fo1HexMath.Width * Fo1HexMath.Height ||
            floorIds.Length != Fo1HexMath.FloorWidth * Fo1HexMath.FloorHeight)
            throw new ArgumentException("Fallout tactical session received an invalid grid.");
        if (!walkable[entryTile])
            throw new InvalidOperationException($"V13ENT entry tile is not provisionally walkable: {entryTile}");
        if (ratActivationDistanceHexes is < 1 or > 25)
            throw new InvalidOperationException(
                $"Fallout rat activation distance is invalid: {ratActivationDistanceHexes}");
        _sceneSha256 = sceneSha256;
        _runtimeProfile = runtimeProfile;
        _walkable = walkable;
        _floorIds = floorIds;
        _floorNames = floorNames;
        _playerTile = entryTile;
        _entryTile = entryTile;
        _doorTile = doorTile;
        _maximumActionPoints = actionPoints;
        _actionPoints = actionPoints;
        _ratActivationDistanceHexes = ratActivationDistanceHexes;
        _playerProfile = playerProfile;
        playerProfile.Validate();
        _playerHitPoints = playerProfile.HitPoints;
        _magazineRounds = playerProfile.RangedWeapon.InitialLoadedRounds;
        foreach (var row in playerProfile.Inventory.Base)
            AddInventoryObjects(row.Symbol, row.Objects);
        _reserveRounds = InventoryObjects(playerProfile.Inventory.AmmunitionSymbol) *
            playerProfile.Inventory.AmmunitionRoundsPerObject;
        _mobs = mobs;
        foreach (var mob in mobs.Where(mob => mob.Alive))
            _mobsByTile.Add(mob.Tile, mob);
        _savePath = ResolvePath(savePath ?? "user://saves/fo1-v13ent-hex-v1.json");
        Name = "Fo1TacticalSession";
        Load();
        BuildWorldMarkers();
    }

    public override void _Ready()
    {
        BuildHud();
        RefreshHud();
    }

    public override void _Process(double delta)
    {
        _fpsShotCooldownSeconds = Math.Max(0.0, _fpsShotCooldownSeconds - delta);
        _fpsMeleeCooldownSeconds = Math.Max(0.0, _fpsMeleeCooldownSeconds - delta);
        RefreshMobReadability();
        RefreshTargetReticle();
        if (_firstPersonModeActive)
            return;
        if (_movement.Count == 0)
            return;
        PlayPlayerAnimation(_playerMoveAnimation);
        var targetTile = _movement.Peek();
        var target = Fo1HexMath.Center(targetTile) +
            Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters;
        if (_playerToken.Position.DistanceSquaredTo(target) > 0.0001f)
            _playerToken.LookAt(target, Vector3.Up);
        _playerToken.Position = _playerToken.Position.MoveToward(
            target,
            (float)delta * _runtimeProfile.Gameplay.TacticalMoveSpeedMetersPerSecond);
        if (_playerToken.Position.DistanceTo(target) >
            _runtimeProfile.Gameplay.TacticalArrivalToleranceMeters)
            return;
        _playerToken.Position = target;
        _movement.Dequeue();
        _playerTile = targetTile;
        if (_movement.Count == 0)
            PlayPlayerAnimation(_playerIdleAnimation);
        _actionPoints = Math.Max(
            0,
            _actionPoints - _runtimeProfile.Gameplay.TacticalMoveActionPointCost);
        _status = _movement.Count == 0
            ? $"Arrived at hex {_playerTile}"
            : $"Moving: {_movement.Count} step(s) queued";
        RefreshPathMarkers();
        RefreshHud();
        Save();
    }

    internal void SetHoveredTile(int tile)
    {
        if (_hoveredTile == tile)
            return;
        _hoveredTile = tile;
        _hoverMarker.Visible = _worldGuidesVisible && tile >= 0;
        if (tile >= 0)
        {
            _hoverMarker.Position = Fo1HexMath.Center(tile) + Vector3.Up * 0.055f;
            var material = _hoverMarker.MaterialOverride as StandardMaterial3D;
            if (material is not null)
                material.AlbedoColor = _walkable[tile]
                    ? new Color(0.35f, 1.0f, 0.28f, 0.85f)
                    : new Color(1.0f, 0.25f, 0.18f, 0.85f);
        }
        RefreshHud();
    }

    internal void SelectTile(int tile)
    {
        if (tile < 0 || tile >= _walkable.Length)
            return;
        _selectedTile = tile;
        _movement.Clear();
        if (!_walkable[tile])
        {
            _status = $"Hex {tile} has no non-default floor art; blocked in this proof";
            RefreshPathMarkers();
            RefreshHud();
            return;
        }
        var path = FindPath(_playerTile, tile);
        if (path.Count == 0 && tile != _playerTile)
        {
            _status = $"No provisional floor path to hex {tile}";
            RefreshPathMarkers();
            RefreshHud();
            return;
        }
        var allowed = Math.Min(_actionPoints, path.Count);
        if (path.Count == 0 && tile == _playerTile)
            SnapPlayerToHexCenter();
        foreach (var step in path.Take(allowed))
            _movement.Enqueue(step);
        _status = path.Count == 0
            ? $"Already at hex {tile}"
            : allowed == 0
                ? "No AP remaining; press Space to end turn"
                : allowed < path.Count
                    ? $"Path is {path.Count} hexes; moving {allowed} with remaining AP"
                    : $"Moving {allowed} hex(es) at 1 AP each";
        RefreshPathMarkers();
        RefreshHud();
    }

    internal void ActivateTile(int tile, bool attackRequested)
    {
        if (_mobsByTile.TryGetValue(tile, out var mob) && mob.Alive)
        {
            SelectMob(mob);
            if (attackRequested)
                AttackSelected();
            return;
        }
        SelectTile(tile);
    }

    internal CombatResult AttackSelected() => _meleeWeaponEquipped
        ? AttackSelectedMelee()
        : AttackSelectedRanged();

    internal CombatResult AttackSelectedRanged()
    {
        SetEquippedWeapon(melee: false);
        var target = _selectedMob;
        if (target is null || !target.Alive)
            return RejectCombat("ranged", "tactical", "Select a living target first");
        var weapon = _playerProfile.RangedWeapon;
        var distance = Fo1HexMath.Distance(_playerTile, target.Tile);
        if (distance > weapon.RangeHexes)
            return RejectCombat(
                "ranged",
                "tactical",
                $"{target.DisplayName} is {distance} hexes away; range is {weapon.RangeHexes}");
        if (_actionPoints < _playerProfile.WeaponActionPointCost)
            return RejectCombat(
                "ranged",
                "tactical",
                $"Need {_playerProfile.WeaponActionPointCost} AP to fire {weapon.Name}");
        if (_magazineRounds < weapon.RoundsPerAttack)
        {
            _combatPresentation?.PresentDryFire(_playerToken.GlobalPosition);
            return RejectCombat(
                "ranged",
                "tactical",
                $"{weapon.Name} is empty • press R to reload ({_reserveRounds} reserve)");
        }

        _playerToken.LookAt(Fo1HexMath.Center(target.Tile), Vector3.Up);
        target.Alert();
        _actionPoints -= _playerProfile.WeaponActionPointCost;
        _magazineRounds -= weapon.RoundsPerAttack;
        PlayPlayerCombatAnimation(_playerRangedAttackAnimation);
        _attacks++;
        _rangedAttacks++;
        _combatSequence++;
        var chance = TacticalHitChance(weapon, target, distance);
        var roll = DeterministicPercent("tactical-ranged", target.Serial);
        if (roll > chance)
        {
            PresentTacticalRanged(target, hit: false);
            _status = $"{weapon.Name} missed {target.DisplayName} • {roll}% roll / {chance}% chance";
            RefreshHud();
            Save();
            return new CombatResult(true, "ranged", "tactical", false, 0, false, chance, roll);
        }

        var damage = RollDamage(weapon, target.Serial, melee: false);
        var applied = ApplyDamage(target, damage, firstPerson: false);
        _rangedHits++;
        PresentTacticalRanged(target, hit: true);
        _status = target.Alive
            ? $"{weapon.Name} hit {target.DisplayName} for {applied} • " +
                $"{target.HitPoints}/{target.MaximumHitPoints} HP"
            : $"{weapon.Name} hit {target.DisplayName} for {applied} • killed";
        RefreshHud();
        Save();
        return new CombatResult(true, "ranged", "tactical", true, applied, !target.Alive, chance, roll);
    }

    internal CombatResult AttackSelectedMelee()
    {
        SetEquippedWeapon(melee: true);
        var target = _selectedMob;
        if (target is null || !target.Alive)
            return RejectCombat("melee", "tactical", "Select a living target first");
        var weapon = _playerProfile.MeleeWeapon;
        var distance = Fo1HexMath.Distance(_playerTile, target.Tile);
        if (distance > weapon.RangeHexes)
            return RejectCombat(
                "melee",
                "tactical",
                $"Move next to {target.DisplayName}; {weapon.Name} range is {weapon.RangeHexes} hex");
        if (_actionPoints < weapon.ActionPointCost)
            return RejectCombat(
                "melee",
                "tactical",
                $"Need {weapon.ActionPointCost} AP to use {weapon.Name}");

        _playerToken.LookAt(Fo1HexMath.Center(target.Tile), Vector3.Up);
        target.Alert();
        _actionPoints -= weapon.ActionPointCost;
        PlayPlayerCombatAnimation(_playerMeleeAttackAnimation);
        _attacks++;
        _meleeAttacks++;
        _combatSequence++;
        var chance = TacticalHitChance(weapon, target, distance);
        var roll = DeterministicPercent("tactical-melee", target.Serial);
        if (roll > chance)
        {
            PresentTacticalMelee(target, hit: false);
            _status = $"{weapon.Name} missed {target.DisplayName} • {roll}% roll / {chance}% chance";
            RefreshHud();
            Save();
            return new CombatResult(true, "melee", "tactical", false, 0, false, chance, roll);
        }

        var damage = RollDamage(weapon, target.Serial, melee: true);
        var applied = ApplyDamage(target, damage, firstPerson: false);
        _meleeHits++;
        PresentTacticalMelee(target, hit: true);
        _status = target.Alive
            ? $"{weapon.Name} struck {target.DisplayName} for {applied} • " +
                $"{target.HitPoints}/{target.MaximumHitPoints} HP"
            : $"{weapon.Name} struck {target.DisplayName} for {applied} • killed";
        RefreshHud();
        Save();
        return new CombatResult(true, "melee", "tactical", true, applied, !target.Alive, chance, roll);
    }

    internal bool Reload()
    {
        var weapon = _playerProfile.RangedWeapon;
        if (_magazineRounds >= weapon.AmmunitionCapacity)
        {
            _status = $"{weapon.Name} magazine already full • {_magazineRounds}/{weapon.AmmunitionCapacity}";
            RefreshHud();
            return false;
        }
        if (_reserveRounds <= 0)
        {
            _status = $"No {weapon.Name} ammunition remains";
            RefreshHud();
            return false;
        }
        if (!_firstPersonModeActive && _actionPoints < _runtimeProfile.Gameplay.ReloadActionPointCost)
        {
            _status = $"Need {_runtimeProfile.Gameplay.ReloadActionPointCost} AP to reload {weapon.Name}";
            RefreshHud();
            return false;
        }
        var loaded = Math.Min(weapon.AmmunitionCapacity - _magazineRounds, _reserveRounds);
        _magazineRounds += loaded;
        _reserveRounds -= loaded;
        if (!_firstPersonModeActive)
            _actionPoints -= _runtimeProfile.Gameplay.ReloadActionPointCost;
        SetEquippedWeapon(melee: false);
        PlayPlayerCombatAnimation(_playerReloadAnimation);
        _reloads++;
        _combatPresentation?.PresentReload(_playerToken.GlobalPosition);
        _status = $"Reloaded {weapon.Name} • {_magazineRounds}/{weapon.AmmunitionCapacity} + {_reserveRounds}";
        RefreshHud();
        Save();
        return true;
    }

    internal Fo1Mob? CycleTarget()
    {
        var living = _mobs.Where(mob => mob.Alive)
            .OrderBy(mob => Fo1HexMath.Distance(_playerTile, mob.Tile))
            .ThenBy(mob => mob.Serial)
            .ToArray();
        if (living.Length == 0)
        {
            _status = "No living hostile targets remain";
            RefreshHud();
            return null;
        }
        var current = _selectedMob is null ? -1 : Array.IndexOf(living, _selectedMob);
        var target = living[(current + 1) % living.Length];
        SelectMob(target);
        return target;
    }

    internal void ToggleGrid()
    {
        var grid = GetTree().CurrentScene.FindChild("V13ENT_200X200_HEX_GRID", true, false) as GeometryInstance3D;
        if (grid is null)
            throw new InvalidOperationException("Fallout tactical hex overlay is missing.");
        _gridVisible = !_gridVisible;
        grid.Visible = _gridVisible;
        _status = $"Hex grid {(_gridVisible ? "shown" : "hidden")} (G toggles)";
        RefreshHud();
    }

    internal void ToggleSourceOverlay()
    {
        if (_firstPersonModeActive)
        {
            ApplySourceOverlayVisibility(false);
            _status = "Source 2.5D reference is tactical-only; FPS remains fully 3D";
            RefreshHud();
            return;
        }
        _sourceOverlayVisible = !_sourceOverlayVisible;
        ApplySourceOverlayVisibility(_sourceOverlayVisible);
        _status = $"Source floor/scenery reference {(_sourceOverlayVisible ? "shown" : "hidden")} (V toggles)";
        RefreshHud();
    }

    private void ApplySourceOverlayVisibility(bool visible)
    {
        var overlay = GetTree().CurrentScene.FindChild(
            "FO1_SOURCE_STATIC_SPRITE_OVERLAY",
            true,
            false) as Node3D;
        if (overlay is null)
            throw new InvalidOperationException("Fallout source-sprite reference overlay is missing.");
        overlay.Visible = visible;
        if (GetTree().CurrentScene.FindChild(
                "FO1_OWNED_CONTINUOUS_CAVE_FLOOR",
                true,
                false) is GeometryInstance3D ownedFloor)
            ownedFloor.Visible = !visible;
        _playerSourceSprite.Visible = visible || _ownedPlayer is null;
        foreach (var name in new[] { "ExactV13Secr3Frame", "VaultDoorHexIdentity" })
        {
            if (GetTree().CurrentScene.FindChild(name, true, false) is GeometryInstance3D geometry)
                geometry.Visible = visible;
        }
    }

    internal void Toggle3DBlockout()
    {
        _blockout3dVisible = !_blockout3dVisible;
        foreach (var name in new[]
                 {
                     "V13ENT_FIXED_3D_CAVE_GEOMETRY",
                     "V13ENT_3D_WALL_BLOCKERS",
                     "V13ENT_3D_ROCK_BLOCKERS",
                 })
        {
            if (GetTree().CurrentScene.FindChild(name, true, false) is GeometryInstance3D geometry)
                geometry.Visible = _blockout3dVisible;
        }
        _status = $"Experimental 3D topology blockout {(_blockout3dVisible ? "shown" : "hidden")} (B toggles)";
        RefreshHud();
    }

    internal void EndTurn()
    {
        _movement.Clear();
        PlayPlayerAnimation(_playerIdleAnimation);
        RunRatTurn();
        _turn++;
        _actionPoints = _maximumActionPoints;
        _status = _playerHitPoints <= 0
            ? "Vault Dweller is down — combat proof failed"
            : $"Turn {_turn}: {_lastRatActors} locally active rat(s) acted, player AP restored";
        RefreshPathMarkers();
        RefreshHud();
        Save();
    }

    internal void SaveAndNotify()
    {
        Save();
        _status = $"Saved at hex {_playerTile}";
        RefreshHud();
    }

    internal void SetCameraStatus(string status)
    {
        _status = status;
        RefreshHud();
    }

    internal void ApplyCharacter(Fo1CharacterProfile profile)
    {
        profile.Validate();
        if (_characterProfile is not null)
        {
            if (!SameCharacter(_characterProfile, profile))
                throw new InvalidOperationException(
                    "Fallout save already belongs to a different created character.");
            _status = $"Resumed {_characterProfile.Name} with the saved combat inventory";
            RefreshHud();
            return;
        }
        if (_turn != 1 || _attacks != 0 || _kills != 0 || _playerTile != _entryTile)
            throw new InvalidOperationException(
                "Fallout character profile can only be applied to a fresh V13ENT session.");
        ApplyCharacterStats(profile);
        ApplyTagInventory(profile);
        _actionPoints = profile.ActionPoints;
        _playerHitPoints = profile.HitPoints;
        _status = $"{profile.Name} left Vault 13 • selected SPECIAL now drives live combat";
        RefreshHud();
        Save();
    }

    private void ApplyCharacterStats(Fo1CharacterProfile profile)
    {
        _characterProfile = profile;
        _playerProfile = _playerProfile with
        {
            Name = profile.Name,
            HitPoints = profile.HitPoints,
            ArmorClass = profile.ArmorClass,
            Sequence = profile.Sequence,
            Strength = profile.EffectiveStrength,
            Perception = profile.EffectivePerception,
            MeleeDamage = profile.MeleeDamage,
            Skills = profile.Skills(),
            RangedWeapon = _playerProfile.RangedWeapon with
            {
                ActionPointCost = Math.Max(
                    1,
                    _playerProfile.RangedWeapon.ActionPointCost +
                    profile.WeaponActionPointAdjustment),
            },
        };
        _maximumActionPoints = profile.ActionPoints;
    }

    private void ApplyTagInventory(Fo1CharacterProfile profile)
    {
        if (!_tagInventoryApplied)
        {
            foreach (var bonus in _playerProfile.Inventory.TagBonuses.Where(
                         bonus => profile.TaggedSkills.Contains(bonus.Skill, StringComparer.Ordinal)))
            {
                foreach (var row in bonus.Items)
                {
                    AddInventoryObjects(row.Symbol, row.Objects);
                    if (row.Symbol == _playerProfile.Inventory.AmmunitionSymbol)
                        _reserveRounds += row.Objects *
                            _playerProfile.Inventory.AmmunitionRoundsPerObject;
                }
            }
            _tagInventoryApplied = true;
        }
    }

    private static bool SameCharacter(Fo1CharacterProfile first, Fo1CharacterProfile second) =>
        first.Name == second.Name && first.Age == second.Age && first.Sex == second.Sex &&
        first.Strength == second.Strength && first.Perception == second.Perception &&
        first.Endurance == second.Endurance && first.Charisma == second.Charisma &&
        first.Intelligence == second.Intelligence && first.Agility == second.Agility &&
        first.Luck == second.Luck &&
        first.TaggedSkills.SequenceEqual(second.TaggedSkills, StringComparer.Ordinal) &&
        first.Traits.SequenceEqual(second.Traits, StringComparer.Ordinal);

    internal void SetWorldGuidesVisible(bool visible)
    {
        _worldGuidesVisible = visible;
        _hoverMarker.Visible = visible && _hoveredTile >= 0;
        _pathMarkers.Visible = visible;
    }

    internal void SnapPlayerToHexCenter()
    {
        _movement.Clear();
        _playerToken.Position = Fo1HexMath.Center(_playerTile) +
            Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters;
        PlayPlayerAnimation(_playerIdleAnimation);
        RefreshPathMarkers();
    }

    internal void SetFirstPersonModeActive(bool active)
    {
        if (_firstPersonModeActive == active)
            return;
        _movement.Clear();
        _firstPersonModeActive = active;
        _firstPersonMoving = false;
        ApplySourceOverlayVisibility(active ? false : _sourceOverlayVisible);
        if (!active)
        {
            var nearest = Fo1HexMath.NearestTile(_playerToken.Position);
            if (CanWalk(nearest))
                _playerTile = nearest;
            SnapPlayerToHexCenter();
            Save();
        }
        else
        {
            SetHoveredTile(-1);
            PlayPlayerAnimation(_playerIdleAnimation);
        }
        if (_fpsCrosshair is not null)
            _fpsCrosshair.Visible = active;
        if (_controlsLabel is not null)
            _controlsLabel.Text = ControlsText();
        _status = active
            ? "FPS MODE • continuous WASD + mouse look • LMB ranged • RMB melee • R reload"
            : $"TACTICAL MODE • snapped to authoritative hex center {_playerTile}";
        RefreshHud();
    }

    internal bool TryMoveFirstPerson(Vector3 direction, float distanceMeters)
    {
        if (!_firstPersonModeActive || distanceMeters <= 0.0f)
            return false;
        direction.Y = 0.0f;
        if (direction.LengthSquared() <= 0.0001f)
        {
            SetFirstPersonMoving(false);
            return false;
        }
        direction = direction.Normalized();
        var delta = direction * MathF.Min(
            distanceMeters,
            _runtimeProfile.Gameplay.FirstPersonMaximumSubstepMeters);
        var current = _playerToken.Position;
        var candidates = new[]
        {
            current + delta,
            current + new Vector3(delta.X, 0.0f, 0.0f),
            current + new Vector3(0.0f, 0.0f, delta.Z),
        };
        foreach (var candidateSource in candidates)
        {
            var candidate = new Vector3(
                candidateSource.X,
                _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters,
                candidateSource.Z);
            var tile = Fo1HexMath.NearestTile(candidate);
            if (!CanWalk(tile))
                continue;
            _playerToken.LookAt(candidate + direction, Vector3.Up);
            _playerToken.Position = candidate;
            _playerTile = tile;
            SetFirstPersonMoving(true);
            return true;
        }
        SetFirstPersonMoving(false);
        return false;
    }

    internal void SetFirstPersonMoving(bool moving)
    {
        if (_firstPersonMoving == moving)
            return;
        _firstPersonMoving = moving;
        PlayPlayerAnimation(moving ? _playerMoveAnimation : _playerIdleAnimation);
    }

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
        (int)(DeterministicUInt(purpose, targetSerial) % 100U) + 1;

    private uint DeterministicUInt(string purpose, int targetSerial)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"{_sceneSha256}|{_turn}|{_combatSequence}|{purpose}|{_playerTile}|{targetSerial}");
        var hash = SHA256.HashData(payload);
        return (uint)(hash[0] << 24 | hash[1] << 16 | hash[2] << 8 | hash[3]);
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

    internal void SetCinematicPlayerAnimation(bool active, bool moving)
    {
        if (_playerAnimationPlayer is null)
            return;
        _playerAnimationPlayer.ProcessMode = active
            ? Node.ProcessModeEnum.Always
            : Node.ProcessModeEnum.Inherit;
        PlayPlayerAnimation(moving ? _playerMoveAnimation : _playerIdleAnimation);
    }

    internal void AttachCamera(Camera3D camera)
    {
        _camera = camera;
        RefreshTargetReticle();
    }

    internal Fo1PipBoy2000 AttachPipBoy(
        Fo1CharacterStartContract contract,
        Fo1CharacterProfile profile)
    {
        if (_pipBoy is not null)
            throw new InvalidOperationException("Fallout tactical session already has a Pip-Boy 2000.");
        var pipBoy = new Fo1PipBoy2000();
        pipBoy.Configure(contract, this, profile);
        AddChild(pipBoy);
        _pipBoy = pipBoy;
        return pipBoy;
    }

    internal Fo1CombatPresentation AttachCombatPresentation(JsonElement source)
    {
        if (_combatPresentation is not null)
            throw new InvalidOperationException(
                "Fallout tactical session already has a combat presentation.");
        var presentation = new Fo1CombatPresentation();
        presentation.Configure(source, _runtimeProfile.CombatPresentation);
        AddChild(presentation);
        _combatPresentation = presentation;
        return presentation;
    }

    internal void AttachClassicInterface(Fo1CharacterStartContract contract)
    {
        if (_classicHud is not null)
            throw new InvalidOperationException(
                "Fallout tactical session already has its classic gameplay interface.");
        if (_pipBoy is null)
            throw new InvalidOperationException(
                "Fallout classic interface requires the player's Pip-Boy 2000 to be attached first.");
        var classicHud = new Fo1ClassicHud();
        classicHud.Configure(contract.InterfaceHud, TogglePipBoy, SwapEquippedWeapon);
        Hud.AddChild(classicHud);
        _classicHud = classicHud;
        _debugHudRoot.Visible = false;
        RefreshHud();
    }

    internal void TogglePipBoy()
    {
        if (_pipBoy is null)
        {
            _status = "Pip-Boy 2000 becomes available after character selection";
            RefreshHud();
            return;
        }
        _pipBoy.Toggle();
    }

    internal ActorModelSlice.LoadedActor AttachOwnedPlayer(
        string modelPath,
        string sidecarPath)
    {
        if (_ownedPlayer is not null)
            throw new InvalidOperationException("Fallout tactical player already has an owned 3D presentation.");
        var actor = ActorModelSlice.Load(modelPath, sidecarPath, _playerToken);
        actor.Root.Name = "OwnedVaultDweller";
        var groundDelta = _playerToken.GlobalPosition.Y - actor.Bounds.Position.Y;
        actor.Root.Position += Vector3.Up * groundDelta;
        _playerToken.LookAt(Fo1HexMath.Center(_doorTile), Vector3.Up);
        var grounded = actor with
        {
            Bounds = new Aabb(
                actor.Bounds.Position + Vector3.Up * groundDelta,
                actor.Bounds.Size),
        };
        _ownedPlayer = grounded;
        _playerAnimationPlayer = grounded.AnimationPlayer;
        _playerIdleAnimation = grounded.PlayingAnimation;
        _playerMoveAnimation = grounded.AnimationPlayer.GetAnimationList()
            .Select(name => name.ToString())
            .FirstOrDefault(name =>
                name != "RESET" &&
                name != _playerIdleAnimation &&
                name.Contains("forward", StringComparison.OrdinalIgnoreCase))
            ?? grounded.AnimationPlayer.GetAnimationList()
                .Select(name => name.ToString())
                .FirstOrDefault(name => name != "RESET" && name != _playerIdleAnimation)
            ?? "";
        var animationNames = grounded.AnimationPlayer.GetAnimationList()
            .Select(name => name.ToString())
            .ToArray();
        _playerRangedAttackAnimation = animationNames.FirstOrDefault(
            name => name.Equals("AttackRight", StringComparison.OrdinalIgnoreCase)) ?? "";
        _playerMeleeAttackAnimation = animationNames.FirstOrDefault(
            name => name.Contains("AttackRight_A", StringComparison.OrdinalIgnoreCase)) ?? "";
        _playerReloadAnimation = animationNames.FirstOrDefault(
            name => name.Contains("ReloadA", StringComparison.OrdinalIgnoreCase)) ?? "";
        _playerSourceSprite.Visible = false;
        return grounded;
    }

    internal Fo1ThirdPersonWeapon.LoadedWeapon AttachOwnedPlayerWeapon(JsonElement source)
    {
        if (_ownedPlayer is null)
            throw new InvalidOperationException(
                "Fallout third-person weapon requires the owned 3D player first.");
        if (_ownedPlayerWeapon is not null)
            throw new InvalidOperationException(
                "Fallout tactical player already has an owned third-person weapon.");
        _ownedPlayerWeapon = Fo1ThirdPersonWeapon.Attach(source, _ownedPlayer.Value);
        ApplyEquippedWeaponVisibility();
        return _ownedPlayerWeapon.Value;
    }

    internal Fo1ThirdPersonWeapon.LoadedWeapon AttachOwnedPlayerMeleeWeapon(JsonElement source)
    {
        if (_ownedPlayer is null)
            throw new InvalidOperationException(
                "Fallout third-person melee weapon requires the owned 3D player first.");
        if (_ownedPlayerMeleeWeapon is not null)
            throw new InvalidOperationException(
                "Fallout tactical player already has an owned third-person melee weapon.");
        _ownedPlayerMeleeWeapon = Fo1ThirdPersonWeapon.Attach(source, _ownedPlayer.Value);
        ApplyEquippedWeaponVisibility();
        return _ownedPlayerMeleeWeapon.Value;
    }

    internal void SwapEquippedWeapon()
    {
        SetEquippedWeapon(!_meleeWeaponEquipped);
        _status = $"Equipped {EquippedWeaponName} • {EquippedWeaponActionPointCost} AP";
        RefreshHud();
        Save();
    }

    private void SetEquippedWeapon(bool melee)
    {
        var symbol = melee
            ? _playerProfile.Inventory.EquippedMeleeSymbol
            : _playerProfile.Inventory.EquippedRangedSymbol;
        if (InventoryObjects(symbol) <= 0)
            throw new InvalidOperationException(
                $"Fallout cannot equip an inventory item that is not present: {symbol}.");
        _meleeWeaponEquipped = melee;
        ApplyEquippedWeaponVisibility();
    }

    private void ApplyEquippedWeaponVisibility()
    {
        if (_ownedPlayerWeapon is not null)
            _ownedPlayerWeapon.Value.Root.Visible = !_meleeWeaponEquipped;
        if (_ownedPlayerMeleeWeapon is not null)
            _ownedPlayerMeleeWeapon.Value.Root.Visible = _meleeWeaponEquipped;
    }

    private void PlayPlayerCombatAnimation(string name)
    {
        if (_playerAnimationPlayer is null || string.IsNullOrEmpty(name) ||
            !_playerAnimationPlayer.HasAnimation(name))
            return;
        _playerAnimationPlayer.Play(name);
        _playerAnimationPlayer.Seek(0.0, true);
    }

    private void PlayPlayerAnimation(string name)
    {
        if (_playerAnimationPlayer is null || string.IsNullOrEmpty(name) ||
            _playerAnimationPlayer.CurrentAnimation.ToString() == name)
            return;
        _playerAnimationPlayer.Play(name);
        if (name == _playerMoveAnimation)
            _playerMoveAnimationPlaybacks++;
    }

    internal object Report() => new
    {
        schema = SaveSchema,
        sceneSha256 = _sceneSha256,
        playerTile = _playerTile,
        playerHex = new[] { _playerTile % 200, _playerTile / 200 },
        doorTile = _doorTile,
        turn = _turn,
        actionPoints = _actionPoints,
        maximumActionPoints = _maximumActionPoints,
        movementCostPerHex = 1,
        playerHexCenterErrorMeters = PlayerHexCenterErrorMeters,
        firstPersonMode = new
        {
            active = _firstPersonModeActive,
            locomotion = "continuous-source-walk-mask-constrained",
            tacticalActionPointsConsumed = false,
            shots = _fpsShots,
            hits = _fpsHits,
            kills = _fpsKills,
        },
        queuedSteps = _movement.Count,
        playerHitPoints = _playerHitPoints,
        playerMaximumHitPoints = _playerProfile.HitPoints,
        playerArmorClass = _playerProfile.ArmorClass,
        playerSequence = _playerProfile.Sequence,
        playerName = _playerProfile.Name,
        combat = new
        {
            equippedWeapon = new
            {
                symbol = EquippedWeaponSymbol,
                name = EquippedWeaponName,
                actionPointCost = EquippedWeaponActionPointCost,
            },
            rangedWeapon = new
            {
                name = _playerProfile.RangedWeapon.Name,
                pid = _playerProfile.RangedWeapon.Pid,
                prototypeSha256 = _playerProfile.RangedWeapon.PrototypeSha256,
                minimumDamage = _playerProfile.RangedWeapon.MinimumDamage,
                maximumDamage = _playerProfile.RangedWeapon.MaximumDamage,
                rangeHexes = _playerProfile.RangedWeapon.RangeHexes,
                actionPointCost = _playerProfile.RangedWeapon.ActionPointCost,
                magazineRounds = _magazineRounds,
                magazineCapacity = _playerProfile.RangedWeapon.AmmunitionCapacity,
                reserveRounds = _reserveRounds,
                attempts = _rangedAttacks,
                hits = _rangedHits,
            },
            meleeWeapon = new
            {
                name = _playerProfile.MeleeWeapon.Name,
                pid = _playerProfile.MeleeWeapon.Pid,
                prototypeSha256 = _playerProfile.MeleeWeapon.PrototypeSha256,
                minimumDamage = _playerProfile.MeleeWeapon.MinimumDamage,
                maximumDamage = _playerProfile.MeleeWeapon.MaximumDamage,
                characterMeleeDamage = _playerProfile.MeleeDamage,
                rangeHexes = _playerProfile.MeleeWeapon.RangeHexes,
                actionPointCost = _playerProfile.MeleeWeapon.ActionPointCost,
                attempts = _meleeAttacks,
                hits = _meleeHits,
            },
            reloads = _reloads,
            inventoryObjects = _inventoryObjects.OrderBy(row => row.Key)
                .ToDictionary(row => row.Key, row => row.Value),
            tagInventoryApplied = _tagInventoryApplied,
        },
        attacks = _attacks,
        kills = _kills,
        mobs = _mobs.Select(mob => mob.Report()).ToArray(),
        livingMobs = _mobs.Count(mob => mob.Alive),
        alertedMobs = AlertedMobs,
        lastRatActors = _lastRatActors,
        ratActivationDistanceHexes = _ratActivationDistanceHexes,
        visibleHostileMarkers = VisibleHostileMarkers,
        visibleHostileBeacons = VisibleHostileBeacons,
        visibleHostileLabels = VisibleHostileLabels,
        provisionalWalkableHexes = _walkable.Count(value => value),
        savePath = _savePath,
        character = _characterProfile?.Report(),
        classicInterface = _classicHud?.Report(),
        pipBoy = _pipBoy?.Report(),
        combatPresentation = _combatPresentation?.Report(),
        playerPresentation = new
        {
            owned3d = _ownedPlayer is not null,
            formId = _ownedPlayer?.FormId,
            meshes = _ownedPlayer?.Meshes ?? 0,
            importedAnimations = _ownedPlayer?.Animations ?? 0,
            idleAnimation = _playerIdleAnimation,
            moveAnimation = _playerMoveAnimation,
            rangedAttackAnimation = _playerRangedAttackAnimation,
            meleeAttackAnimation = _playerMeleeAttackAnimation,
            reloadAnimation = _playerReloadAnimation,
            currentAnimation = _playerAnimationPlayer?.CurrentAnimation.ToString(),
            moveAnimationPlaybacks = _playerMoveAnimationPlaybacks,
            thirdPersonWeapon = _ownedPlayerWeapon is null
                ? null
                : new
                {
                    role = _ownedPlayerWeapon.Value.Role,
                    formId = _ownedPlayerWeapon.Value.FormId,
                    editorId = _ownedPlayerWeapon.Value.EditorId,
                    sourceSha256 = _ownedPlayerWeapon.Value.SourceSha256,
                    bone = _ownedPlayerWeapon.Value.BoneName,
                    muzzleMarker = _ownedPlayerWeapon.Value.MuzzleMarker,
                    shellMarker = _ownedPlayerWeapon.Value.ShellMarker,
                    meshes = _ownedPlayerWeapon.Value.Meshes,
                    surfaces = _ownedPlayerWeapon.Value.Surfaces,
                    materialBindings = _ownedPlayerWeapon.Value.MaterialBindings,
                    materialTextures = _ownedPlayerWeapon.Value.MaterialTextures,
                    tacticalAndThirdPersonOnly = true,
                    visible = _ownedPlayerWeapon.Value.Root.IsVisibleInTree(),
                },
            thirdPersonMeleeWeapon = _ownedPlayerMeleeWeapon is null
                ? null
                : new
                {
                    role = _ownedPlayerMeleeWeapon.Value.Role,
                    formId = _ownedPlayerMeleeWeapon.Value.FormId,
                    gameplayPid = _ownedPlayerMeleeWeapon.Value.GameplayPid,
                    editorId = _ownedPlayerMeleeWeapon.Value.EditorId,
                    sourceSha256 = _ownedPlayerMeleeWeapon.Value.SourceSha256,
                    bone = _ownedPlayerMeleeWeapon.Value.BoneName,
                    meshes = _ownedPlayerMeleeWeapon.Value.Meshes,
                    surfaces = _ownedPlayerMeleeWeapon.Value.Surfaces,
                    materialBindings = _ownedPlayerMeleeWeapon.Value.MaterialBindings,
                    materialTextures = _ownedPlayerMeleeWeapon.Value.MaterialTextures,
                    tacticalAndThirdPersonOnly = true,
                    visible = _ownedPlayerMeleeWeapon.Value.Root.IsVisibleInTree(),
                },
        },
    };

    private List<int> FindPath(int start, int target)
    {
        if (start == target)
            return [];
        var parents = Enumerable.Repeat(-2, _walkable.Length).ToArray();
        var queue = new Queue<int>();
        parents[start] = -1;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var neighbor in Fo1HexMath.Neighbors(current))
            {
                if (!_walkable[neighbor] || parents[neighbor] != -2)
                    continue;
                parents[neighbor] = current;
                if (neighbor == target)
                {
                    queue.Clear();
                    break;
                }
                queue.Enqueue(neighbor);
            }
        }
        if (parents[target] == -2)
            return [];
        var path = new List<int>();
        for (var current = target; current != start; current = parents[current])
            path.Add(current);
        path.Reverse();
        return path;
    }

    private void SelectMob(Fo1Mob mob)
    {
        _selectedMob?.SetSelected(false);
        _selectedMob = mob;
        mob.SetSelected(true);
        _selectedTile = mob.Tile;
        _status = $"TARGET {mob.DisplayName} • HP {mob.HitPoints}/{mob.MaximumHitPoints} • " +
            $"AC {mob.ArmorClass} • AP {mob.ActionPoints}/{mob.MaximumActionPoints} • " +
            $"double-click or X to attack";
        RefreshHud();
        RefreshTargetReticle();
    }

    private void RunRatTurn()
    {
        foreach (var mob in _mobs.Where(mob => mob.Alive &&
                     Fo1HexMath.Distance(mob.Tile, _playerTile) <= _ratActivationDistanceHexes))
            mob.Alert();
        var actors = _mobs.Where(mob => mob.Alive && mob.Alerted)
                     .OrderByDescending(mob => mob.Sequence)
                     .ThenBy(mob => mob.Serial)
                     .ToArray();
        _lastRatActors = actors.Length;
        foreach (var mob in actors)
        {
            mob.ResetActionPoints();
            var distance = Fo1HexMath.Distance(mob.Tile, _playerTile);
            if (distance <= _runtimeProfile.Gameplay.RatAttackRangeHexes)
            {
                RatAttack(mob);
                continue;
            }
            var original = mob.Tile;
            _walkable[original] = true;
            _mobsByTile.Remove(original);
            var path = FindPath(original, _playerTile);
            var movement = Math.Min(
                _runtimeProfile.Gameplay.RatMovementLimitHexes,
                Math.Max(0, path.Count - 1));
            movement = Math.Min(movement, mob.ActionPoints);
            var destination = movement > 0 ? path[movement - 1] : original;
            for (var index = 0; index < movement; index++)
                mob.SpendActionPoint();
            mob.MoveTo(destination);
            _walkable[destination] = false;
            _mobsByTile[destination] = mob;
            if (Fo1HexMath.Distance(destination, _playerTile) <=
                    _runtimeProfile.Gameplay.RatAttackRangeHexes &&
                mob.ActionPoints > 0)
                RatAttack(mob);
        }
    }

    private void RatAttack(Fo1Mob mob)
    {
        mob.PlayAttack();
        var damage = Math.Max(_runtimeProfile.Gameplay.MinimumDamage, mob.MeleeDamage);
        _playerHitPoints = Math.Max(0, _playerHitPoints - damage);
        mob.SpendActionPoint();
    }

    private void BuildWorldMarkers()
    {
        _playerToken = new Node3D
        {
            Name = "VaultDwellerPresentation",
            Position = Fo1HexMath.Center(_playerTile) +
                Vector3.Up * _runtimeProfile.Scene.SourceSprites.GroundAnchorMeters,
        };
        AddChild(_playerToken);
        _playerSourceSprite = new Sprite3D
        {
            Name = "VaultDwellerSourceSprite",
            Texture = _playerProfile.Texture,
            PixelSize = _playerProfile.PixelSize,
            Offset = new Vector2(
                _playerProfile.FrameOffset.X,
                -_playerProfile.FrameOffset.Y + _playerProfile.Height / 2.0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            Shaded = false,
            DoubleSided = true,
            AlphaCut = SpriteBase3D.AlphaCutMode.OpaquePrepass,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Scale = Vector3.One * 1.28f,
        };
        _playerToken.AddChild(_playerSourceSprite);
        _hoverMarker = new MeshInstance3D
        {
            Name = "HoveredFalloutHex",
            Mesh = Fo1HexVisuals.BuildRingMesh(0.78f, 0.98f),
            MaterialOverride = Fo1HexVisuals.Material(new Color(0.35f, 1.0f, 0.28f, 0.85f), true),
            Visible = false,
        };
        AddChild(_hoverMarker);
        _pathMarkers = new MultiMeshInstance3D
        {
            Name = "QueuedFalloutHexPath",
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = Fo1HexVisuals.BuildRingMesh(0.86f, 0.94f),
            },
            MaterialOverride = Fo1HexVisuals.Material(new Color(0.95f, 0.76f, 0.18f, 0.72f), true),
        };
        AddChild(_pathMarkers);
    }

    private void RefreshPathMarkers()
    {
        var steps = _movement.ToArray();
        _pathMarkers.Multimesh.InstanceCount = steps.Length;
        for (var index = 0; index < steps.Length; index++)
            _pathMarkers.Multimesh.SetInstanceTransform(
                index,
                new Transform3D(Basis.Identity, Fo1HexMath.Center(steps[index]) + Vector3.Up * 0.04f));
    }

    private void BuildHud()
    {
        Hud = new CanvasLayer { Name = "Fo1HexHud", Layer = 50 };
        AddChild(Hud);
        _debugHudRoot = new Control
        {
            Name = "DevelopmentStatusHud",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Hud.AddChild(_debugHudRoot);
        var panel = new ColorRect
        {
            Position = new Vector2(18.0f, 532.0f),
            Size = new Vector2(910.0f, 170.0f),
            Color = new Color(0.012f, 0.022f, 0.018f, 0.91f),
        };
        _debugHudRoot.AddChild(panel);
        var labels = new VBoxContainer
        {
            Position = new Vector2(32.0f, 542.0f),
            Size = new Vector2(875.0f, 145.0f),
        };
        _debugHudRoot.AddChild(labels);
        var title = new Label { Text = "FALLOUT 1  •  V13ENT  •  200×200 HEX TACTICAL SLICE" };
        title.AddThemeColorOverride("font_color", new Color(0.96f, 0.77f, 0.28f));
        title.AddThemeFontSizeOverride("font_size", 18);
        labels.AddChild(title);
        _turnLabel = HudLabel(labels);
        _hexLabel = HudLabel(labels);
        _statusLabel = HudLabel(labels);
        _controlsLabel = HudLabel(labels);
        _controlsLabel.Text = ControlsText();
        _controlsLabel.AddThemeFontSizeOverride("font_size", 14);
        BuildTargetReticle();
        BuildFpsCrosshair();
    }

    private string ControlsText() => _firstPersonModeActive
        ? "FPS • WASD move • Mouse look • LMB 10mm • RMB knife • R reload • C tactical • P Pip-Boy • Esc mouse"
        : "TACTICAL • LMB move/select • Tab target • X ranged • Z melee • R reload • C shoulder/FPS • MMB orbit • RMB pan • Wheel zoom • G grid • P Pip-Boy • Space turn • F5 save";

    private void BuildFpsCrosshair()
    {
        _fpsCrosshair = new Control
        {
            Name = "Fo1FpsCrosshair",
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -18.0f,
            OffsetTop = -18.0f,
            OffsetRight = 18.0f,
            OffsetBottom = 18.0f,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        Hud.AddChild(_fpsCrosshair);
        var color = new Color(0.72f, 1.0f, 0.46f, 0.92f);
        foreach (var (position, size) in new[]
                 {
                     (new Vector2(16.0f, 4.0f), new Vector2(2.0f, 9.0f)),
                     (new Vector2(16.0f, 23.0f), new Vector2(2.0f, 9.0f)),
                     (new Vector2(4.0f, 16.0f), new Vector2(9.0f, 2.0f)),
                     (new Vector2(23.0f, 16.0f), new Vector2(9.0f, 2.0f)),
                     (new Vector2(16.0f, 16.0f), new Vector2(2.0f, 2.0f)),
                 })
            _fpsCrosshair.AddChild(new ColorRect
            {
                Position = position,
                Size = size,
                Color = color,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
    }

    private void BuildTargetReticle()
    {
        _targetReticle = new Control
        {
            Name = "SelectedTargetReticle",
            Size = new Vector2(180.0f, 104.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        Hud.AddChild(_targetReticle);
        var color = new Color(1.0f, 0.82f, 0.10f, 0.98f);
        foreach (var (position, size) in new[]
                 {
                     (new Vector2(48.0f, 20.0f), new Vector2(30.0f, 4.0f)),
                     (new Vector2(102.0f, 20.0f), new Vector2(30.0f, 4.0f)),
                     (new Vector2(48.0f, 20.0f), new Vector2(4.0f, 28.0f)),
                     (new Vector2(128.0f, 20.0f), new Vector2(4.0f, 28.0f)),
                     (new Vector2(48.0f, 68.0f), new Vector2(30.0f, 4.0f)),
                     (new Vector2(102.0f, 68.0f), new Vector2(30.0f, 4.0f)),
                     (new Vector2(48.0f, 44.0f), new Vector2(4.0f, 28.0f)),
                     (new Vector2(128.0f, 44.0f), new Vector2(4.0f, 28.0f)),
                     (new Vector2(88.0f, 72.0f), new Vector2(4.0f, 13.0f)),
                 })
        {
            _targetReticle.AddChild(new ColorRect
            {
                Position = position,
                Size = size,
                Color = color,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        }
        _targetReticleLabel = new Label
        {
            Position = Vector2.Zero,
            Size = new Vector2(180.0f, 22.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = "TARGET: GIANT RAT",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _targetReticleLabel.AddThemeColorOverride("font_color", color);
        _targetReticleLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
        _targetReticleLabel.AddThemeConstantOverride("outline_size", 6);
        _targetReticleLabel.AddThemeFontSizeOverride("font_size", 18);
        _targetReticle.AddChild(_targetReticleLabel);
    }

    private void RefreshTargetReticle()
    {
        if (_targetReticle is null || _camera is null || _firstPersonModeActive ||
            _selectedMob is null || !_selectedMob.Alive)
        {
            if (_targetReticle is not null)
                _targetReticle.Visible = false;
            return;
        }
        var tactical = _camera.Projection == Camera3D.ProjectionType.Orthogonal;
        var maximumDistance = tactical ? 8 : 4;
        if (Fo1HexMath.Distance(_playerTile, _selectedMob.Tile) > maximumDistance)
        {
            _targetReticle.Visible = false;
            return;
        }
        var target = _selectedMob.GlobalPosition + Vector3.Up * 0.55f;
        if (_camera.IsPositionBehind(target))
        {
            _targetReticle.Visible = false;
            return;
        }
        var screen = _camera.UnprojectPosition(target);
        var viewport = GetViewport().GetVisibleRect().Size;
        var position = screen - new Vector2(90.0f, 72.0f);
        position.X = Math.Clamp(position.X, 8.0f, MathF.Max(8.0f, viewport.X - 188.0f));
        position.Y = Math.Clamp(position.Y, 8.0f, MathF.Min(440.0f, viewport.Y - 112.0f));
        _targetReticle.Position = position;
        _targetReticleLabel.Text =
            $"TARGET: GIANT RAT  HP {_selectedMob.HitPoints}/{_selectedMob.MaximumHitPoints}";
        _targetReticle.Visible = true;
    }

    private void RefreshMobReadability()
    {
        var tactical = _camera is null ||
            _camera.Projection == Camera3D.ProjectionType.Orthogonal;
        foreach (var mob in _mobs)
            mob.UpdateReadability(_playerTile, tactical);
    }

    private static Label HudLabel(Container parent)
    {
        var label = new Label();
        label.AddThemeColorOverride("font_color", new Color(0.68f, 0.96f, 0.48f));
        label.AddThemeFontSizeOverride("font_size", 16);
        parent.AddChild(label);
        return label;
    }

    private void RefreshHud()
    {
        var inspected = _hoveredTile >= 0 ? _hoveredTile : _selectedTile >= 0 ? _selectedTile : _playerTile;
        var floorId = _floorIds[Fo1HexMath.FloorIndex(inspected)];
        var floorName = _floorNames.GetValueOrDefault(floorId, "unknown.frm");
        var target = _selectedMob is null
            ? "TARGET —"
            : $"TARGET {_selectedMob.DisplayName} {_selectedMob.HitPoints}/{_selectedMob.MaximumHitPoints} HP";
        if (_turnLabel is not null)
        {
            var pips = new string('●', _actionPoints) +
                new string('○', _maximumActionPoints - _actionPoints);
            _turnLabel.Text = _firstPersonModeActive
                ? $"FPS EXPLORATION   HP {_playerHitPoints}/{_playerProfile.HitPoints}   " +
                    $"AC {_playerProfile.ArmorClass}   {EquippedWeaponName}   " +
                    $"AMMO {_magazineRounds}/{_playerProfile.RangedWeapon.AmmunitionCapacity} +{_reserveRounds}   " +
                    $"SHOTS {_fpsShots}  HITS {_fpsHits}"
                : $"COMBAT TURN {_turn}   HP {_playerHitPoints}/{_playerProfile.HitPoints}   " +
                    $"AC {_playerProfile.ArmorClass}   AP {pips} {_actionPoints}/{_maximumActionPoints}   " +
                    $"EQUIPPED {EquippedWeaponName.ToUpperInvariant()} " +
                    $"[{EquippedWeaponActionPointCost} AP]   " +
                    $"10MM {_magazineRounds}/{_playerProfile.RangedWeapon.AmmunitionCapacity} +{_reserveRounds}";
            _hexLabel.Text =
                $"CURSOR HEX {inspected} ({inspected % 200},{inspected / 200})   " +
                $"FLOOR {floorId} {floorName}   " +
                $"{(_walkable[inspected] ? "PROVISIONAL FLOOR" : "NO FLOOR")}";
            _statusLabel.Text = $"{target}   {_status}";
        }
        _classicHud?.Refresh(
            _playerHitPoints,
            _playerProfile.HitPoints,
            _playerProfile.ArmorClass,
            _actionPoints,
            _maximumActionPoints,
            EquippedWeaponSymbol,
            EquippedWeaponActionPointCost,
            _firstPersonModeActive,
            _status);
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        var document = new
        {
            schema = SaveSchema,
            sceneSha256 = _sceneSha256,
            playerTile = _playerTile,
            turn = _turn,
            actionPoints = _actionPoints,
            playerHitPoints = _playerHitPoints,
            attacks = _attacks,
            kills = _kills,
            fpsShots = _fpsShots,
            fpsHits = _fpsHits,
            fpsKills = _fpsKills,
            rangedAttacks = _rangedAttacks,
            rangedHits = _rangedHits,
            meleeAttacks = _meleeAttacks,
            meleeHits = _meleeHits,
            reloads = _reloads,
            combatSequence = _combatSequence,
            magazineRounds = _magazineRounds,
            reserveRounds = _reserveRounds,
            equippedWeaponSymbol = EquippedWeaponSymbol,
            tagInventoryApplied = _tagInventoryApplied,
            inventoryObjects = _inventoryObjects,
            character = _characterProfile?.Report(),
            mobs = _mobs.Select(mob => mob.Report()).ToArray(),
        };
        var temporary = _savePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true,
        }) + System.Environment.NewLine);
        File.Move(temporary, _savePath, true);
    }

    private void Load()
    {
        if (!File.Exists(_savePath))
            return;
        using var document = JsonDocument.Parse(File.ReadAllText(_savePath));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != SaveSchema ||
            root.GetProperty("sceneSha256").GetString() != _sceneSha256)
            throw new InvalidOperationException($"Fallout hex save does not match this scene: {_savePath}");
        var tile = root.GetProperty("playerTile").GetInt32();
        if (tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height || !_walkable[tile])
            throw new InvalidOperationException($"Fallout hex save contains an invalid player tile: {tile}");
        _playerTile = tile;
        if (root.TryGetProperty("character", out var character) &&
            character.ValueKind == JsonValueKind.Object)
            ApplyCharacterStats(ParseSavedCharacter(character));
        _turn = Math.Max(1, root.GetProperty("turn").GetInt32());
        _actionPoints = Math.Clamp(root.GetProperty("actionPoints").GetInt32(), 0, _maximumActionPoints);
        _playerHitPoints = root.GetProperty("playerHitPoints").GetInt32();
        _attacks = root.GetProperty("attacks").GetInt32();
        _kills = root.GetProperty("kills").GetInt32();
        _fpsShots = root.TryGetProperty("fpsShots", out var fpsShots)
            ? Math.Max(0, fpsShots.GetInt32())
            : 0;
        _fpsHits = root.TryGetProperty("fpsHits", out var fpsHits)
            ? Math.Clamp(fpsHits.GetInt32(), 0, _fpsShots)
            : 0;
        _fpsKills = root.TryGetProperty("fpsKills", out var fpsKills)
            ? Math.Clamp(fpsKills.GetInt32(), 0, _fpsHits)
            : 0;
        _rangedAttacks = root.TryGetProperty("rangedAttacks", out var rangedAttacks)
            ? Math.Max(0, rangedAttacks.GetInt32())
            : _fpsShots + _attacks;
        _rangedHits = root.TryGetProperty("rangedHits", out var rangedHits)
            ? Math.Clamp(rangedHits.GetInt32(), 0, _rangedAttacks)
            : _fpsHits + _attacks;
        _meleeAttacks = root.TryGetProperty("meleeAttacks", out var meleeAttacks)
            ? Math.Max(0, meleeAttacks.GetInt32())
            : 0;
        _meleeHits = root.TryGetProperty("meleeHits", out var meleeHits)
            ? Math.Clamp(meleeHits.GetInt32(), 0, _meleeAttacks)
            : 0;
        _reloads = root.TryGetProperty("reloads", out var reloads)
            ? Math.Max(0, reloads.GetInt32())
            : 0;
        _combatSequence = root.TryGetProperty("combatSequence", out var combatSequence)
            ? Math.Max(0, combatSequence.GetInt32())
            : _rangedAttacks + _meleeAttacks;
        _magazineRounds = root.TryGetProperty("magazineRounds", out var magazineRounds)
            ? Math.Clamp(
                magazineRounds.GetInt32(),
                0,
                _playerProfile.RangedWeapon.AmmunitionCapacity)
            : _magazineRounds;
        _reserveRounds = root.TryGetProperty("reserveRounds", out var reserveRounds)
            ? Math.Max(0, reserveRounds.GetInt32())
            : _reserveRounds;
        if (root.TryGetProperty("equippedWeaponSymbol", out var equippedWeaponSymbol))
        {
            var symbol = equippedWeaponSymbol.GetString();
            if (symbol == _playerProfile.Inventory.EquippedRangedSymbol)
                _meleeWeaponEquipped = false;
            else if (symbol == _playerProfile.Inventory.EquippedMeleeSymbol)
                _meleeWeaponEquipped = true;
            else
                throw new InvalidOperationException(
                    $"Fallout save contains an unknown equipped weapon: {symbol}.");
        }
        _tagInventoryApplied = root.TryGetProperty("tagInventoryApplied", out var tagApplied) &&
            tagApplied.GetBoolean();
        if (root.TryGetProperty("inventoryObjects", out var inventoryObjects))
        {
            _inventoryObjects.Clear();
            foreach (var row in inventoryObjects.EnumerateObject())
            {
                var objects = row.Value.GetInt32();
                if (objects < 0)
                    throw new InvalidOperationException(
                        $"Fallout save contains a negative inventory stack: {row.Name}");
                _inventoryObjects[row.Name] = objects;
            }
        }
        if (_characterProfile is not null && !_tagInventoryApplied)
            ApplyTagInventory(_characterProfile);
        var mobRows = root.GetProperty("mobs").EnumerateArray().ToDictionary(
            row => row.GetProperty("serial").GetInt32());
        _mobsByTile.Clear();
        foreach (var mob in _mobs)
            _walkable[mob.Tile] = true;
        foreach (var mob in _mobs)
        {
            var row = mobRows[mob.Serial];
            mob.SetTile(row.GetProperty("tile").GetInt32());
            if (row.TryGetProperty("alerted", out var alerted) && alerted.GetBoolean())
                mob.Alert();
            var targetHp = row.GetProperty("hitPoints").GetInt32();
            if (targetHp < mob.HitPoints)
                mob.TakeDamage(mob.HitPoints - targetHp);
            if (mob.Alive)
                _mobsByTile[mob.Tile] = mob;
            else
                _walkable[mob.Tile] = true;
        }
        ApplyEquippedWeaponVisibility();
    }

    private static Fo1CharacterProfile ParseSavedCharacter(JsonElement source)
    {
        if (source.GetProperty("schema").GetString() != "opennv-fo1-character/v1")
            throw new InvalidOperationException("Fallout save contains an unknown character schema.");
        var special = source.GetProperty("allocatedSpecial");
        var profile = new Fo1CharacterProfile(
            source.GetProperty("name").GetString()!,
            source.GetProperty("age").GetInt32(),
            source.GetProperty("sex").GetString()!,
            special.GetProperty("strength").GetInt32(),
            special.GetProperty("perception").GetInt32(),
            special.GetProperty("endurance").GetInt32(),
            special.GetProperty("charisma").GetInt32(),
            special.GetProperty("intelligence").GetInt32(),
            special.GetProperty("agility").GetInt32(),
            special.GetProperty("luck").GetInt32(),
            source.GetProperty("taggedSkills").EnumerateArray()
                .Select(row => row.GetString()!)
                .ToArray(),
            source.GetProperty("traits").EnumerateArray()
                .Select(row => row.GetString()!)
                .ToArray());
        profile.Validate();
        return profile;
    }

    private static string ResolvePath(string path) =>
        path.StartsWith("user://", StringComparison.Ordinal)
            ? ProjectSettings.GlobalizePath(path)
            : Path.GetFullPath(path);

    internal readonly record struct PlayerProfile(
        string Name,
        Texture2D Texture,
        int Width,
        int Height,
        float PixelSize,
        Vector2 FrameOffset,
        int HitPoints,
        int ArmorClass,
        int Sequence,
        int Strength,
        int Perception,
        int MeleeDamage,
        IReadOnlyDictionary<string, int> Skills,
        WeaponProfile RangedWeapon,
        WeaponProfile MeleeWeapon,
        InventoryProfile Inventory)
    {
        internal string WeaponName => RangedWeapon.Name;
        internal int WeaponMinimumDamage => RangedWeapon.MinimumDamage;
        internal int WeaponMaximumDamage => RangedWeapon.MaximumDamage;
        internal int WeaponRangeHexes => RangedWeapon.RangeHexes;
        internal int WeaponActionPointCost => RangedWeapon.ActionPointCost;

        internal void Validate()
        {
            RangedWeapon.Validate();
            MeleeWeapon.Validate();
            if (MeleeWeapon.Melee is false || RangedWeapon.Melee ||
                Strength <= 0 || Perception <= 0 || MeleeDamage < 0 ||
                Skills.Count == 0 || Skills.Values.Any(value => value < 0))
                throw new InvalidOperationException("Fallout player combat profile is invalid.");
            Inventory.Validate();
        }
    }

    internal readonly record struct WeaponProfile(
        string Name,
        string Pid,
        string PrototypeSha256,
        string Skill,
        int MinimumDamage,
        int MaximumDamage,
        int RangeHexes,
        int ActionPointCost,
        int MinimumStrength,
        int RoundsPerAttack,
        int AmmunitionCapacity,
        int InitialLoadedRounds,
        bool Melee)
    {
        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Pid) ||
                PrototypeSha256.Length != 64 || string.IsNullOrWhiteSpace(Skill) ||
                MinimumDamage <= 0 || MaximumDamage < MinimumDamage || RangeHexes <= 0 ||
                ActionPointCost <= 0 || MinimumStrength <= 0)
                throw new InvalidOperationException("Fallout weapon profile is invalid.");
            if (Melee && (RoundsPerAttack != 0 || AmmunitionCapacity != 0 || InitialLoadedRounds != 0) ||
                !Melee && (RoundsPerAttack <= 0 || AmmunitionCapacity <= 0 ||
                    InitialLoadedRounds is < 0 || InitialLoadedRounds > AmmunitionCapacity))
                throw new InvalidOperationException("Fallout weapon ammunition contract is invalid.");
        }
    }

    internal sealed record InventoryProfile(
        string EquippedRangedSymbol,
        string EquippedMeleeSymbol,
        string AmmunitionSymbol,
        int AmmunitionRoundsPerObject,
        IReadOnlyList<InventoryStack> Base,
        IReadOnlyList<InventoryTagBonus> TagBonuses)
    {
        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(EquippedRangedSymbol) ||
                string.IsNullOrWhiteSpace(EquippedMeleeSymbol) ||
                string.IsNullOrWhiteSpace(AmmunitionSymbol) ||
                AmmunitionRoundsPerObject <= 0 || Base.Count == 0 ||
                Base.Any(row => row.Objects <= 0) ||
                !Base.Any(row => row.Symbol == EquippedRangedSymbol) ||
                !Base.Any(row => row.Symbol == EquippedMeleeSymbol) ||
                !Base.Any(row => row.Symbol == AmmunitionSymbol) ||
                TagBonuses.Select(row => row.Skill).Distinct(StringComparer.Ordinal).Count() !=
                    TagBonuses.Count ||
                TagBonuses.Any(row => string.IsNullOrWhiteSpace(row.Skill) ||
                    row.Items.Count == 0 || row.Items.Any(item => item.Objects <= 0)))
                throw new InvalidOperationException("Fallout starting inventory profile is invalid.");
        }
    }

    internal readonly record struct InventoryStack(string Symbol, string Pid, int Objects);

    internal readonly record struct InventoryTagBonus(
        string Skill,
        IReadOnlyList<InventoryStack> Items);

    private sealed record FirstPersonTarget(
        Fo1Mob Mob,
        float Along,
        float Perpendicular);

    internal readonly record struct CombatResult(
        bool Attempted,
        string Kind,
        string Mode,
        bool Hit,
        int Damage,
        bool Killed,
        int ChancePercent,
        int RollPercent);
}
