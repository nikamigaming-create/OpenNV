using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic;
using OpenNV.Runtime.Presentation.CharacterCreation;

using OpenNV.Runtime.SceneGraph;


using OpenNV.Runtime.Content;
using OpenNV.Runtime.Presentation.Rendering;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal partial class Fo1TacticalSession
{
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
        if (_characterProfile is null)
            throw new InvalidOperationException(
                "Fallout classic interface requires a selected character.");
        var classicInventory = new Fo1ClassicInventoryScreen();
        classicInventory.Configure(
            contract.ClassicInventory,
            _characterProfile,
            contract.PremadeCharacters,
            _playerProfile.Inventory.DisplayNames,
            InventorySnapshot,
            () => EquippedWeaponSymbol,
            _playerProfile.Inventory.EquippedRangedSymbol,
            _playerProfile.Inventory.EquippedMeleeSymbol,
            EquipInventoryWeapon,
            UseInventoryScriptedItem,
            () => { CloseInventory(); });
        Hud.AddChild(classicInventory);
        _classicInventory = classicInventory;
        var classicHud = new Fo1ClassicHud();
        classicHud.Configure(
            contract.InterfaceHud,
            TogglePipBoy,
            ToggleInventory,
            SwapEquippedWeapon);
        Hud.AddChild(classicHud);
        Hud.MoveChild(classicInventory, Hud.GetChildCount() - 1);
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
        if (_classicInventory?.IsOpen == true)
            _classicInventory.Close();
        _pipBoy.Toggle();
    }

    internal void ToggleInventory()
    {
        if (_classicInventory is null)
        {
            _status = "Inventory becomes available after character selection";
            RefreshHud();
            return;
        }
        if (_classicInventory.IsOpen)
        {
            CloseInventory();
            return;
        }
        if (_pipBoy?.IsOpen == true)
            _pipBoy.SetOpen(false);
        _classicInventory.Open();
    }

    internal bool CloseInventory() => _classicInventory?.Close() == true;

    internal ActorModelSlice.LoadedActor AttachOwnedPlayer(
        Fo1HexSceneLoader.PlayerPresentationSource source)
    {
        if (_ownedPlayer is not null)
            throw new InvalidOperationException("Fallout tactical player already has an owned 3D presentation.");
        VerifiedGltfLoader.VerifyHash(source.Model, source.ModelSha256);
        VerifiedGltfLoader.VerifyHash(source.Sidecar, source.SidecarSha256);
        var actor = ActorModelSlice.Load(source.Model, source.Sidecar, _playerToken);
        if (source.BodyProfile is { } bodyProfile)
        {
            var skeleton = actor.Root.FindChildren("*", "Skeleton3D", true, false)
                .OfType<Skeleton3D>()
                .Single();
            CharacterBodyRig.Apply(
                actor.Root,
                skeleton,
                bodyProfile,
                this,
                $"fallout1-gameplay-{source.DonorKey}");
            actor = actor with { Bounds = ActorModelSlice.PosedWorldBounds(actor) };
        }
        BindOwnedPlayerMaterialTextures(actor, source.Sidecar);
        _ownedPlayerLitMaterials = ApplyOwnedPlayerLighting(
            actor.Root,
            source.UnitsToMeters);
        if (actor.FormId != source.SourceActorBaseFormId ||
            actor.AuthoredSurfaces != source.Surfaces ||
            actor.AuthoredTextures != source.Textures ||
            actor.Animations < source.Animations)
            throw new InvalidOperationException(
                "Fallout owned player runtime coverage differs from its scene contract.");
        actor.Root.Name = "OwnedVaultDweller";
        var groundHeight = _ownedPlayerFloorHeightMeters ??
            _playerToken.GlobalPosition.Y;
        var groundDelta = groundHeight - actor.Bounds.Position.Y;
        actor.Root.Position += Vector3.Up * groundDelta;
        _playerToken.LookAt(Fo1HexMath.Center(_doorTile), Vector3.Up);
        var grounded = actor with
        {
            Bounds = new Aabb(
                actor.Bounds.Position + Vector3.Up * groundDelta,
                actor.Bounds.Size),
        };
        _ownedPlayerGroundErrorMeters = MathF.Abs(
            grounded.Bounds.Position.Y - groundHeight);
        _ownedPlayer = grounded;
        _ownedPlayerSource = source;
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
        actor.Root.SetMeta("presentation_role", source.Role);
        actor.Root.SetMeta("source_actor_female", source.SourceActorFemale);
        actor.Root.SetMeta("source_model_sha256", source.ModelSha256);
        actor.Root.SetMeta("source_sidecar_sha256", source.SidecarSha256);
        actor.Root.SetMeta("source_donor_key", source.DonorKey);
        actor.Root.SetMeta(
            "source_body_profile",
            source.BodyProfile?.Id ?? "sex-default");
        actor.Root.SetMeta("selection_state", "unbound-until-character-selection");
        return grounded;
    }

    internal void RegisterOwnedPlayerDonor(Fo1HexSceneLoader.PlayerPresentationSource source)
    {
        if (!_ownedPlayerDonorsBySex.TryAdd(source.DonorKey, source))
            throw new InvalidOperationException(
                $"Fallout 1 has duplicate owned humanoid donor identity: {source.DonorKey}.");
    }

    private void SelectOwnedPlayerDonor(string characterId, string sex)
    {
        var donorKey = _ownedPlayerDonorsBySex.ContainsKey(characterId)
            ? characterId
            : sex;
        if (!_ownedPlayerDonorsBySex.TryGetValue(donorKey, out var source))
            throw new InvalidOperationException(
                $"Fallout 1 selected identity has no registered owned donor for {characterId}/{sex}.");
        if (_ownedPlayerSource is { } current && current == source)
            return;
        if (_ownedPlayerWeaponSource is not { } ranged ||
            _ownedPlayerMeleeWeaponSource is not { } melee)
            throw new InvalidOperationException(
                "Fallout 1 donor selection has no source-bound weapon/socket contracts.");
        _ownedPlayerWeapon?.Root.QueueFree();
        _ownedPlayerMeleeWeapon?.Root.QueueFree();
        _ownedPlayer?.Root.QueueFree();
        _ownedPlayerWeapon = null;
        _ownedPlayerMeleeWeapon = null;
        _ownedPlayer = null;
        _ownedPlayerSource = null;
        _playerAnimationPlayer = null;
        _ = AttachOwnedPlayer(source);
        _ = AttachOwnedPlayerWeapon(ranged);
        _ = AttachOwnedPlayerMeleeWeapon(melee);
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
        _ownedPlayerWeaponSource = source.Clone();
        _ownedRangedWeaponLitMaterials = ApplyOwnedWeaponLighting(
            _ownedPlayerWeapon.Value.Root,
            source.GetProperty("unitsToMeters").GetSingle());
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
        _ownedPlayerMeleeWeaponSource = source.Clone();
        _ownedMeleeWeaponLitMaterials = ApplyOwnedWeaponLighting(
            _ownedPlayerMeleeWeapon.Value.Root,
            source.GetProperty("unitsToMeters").GetSingle());
        ApplyEquippedWeaponVisibility();
        RestoreSavedPlayerPresentationIfReady();
        return _ownedPlayerMeleeWeapon.Value;
    }

    private void RestoreSavedPlayerPresentationIfReady()
    {
        if (_pendingSavedPlayerPresentation is null || _characterProfile is null ||
            _ownedPlayer is null || _ownedPlayerWeapon is null ||
            _ownedPlayerMeleeWeapon is null)
            return;
        BindCharacterPresentation(_characterProfile, _pendingSavedPlayerPresentation);
        _pendingSavedPlayerPresentation = null;
    }

    internal void SwapEquippedWeapon()
    {
        SetEquippedWeapon(!_meleeWeaponEquipped);
        _status = $"Equipped {EquippedWeaponName} • {EquippedWeaponActionPointCost} AP";
        RefreshHud();
        Save();
    }

    internal bool EquipInventoryWeapon(string symbol)
    {
        if (_classicInventory?.IsOpen != true)
            throw new InvalidOperationException(
                "Fallout inventory equipment changes require the owned inventory screen.");
        return EquipInventoryWeaponCore(symbol);
    }

    internal bool UseInventoryScriptedItem(string symbol)
    {
        if (_classicInventory?.IsOpen != true)
            throw new InvalidOperationException(
                "Fallout inventory use requires the owned inventory screen.");
        var flare = _destinationFlareUse;
        if (flare is null || symbol != flare.Symbol || InventoryObjects(symbol) <= 0 ||
            _destinationFlareExpired || _destinationFlareScriptState.Flag("lit") ||
            _destinationInventoryInteraction is null || !_lootedMapInventoryHostSerials.Contains(flare.HostSerial))
            return false;
        if (!flare.Program.Execute(
                "use_proc",
                _destinationFlareScriptState,
                new ClassicScriptContext(
                    SourceIsPlayer: true,
                    CanSeePlayer: false,
                    GameTime: _classicScriptGameTime)) ||
            !_destinationFlareScriptState.Flag("lit"))
            throw new InvalidOperationException(
                "Fallout flare source-script use did not publish its lit state.");
        _status = $"Used {symbol} through its source script; its decoded expiry is active.";
        RefreshHud();
        Save();
        return true;
    }

    private bool ProcessClassicTimedWorldActions()
    {
        var flare = _destinationFlareUse;
        if (flare is null || _destinationFlareExpired ||
            !_destinationFlareScriptState.Flag("lit"))
            return false;
        var execution = flare.Program.ExecuteWithActions(
            "start_proc",
            _destinationFlareScriptState,
            new ClassicScriptContext(
                SourceIsPlayer: false,
                CanSeePlayer: false,
                GameTime: _classicScriptGameTime));
        if (!execution.Executed)
            return false;
        if (!execution.DestroySelf || InventoryObjects(flare.Symbol) <= 0)
            throw new InvalidOperationException(
                "Fallout flare expiry did not execute its decoded destruction.");
        _inventoryObjects[flare.Symbol] = InventoryObjects(flare.Symbol) - 1;
        _destinationFlareExpired = true;
        _status = $"{flare.Symbol} expired and was removed by its source script.";
        return true;
    }

    private bool EquipInventoryWeaponCore(string symbol)
    {
        bool melee;
        if (symbol == _playerProfile.Inventory.EquippedRangedSymbol)
            melee = false;
        else if (symbol == _playerProfile.Inventory.EquippedMeleeSymbol)
            melee = true;
        else
            throw new InvalidOperationException(
                $"Fallout inventory item is not an active-hand weapon: {symbol}.");
        if (_meleeWeaponEquipped == melee)
            return false;
        SetEquippedWeapon(melee);
        _status = $"Equipped {EquippedWeaponName} from inventory • " +
            $"{EquippedWeaponActionPointCost} AP";
        RefreshHud();
        Save();
        return true;
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
        var actorSupportsWeapons = _playerPresentationBinding is null ||
            _playerPresentationBinding.UsesOwnedDonor;
        if (_ownedPlayerWeapon is not null)
            _ownedPlayerWeapon.Value.Root.Visible =
                actorSupportsWeapons && !_meleeWeaponEquipped;
        if (_ownedPlayerMeleeWeapon is not null)
            _ownedPlayerMeleeWeapon.Value.Root.Visible =
                actorSupportsWeapons && _meleeWeaponEquipped;
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
        var moving = name == _playerMoveAnimation;
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
        playerHex = new[] { _playerTile % Fo1TacticalSessionNumericContracts.PresentationInt200, _playerTile / Fo1TacticalSessionNumericContracts.PresentationInt200 },
        doorTile = _doorTile,
        sourceDoor = _sourceDoor?.Report(_sourceDoorOpen),
        exitGridTransition = _exitGridTransition?.Report(
            _activatedExitGridTile,
            destinationSceneLoaded: _loadedDestinationPresentation is not null),
        destinationPresentation = _loadedDestinationPresentation?.Report(_exitGridTransition!),
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
        classicInventory = _classicInventory?.Report(),
        pipBoy = _pipBoy?.Report(),
        combatPresentation = _combatPresentation?.Report(),
        playerPresentation = new
        {
            owned3d = _ownedPlayer is not null,
            ground = new
            {
                sourceBoundFloorHeightMeters = _ownedPlayerFloorHeightMeters,
                errorMeters = _ownedPlayerGroundErrorMeters,
            },
            selection = _playerPresentationBinding?.Report(),
            formId = _ownedPlayer?.FormId,
            meshes = _ownedPlayer?.Meshes ?? 0,
            importedAnimations = _ownedPlayer?.Animations ?? 0,
            litMaterials = _ownedPlayerLitMaterials,
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
                    litMaterials = _ownedRangedWeaponLitMaterials,
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
                    litMaterials = _ownedMeleeWeaponLitMaterials,
                    tacticalAndThirdPersonOnly = true,
                    visible = _ownedPlayerMeleeWeapon.Value.Root.IsVisibleInTree(),
                },
        },
    };

    private int ApplyOwnedPlayerLighting(
        Node root,
        float unitsToMeters)
    {
        if (!float.IsFinite(unitsToMeters) || unitsToMeters <= 0.0f)
            throw new InvalidOperationException(
                "Fallout owned player lighting requires a finite source unit scale.");
        var atmosphere = _runtimeProfile.Scene.Atmosphere;
        var ambient = new Color(
            atmosphere.AmbientColor.R * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.G * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.B * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.A);
        var fogFar = _runtimeProfile.Camera.Tactical.FarClipMeters / unitsToMeters;
        var configured = RuntimeMaterialLoader.ApplyRetailActorLighting(
            root,
            ambient,
            atmosphere.FogColor,
            0.0f,
            fogFar,
            Fo1TacticalSessionNumericContracts.PresentationFloat1Point0f,
            unitsToMeters);
        if (configured < 1)
            throw new InvalidOperationException(
                "Fallout owned player geometry has no compatible retail-lit material.");
        return configured;
    }

    private static void BindOwnedPlayerMaterialTextures(
        ActorModelSlice.LoadedActor actor,
        string sidecarPath)
    {
        var resolvedSidecar = VerifiedGltfLoader.ResolvePath(sidecarPath);
        using var document = JsonDocument.Parse(File.ReadAllText(resolvedSidecar));
        var root = document.RootElement;
        var textureRows = root.GetProperty("textures").EnumerateArray().ToArray();
        var surfaceRows = root.GetProperty("surfaces").EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("runtimeNodeName").GetString()!,
                StringComparer.Ordinal);
        var rebound = 0;
        var faceGenSurfaces = 0;
        foreach (var surface in actor.Surfaces)
        {
            if (!surfaceRows.TryGetValue(surface.RuntimeNodeName, out var row))
                throw new InvalidOperationException(
                    $"Fallout owned player surface is absent from its sidecar: {surface.RuntimeNodeName}.");
            var material = row.GetProperty("material");
            if (material.TryGetProperty("faceGen", out var faceGen) &&
                faceGen.ValueKind != JsonValueKind.Null)
            {
                faceGenSurfaces++;
                continue;
            }
            if (surface.Mesh.GetSurfaceOverrideMaterial(0) is not ShaderMaterial shader ||
                shader.ResourceName != RuntimeMaterialLoader.RetailActorMaterialResourceName)
                throw new InvalidOperationException(
                    $"Fallout owned player surface has no retail actor material: {surface.RuntimeNodeName}.");
            var generatedDiffuse = material.TryGetProperty(
                    "generatedDiffuseSha256",
                    out var generatedDiffuseProperty) &&
                generatedDiffuseProperty.ValueKind == JsonValueKind.String
                    ? generatedDiffuseProperty.GetString()
                    : null;
            var diffuse = generatedDiffuse is null
                ? SingleTextureRow(
                    textureRows,
                    "identity",
                    material.GetProperty("resolvedDiffuse").GetString()!,
                    resolvedSidecar)
                : SingleTextureRow(
                    textureRows,
                    "identity",
                    $"generated:{surface.Role}:{generatedDiffuse}",
                    resolvedSidecar);
            var normal = SingleTextureRow(
                textureRows,
                "identity",
                material.GetProperty("resolvedNormal").GetString()!,
                resolvedSidecar);
            var diffuseTexture = LoadOwnedPlayerTexture(diffuse, resolvedSidecar);
            var normalTexture = LoadOwnedPlayerTexture(normal, resolvedSidecar);
            shader.SetShaderParameter("base_map", diffuseTexture);
            shader.SetShaderParameter("normal_map", normalTexture);
            shader.SetShaderParameter("use_base_map", true);
            shader.SetShaderParameter("use_normal_map", true);
            VerifyOwnedPlayerTextureReadback(
                shader,
                "base_map",
                diffuseTexture,
                surface.RuntimeNodeName);
            VerifyOwnedPlayerTextureReadback(
                shader,
                "normal_map",
                normalTexture,
                surface.RuntimeNodeName);
            rebound++;
        }
        if (rebound + faceGenSurfaces != actor.AuthoredSurfaces)
            throw new InvalidOperationException(
                $"Fallout owned player texture coverage drifted: rebound={rebound} " +
                $"surfaces={actor.AuthoredSurfaces}.");
        GD.Print(
            $"OPENNV_FO1_OWNED_ACTOR_TEXTURE_BINDING_PASS surfaces={rebound} " +
            $"faceGenSurfaces={faceGenSurfaces} shaderReadback=rid-and-pixel-sha256");
    }

    private static JsonElement SingleTextureRow(
        IReadOnlyList<JsonElement> rows,
        string property,
        string expected,
        string sidecarPath)
    {
        var matches = rows.Where(row =>
                row.GetProperty(property).GetString()?.Equals(
                    expected,
                    StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"Fallout owned player texture identity is missing or ambiguous in {sidecarPath}: " +
                $"{property}={expected}.");
        return matches[0];
    }

    private static Texture2D LoadOwnedPlayerTexture(JsonElement row, string sidecarPath)
    {
        var root = Path.GetDirectoryName(sidecarPath)
            ?? throw new InvalidOperationException(
                $"Fallout owned player sidecar has no directory: {sidecarPath}.");
        var path = Path.GetFullPath(Path.Combine(root, row.GetProperty("png").GetString()!));
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Fallout owned player texture escapes its actor artifact: {path}.");
        VerifiedGltfLoader.VerifyHash(path, row.GetProperty("pngSha256").GetString()!);
        var image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty() ||
            image.GetWidth() != row.GetProperty("width").GetInt32() ||
            image.GetHeight() != row.GetProperty("height").GetInt32())
            throw new InvalidOperationException(
                $"Fallout owned player texture is invalid: {path}.");
        if (!image.HasMipmaps() && image.GetWidth() > 1 && image.GetHeight() > 1)
            image.GenerateMipmaps();
        var pixelSha256 = Convert.ToHexString(SHA256.HashData(image.GetData())).ToLowerInvariant();
        var texture = ImageTexture.CreateFromImage(image);
        texture.SetMeta("opennv_source_pixel_sha256", pixelSha256);
        if (!texture.GetRid().IsValid)
            throw new InvalidOperationException(
                $"Fallout owned player texture has no rendering RID: {path}.");
        return texture;
    }

    private static void VerifyOwnedPlayerTextureReadback(
        ShaderMaterial shader,
        string parameter,
        Texture2D expected,
        string surface)
    {
        if (shader.GetShaderParameter(parameter).AsGodotObject() is not Texture2D readback ||
            !readback.GetRid().IsValid ||
            readback.GetRid() != expected.GetRid())
            throw new InvalidOperationException(
                $"Fallout owned player shader texture RID did not read back: {surface}/{parameter}.");
        var image = readback.GetImage();
        var pixelSha256 = image is null || image.IsEmpty()
            ? ""
            : Convert.ToHexString(SHA256.HashData(image.GetData())).ToLowerInvariant();
        if (!expected.HasMeta("opennv_source_pixel_sha256") ||
            pixelSha256 != expected.GetMeta("opennv_source_pixel_sha256").AsString())
            throw new InvalidOperationException(
                $"Fallout owned player shader pixel source did not read back: {surface}/{parameter}.");
    }

    private int ApplyOwnedWeaponLighting(Node root, float unitsToMeters)
    {
        if (!float.IsFinite(unitsToMeters) || unitsToMeters <= 0.0f)
            throw new InvalidOperationException(
                "Fallout owned weapon lighting requires a finite source unit scale.");
        var atmosphere = _runtimeProfile.Scene.Atmosphere;
        var ambient = new Color(
            atmosphere.AmbientColor.R * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.G * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.B * atmosphere.AmbientEnergy,
            atmosphere.AmbientColor.A);
        var fogFar = _runtimeProfile.Camera.Tactical.FarClipMeters / unitsToMeters;
        var configured = RuntimeMaterialLoader.ApplyRetailAmbientDirectionalLighting(
            root,
            ambient,
            atmosphere.FogColor,
            0.0f,
            fogFar,
            Fo1TacticalSessionNumericContracts.PresentationFloat1Point0f,
            unitsToMeters) + CountStandardLitMaterials(root);
        if (configured < 1)
            throw new InvalidOperationException(
                "Fallout held weapon has no compatible light-responsive material.");
        return configured;
    }

    private static int CountStandardLitMaterials(Node root)
    {
        return NodeTraversal.Descendants<Node>(root).OfType<MeshInstance3D>()
            .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0)
                .Select(mesh.GetActiveMaterial))
            .OfType<BaseMaterial3D>()
            .Count(material => material.ShadingMode != BaseMaterial3D.ShadingModeEnum.Unshaded);
    }
}
