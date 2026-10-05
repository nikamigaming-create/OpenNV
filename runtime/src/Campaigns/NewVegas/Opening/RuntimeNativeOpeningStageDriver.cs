using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.Presentation.Rendering;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver : Node
{
    private RuntimeNativePlayer _player = null!;
    private FalloutOpeningStageMachine? _machine;
    private FalloutOpeningStageTransitionGraph _transitions = null!;
    private string _sourceQuestEditorId = string.Empty;
    private FalloutPlayerControlState _sourceControls = FalloutPlayerControlState.AllEnabled;
    private bool _configured;
    private FalloutOpeningInventoryGrant _openingGrant = null!;
    private FalloutNativeRaceSexContract _raceSexContract = null!;
    private FalloutNativeVigorContract _vigorContract = null!;
    private FalloutNativeVigorContract? _specialMenuContract;
    private FalloutNativeTagSkillContract _tagSkillContract = null!;
    private FalloutNativeTraitFarewellContract _traitFarewellContract = null!;
    private FalloutPluginStack _pluginStack = null!;
    private string _savePath = string.Empty;
    private string _saveCompatibilityId = string.Empty;
    private FalloutFormKey _activeCell;
    private string _playerName = string.Empty;
    private FalloutNativeRaceSexSelection _character = null!;
    private long _characterRevision;
    internal long PlayerAppearanceRevision => _characterRevision + _scripts.Session.PlayerAppearanceRevision +
        (_scripts.References?.ActorAppearanceRevision(_pluginStack.RuntimeFormKey(0x14)) ?? 0);
    private FalloutPlayerActorValues _playerActorValues = null!;
    private FalloutPlayerVitals _vitals = null!;
    private FalloutPlayerExperience _experience = null!;
    private FalloutPlayerSkills _playerSkills = null!;
    private FalloutPlayerIngestibles _ingestibles = null!;
    private FalloutPlayerTagSkills _tagSkills = null!;
    private IReadOnlyList<FalloutNativeTraitIdentity> _traits = [];
    private RuntimeNativePlayerNameEntry? _nameEntry;
    private RuntimeNativeRaceSexEntry? _raceSexEntry;
    private FalloutRaceMenuDevices? _raceMenuDevices;
    private bool _raceMenuDevicesRead;
    private string _raceMenuCommand = "showracemenu";
    private RuntimeNativeVigorEntry? _vigorEntry;
    private RuntimeNativeTagSkillEntry? _tagSkillEntry;
    private FalloutTagSkillMenuRequest? _tagMenuRequest;
    private FalloutNativeTagSkillContract? _activeTagSkillContract;
    private RuntimeNativeTraitEntry? _traitEntry;
    private bool _stage200Saved;
    private FalloutOpeningControlGraph _controls = null!;
    private bool _moviePlaying;
    private RuntimeNativeSpeech? _speech;
    private FaceGenLipConfiguration _lipConfiguration = null!;
    private string? _speechStage;
    private RuntimeNativePlayerPackage? _playerPackage;
    private FalloutImageSpaceState _imageSpaceState = null!;
    private FalloutQuestState _quests = null!;
    private FalloutQuestScripts _scripts = null!;
    private FalloutQuestScriptHost _scriptHost = null!;
    private FalloutPlayerInventory _inventory = null!;
    private Func<FalloutQuestScriptsSnapshot?> _captureScripts = null!;
    private FalloutGlobalState? _globals;
    private FalloutGameTime? _gameTime;
    private FalloutSkyLightingState? _skyLighting;
    private bool _restoringEnteredStage;
    private Func<RuntimeNativeImageSpace> _imageSpacePresenter = null!;
    private string? _executionError;
    internal string? ExecutionError
    {
        get => _executionError;
        private set { _executionError = value; _stageResultDriverFailure = null; }
    }
    internal string? ExecutionFault => ExecutionError ?? _speech?.Error ?? _conversation?.ExecutionFault ?? TerminalExecutionFault;
    internal string? BlockingExecutionError => _stageResultDriverFailure?.Error == ExecutionError ? null : ExecutionError;
    internal string? BlockingExecutionFault => BlockingExecutionError ?? _speech?.Error ?? _conversation?.ExecutionFault ?? BlockingTerminalExecutionFault;
    private readonly List<object> _headTrackingCommands = [];
    internal object[] HeadTrackingCommands => _headTrackingCommands.ToArray();

    internal string QuestEditorId => _machine?.QuestEditorId ?? _sourceQuestEditorId;
    internal short Stage => _machine?.Stage ?? _quests.Stage(FalloutDialogueTopic.Find(_pluginStack, "QUST", _sourceQuestEditorId).FormKey);
    internal float? TimerSeconds => _machine?.TimerSeconds;
    internal IReadOnlyCollection<string> PendingBlockers => _machine?.PendingBlockers ?? [];
    private FalloutPlayerControlState PlayerControls => _machine?.ControlState ?? _sourceControls;
    internal FalloutFormKey ActiveCell => _activeCell;
    internal void EnterWorldCell(FalloutFormKey cell) => _activeCell = cell;
    internal void RequestWorldSave() => _saveRequested = true;
    internal bool HasCampaignSave => File.Exists(_savePath);
    internal string PlayerName => _playerName;
    internal int PlayerLevel => SourcePlayerLevel;
    internal FalloutNativeSpecialState Special => _playerActorValues.BaseSpecial;
    internal FalloutSpecialAllocationBinding SpecialAllocationBinding => _playerActorValues.AllocationBinding;
    internal GameplayVitals Vitals => _vitals.State;
    private FalloutActorDefenseResolver? _incomingDefense;
    internal void DamagePlayer(FalloutWeaponDamage damage, byte part)
    {
        _incomingDefense ??= new(_pluginStack);
        var armor = _inventory.Equipped.Select(_pluginStack.RuntimeFormKey)
            .Where(key => _pluginStack.GetEffective(key).Signature == "ARMO").ToArray();
        var defense = _incomingDefense.Read(_raceSexContract.Player, _inventory, armor);
        _vitals.Damage(defense.Absorb(damage.Amount, FalloutGameSettingFloats.Read(_pluginStack, "fMinDamMultiplier"), damage.AmmoEffects),
            part, damage.LimbMultiplier);
    }
    internal object? SpeechState => _speech?.State;
    internal FalloutSpeechSubtitle? Subtitle => _speech?.Subtitle;
    internal Action<FalloutSpeechSubtitle>? PrepareSubtitle { get; set; }
    internal Action<FalloutSpeechCompletionReceipt>? SayToCompleted { get; set; }
    internal object? PlayerPackageState => _playerPackage?.State;
    internal object? CharacterCreationState => _raceSexEntry?.State;
    internal object? TraitMenuState => _traitEntry?.State;
    internal object? VigorState => _vigorEntry?.State;
    internal IEnumerable<uint> ActiveMenus()
    {
        if (_nameEntry is not null) yield return 1051;
        if (_raceSexEntry is not null) yield return 1036;
        if (_vigorEntry is not null) yield return 1074;
        if (_specialBookEntry is not null) yield return FalloutSpecialBookPresentation.MenuId;
        if (_tagSkillEntry is not null) yield return 1048;
        if (_traitEntry is not null) yield return 1084;
        if (_recipeMenu is not null) yield return 1077;
        if (_barterMenu is not null) yield return 1053;
        if (_terminalMenus.Values.Any(menu => menu.Active)) yield return FalloutTerminal.MenuId;
    }

    internal void Configure(
        FalloutOpeningStageTransitionGraph transitions,
        FalloutOpeningControlGraph controls,
        RuntimeNativePlayer player,
        FalloutOpeningInventoryGrant openingGrant,
        FalloutNativeRaceSexContract raceSexContract,
        FalloutNativeVigorContract vigorContract,
        FalloutNativeTagSkillContract tagSkillContract,
        FalloutNativeTraitFarewellContract traitFarewellContract,
        FalloutPluginStack pluginStack,
        string savePath,
        string saveCompatibilityId,
        FalloutFormKey initialCell,
        FalloutNativeCampaignRestore? restore,
        FaceGenLipConfiguration lipConfiguration,
        FalloutImageSpaceState imageSpaceState,
        FalloutQuestState quests,
        FalloutQuestScripts scripts,
        FalloutPlayerInventory inventory,
        Func<FalloutQuestScriptsSnapshot?> captureScripts,
        FalloutGlobalState? globals,
        FalloutGameTime? gameTime,
        FalloutSkyLightingState? skyLighting,
        Func<RuntimeNativeImageSpace> imageSpacePresenter,
        string initialQuestEditorId,
        short initialStage, FalloutPlayerControlState? initialControls = null)
    {
        if (_configured)
            throw new InvalidOperationException("Native opening stage driver was already configured.");
        _configured = true;
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _openingGrant = openingGrant ?? throw new ArgumentNullException(nameof(openingGrant));
        _raceSexContract = raceSexContract ?? throw new ArgumentNullException(nameof(raceSexContract));
        _vigorContract = vigorContract ?? throw new ArgumentNullException(nameof(vigorContract));
        _tagSkillContract = tagSkillContract ?? throw new ArgumentNullException(nameof(tagSkillContract));
        _traitFarewellContract = traitFarewellContract ??
            throw new ArgumentNullException(nameof(traitFarewellContract));
        _pluginStack = pluginStack ?? throw new ArgumentNullException(nameof(pluginStack));
        _controls = controls;
        _transitions = new(transitions.Transitions.Where(transition => transition.Kind != "stage-script" || transition.Blockers.Count != 0)
            .Select(transition => transition.Kind == "stage-script" ? transition with { Kind = "script-wait" } : transition).ToArray());
        _sourceQuestEditorId = restore?.State.QuestEditorId ?? initialQuestEditorId;
        _sourceControls = restore is null ? initialControls ?? FalloutPlayerControlState.AllEnabled : FalloutNativeCampaignSave.RestorePlayerControls(restore.State);
        _savePath = Path.GetFullPath(savePath ?? throw new ArgumentNullException(nameof(savePath)));
        _saveCompatibilityId = string.IsNullOrWhiteSpace(saveCompatibilityId)
            ? throw new ArgumentException(
                "Native save compatibility identity is required.", nameof(saveCompatibilityId))
            : saveCompatibilityId;
        _activeCell = restore?.State.ActiveCell ?? initialCell;
        _stage200Saved = restore?.State.CharacterCreationComplete == true;
        _playerName = restore?.State.PlayerName ?? FalloutDialogueTopic.Text(
            FalloutDialogueTopic.Find(pluginStack, "GMST", "sDefaultPlayerName")
                .ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span);
        _character = restore?.State.Character ?? raceSexContract.Initial;
        FalloutNativeRaceSexResolver.Validate(raceSexContract, _character);
        _playerActorValues = new(pluginStack, restore?.State.PlayerActorValues,
            restore?.State.PlayerActorValues is null ? restore?.State.Special : null);
        _tagSkills = new(pluginStack, tagSkillContract, restore?.State.TagSkillSlots, restore?.State.TagSkills);
        _traits = restore?.State.Traits ?? [];
        FalloutNativeTraitFarewellResolver.ValidateTraits(traitFarewellContract, _traits);
        _lipConfiguration = lipConfiguration;
        _imageSpaceState = imageSpaceState;
        _quests = quests;
        _scripts = scripts;
        BindSourceManualSaves();
        _scripts.References!.BindActorAlert(_pluginStack.RuntimeFormKey(0x14), _player.Activity);
        _restoreFinishedSpeech = restore?.State.FinishedSpeech;
        _restoreFinishedSpeechStage = restore?.State.FinishedSpeechStage;
        _restoreStageResults = restore?.State.QuestStageResults;
        _restoreStageResultFailure = restore?.State.StageResultFailure;
        _restoreTerminalResults = restore?.State.TerminalResults;
        _inventory = inventory;
        _globals = globals;
        _scripts.References!.BindPlayerAppearance(() => PlayerCreationState);
        _scriptHost = new((quest, stage) =>
        {
            var source = _controls.Quests.Values.SelectMany(values => values.Values)
                .SingleOrDefault(value => value.Quest == quest && value.Stage == stage);
            return () =>
            {
                GD.Print($"OPENNV_NATIVE_SET_STAGE quest={quest} stage={stage} owner=shared-script-host");
                if (source is null)
                {
                    // Background quests may also enter stages. Keep the configured
                    // startup identity; the shared quest state owns every stage.
                    (_stageResults ?? throw new InvalidOperationException("Quest stage result owner is absent.")).Enter(quest, stage);
                }
                else
                {
                    if (_machine is null)
                    {
                        _restoringEnteredStage = false;
                        _machine = new(_transitions, _controls, source.QuestEditorId, stage, _sourceControls);
                    }
                    else _machine.EnterScriptStage(source.QuestEditorId, stage);
                    Synchronize();
                }
            };
        }, name =>
        {
            if (name.Equals("Health", StringComparison.OrdinalIgnoreCase)) return Vitals.ExactHitPoints;
            if (name.Equals("RadiationRads", StringComparison.OrdinalIgnoreCase)) return Vitals.RadiationRads;
            if (name.Equals("ActionPoints", StringComparison.OrdinalIgnoreCase)) return Vitals.ActionPoints;
            if (name.Equals("XP", StringComparison.OrdinalIgnoreCase)) return Vitals.ExperiencePoints;
            return IsSpecial(name) ? _playerActorValues.ReadCurrent(FalloutPlayerActorValues.SpecialValue(name)) : _playerSkills.Value(name);
        }, RequireLevelUpOwner: () => _vitals.RequireLevelUpOwner(),
            ReadPlayerActorValue: ReadPlayerActorValue, ChangePlayerActorValue: _playerActorValues.Change,
            Inventory: InventoryCommands, ResetPlayerHealth: () => _vitals.ResetHealth(), CurrentPackage: CurrentActorPackage,
            Sitting: ActorSitting, TagSkills: _tagSkills, RewardXp: value => _experience.Reward(value));
        _captureScripts = captureScripts;
        _playerSkills = new(pluginStack, () => Special, IsPlayerTagSkill, () => _traits, globals, inventory,
            raceSexContract.Player, () => _scripts.References!.ActorRace(pluginStack.RuntimeFormKey(0x14)), () => _scripts.Session.Hardcore,
            () => _scripts.References!.AcquiredPerks(pluginStack.RuntimeFormKey(0x14)), _playerActorValues);
        _playerActorValues.BindConstantModifiers(_playerSkills.Modifiers);
        _vitals = FalloutPlayerVitals.FromActorValues(pluginStack, _playerActorValues, restore?.State.Vitals);
        _experience = new(pluginStack, _vitals, () => _playerSkills.PerkEntries);
        _ingestibles = new(pluginStack, inventory, _vitals,
            FalloutBodyPartData.Read(pluginStack.GetEffective(pluginStack.RuntimeFormKey(0x1d))),
            _playerSkills.Value, _playerSkills.HasPerk, () => _scripts.Session.Hardcore);
        if (restore?.State.Ingestibles is { } ingestibles) _ingestibles.Restore(ingestibles);
        _player.ConfigureExplosionExposure(() => _activeCell, amount =>
        {
            var resistance = Math.Min(_playerSkills.Value(20), FalloutGameSettingFloats.Read(pluginStack, "fPlayerMaxResistance"));
            var absorbed = amount * (1 - resistance / 100f) * FalloutGameSettingFloats.Read(pluginStack, "fRadiationAccumulationRate");
            _vitals.Publish(Vitals with { RadiationRads = Vitals.RadiationRads + absorbed });
        }, _imageSpaceState, restore?.State.ExplosionExposure);
        _gameTime = gameTime;
        _skyLighting = skyLighting;
        _restoringEnteredStage = restore is not null;
        _imageSpacePresenter = imageSpacePresenter;
        if (controls.Quests.TryGetValue(_sourceQuestEditorId, out var initialStages) && initialStages.ContainsKey(restore?.State.Stage ?? initialStage))
            _machine = new(_transitions, controls, _sourceQuestEditorId, restore?.State.Stage ?? initialStage, _sourceControls);
        Name = "NativeOpeningStageDriver";
        Synchronize();
    }

    internal void CompleteBlocker(string blocker)
    {
        if (_machine is null || !_machine.PendingBlockers.Contains(blocker, StringComparer.OrdinalIgnoreCase)) return;
        var beforeQuest = _machine.QuestEditorId;
        var beforeStage = _machine.Stage;
        var changed = _machine.CompleteBlocker(blocker);
        GD.Print(
            $"OPENNV_NATIVE_OPENING_BLOCKER_COMPLETE quest={beforeQuest} stage={beforeStage} " +
            $"blocker={blocker} remaining={string.Join(',', _machine.PendingBlockers)}");
        if (changed)
            Synchronize();
    }

    private void OpenVigorMenu(int total)
    {
        if (_vigorEntry is not null || _specialBookEntry is not null || BlockingExecutionError is not null)
            throw new InvalidOperationException("SPECIAL menu cannot open while its owner is busy or failed.");
        _specialMenuContract = _vigorContract with { RequiredTotal = total };
        _vigorEntry = new RuntimeNativeVigorEntry();
        AddChild(_vigorEntry);
        _vigorEntry.Accepted += AcceptSpecial;
        _vigorEntry.Configure(_specialMenuContract, Special, _pluginStack, _imageSpacePresenter());
        _player.SetModalInput(true);
        GD.Print(
            $"OPENNV_NATIVE_VIGOR_OPEN stage={Stage} total={total} " +
            $"reference={_vigorContract.TesterReference.FormKey} " +
            "source=live-player-vigor-scripts presentation=owned-love-tester-menu parity=unverified");
    }

    public override void _Process(double delta)
    {
        if (BlockingExecutionError is not null) return;
        try
        {
            DrainSourceManualSaves();
            if (BlockingExecutionError is not null) return;
            RefreshRadioStations();
            _ingestibles.Advance(delta);
            _stageResults?.Continue();
            if (_saveRequested && SaveContinuationBlocker is null && !_scripts.References!.PlayerMoves.Pending)
                SaveCurrentState();
            _playerPackage?.Advance(delta);
            foreach (var expired in _imageSpaceState.Advance(delta))
                GD.Print($"OPENNV_NATIVE_IMAD_EXPIRED source={expired.Form} duration={expired.Duration:R} owner=gameplay-clock");
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or FileNotFoundException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            RetainDriverFailure(error);
            GD.PushError($"OPENNV_NATIVE_PLAYER_PACKAGE_DIVERGENCE: {error.Message}");
            return;
        }
        if (_machine is not null && !_moviePlaying && _nameEntry is null && _raceSexEntry is null && _vigorEntry is null && _specialBookEntry is null &&
            _tagSkillEntry is null && _traitEntry is null && _recipeMenu is null && _barterMenu is null)
        {
            try { _scripts.AdvanceClaimed(_controls.Stage(QuestEditorId, Stage).Quest, delta, _scriptHost); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
            {
                RetainDriverFailure(error);
                GD.PushError($"OPENNV_NATIVE_QUEST_SCRIPT_DIVERGENCE quest={QuestEditorId} stage={Stage}: {error.Message}");
                return;
            }
        }
    }

    public override void _Ready()
    {
        _playerPackage = new RuntimeNativePlayerPackage(_pluginStack, _player, _scripts.Session, _scripts.References!, () => _activeCell);
        _scripts.References!.UnloadedPackages = new(_pluginStack, _scripts.References, _quests, _gameTime,
            _globals, ExecutePackageEvent, () => SourcePlayerLevel);
        _speech = new RuntimeNativeSpeech();
        _speech.PrepareSubtitle = subtitle => (PrepareSubtitle ??
            throw new NotSupportedException("Source subtitle presentation is absent."))(subtitle);
        _speech.SayToCompleted += receipt => (SayToCompleted ??
            throw new NotSupportedException("Source SayToDone event dispatch is absent."))(receipt);
        _speech.InfoCompleted += _ =>
        {
            if (_machine is not null && !_speech.Active && _speechStage == $"{_machine.QuestEditorId}:{_machine.Stage}" &&
                _machine.PendingBlockers.Contains("sayto", StringComparer.OrdinalIgnoreCase))
            {
                _machine.CompleteDialogueSpeech();
                Synchronize();
            }
        };
        _speech.Configure(_pluginStack, _lipConfiguration, quest => _quests.Stage(quest), condition =>
        {
            if (condition.Function == 53) return (float)_scripts.References!.ReadVariable(_quests, condition.FormArgument1, condition.Argument2);
            if (condition.Function == 74) return (_globals ?? throw new InvalidOperationException("Dialogue has no global owner.")).Get(condition.FormArgument1);
            if (condition.RunOn == 1 && condition.Function == 70 && condition.Reference == 0)
            {
                if (condition.Argument1 > 1) throw new InvalidDataException("GetIsSex has an invalid sex argument.");
                return (condition.Argument1 == 1) == _character.Female ? 1 : 0;
            }
            if (condition.RunOn == 0 && condition.Function is 59 or 79 or 546) return _quests.Evaluate(condition);
            throw new NotSupportedException($"Dialogue condition {condition.Function} RunOn {condition.RunOn} has no actor/quest owner.");
        }, _scripts.SaidInfos, actor => _scripts.References!.Get(actor).Templates,
            actor => _scripts.References!.Get(actor).SoundRandom, _player.UnitsToMeters, _quests, () => _character.Female,
            actor => _scripts.References!.ActorRace(actor), DialogueActorValue, _scripts.ScriptValues.RandomBounded, _scripts.References,
            CurrentActorPackage, _scripts.ActorQueries.GetVampire, InventoryCommands.ItemCount,
            reference => ReferencePresentation().TryResolve(reference), ReferenceDistance, ReferenceInZone, ActorSitting,
            _scripts.References!.GetDeadCount);
        AddChild(_speech);
        ConfigureConversation();
        ConfigureDetectionAndFinishedSpeech();
        ApplyEnteredActorCommands();
    }

    private void Synchronize()
    {
        ApplyEnteredActorCommands();
        if (BlockingExecutionError is not null) return;
        _player.ApplySourceControls(PlayerControls);
        if (_machine is null) return;
        SynchronizeNameEntry();
        SynchronizeRaceSexEntry();
        SynchronizeTagSkillEntry();
        SynchronizeTraitEntry();
        GD.Print(
            $"OPENNV_NATIVE_OPENING_STAGE quest={_machine.QuestEditorId} stage={_machine.Stage} " +
            $"movement={_machine.ControlState.Movement} looking={_machine.ControlState.Looking} " +
            $"pipBoy={_machine.ControlState.PipBoy} fighting={_machine.ControlState.Fighting} " +
            $"timer={(_machine.TimerSeconds?.ToString("R") ?? "none")} " +
            $"blockers={string.Join(',', _machine.PendingBlockers)} " +
            "source=live-qust-scpt-dial-info");
        if (_machine.QuestEditorId == FalloutNativeCampaignSave.OpeningQuestEditorId &&
            _machine.Stage == FalloutNativeCampaignSave.CompletedOpeningStage &&
            !_stage200Saved)
        {
            _saveRequested = true;
        }
    }

    private void ApplyEnteredActorCommands()
    {
        if (!IsInsideTree() || BlockingExecutionError is not null || _machine is null) return;
        try
        {
            while (_machine.TryTakeEnteredStage(out var stage))
            {
                if (_restoringEnteredStage) { _restoringEnteredStage = false; continue; }
                (_stageResults ?? throw new InvalidOperationException("Quest stage owner is absent.")).Enter(stage!.Quest, stage.Stage);
            }
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or FileNotFoundException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            RetainDriverFailure(error);
            GD.PushError($"OPENNV_NATIVE_STAGE_DIVERGENCE quest={QuestEditorId} stage={Stage}: {error.Message}");
        }
    }

    private void ApplyLookCommand(FalloutBoundLookCommand command)
    {
        var actor = GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
            .SingleOrDefault(value => value.Appearance.Reference == command.Actor);
        var requiresProcess = FalloutHeadTrackingPrograms.RequiresProcess(_pluginStack.GetEffective(command.Actor), _activeCell, actor is not null);
        if (requiresProcess) actor!.ApplyBoundHeadTrackingCommand(command);
        _headTrackingCommands.Add(new
        {
            actor = command.Actor.ToString(),
            target = command.Target?.ToString(),
            sourceLine = command.Line,
            disposition = requiresProcess ? "script-head-target" : "no-active-actor-process",
        });
        GD.Print($"OPENNV_NATIVE_LOOK_RESULT actor={command.Actor} target={command.Target?.ToString() ?? "none"} process={requiresProcess}");
    }

    internal FalloutNativeCampaignState PersistWorldState(FalloutFormKey activeCell)
    {
        if (Vitals.HitPoints == 0)
            throw new InvalidOperationException("Cannot replace a playable save after player death.");
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var state = CaptureCurrentState(activeCell);
        var captured = System.Diagnostics.Stopwatch.GetTimestamp();
        FalloutNativeCampaignSave.Write(_savePath, state);
        var written = System.Diagnostics.Stopwatch.GetTimestamp();
        _activeCell = activeCell;
        GD.Print(
            $"OPENNV_NATIVE_WORLD_SAVED cell={activeCell} stage={state.QuestEditorId}:{state.Stage} " +
            $"save={_savePath} owner=native-campaign-state " +
            $"captureMilliseconds={System.Diagnostics.Stopwatch.GetElapsedTime(started, captured).TotalMilliseconds:F3} " +
            $"writeMilliseconds={System.Diagnostics.Stopwatch.GetElapsedTime(captured, written).TotalMilliseconds:F3}");
        return state;
    }

    private void SynchronizeNameEntry(bool sourceRequested = false)
    {
        var pending = sourceRequested || PendingBlockers.Contains("getplayername", StringComparer.OrdinalIgnoreCase);
        if (!pending)
        {
            if (_nameEntry is not null)
            {
                _nameEntry.QueueFree();
                _nameEntry = null;
            }
            return;
        }
        if (_nameEntry is not null)
            return;
        _nameEntry = new RuntimeNativePlayerNameEntry();
        AddChild(_nameEntry);
        _nameEntry.Accepted += AcceptPlayerName;
        _nameEntry.Configure(_playerName, _pluginStack);
        GD.Print(
            $"OPENNV_NATIVE_NAME_ENTRY_OPEN quest={QuestEditorId} stage={Stage} " +
            "source=getplayername presentation=owned-xml-font-texture-atlas parity=unmeasured");
    }

    private void AcceptPlayerName(string value)
    {
        _playerName = value;
        if (_nameEntry is not null)
        {
            _nameEntry.Accepted -= AcceptPlayerName;
            _nameEntry.ReleasePause();
            _nameEntry.QueueFree();
            _nameEntry = null;
        }
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        GD.Print(
            $"OPENNV_NATIVE_NAME_ACCEPTED characters={_playerName.Length} " +
            "source=configured-player-input");
        CompleteBlocker("getplayername");
    }

    private string RaceMenuModel(string command)
    {
        if (!_raceMenuDevicesRead)
        {
            var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Race menu has no owned source.");
            if (source.TryRead("nvse/plugins/ttw_nvse.dll", null, out var plugin, out _))
                _raceMenuDevices = FalloutExecutableStringTable.ReadTtwRaceMenuDevices(
                    Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "FalloutNV.exe"), plugin);
            _raceMenuDevicesRead = true;
        }
        return _raceMenuDevices?.ModelFor(command) ?? (command == "showracemenu" ? "meshes/terminals/nv_reflectron_ui.nif" :
            throw new NotSupportedException("Gene projector has no selected owned TTW plugin declaration."));
    }

    private void SynchronizeRaceSexEntry(bool sourceRequested = false, string? sourceCommand = null)
    {
        var pendingCommand = PendingBlockers.FirstOrDefault(command => command.Equals("showracemenu", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("ttw_showgeneprojector", StringComparison.OrdinalIgnoreCase));
        var pending = sourceRequested || pendingCommand is not null;
        if (!pending)
        {
            if (_raceSexEntry is not null)
            {
                _raceSexEntry.QueueFree();
                _raceSexEntry = null;
            }
            return;
        }
        if (_raceSexEntry is not null)
        {
            if (sourceRequested) throw new NotSupportedException("Overlapping race-menu requests have no native replacement owner.");
            return;
        }
        _raceMenuCommand = sourceCommand ?? pendingCommand ?? "showracemenu";
        var modelPath = RaceMenuModel(_raceMenuCommand);
        _raceSexEntry = new RuntimeNativeRaceSexEntry();
        AddChild(_raceSexEntry);
        _raceSexEntry.Accepted += AcceptCharacter;
        _raceSexEntry.Failed += error => ExecutionError = error.Message;
        _raceSexEntry.Configure(_raceSexContract, _character, _pluginStack, _imageSpacePresenter(), modelPath);
        GD.Print(
            $"OPENNV_NATIVE_RACESEX_OPEN quest={QuestEditorId} stage={Stage} " +
            $"race={_character.RaceEditorId}/{_character.RaceRuntimeFormId:x8} " +
            $"command={_raceMenuCommand} model={modelPath} " +
            "source=player-race-hair-eyes-ctl presentation=owned-rendered-menu parity=unverified");
    }

    private void AcceptCharacter(FalloutNativeRaceSexSelection selection)
    {
        FalloutNativeRaceSexResolver.Validate(_raceSexContract, selection);
        _character = selection;
        _characterRevision++;
        if (_raceSexEntry is not null)
        {
            _raceSexEntry.Accepted -= AcceptCharacter;
            _raceSexEntry.ReleasePause();
            _raceSexEntry.QueueFree();
            _raceSexEntry = null;
        }
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        GD.Print(
            $"OPENNV_NATIVE_RACESEX_ACCEPTED female={_character.Female} " +
            $"race={_character.RaceEditorId}/{_character.RaceRuntimeFormId:x8} " +
            $"hair={_character.HairEditorId}/{_character.HairRuntimeFormId:x8} " +
            $"eyes={_character.EyesEditorId}/{_character.EyesRuntimeFormId:x8} " +
            "source=live-winning-records");
        CompleteBlocker(_raceMenuCommand);
        // The existing modal handoff delivers the source MenuMode event here.
        // Exact scheduling while the menu is open remains a separate owner.
        if (_machine is not null) _scripts.ExecuteClaimedMenu(_controls.Stage(QuestEditorId, Stage).Quest, 1036, _scriptHost);
    }

    private void AcceptSpecial(FalloutNativeSpecialState state)
    {
        try { AcceptSpecialCore(state); }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or NotSupportedException)
        {
            ExecutionError = error.Message;
            GD.PushError($"OPENNV_NATIVE_VIGOR_DIVERGENCE {error.Message}");
        }
    }

    private void AcceptSpecialCore(FalloutNativeSpecialState state)
    {
        FalloutNativeVigorResolver.Validate(_specialMenuContract ?? _vigorContract, state);
        for (var index = 0; index < state.Values.Count; index++) _playerActorValues.WriteBaseInteger(index + 5, state.Values[index]);
        _ = Vitals;
        if (_vigorEntry is not null)
        {
            _vigorEntry.Accepted -= AcceptSpecial;
            _vigorEntry.QueueFree();
            _vigorEntry = null;
        }
        _player.SetModalInput(false);
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        GD.Print(
            $"OPENNV_NATIVE_SPECIAL_ACCEPTED total={Special.Values.Sum()} " +
            $"values={string.Join(',', Special.Values)} stage={Stage} " +
            "source=configured-player-input-live-vigor-contract");
        Synchronize();
    }

    private void SynchronizeTagSkillEntry(bool sourceRequested = false)
    {
        var pending = sourceRequested || PendingBlockers.Contains("settagskills", StringComparer.OrdinalIgnoreCase);
        if (!pending)
        {
            if (_tagSkillEntry is not null)
            {
                _tagSkillEntry.Accepted -= AcceptTagSkills;
                _tagSkillEntry.ReleasePause();
                _tagSkillEntry.QueueFree();
                _tagSkillEntry = null;
            }
            _tagMenuRequest = null;
            _activeTagSkillContract = null;
            return;
        }
        if (_tagSkillEntry is not null)
            return;
        _tagSkillEntry = new RuntimeNativeTagSkillEntry();
        AddChild(_tagSkillEntry);
        _tagSkillEntry.Accepted += AcceptTagSkills;
        _tagSkillEntry.Failed += error => ExecutionError = error.Message;
        var request = _tagMenuRequest ?? new(_tagSkillContract.RequiredCount, true);
        _activeTagSkillContract = _tagSkillContract with { RequiredCount = request.TotalCount };
        _tagSkillEntry.Configure(_pluginStack, _activeTagSkillContract, _tagSkills.Selection,
            skill => _playerSkills.Value(FalloutNativeTagSkillResolver.ActorValueName(_pluginStack, skill)), request.ShowInitialTaggedSkills);
        var releaseModalInput = _player.AcquireModalInput();
        _tagSkillEntry.Released += () =>
        {
            releaseModalInput();
            if (!_player.ModalInput && DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
        };
        GD.Print(
            $"OPENNV_NATIVE_TAG_SKILLS_OPEN stage={Stage} " +
            $"choices={_tagSkillContract.Skills.Count} required={request.TotalCount} initial={request.ShowInitialTaggedSkills} " +
            "source=live-settagskills-avif presentation=menus/chargen/char_gen_menu.xml");
    }

    private void AcceptTagSkills(IReadOnlyList<FalloutNativeSkillIdentity> selection)
    {
        var contract = _activeTagSkillContract ?? throw new InvalidOperationException("Tag acceptance has no source menu contract.");
        FalloutNativeTagSkillResolver.Validate(contract, selection);
        _tagSkills.AcceptMenu(selection, contract.RequiredCount);
        if (_tagSkillEntry is not null)
        {
            _tagSkillEntry.Accepted -= AcceptTagSkills;
            _tagSkillEntry.ReleasePause();
            _tagSkillEntry.QueueFree();
            _tagSkillEntry = null;
        }
        _tagMenuRequest = null;
        _activeTagSkillContract = null;
        GD.Print(
            $"OPENNV_NATIVE_TAG_SKILLS_ACCEPTED skills=" +
            $"{string.Join(',', _tagSkills.Selection.Select(value => value.EditorId))} " +
            "source=configured-player-input-live-avif-contract");
        CompleteBlocker("settagskills");
    }

    private void SynchronizeTraitEntry(bool sourceRequested = false)
    {
        var pending = sourceRequested || PendingBlockers.Contains("showtraitmenu", StringComparer.OrdinalIgnoreCase);
        if (!pending)
        {
            if (_traitEntry is not null)
            {
                _traitEntry.ReleasePause();
                _traitEntry.QueueFree();
                _traitEntry = null;
            }
            return;
        }
        if (_traitEntry is not null)
            return;
        _traitEntry = new RuntimeNativeTraitEntry();
        AddChild(_traitEntry);
        _traitEntry.Accepted += AcceptTraits;
        _traitEntry.Failed += error => ExecutionError = error.Message;
        _traitEntry.Configure(_pluginStack, _traitFarewellContract, _traits);
        _player.SetModalInput(true);
        GD.Print(
            $"OPENNV_NATIVE_TRAITS_OPEN stage={Stage} " +
            $"choices={_traitFarewellContract.Traits.Count} maximum=" +
            $"{_traitFarewellContract.MaximumTraits} " +
            "source=live-showtraitmenu-perk presentation=menus/trait_menu.xml");
    }

    private void AcceptTraits(IReadOnlyList<FalloutNativeTraitIdentity> selection)
    {
        FalloutNativeTraitFarewellResolver.ValidateTraits(_traitFarewellContract, selection);
        _traits = selection.OrderBy(value => value.RuntimeFormId).ToArray();
        if (_traitEntry is not null)
        {
            _traitEntry.Accepted -= AcceptTraits;
            _traitEntry.ReleasePause();
            _traitEntry.QueueFree();
            _traitEntry = null;
        }
        _player.SetModalInput(false);
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        GD.Print(
            $"OPENNV_NATIVE_TRAITS_ACCEPTED traits=" +
            $"{string.Join(',', _traits.Select(value => value.EditorId))} " +
            "source=configured-player-input-live-perk-contract");
        CompleteBlocker("showtraitmenu");
    }

}
