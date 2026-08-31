using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Fallout2.CharacterStart;

using OpenNV.Runtime.SceneGraph;

namespace OpenNV.Runtime.Campaigns.Fallout2.Temple;

internal sealed record Fo2TempleConfrontationInput(
    string Action,
    Key PhysicalKey,
    JoyButton JoyButton);

internal sealed record Fo2TempleConfrontationProfile(
    string ResourcePath,
    string Sha256,
    string Id,
    string AdapterIdentity,
    int InteractionRangeHexes,
    int MovementActionPointCost,
    string MovementResolution,
    int AttackActionPointCost,
    string HitResolution,
    Fo2TempleConfrontationInput Combat,
    Fo2TempleConfrontationInput Attack,
    Fo2TempleConfrontationInput EndTurn,
    Fo2TempleConfrontationInput Loot,
    Fo2TempleConfrontationInput Inventory,
    string Title,
    string Boundary,
    Vector2 OffsetPixels,
    float WidthPixels,
    int FontSizePixels)
{
    private const string Resource = "res://config/fo2-temple-confrontation-runtime-v1.json";

    internal static Fo2TempleConfrontationProfile Load(
        Fo2TempleConfrontationContract contract)
    {
        var bytes = Godot.FileAccess.GetFileAsBytes(Resource);
        if (bytes.Length == 0)
            throw new FileNotFoundException(
                "Fallout 2 Temple confrontation runtime profile is missing.",
                Resource);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var source = root.GetProperty("source");
        var adapter = root.GetProperty("adapter");
        var input = root.GetProperty("input");
        var ui = root.GetProperty("ui");
        var promotion = root.GetProperty("promotion");
        if (RequiredString(root, "schema") !=
                "opennv-fo2-temple-confrontation-runtime/v1" ||
            RequiredString(root, "campaign") != "Fallout2" ||
            source.GetProperty("mapIndex").GetInt32() != Fo2TemplePresentationCatalog.MapIndex ||
            source.GetProperty("critterSerial").GetInt32() != contract.Critter.Serial ||
            RequiredString(source, "critterPid") != contract.Critter.Pid ||
            RequiredString(source, "critterSid") != contract.Critter.Sid ||
            source.GetProperty("lootSerial").GetInt32() != contract.DefeatLoot.Serial ||
            RequiredString(source, "lootPid") != contract.DefeatLoot.Pid ||
            adapter.GetProperty("targetTurns").GetBoolean() ||
            adapter.GetProperty("generalIntScripts").GetBoolean() ||
            !adapter.GetProperty("boundedGuardianDialogue").GetBoolean() ||
            RequiredString(adapter, "playerActionPointFormula") !=
                "5 + effectiveAgility / 2 - bruiserPenalty" ||
            RequiredString(adapter, "playerHitPointFormula") !=
                "15 + 2 * effectiveEndurance + effectiveStrength" ||
            RequiredString(adapter, "playerMeleeDamageFormula") !=
                "1 + max(1, effectiveStrength - 5) + heavyHandedBonus" ||
            !promotion.GetProperty("interactive").GetBoolean() ||
            !promotion.GetProperty("persistent").GetBoolean() ||
            promotion.GetProperty("retailCombatParity").GetBoolean() ||
            promotion.GetProperty("scriptParity").GetBoolean() ||
            promotion.GetProperty("campaignComplete").GetBoolean())
            throw new InvalidOperationException(
                "Unexpected Fallout 2 Temple confrontation runtime profile.");
        var result = new Fo2TempleConfrontationProfile(
            Resource,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            RequiredString(root, "id"),
            RequiredString(adapter, "identity"),
            adapter.GetProperty("interactionRangeHexes").GetInt32(),
            adapter.GetProperty("movementActionPointCost").GetInt32(),
            RequiredString(adapter, "movementResolution"),
            adapter.GetProperty("attackActionPointCost").GetInt32(),
            RequiredString(adapter, "hitResolution"),
            ReadInput(input, "combat"),
            ReadInput(input, "attack"),
            ReadInput(input, "endTurn"),
            ReadInput(input, "loot"),
            ReadInput(input, "inventory"),
            RequiredString(ui, "title"),
            RequiredString(ui, "boundary"),
            ReadVector2(ui.GetProperty("offsetPixels")),
            ui.GetProperty("widthPixels").GetSingle(),
            ui.GetProperty("fontSizePixels").GetInt32());
        var bindings = new[]
        {
            result.Combat,
            result.Attack,
            result.EndTurn,
            result.Loot,
            result.Inventory,
        };
        if (result.Id != "fo2-temple-guardian-bounded-combat-v1" ||
            result.AdapterIdentity !=
                "source-acklint-dialogue-path-ap-player-turn-melee-no-target-ai-v3" ||
            result.InteractionRangeHexes != 1 || result.MovementActionPointCost != 1 ||
            result.MovementResolution != "exact-adjacent-source-walk-mask-hex-v1" ||
            result.AttackActionPointCost <= 0 ||
            result.HitResolution != "deterministic-hit-no-roll" ||
            result.WidthPixels <= 0.0f || result.FontSizePixels <= 0 ||
            bindings.Select(row => row.Action).Distinct(StringComparer.Ordinal).Count() !=
                bindings.Length ||
            bindings.Select(row => row.PhysicalKey).Distinct().Count() != bindings.Length ||
            bindings.Select(row => row.JoyButton).Distinct().Count() != bindings.Length)
            throw new InvalidOperationException(
                "Fallout 2 Temple confrontation profile values are invalid.");
        return result;
    }

    internal void ConfigureInput()
    {
        foreach (var binding in new[] { Combat, Attack, EndTurn, Loot, Inventory })
        {
            if (InputMap.HasAction(binding.Action))
                InputMap.EraseAction(binding.Action);
            InputMap.AddAction(binding.Action);
            InputMap.ActionAddEvent(binding.Action, new InputEventKey
            {
                PhysicalKeycode = binding.PhysicalKey,
            });
            InputMap.ActionAddEvent(binding.Action, new InputEventJoypadButton
            {
                ButtonIndex = binding.JoyButton,
            });
        }
    }

    private static Fo2TempleConfrontationInput ReadInput(
        JsonElement source,
        string property)
    {
        var value = source.GetProperty(property);
        return new Fo2TempleConfrontationInput(
            RequiredString(value, "action"),
            Enum.TryParse<Key>(RequiredString(value, "physicalKey"), true, out var key) &&
                key != Key.None
                ? key
                : throw new InvalidOperationException(
                    $"Fallout 2 Temple input key is invalid: {property}"),
            Enum.TryParse<JoyButton>(RequiredString(value, "joyButton"), true, out var button) &&
                button != JoyButton.Invalid
                ? button
                : throw new InvalidOperationException(
                    $"Fallout 2 Temple joypad button is invalid: {property}"));
    }

    private static string RequiredString(JsonElement source, string property)
    {
        var value = source.GetProperty(property).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Fallout 2 Temple confrontation string is empty: {property}");
    }

    private static Vector2 ReadVector2(JsonElement source)
    {
        var values = source.EnumerateArray().Select(row => row.GetSingle()).ToArray();
        if (values.Length != 2 || values.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException(
                "Fallout 2 Temple confrontation vector is invalid.");
        return new Vector2(values[0], values[1]);
    }
}

internal sealed record Fo2TempleConfrontationState(
    int TargetHitPoints,
    int PlayerActionPoints,
    bool CombatActive,
    bool SpearLooted,
    bool SpearEquipped)
{
    internal void Validate(
        Fo2TempleConfrontationContract contract,
        int maximumPlayerActionPoints)
    {
        if (TargetHitPoints is < 0 || TargetHitPoints > contract.Critter.CurrentHitPoints ||
            PlayerActionPoints is < 0 || PlayerActionPoints > maximumPlayerActionPoints ||
            CombatActive && TargetHitPoints == 0 || SpearLooted && TargetHitPoints != 0 ||
            SpearEquipped && !SpearLooted)
            throw new InvalidOperationException(
                "Fallout 2 Temple confrontation save state is invalid.");
    }
}

internal sealed partial class Fo2TempleConfrontationRuntime : CanvasLayer
{
    private const int HudLayer = 20;
    private const int StrengthSpecialIndex = 0;
    private const int EnduranceSpecialIndex = 2;
    private const int IntelligenceSpecialIndex = 4;
    private const int AgilitySpecialIndex = 5;
    private const int BaseActionPoints = 5;
    private const int BaseHitPoints = 15;
    private const int NeutralSpecialValue = 5;
    private const int MaximumSpecialValue = 10;
    private const float DefeatedColorChannel = 0.35f;
    private const float DefeatedAlpha = 0.65f;
    private const float DegreesPerSourceRotation =
        360.0f / Fo1HexMath.DirectionCount;
    private readonly Fo2TempleConfrontationContract _contract;
    private readonly Fo2TempleConfrontationProfile _profile;
    private readonly Fo2ArroyoCavesPlayerBody _player;
    private readonly Sprite3D _targetSprite;
    private readonly int _maximumPlayerHitPoints;
    private readonly int _maximumPlayerActionPoints;
    private readonly int _playerMeleeDamage;
    private readonly Label _vitals;
    private readonly Label _status;
    private readonly Fo2TempleInventoryScreen _inventory;
    private readonly Fo2TempleGuardianDialogue _dialogue;
    private readonly Fo2TempleTransitionRuntime _transition;
    private Fo2TempleConfrontationState _state;

    internal event Action? StateChanged;
    internal Fo2TempleConfrontationState State => _state;
    internal int MaximumPlayerActionPoints => _maximumPlayerActionPoints;
    internal int MaximumPlayerHitPoints => _maximumPlayerHitPoints;
    internal int PlayerMeleeDamage => _playerMeleeDamage;
    internal bool TargetVisible => _targetSprite.Visible;
    internal bool InventoryVisible => _inventory.IsOpen;
    internal string InventoryCharacterText => _inventory.CharacterText;
    internal string InventoryItemText => _inventory.ItemText;
    internal string InventoryInspectionText => _inventory.InspectionText;
    internal string InventorySourceLogicalPath => _inventory.SourceLogicalPath;
    internal string InventorySourceSha256 => _inventory.SourceSha256;
    internal string InventoryAction => _profile.Inventory.Action;
    internal Key InventoryPhysicalKey => _profile.Inventory.PhysicalKey;
    internal string InventoryEquipAction => _profile.Attack.Action;
    internal string InventoryInspectAction => _profile.Loot.Action;
    internal bool InventorySpearSelected => _inventory.SpearSelected;
    internal bool InventoryInspectionVisible => _inventory.InspectionVisible;
    internal bool DialogueVisible => _dialogue.IsOpen;
    internal string DialogueNodeId => _dialogue.CurrentNodeId;
    internal string DialogueReplyText => _dialogue.ReplyText;
    internal IReadOnlyList<Fo2TempleGuardianDialogueOption> DialogueOptions =>
        _dialogue.AvailableOptions;
    internal IReadOnlyList<string> DialogueVisitedNodes => _dialogue.VisitedNodes;
    internal Fo2TempleGuardianScript GuardianScript => _contract.GuardianScript;
    internal int MovementActionPointCost => _profile.MovementActionPointCost;
    internal string MovementResolution => _profile.MovementResolution;
    internal bool TargetPlacementExact =>
        _targetSprite.GetMeta("map_tile").AsInt32() == _contract.Critter.Tile &&
        _targetSprite.GetMeta("map_serial").AsInt32() == _contract.Critter.Serial &&
        _targetSprite.GetMeta("source_pid").AsString() == _contract.Critter.Pid &&
        _targetSprite.GetMeta("source_sid").AsString() == _contract.Critter.Sid &&
        _targetSprite.Position.IsEqualApprox(Fo1HexMath.Center(_contract.Critter.Tile)) &&
        Mathf.IsEqualApprox(
            _targetSprite.RotationDegrees.Y,
            -_contract.Critter.Rotation * DegreesPerSourceRotation);
    internal string TargetNodePath => _targetSprite.GetPath().ToString();
    internal string TargetSourceLogicalPath =>
        _targetSprite.GetMeta("source_logical_path").AsString();
    internal string TargetSourceSha256 =>
        _targetSprite.GetMeta("source_sha256").AsString();
    internal Vector3 TargetWorldPosition => _targetSprite.Position;
    internal Vector3 TargetRotationDegrees => _targetSprite.RotationDegrees;
    internal string PlayerSourceAnimationCode => _player.Presentation.AnimationCode;
    internal bool PlayerUsesOwnedFrmRelief =>
        _player.Presentation.UsesOwnedFrmRelief;
    internal string PlayerSourceLogicalPath =>
        _player.Presentation.CurrentFrame.LogicalPath;
    internal string PlayerSourceSha256 => _player.Presentation.CurrentFrame.SourceSha256;
    internal string PlayerPngSha256 => _player.Presentation.CurrentFrame.PngSha256;
    internal int PlayerReliefIslands => _player.Presentation.ReliefIslands;
    internal int PlayerMoldedFaceTriangles =>
        _player.Presentation.MoldedFaceTriangles;
    internal int PlayerMoldedSideTriangles =>
        _player.Presentation.MoldedSideTriangles;
    internal bool PlayerEquippedCompositeVisible =>
        _player.Presentation.EquippedCompositeVisible;
    internal bool PlayerEquipmentSocketResolved =>
        _player.Presentation.EquipmentSocketResolved;
    internal string PlayerEquipmentSocketName => _player.Presentation.EquipmentSocketName;
    internal bool PlayerEquippedWeaponGeometryVisible =>
        _player.Presentation.EquippedWeaponGeometryVisible;

    private Fo2TempleConfrontationRuntime(
        Fo2TempleConfrontationContract contract,
        Fo2TempleConfrontationProfile profile,
        Fo2ArroyoCavesPlayerBody player,
        Sprite3D targetSprite,
        Fo2CharacterSelection character,
        Fo2CharacterStartAsset inventorySource,
        Fo2TempleTransitionRuntime transition,
        Fo2TempleConfrontationState? restored)
    {
        _contract = contract;
        _profile = profile;
        _player = player;
        _targetSprite = targetSprite;
        _transition = transition;
        _maximumPlayerHitPoints = MaximumHitPoints(character);
        _maximumPlayerActionPoints = MaximumActionPoints(character);
        _playerMeleeDamage = MeleeDamage(character);
        _state = restored ?? new Fo2TempleConfrontationState(
            contract.Critter.CurrentHitPoints,
            _maximumPlayerActionPoints,
            false,
            false,
            false);
        _state.Validate(contract, _maximumPlayerActionPoints);
        Name = "FO2_TEMPLE_BOUNDED_CONFRONTATION";
        Layer = HudLayer;
        var panel = new PanelContainer
        {
            Name = "FO2_TEMPLE_COMBAT_HUD",
            Position = profile.OffsetPixels,
            CustomMinimumSize = new Vector2(profile.WidthPixels, 0.0f),
        };
        var content = new VBoxContainer();
        panel.AddChild(content);
        content.AddChild(Label(profile.Title));
        _vitals = Label("");
        content.AddChild(_vitals);
        _status = Label(profile.Boundary);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(_status);
        content.AddChild(Label("Inventory: press I / controller Y"));
        content.AddChild(Label(
            "C/X: Combat   Space/A: Attack   Enter/Start: End Turn   E/B: Talk/Loot   I/Y: Inventory"));
        AddChild(panel);
        _inventory = new Fo2TempleInventoryScreen(
            inventorySource,
            character,
            contract.DefeatLoot,
            profile.FontSizePixels);
        AddChild(_inventory);
        _dialogue = new Fo2TempleGuardianDialogue(
            contract.GuardianScript,
            character.Profile.Name,
            EffectiveIntelligence(character),
            player.GetMeta("character_fid").AsString(),
            profile.WidthPixels,
            profile.FontSizePixels);
        AddChild(_dialogue);
        RefreshPresentation();
    }

    internal static Fo2TempleConfrontationRuntime Build(
        Fo2TemplePresentationCatalog catalog,
        Fo2TempleSceneCoverage scene,
        Fo2ArroyoCavesPlayerBody player,
        Fo2CharacterSelection character,
        Fo2CharacterStartAsset inventorySource,
        Fo2TempleTransitionRuntime transition,
        Fo2TempleConfrontationState? restored = null)
    {
        if (player.CurrentMapIndex != Fo2TemplePresentationCatalog.MapIndex ||
            player.CurrentMapSha256 != catalog.MapSha256)
            throw new InvalidOperationException(
                "Fallout 2 Temple confrontation requires the active owned Temple map.");
        var profile = Fo2TempleConfrontationProfile.Load(catalog.Confrontation);
        profile.ConfigureInput();
        var targets = NodeTraversal.Descendants<Sprite3D>(scene.Root)
            .Where(sprite => sprite.HasMeta("map_serial") &&
                sprite.GetMeta("map_serial").AsInt32() == catalog.Confrontation.Critter.Serial)
            .ToArray();
        var targetPlacement = catalog.ObjectPlacements.Single(row =>
            row.Serial == catalog.Confrontation.Critter.Serial);
        var targetArtifact = catalog.Artifacts.TryGetValue(
            targetPlacement.ArtifactId,
            out var admittedTargetArtifact)
            ? admittedTargetArtifact
            : throw new InvalidOperationException(
                "Fallout 2 Temple confrontation target artifact is absent.");
        if (targets.Length != 1 ||
            targets[0].GetMeta("source_pid").AsString() != catalog.Confrontation.Critter.Pid ||
            targets[0].GetMeta("source_sid").AsString() != catalog.Confrontation.Critter.Sid ||
            targets[0].GetMeta("source_logical_path").AsString() !=
                targetPlacement.LogicalPath ||
            targets[0].GetMeta("source_sha256").AsString() != targetArtifact.SourceSha256)
            throw new InvalidOperationException(
                "Fallout 2 Temple confrontation target sprite identity is absent.");
        var runtime = new Fo2TempleConfrontationRuntime(
            catalog.Confrontation,
            profile,
            player,
            targets[0],
            character,
            inventorySource,
            transition,
            restored);
        if (!runtime.TargetPlacementExact || runtime.TargetSourceSha256.Length != 64)
            throw new InvalidOperationException(
                "Fallout 2 Temple confrontation target placement drifted.");
        scene.Root.AddChild(runtime);
        return runtime;
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (Input.IsActionJustPressed(_profile.Inventory.Action))
        {
            ToggleInventory();
            return;
        }
        if (_inventory.IsOpen)
        {
            if (Input.IsActionJustPressed(_profile.Attack.Action))
                ToggleSpearEquipment();
            else if (Input.IsActionJustPressed(_profile.Loot.Action))
                InspectSelectedInventoryItem();
            return;
        }
        if (_dialogue.IsOpen)
            return;
        if (Input.IsActionJustPressed(_profile.Combat.Action))
            ToggleCombat();
        if (Input.IsActionJustPressed(_profile.Attack.Action))
            Attack();
        if (Input.IsActionJustPressed(_profile.EndTurn.Action))
            EndTurn();
        if (Input.IsActionJustPressed(_profile.Loot.Action))
        {
            if (_state.TargetHitPoints > 0 && !_state.CombatActive)
                Talk();
            else if (!TryApplyTempleExit())
                Loot();
        }
    }

    internal void ToggleInventory()
    {
        if (_inventory.Close())
        {
            _player.SetPhysicsProcess(true);
            return;
        }
        _player.Presentation.StopWalking();
        _player.SetPhysicsProcess(false);
        _inventory.Open(_state.SpearLooted, _state.SpearEquipped);
    }

    internal bool CloseInventoryIfOpen()
    {
        if (_dialogue.Close())
        {
            _player.SetPhysicsProcess(true);
            return true;
        }
        if (!_inventory.Close())
            return false;
        _player.SetPhysicsProcess(true);
        return true;
    }

    internal bool ToggleSpearEquipment()
    {
        if (!_inventory.IsOpen || !_inventory.SpearSelected || !_state.SpearLooted)
            return false;
        _state = _state with { SpearEquipped = !_state.SpearEquipped };
        Changed();
        return true;
    }

    internal bool InspectSelectedInventoryItem()
    {
        if (!_inventory.IsOpen || !_inventory.SpearSelected || !_state.SpearLooted)
            return false;
        _inventory.ShowInspection(_state.SpearEquipped);
        return true;
    }

    internal bool ToggleCombat()
    {
        if (_dialogue.IsOpen)
            return false;
        if (_state.TargetHitPoints == 0)
        {
            SetStatus("The guardian is defeated; use Loot from an adjacent hex.");
            return false;
        }
        _state = _state with { CombatActive = !_state.CombatActive };
        SetStatus(_state.CombatActive
            ? "Combat active. This bounded adapter owns player AP only; target AI is unresolved."
            : "Combat inactive.");
        Changed();
        return true;
    }

    internal bool Talk()
    {
        if (_dialogue.IsOpen || _inventory.IsOpen || _state.CombatActive ||
            _state.TargetHitPoints == 0 || !Adjacent())
        {
            SetStatus("Talk requires a live, non-hostile adjacent guardian.");
            return false;
        }
        _player.Presentation.StopWalking();
        _player.SetPhysicsProcess(false);
        _dialogue.Open();
        SetStatus("Owned ACKlint dialogue active; general INT execution remains disabled.");
        return true;
    }

    internal bool SelectDialogueOption(int messageId)
    {
        if (!_dialogue.Choose(messageId))
            return false;
        if (!_dialogue.IsOpen)
        {
            _player.SetPhysicsProcess(true);
            SetStatus("Owned ACKlint dialogue branch complete.");
        }
        return true;
    }

    internal bool Attack()
    {
        if (!_state.CombatActive || _state.TargetHitPoints == 0)
        {
            SetStatus("Enter combat before attacking a live target.");
            return false;
        }
        if (!Adjacent())
        {
            SetStatus("Move to a source-walkable hex adjacent to the guardian.");
            return false;
        }
        if (_state.PlayerActionPoints < _profile.AttackActionPointCost)
        {
            SetStatus("Not enough AP. End Turn to restore player AP.");
            return false;
        }
        var remaining = Math.Max(0, _state.TargetHitPoints - _playerMeleeDamage);
        _state = _state with
        {
            TargetHitPoints = remaining,
            PlayerActionPoints = _state.PlayerActionPoints - _profile.AttackActionPointCost,
            CombatActive = remaining > 0,
        };
        SetStatus(remaining > 0
            ? $"Deterministic bounded hit: {_playerMeleeDamage} damage."
            : $"{_contract.Critter.DisplayName} defeated. The exact nested loot is now available.");
        Changed();
        return true;
    }

    internal bool TryMove(int destinationTile)
    {
        if (!_state.CombatActive || _state.TargetHitPoints == 0 ||
            _state.PlayerActionPoints < _profile.MovementActionPointCost ||
            destinationTile == _contract.Critter.Tile ||
            !_player.TryTacticalStep(destinationTile))
        {
            SetStatus("Combat movement requires AP and one source-walkable adjacent hex.");
            return false;
        }
        _state = _state with
        {
            PlayerActionPoints =
                _state.PlayerActionPoints - _profile.MovementActionPointCost,
        };
        SetStatus(
            $"Source hex step to {destinationTile}: " +
            $"{_profile.MovementActionPointCost} AP.");
        Changed();
        return true;
    }

    internal bool TryPostGuardianStep(int destinationTile)
    {
        _ = destinationTile;
        SetStatus(
            "Klint is not the final trial guardian. Complete Cameron in ARCAVES first.");
        return false;
    }

    internal bool TryApplyTempleExit()
    {
        SetStatus(
            "ARTEMPLE exit remains blocked until Cameron sets global 10 and ACKlint moves the gate.");
        return false;
    }

    internal bool EndTurn()
    {
        if (_state.TargetHitPoints == 0 || !_state.CombatActive)
        {
            SetStatus(_state.TargetHitPoints == 0
                ? "Combat is complete."
                : "Enter combat before ending the player turn.");
            return false;
        }
        _state = _state with { PlayerActionPoints = _maximumPlayerActionPoints };
        SetStatus("Player AP restored. Target turns/AI are not implemented in this adapter.");
        Changed();
        return true;
    }

    internal bool Loot()
    {
        if (_state.TargetHitPoints != 0 || _state.SpearLooted || !Adjacent())
        {
            SetStatus(_state.TargetHitPoints != 0
                ? "The source-owned loot remains on the live guardian."
                : _state.SpearLooted
                    ? "The exact spear is already in inventory."
                    : "Move adjacent before looting.");
            return false;
        }
        _state = _state with { SpearLooted = true };
        SetStatus(
            $"Looted {_contract.DefeatLoot.Quantity} × {_contract.DefeatLoot.DisplayName}. " +
            "Open Inventory to inspect the exact stack.");
        Changed();
        return true;
    }

    internal static int MaximumActionPoints(Fo2CharacterSelection character)
    {
        var agility = Effective(character.Profile, AgilitySpecialIndex);
        var bruiserPenalty = character.Profile.Traits.Contains("Bruiser", StringComparer.Ordinal)
            ? 2
            : 0;
        return Math.Max(1, BaseActionPoints + agility / 2 - bruiserPenalty);
    }

    internal static int MaximumHitPoints(Fo2CharacterSelection character)
    {
        var strength = Effective(character.Profile, StrengthSpecialIndex);
        var endurance = Effective(character.Profile, EnduranceSpecialIndex);
        return BaseHitPoints + 2 * endurance + strength;
    }

    internal static int EffectiveIntelligence(Fo2CharacterSelection character) =>
        Effective(character.Profile, IntelligenceSpecialIndex);

    private static int MeleeDamage(Fo2CharacterSelection character)
    {
        var strength = Effective(character.Profile, StrengthSpecialIndex);
        var heavyHandedBonus = character.Profile.Traits.Contains(
                "Heavy Handed",
                StringComparer.Ordinal)
            ? 4
            : 0;
        return 1 + Math.Max(1, strength - NeutralSpecialValue) + heavyHandedBonus;
    }

    private bool Adjacent() =>
        Fo1HexMath.Distance(_player.CurrentTile, _contract.Critter.Tile) <=
        _profile.InteractionRangeHexes;

    private void Changed()
    {
        _state.Validate(_contract, _maximumPlayerActionPoints);
        RefreshPresentation();
        StateChanged?.Invoke();
    }

    private void SetStatus(string value)
    {
        _status.Text = value;
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        _player.Presentation.SetSpearEquipped(_contract.DefeatLoot, _state.SpearEquipped);
        _targetSprite.Visible = !_state.SpearLooted;
        _targetSprite.Modulate = _state.TargetHitPoints == 0
            ? new Color(
                DefeatedColorChannel,
                DefeatedColorChannel,
                DefeatedColorChannel,
                DefeatedAlpha)
            : Colors.White;
        _vitals.Text =
            $"HP {_maximumPlayerHitPoints}/{_maximumPlayerHitPoints}   " +
            $"AP {_state.PlayerActionPoints}/{_maximumPlayerActionPoints}   " +
            $"{_contract.Critter.DisplayName} HP {_state.TargetHitPoints}/" +
            $"{_contract.Critter.CurrentHitPoints}   " +
            $"Combat {(_state.CombatActive ? "ON" : "OFF")}";
        _inventory.Refresh(_state.SpearLooted, _state.SpearEquipped);
    }

    private Label Label(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", _profile.FontSizePixels);
        return label;
    }

    private static int Effective(Fo2CharacterProfile profile, int index)
    {
        var value = profile.Special[index];
        if (profile.Traits.Contains("Gifted", StringComparer.Ordinal))
            value++;
        if (index == StrengthSpecialIndex &&
            profile.Traits.Contains("Bruiser", StringComparer.Ordinal))
            value += 2;
        if (index == AgilitySpecialIndex &&
            profile.Traits.Contains("Small Frame", StringComparer.Ordinal))
            value++;
        return Math.Clamp(value, 1, MaximumSpecialValue);
    }

}
