using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Campaigns.Classic;


namespace OpenNV.Runtime.Campaigns.Fallout1;

internal partial class Fo1TacticalSession
{
    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_savePath)!);
        var cameraState = _cameraRig?.CaptureSaveState();
        if (_characterProfile is not null && cameraState is null)
            throw new InvalidOperationException(
                "Fallout 1 character save requires an attached camera state.");
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
            classicPlayerStatus = new
            {
                state = _classicPlayerStatus.Save(),
                gameTime = _classicScriptGameTime,
            },
            destinationFlare = _destinationFlareUse is null ? null : new
            {
                descriptorSha256 = _destinationFlareUse.Sha256,
                scriptState = _destinationFlareScriptState.Save(),
                gameTime = _classicScriptGameTime,
                expiryLocalIndex = _destinationFlareUse.ExpiryLocalIndex,
                expiryDurationGameTicks = _destinationFlareUse.ExpiryDurationGameTicks,
                expired = _destinationFlareExpired,
                expiry = "decoded-destroy-self",
            },
            destinationGenericDoor = _destinationGenericDoor is null ? null : new
            {
                descriptorSha256 = _destinationGenericDoor.Sha256,
                open = _destinationGenericDoorOpen,
            },
            destinationMedicLook = _destinationMedicLook is null ? null : new
            {
                descriptorSha256 = _destinationMedicLook.Sha256,
                viewed = _destinationMedicLookViewed,
                dialogueProcedure = _destinationMedicDialogueProcedure,
            },
            destinationReturnExitGrid = _destinationReturnExitGrid is null ? null : new
            {
                descriptorSha256 = _destinationReturnExitGrid.Sha256,
                activatedTile = _activatedDestinationReturnExitGridTile,
            },
            lootedMapInventoryHostSerials = _inactiveMapInventoryHostSerials
                .Concat(_lootedMapInventoryHostSerials).Distinct().Order().ToArray(),
            exitGridTransition = _exitGridTransition is null ? null : new
            {
                descriptorSha256 = _exitGridTransition.Sha256,
                activatedTile = _activatedExitGridTile,
            },
            activeMap = SaveActiveMap(),
            sourceDoor = _sourceDoor?.Report(_sourceDoorOpen),
            character = _characterProfile?.Report(),
            camera = cameraState?.SaveState(),
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
        var savedActiveMap = root.TryGetProperty("activeMap", out var activeMap) &&
            activeMap.ValueKind != JsonValueKind.Null
            ? activeMap.Clone()
            : (JsonElement?)null;
        var tile = root.GetProperty("playerTile").GetInt32();
        if (tile is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            (savedActiveMap is null && !_walkable[tile]))
            throw new InvalidOperationException($"Fallout hex save contains an invalid player tile: {tile}");
        _playerTile = tile;
        if (root.TryGetProperty("sourceDoor", out var sourceDoor) &&
            sourceDoor.ValueKind != JsonValueKind.Null)
        {
            var configured = _sourceDoor ?? throw new InvalidOperationException(
                "Fallout save has a source door state but no door contract is configured.");
            if (sourceDoor.GetProperty("Serial").GetInt32() != configured.Serial ||
                sourceDoor.GetProperty("Tile").GetInt32() != configured.Tile ||
                sourceDoor.GetProperty("PrototypeSha256").GetString() != configured.PrototypeSha256 ||
                sourceDoor.GetProperty("InitiallyBlocked").GetBoolean() != configured.InitiallyBlocked)
                throw new InvalidOperationException("Fallout save source door differs from its MAP contract.");
            _sourceDoorOpen = sourceDoor.GetProperty("open").GetBoolean();
            if (_sourceDoorOpen)
                _walkable[configured.Tile] = true;
        }
        if (root.TryGetProperty("exitGridTransition", out var exitGridTransition) &&
            exitGridTransition.ValueKind != JsonValueKind.Null)
        {
            if (_exitGridTransition is null ||
                exitGridTransition.GetProperty("descriptorSha256").GetString() != _exitGridTransition.Sha256)
                throw new InvalidOperationException("Fallout save exit-grid transition does not match its descriptor.");
            if (exitGridTransition.TryGetProperty("activatedTile", out var activated) &&
                activated.ValueKind != JsonValueKind.Null)
            {
                var activatedTile = activated.GetInt32();
                if (!_exitGridTransition.IsTrigger(activatedTile) ||
                    (savedActiveMap is null && activatedTile != _playerTile))
                    throw new InvalidOperationException("Fallout save exit-grid activation is not a source trigger.");
                _activatedExitGridTile = activatedTile;
            }
        }
        Fo1DestinationPresentationContract? savedDestination = null;
        if (savedActiveMap is not null)
        {
            var activeKind = savedActiveMap.Value.GetProperty("kind").GetString();
            if (activeKind == "destination")
                savedDestination = LoadSavedDestination(savedActiveMap.Value);
            else if (activeKind == "source-return")
                LoadSavedSourceReturn(savedActiveMap.Value, tile);
            else
                throw new InvalidOperationException("Fallout save has an unknown active MAP kind.");
        }
        if (root.TryGetProperty("character", out var character) &&
            character.ValueKind == JsonValueKind.Object)
        {
            JsonElement? legacyPresentationIdentity =
                root.TryGetProperty("playerPresentationIdentity", out var presentationIdentity) &&
                presentationIdentity.ValueKind == JsonValueKind.Object
                    ? presentationIdentity.Clone()
                    : null;
            ApplyCharacterStats(ParseSavedCharacter(character, legacyPresentationIdentity));
            _pendingSavedPlayerPresentation = Fo1PlayerPresentationIdentity.FromProfile(
                _characterProfile!);
            _restoredCameraState = root.TryGetProperty("camera", out var camera) &&
                camera.ValueKind == JsonValueKind.Object
                ? Fo1CameraSaveState.Load(camera)
                : null;
            _restoredCharacterFromSave = true;
        }
        else if (root.TryGetProperty(
                     "playerPresentationIdentity",
                     out var orphanedPresentationIdentity) &&
                 orphanedPresentationIdentity.ValueKind == JsonValueKind.Object)
            throw new InvalidOperationException(
                "Fallout 1 save has a player presentation identity without a character.");
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
        if (root.TryGetProperty("classicPlayerStatus", out var classicPlayerStatus))
        {
            _classicPlayerStatus = ClassicPlayerStatusState.Restore(
                classicPlayerStatus.GetProperty("state"));
            _classicScriptGameTime = classicPlayerStatus.GetProperty("gameTime").GetInt32();
            if (_classicScriptGameTime < 0)
                throw new InvalidOperationException(
                    "Fallout save classic player status state is invalid.");
        }
        if (root.TryGetProperty("destinationFlare", out var destinationFlare) &&
            destinationFlare.ValueKind != JsonValueKind.Null)
        {
            if (_destinationFlareUse is null ||
                destinationFlare.GetProperty("descriptorSha256").GetString() != _destinationFlareUse.Sha256)
                throw new InvalidOperationException("Fallout save flare state does not match its descriptor.");
            _destinationFlareScriptState = ClassicScriptState.Restore(
                destinationFlare.GetProperty("scriptState"));
            var savedFlareGameTime = destinationFlare.GetProperty("gameTime").GetInt32();
            if (_classicScriptGameTime != 0 && _classicScriptGameTime != savedFlareGameTime)
                throw new InvalidOperationException(
                    "Fallout saved flare game time conflicts with shared classic time.");
            _classicScriptGameTime = savedFlareGameTime;
            if (destinationFlare.GetProperty("expiryLocalIndex").GetInt32() !=
                    _destinationFlareUse.ExpiryLocalIndex ||
                destinationFlare.GetProperty("expiryDurationGameTicks").GetInt32() !=
                    _destinationFlareUse.ExpiryDurationGameTicks ||
                destinationFlare.GetProperty("expiry").GetString() !=
                    "decoded-destroy-self")
                throw new InvalidOperationException(
                    "Fallout saved flare script state is invalid.");
            _destinationFlareExpired = destinationFlare.GetProperty("expired").GetBoolean();
            var savedExpiry = _destinationFlareUse.Program.ExecuteWithActions(
                "start_proc",
                _destinationFlareScriptState,
                new ClassicScriptContext(false, false, _classicScriptGameTime));
            if (_destinationFlareExpired != savedExpiry.DestroySelf ||
                _destinationFlareExpired && !_destinationFlareScriptState.Flag("lit"))
                throw new InvalidOperationException(
                    "Fallout saved flare destruction state is invalid.");
        }
        if (root.TryGetProperty("destinationGenericDoor", out var destinationGenericDoor) &&
            destinationGenericDoor.ValueKind != JsonValueKind.Null)
        {
            if (_destinationGenericDoor is null ||
                destinationGenericDoor.GetProperty("descriptorSha256").GetString() != _destinationGenericDoor.Sha256)
                throw new InvalidOperationException("Fallout save generic-door state does not match its descriptor.");
            _destinationGenericDoorOpen = destinationGenericDoor.GetProperty("open").GetBoolean();
            if (_destinationGenericDoorOpen)
                _walkable[_destinationGenericDoor.Door.Tile] = true;
        }
        if (root.TryGetProperty("destinationMedicLook", out var destinationMedicLook) &&
            destinationMedicLook.ValueKind != JsonValueKind.Null)
        {
            if (_destinationMedicLook is null ||
                destinationMedicLook.GetProperty("descriptorSha256").GetString() != _destinationMedicLook.Sha256)
                throw new InvalidOperationException("Fallout save Medic look state does not match its descriptor.");
            _destinationMedicLookViewed = destinationMedicLook.GetProperty("viewed").GetBoolean();
            if (destinationMedicLook.TryGetProperty("dialogueProcedure", out var procedure) &&
                procedure.ValueKind != JsonValueKind.Null)
            {
                var savedProcedure = procedure.GetString() ?? "";
                if (!_destinationMedicLook.DialogueNodes.ContainsKey(savedProcedure) &&
                    savedProcedure != _destinationMedicLook.RadiationFollowupProcedure)
                    throw new InvalidOperationException(
                        "Fallout save Medic dialogue procedure is not decoded by its descriptor.");
                _destinationMedicDialogueProcedure = savedProcedure;
            }
        }
        if (root.TryGetProperty("destinationReturnExitGrid", out var destinationReturnExitGrid) &&
            destinationReturnExitGrid.ValueKind != JsonValueKind.Null)
        {
            if (_destinationReturnExitGrid is null ||
                destinationReturnExitGrid.GetProperty("descriptorSha256").GetString() != _destinationReturnExitGrid.Sha256)
                throw new InvalidOperationException("Fallout save return exit-grid state does not match its descriptor.");
            if (destinationReturnExitGrid.TryGetProperty("activatedTile", out var activated) &&
                activated.ValueKind != JsonValueKind.Null)
            {
                var activatedReturnTile = activated.GetInt32();
                if (!_destinationReturnExitGrid.IsTrigger(activatedReturnTile))
                    throw new InvalidOperationException("Fallout save return exit-grid activation is not source-authored.");
                _activatedDestinationReturnExitGridTile = activatedReturnTile;
            }
        }
        var savedLootedHostSerials = root.TryGetProperty("lootedMapInventoryHostSerials", out var lootedHosts)
            ? lootedHosts.EnumerateArray().Select(value => value.GetInt32()).ToArray()
            : Array.Empty<int>();
        var duplicateLootedHostSerials = savedLootedHostSerials
            .GroupBy(serial => serial)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateLootedHostSerials.Any(serial =>
                !_returnedToSource || !_returnInactiveDestinationHostSerials.Contains(serial)))
            throw new InvalidOperationException(
                "Fallout save contains a duplicate MAP inventory host outside the explicit returned-map boundary.");
        savedLootedHostSerials = savedLootedHostSerials.Distinct().ToArray();
        if (_characterProfile is not null && !_tagInventoryApplied)
            ApplyTagInventory(_characterProfile);
        if (savedDestination is null)
        {
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
        }
        var sourceHostSerials = _mapInventoryHosts.Keys.ToHashSet();
        if (savedDestination is not null)
            ApplyDestinationTacticalState(savedDestination, tile);
        _lootedMapInventoryHostSerials.Clear();
        _inactiveMapInventoryHostSerials.Clear();
        foreach (var serial in savedLootedHostSerials)
        {
            if (_mapInventoryHosts.ContainsKey(serial))
            {
                if (!_lootedMapInventoryHostSerials.Add(serial))
                    throw new InvalidOperationException(
                        $"Fallout save contains a duplicate MAP inventory host: {serial}");
            }
            else if ((savedDestination is not null && sourceHostSerials.Contains(serial)) ||
                     (_returnedToSource && _returnInactiveDestinationHostSerials.Contains(serial)))
            {
                if (!_inactiveMapInventoryHostSerials.Add(serial))
                    throw new InvalidOperationException(
                        $"Fallout save contains a duplicate inactive MAP inventory host: {serial}");
            }
            else
                throw new InvalidOperationException(
                    $"Fallout save contains an unknown MAP inventory host: {serial}");
        }
        ApplyEquippedWeaponVisibility();
    }

    private object? SaveActiveMap()
    {
        if (_returnedToSource)
            return SaveReturnedSourceMap();
        if (_loadedDestinationPresentation is null)
            return null;
        var transition = _exitGridTransition ?? throw new InvalidOperationException(
            "Fallout active destination has no exit-grid contract.");
        if (string.IsNullOrWhiteSpace(_destinationPresentationPath))
            throw new InvalidOperationException("Fallout active destination has no explicit presentation path.");
        var destination = _loadedDestinationPresentation;
        return new
        {
            schema = ActiveMapSchema,
            kind = "destination",
            mapId = destination.Map.Id,
            sourceFile = destination.Map.SourceFile,
            sourceMapSha256 = destination.SourceMapSha256,
            elevation = transition.DestinationElevation,
            presentation = new
            {
                path = _destinationPresentationPath,
                sha256 = destination.Catalog.CampaignSha256,
            },
            inventoryInteraction = _destinationInventoryInteraction is null ? null : new
            {
                path = _destinationInventoryInteraction.Path,
                sha256 = _destinationInventoryInteraction.Sha256,
            },
            flareUse = _destinationFlareUse is null ? null : new
            {
                path = _destinationFlareUse.Path,
                sha256 = _destinationFlareUse.Sha256,
            },
            genericDoor = _destinationGenericDoor is null ? null : new
            {
                path = _destinationGenericDoor.Path,
                sha256 = _destinationGenericDoor.Sha256,
            },
            medicLook = _destinationMedicLook is null ? null : new
            {
                path = _destinationMedicLook.Path,
                sha256 = _destinationMedicLook.Sha256,
            },
            returnExitGrid = _destinationReturnExitGrid is null ? null : new
            {
                path = _destinationReturnExitGrid.Path,
                sha256 = _destinationReturnExitGrid.Sha256,
            },
        };
    }

    private object SaveReturnedSourceMap()
    {
        var forward = _exitGridTransition ?? throw new InvalidOperationException(
            "Fallout returned source save has no original exit-grid contract.");
        var reverse = _destinationReturnExitGrid ?? throw new InvalidOperationException(
            "Fallout returned source save has no reciprocal exit-grid contract.");
        if (_activatedDestinationReturnExitGridTile is null ||
            reverse.DestinationMapIndex != forward.SourceMapIndex ||
            reverse.DestinationMapName != forward.SourceMapName ||
            !string.Equals(reverse.DestinationMapSha256, _sourceMapSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout returned source save has an incomplete reciprocal MAP join.");
        var inactiveDestinationInventoryHost = _destinationInventoryInteraction is null
            ? null
            : new
            {
                presentation = new
                {
                    path = _destinationPresentationPath ?? throw new InvalidOperationException(
                        "Fallout returned source save has no explicit destination presentation path."),
                    sha256 = Fo1DestinationPresentationContract.Load(
                        _destinationPresentationPath!, forward).Catalog.CampaignSha256,
                },
                interaction = new
                {
                    path = _destinationInventoryInteraction.Path,
                    sha256 = _destinationInventoryInteraction.Sha256,
                    hostSerial = _destinationInventoryInteraction.Host.Serial,
                },
            };
        return new
        {
            schema = ActiveMapSchema,
            kind = "source-return",
            mapIndex = reverse.DestinationMapIndex,
            mapName = reverse.DestinationMapName,
            sourceMapSha256 = reverse.DestinationMapSha256,
            elevation = reverse.DestinationElevation,
            rotation = reverse.DestinationRotation,
            arrivalTile = reverse.DestinationTile,
            sourceSceneSha256 = _sceneSha256,
            returnExitGrid = new { path = reverse.Path, sha256 = reverse.Sha256 },
            inactiveDestinationInventoryHost,
        };
    }

    private void LoadSavedSourceReturn(JsonElement activeMap, int playerTile)
    {
        var forward = _exitGridTransition ?? throw new InvalidOperationException(
            "Fallout returned source save has no original exit-grid contract.");
        if (activeMap.GetProperty("schema").GetString() != ActiveMapSchema ||
            activeMap.GetProperty("kind").GetString() != "source-return" ||
            activeMap.GetProperty("mapIndex").GetInt32() != forward.SourceMapIndex ||
            activeMap.GetProperty("mapName").GetString() != forward.SourceMapName ||
            activeMap.GetProperty("sourceMapSha256").GetString() != _sourceMapSha256 ||
            activeMap.GetProperty("elevation").GetInt32() < 0 ||
            activeMap.GetProperty("rotation").GetInt32() is < 0 or >= Fo1HexMath.DirectionCount ||
            activeMap.GetProperty("arrivalTile").GetInt32() is < 0 or >= Fo1HexMath.Width * Fo1HexMath.Height ||
            activeMap.GetProperty("sourceSceneSha256").GetString() != _sceneSha256)
            throw new InvalidOperationException("Fallout returned source save differs from its V13ENT MAP contract.");
        if (string.IsNullOrWhiteSpace(_destinationReturnExitGridPath))
            throw new InvalidOperationException(
                "Fallout returned source restore requires the explicit reciprocal exit-grid descriptor.");
        var savedReverse = activeMap.GetProperty("returnExitGrid");
        if (savedReverse.GetProperty("path").GetString() != _destinationReturnExitGridPath)
            throw new InvalidOperationException(
                "Fallout returned source reciprocal exit-grid path differs from launch input.");
        var reverse = Fo1ExitGridTransitionContract.Load(_destinationReturnExitGridPath);
        if (savedReverse.GetProperty("sha256").GetString() != reverse.Sha256 ||
            reverse.SourceMapIndex != forward.DestinationMapIndex ||
            reverse.SourceMapName != forward.DestinationMapName ||
            !string.Equals(reverse.SourceMapSha256, forward.DestinationMapSha256, StringComparison.OrdinalIgnoreCase) ||
            reverse.DestinationMapIndex != forward.SourceMapIndex ||
            reverse.DestinationMapName != forward.SourceMapName ||
            !string.Equals(reverse.DestinationMapSha256, _sourceMapSha256, StringComparison.OrdinalIgnoreCase) ||
            reverse.DestinationTile != activeMap.GetProperty("arrivalTile").GetInt32() ||
            reverse.DestinationElevation != activeMap.GetProperty("elevation").GetInt32() ||
            reverse.DestinationRotation != activeMap.GetProperty("rotation").GetInt32() ||
            !_sourceWalkable[playerTile])
            throw new InvalidOperationException("Fallout returned source MAP join drifted or saved an invalid player tile.");
        _destinationReturnExitGrid = reverse;
        _returnInactiveDestinationHostSerials.Clear();
        if (activeMap.TryGetProperty("inactiveDestinationInventoryHost", out var inactiveHost) &&
            inactiveHost.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationPresentationPath) ||
                string.IsNullOrWhiteSpace(_destinationInventoryInteractionPath))
                throw new InvalidOperationException(
                    "Fallout returned source restore requires explicit destination inventory provenance.");
            var presentation = inactiveHost.GetProperty("presentation");
            if (presentation.GetProperty("path").GetString() != _destinationPresentationPath)
                throw new InvalidOperationException(
                    "Fallout returned source destination presentation path differs from launch input.");
            var destination = Fo1DestinationPresentationContract.Load(_destinationPresentationPath, forward);
            if (presentation.GetProperty("sha256").GetString() != destination.Catalog.CampaignSha256)
                throw new InvalidOperationException(
                    "Fallout returned source destination presentation hash drifted.");
            var interaction = inactiveHost.GetProperty("interaction");
            if (interaction.GetProperty("path").GetString() != _destinationInventoryInteractionPath)
                throw new InvalidOperationException(
                    "Fallout returned source destination inventory path differs from launch input.");
            var loadedInteraction = Fo1DestinationInventoryInteractionContract.Load(
                _destinationInventoryInteractionPath, destination, forward);
            if (interaction.GetProperty("sha256").GetString() != loadedInteraction.Sha256 ||
                interaction.GetProperty("hostSerial").GetInt32() != loadedInteraction.Host.Serial)
                throw new InvalidOperationException(
                    "Fallout returned source destination inventory hash drifted.");
            _destinationInventoryInteraction = loadedInteraction;
            _destinationFlareUse = string.IsNullOrWhiteSpace(_destinationFlareUsePath)
                ? null
                : Fo1DestinationFlareUseContract.Load(
                    _destinationFlareUsePath, loadedInteraction);
            _destinationGenericDoor = string.IsNullOrWhiteSpace(_destinationGenericDoorPath)
                ? null
                : Fo1DestinationGenericDoorContract.Load(
                    _destinationGenericDoorPath, destination, forward);
            _destinationMedicLook = string.IsNullOrWhiteSpace(_destinationMedicLookPath)
                ? null
                : _destinationGenericDoor is null
                    ? throw new InvalidOperationException(
                        "Fallout returned source restore requires its explicit generic-door prerequisite.")
                    : Fo1DestinationMedicLookContract.Load(
                        _destinationMedicLookPath, destination, forward, _destinationGenericDoor);
            _returnInactiveDestinationHostSerials.Add(loadedInteraction.Host.Serial);
        }
        _loadedDestinationPresentation = null;
        _returnedToSource = true;
    }

    private Fo1DestinationPresentationContract LoadSavedDestination(JsonElement activeMap)
    {
        var transition = _exitGridTransition ?? throw new InvalidOperationException(
            "Fallout active destination save has no exit-grid contract.");
        if (_activatedExitGridTile is null)
            throw new InvalidOperationException("Fallout active destination save has no committed exit-grid.");
        if (activeMap.GetProperty("schema").GetString() != ActiveMapSchema ||
            activeMap.GetProperty("kind").GetString() != "destination" ||
            activeMap.GetProperty("mapId").GetString() !=
                Path.GetFileNameWithoutExtension(transition.DestinationMapName).ToLowerInvariant() ||
            activeMap.GetProperty("sourceFile").GetString() != transition.DestinationMapName ||
            activeMap.GetProperty("sourceMapSha256").GetString() != transition.DestinationMapSha256 ||
            activeMap.GetProperty("elevation").GetInt32() != transition.DestinationElevation)
            throw new InvalidOperationException("Fallout active destination save differs from its exit-grid contract.");
        if (string.IsNullOrWhiteSpace(_destinationPresentationPath))
            throw new InvalidOperationException(
                "Fallout active destination restore requires an explicit presentation cache path.");
        var presentation = activeMap.GetProperty("presentation");
        if (presentation.GetProperty("path").GetString() != _destinationPresentationPath)
            throw new InvalidOperationException("Fallout active destination save presentation path differs from launch input.");
        var destination = Fo1DestinationPresentationContract.Load(_destinationPresentationPath, transition);
        if (presentation.GetProperty("sha256").GetString() != destination.Catalog.CampaignSha256)
            throw new InvalidOperationException("Fallout active destination save presentation hash drifted.");
        if (activeMap.TryGetProperty("inventoryInteraction", out var interaction) &&
            interaction.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationInventoryInteractionPath) ||
                interaction.GetProperty("path").GetString() != _destinationInventoryInteractionPath)
                throw new InvalidOperationException("Fallout active destination inventory interaction path differs from launch input.");
            var loadedInteraction = Fo1DestinationInventoryInteractionContract.Load(
                _destinationInventoryInteractionPath,
                destination,
                transition);
            if (interaction.GetProperty("sha256").GetString() != loadedInteraction.Sha256)
                throw new InvalidOperationException("Fallout active destination inventory interaction hash drifted.");
            _destinationInventoryInteraction = loadedInteraction;
        }
        if (activeMap.TryGetProperty("flareUse", out var flareUse) &&
            flareUse.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationFlareUsePath) ||
                flareUse.GetProperty("path").GetString() != _destinationFlareUsePath ||
                _destinationInventoryInteraction is null)
                throw new InvalidOperationException("Fallout active destination flare use path differs from launch input.");
            var loadedFlareUse = Fo1DestinationFlareUseContract.Load(
                _destinationFlareUsePath, _destinationInventoryInteraction);
            if (flareUse.GetProperty("sha256").GetString() != loadedFlareUse.Sha256)
                throw new InvalidOperationException("Fallout active destination flare use hash drifted.");
            _destinationFlareUse = loadedFlareUse;
        }
        if (activeMap.TryGetProperty("genericDoor", out var genericDoor) &&
            genericDoor.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationGenericDoorPath) ||
                genericDoor.GetProperty("path").GetString() != _destinationGenericDoorPath)
                throw new InvalidOperationException("Fallout active destination generic-door path differs from launch input.");
            var loadedGenericDoor = Fo1DestinationGenericDoorContract.Load(
                _destinationGenericDoorPath, destination, transition);
            if (genericDoor.GetProperty("sha256").GetString() != loadedGenericDoor.Sha256)
                throw new InvalidOperationException("Fallout active destination generic-door hash drifted.");
            _destinationGenericDoor = loadedGenericDoor;
        }
        if (activeMap.TryGetProperty("medicLook", out var medicLook) &&
            medicLook.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationMedicLookPath) ||
                medicLook.GetProperty("path").GetString() != _destinationMedicLookPath ||
                _destinationGenericDoor is null)
                throw new InvalidOperationException("Fallout active destination Medic look path differs from launch input.");
            var loadedMedicLook = Fo1DestinationMedicLookContract.Load(
                _destinationMedicLookPath, destination, transition, _destinationGenericDoor);
            if (medicLook.GetProperty("sha256").GetString() != loadedMedicLook.Sha256)
                throw new InvalidOperationException("Fallout active destination Medic look hash drifted.");
            _destinationMedicLook = loadedMedicLook;
        }
        if (activeMap.TryGetProperty("returnExitGrid", out var returnExitGrid) &&
            returnExitGrid.ValueKind != JsonValueKind.Null)
        {
            if (string.IsNullOrWhiteSpace(_destinationReturnExitGridPath) ||
                returnExitGrid.GetProperty("path").GetString() != _destinationReturnExitGridPath)
                throw new InvalidOperationException("Fallout active destination return exit-grid path differs from launch input.");
            var loadedReturnExit = Fo1ExitGridTransitionContract.Load(_destinationReturnExitGridPath);
            loadedReturnExit.ValidateAgainstScene(destination.SourceMapSha256);
            if (returnExitGrid.GetProperty("sha256").GetString() != loadedReturnExit.Sha256)
                throw new InvalidOperationException("Fallout active destination return exit-grid hash drifted.");
            _destinationReturnExitGrid = loadedReturnExit;
        }
        _loadedDestinationPresentation = destination;
        return destination;
    }

    private static Fo1CharacterProfile ParseSavedCharacter(
        JsonElement source,
        JsonElement? legacyPresentationIdentity)
    {
        var schema = source.GetProperty("schema").GetString();
        if (!string.Equals(
                schema,
                "opennv-fo1-character/v1",
                StringComparison.Ordinal) &&
            !string.Equals(
                schema,
                "opennv-fo1-character/v2",
                StringComparison.Ordinal) &&
            !string.Equals(
                schema,
                "opennv-fo1-character/v3",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Fallout save contains an unknown character schema: {schema}");
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
        if ((schema is "opennv-fo1-character/v2" or "opennv-fo1-character/v3") &&
            source.TryGetProperty("appearance", out var appearance) &&
            appearance.ValueKind == JsonValueKind.Object)
        {
            if (appearance.GetProperty("schema").GetString() !=
                    Fo1CharacterAppearance.ExpectedSchema ||
                appearance.GetProperty("mode").GetString() !=
                    "hex-local-procedural-custom")
                throw new InvalidOperationException(
                    "Fallout save contains an unknown character appearance.");
            profile = profile with
            {
                Appearance = new Fo1CharacterAppearance(
                    appearance.GetProperty("faceShapeId").GetString()!,
                    appearance.GetProperty("hairStyleId").GetString()!,
                    appearance.GetProperty("skinToneId").GetString()!,
                    appearance.GetProperty("hairColorId").GetString()!,
                    appearance.GetProperty("eyeColorId").GetString()!,
                    appearance.GetProperty("recipeId").GetString()!,
                    appearance.GetProperty("recipeSha256").GetString()!,
                    appearance.GetProperty("generatorId").GetString()!,
                    appearance.GetProperty("portraitPath").GetString()!,
                    appearance.GetProperty("portraitSha256").GetString()!,
                    appearance.GetProperty("portraitWidth").GetInt32(),
                    appearance.GetProperty("portraitHeight").GetInt32()),
            };
        }
        profile = profile with
        {
            Identity = schema == "opennv-fo1-character/v3"
                ? ParseSavedCharacterIdentity(source.GetProperty("identity"))
                : ParseLegacyCharacterIdentity(
                    legacyPresentationIdentity ?? throw new InvalidOperationException(
                        "Legacy Fallout 1 character save has no presentation identity."),
                    profile),
        };
        profile.Validate();
        return profile;
    }

    private static Fo1CharacterIdentity ParseSavedCharacterIdentity(JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object ||
            source.GetProperty("schema").GetString() != Fo1CharacterIdentity.ExpectedSchema)
            throw new InvalidOperationException(
                "Fallout 1 saved character identity schema is unknown.");
        return new Fo1CharacterIdentity(
            source.GetProperty("characterId").GetString()!,
            source.GetProperty("role").GetString()!,
            source.GetProperty("mode").GetString()!,
            source.GetProperty("editingLocked").GetBoolean(),
            source.GetProperty("ownedGcdSha256").GetString()!,
            source.GetProperty("ownedBiographySha256").GetString()!,
            source.GetProperty("ownedPortraitFrmSha256").GetString()!);
    }

    private static Fo1CharacterIdentity ParseLegacyCharacterIdentity(
        JsonElement source,
        Fo1CharacterProfile profile)
    {
        if (source.GetProperty("schema").GetString() !=
                "opennv-fo1-player-presentation-identity/v1" ||
            source.GetProperty("characterName").GetString() != profile.Name ||
            source.GetProperty("sex").GetString() != profile.Sex)
            throw new InvalidOperationException(
                "Legacy Fallout 1 saved presentation identity is invalid.");
        var characterId = source.GetProperty("characterId").GetString()!;
        if (characterId == "custom")
            return Fo1CharacterIdentity.Custom;
        var role = characterId switch
        {
            "max-stone" => "combat",
            "natalia" => "stealth",
            "albert" => "diplomat",
            _ => throw new InvalidOperationException(
                $"Legacy Fallout 1 character identity is unknown: {characterId}"),
        };
        return Fo1CharacterIdentity.Premade(
            characterId,
            role,
            source.GetProperty("ownedGcdSha256").GetString()!,
            Fo1CharacterIdentity.LegacyBiographyHash,
            source.GetProperty("ownedPortraitFrmSha256").GetString()!);
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
                PrototypeSha256.Length != Fo1TacticalSessionNumericContracts.PresentationInt64 || string.IsNullOrWhiteSpace(Skill) ||
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
        IReadOnlyDictionary<string, string> DisplayNames,
        IReadOnlyList<InventoryStack> Base,
        IReadOnlyList<InventoryTagBonus> TagBonuses)
    {
        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(EquippedRangedSymbol) ||
                string.IsNullOrWhiteSpace(EquippedMeleeSymbol) ||
                string.IsNullOrWhiteSpace(AmmunitionSymbol) ||
                AmmunitionRoundsPerObject <= 0 || DisplayNames.Count == 0 ||
                DisplayNames.Any(row => string.IsNullOrWhiteSpace(row.Key) ||
                    string.IsNullOrWhiteSpace(row.Value)) || Base.Count == 0 ||
                Base.Any(row => row.Objects <= 0) ||
                !Base.Any(row => row.Symbol == EquippedRangedSymbol) ||
                !Base.Any(row => row.Symbol == EquippedMeleeSymbol) ||
                !Base.Any(row => row.Symbol == AmmunitionSymbol) ||
                TagBonuses.Select(row => row.Skill).Distinct(StringComparer.Ordinal).Count() !=
                    TagBonuses.Count ||
                TagBonuses.Any(row => string.IsNullOrWhiteSpace(row.Skill) ||
                    row.Items.Count == 0 || row.Items.Any(item => item.Objects <= 0)) ||
                Base.Concat(TagBonuses.SelectMany(row => row.Items))
                    .Any(row => !DisplayNames.ContainsKey(row.Symbol)))
                throw new InvalidOperationException("Fallout starting inventory profile is invalid.");
        }
    }

    internal readonly record struct InventoryStack(string Symbol, string Pid, int Objects);

    internal sealed record MapInventoryHost(
        int Serial,
        int Tile,
        string Pid,
        string PrototypeSha256,
        IReadOnlyList<MapInventoryItem> Items)
    {
        internal void Validate()
        {
            if (Serial < 0 || Tile < 0 || Tile >= Fo1HexMath.Width * Fo1HexMath.Height ||
                string.IsNullOrWhiteSpace(Pid) || PrototypeSha256.Length !=
                Fo1TacticalSessionNumericContracts.PresentationInt64 || Items.Count == 0 ||
                Items.Select(item => item.Index).Distinct().Count() != Items.Count ||
                Items.Any(item => !item.IsValid))
                throw new InvalidOperationException("Fallout MAP inventory-host contract is invalid.");
        }
    }

    internal readonly record struct MapInventoryItem(
        int Index,
        int Serial,
        string Symbol,
        string DisplayName,
        string Pid,
        int Objects,
        string PrototypeSha256,
        string SubtypeName)
    {
        internal bool IsValid => Index >= 0 && Serial >= 0 &&
            !string.IsNullOrWhiteSpace(Symbol) && !string.IsNullOrWhiteSpace(DisplayName) &&
            !string.IsNullOrWhiteSpace(Pid) &&
            Objects > 0 && PrototypeSha256.Length ==
            Fo1TacticalSessionNumericContracts.PresentationInt64 &&
            !string.IsNullOrWhiteSpace(SubtypeName);
    }

    internal readonly record struct MapInventoryPickup(
        MapInventoryHost Host,
        IReadOnlyDictionary<string, int> Inventory);

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
