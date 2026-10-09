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
    private string _sourceQuestEditorId = string.Empty;
    private FalloutPlayerControlState _sourceControls = FalloutPlayerControlState.AllEnabled;
    private bool _configured;
    private FalloutNativeRaceSexContract _raceSexContract = null!;
    private FalloutLoveTesterSource? _specialMenuSource;
    private FalloutNativeSpecialAllocation? _specialMenuContract;
    private IReadOnlyList<FalloutNativeSkillIdentity> _skillCatalog = [];
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
    private string? _raceMenuDefaultModel;
    private bool _raceMenuDevicesRead;
    private string _raceMenuCommand = "showracemenu";
    private RuntimeNativeVigorEntry? _vigorEntry;
    private RuntimeNativeTagSkillEntry? _tagSkillEntry;
    private FalloutTagSkillMenuRequest? _tagMenuRequest;
    private FalloutNativeTagSkillChoices? _activeTagSkillContract;
    private RuntimeNativeTraitEntry? _traitEntry;
    private FalloutOpeningControlGraph _controls = null!;
    private bool _moviePlaying;
    private RuntimeNativeSpeech? _speech;
    private FaceGenLipConfiguration _lipConfiguration = null!;
    private string? _speechStage;
    private RuntimeNativePlayerPackage? _playerPackage;
    private FalloutPlayerPackageAudioSnapshot? _restorePlayerPackageAudio;
    private FalloutImageSpaceState _imageSpaceState = null!;
    private FalloutQuestState _quests = null!;
    private FalloutQuestScripts _scripts = null!;
    private FalloutQuestScriptHost _scriptHost = null!;
    private FalloutPlayerInventory _inventory = null!;
    private Func<FalloutQuestScriptsSnapshot?> _captureScripts = null!;
    private FalloutGlobalState? _globals;
    private FalloutGameTime? _gameTime;
    private FalloutSkyLightingState? _skyLighting;
    private (FalloutFormKey Quest, short Stage)? _initialStageResultRequest;
    private Func<RuntimeNativeImageSpace> _imageSpacePresenter = null!;
    private string? _executionError;
    internal string? ExecutionError
    {
        get => _executionError;
        private set { _executionError = value; _stageResultDriverFailure = null; }
    }
    internal string? ExecutionFault => ExecutionError ?? _player?.PlayerPhysicalFailure ?? NativePluginExecutionFailure ?? _speech?.Error ?? _conversation?.ExecutionFault ?? TerminalExecutionFault ?? SourceManualSaveFailure;
    internal string? BlockingExecutionError => _stageResultDriverFailure?.Error == ExecutionError ? null : ExecutionError;
    internal string? BlockingExecutionFault => BlockingExecutionError ?? _player?.PlayerPhysicalFailure ?? NativePluginExecutionFailure ?? _speech?.Error ?? _conversation?.ExecutionFault ?? BlockingTerminalExecutionFault;
    private readonly List<object> _headTrackingCommands = [];
    internal object[] HeadTrackingCommands => _headTrackingCommands.ToArray();

    internal string QuestEditorId => _sourceQuestEditorId;
    internal short Stage => _quests.Stage(FalloutDialogueTopic.Find(_pluginStack, "QUST", _sourceQuestEditorId).FormKey);
    // Timer identities and values belong to original quest variables and the
    // shared script clock. This owner cannot infer a generic fTimer from SCTX.
    internal float? TimerSeconds => null;
    internal IReadOnlyCollection<string> PendingBlockers => ActiveSourcePresentationOwners();
    private FalloutPlayerControlState PlayerControls => _sourceControls;
    internal FalloutFormKey ActiveCell => _activeCell;
    internal void EnterWorldCell(FalloutFormKey cell) => _activeCell = cell;
    internal bool HasCampaignSave => File.Exists(_savePath);
    internal string PlayerName => _playerName;
    internal int PlayerLevel => SourcePlayerLevel;
    internal FalloutNativeSpecialState Special => _playerActorValues.BaseSpecial;
    internal FalloutSpecialAllocationBinding SpecialAllocationBinding => _playerActorValues.AllocationBinding;
    internal GameplayVitals Vitals => _vitals.State;
    private FalloutActorDefenseResolver? _incomingDefense;
    internal void DamagePlayer(FalloutWeaponDamage damage, byte part)
    {
        _scripts.References?.BeforeActorHit(_pluginStack.RuntimeFormKey(0x14));
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
        if (_vigorEntry is not null) yield return (_specialMenuSource ?? throw new InvalidOperationException("SPECIAL menu source is absent.")).MenuId;
        if (_specialBookEntry is not null) yield return FalloutSpecialBookPresentation.MenuId;
        if (_tagSkillEntry is not null) yield return 1048;
        if (_traitEntry is not null) yield return 1084;
        if (PlayerLevelUpMenuId is { } levelUp) yield return levelUp;
        if (_recipeMenu is not null) yield return 1077;
        if (_barterMenu is not null) yield return 1053;
        if (_terminalMenus.Values.Any(menu => menu.Active)) yield return FalloutTerminal.MenuId;
    }

    internal void Configure(
        FalloutOpeningStageTransitionGraph transitions,
        FalloutOpeningControlGraph controls,
        RuntimeNativePlayer player,
        FalloutNativeRaceSexContract raceSexContract,
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
        short initialStage, FalloutPlayerControlState? initialControls = null,
        IReadOnlyList<FalloutQuestStageResultSnapshot>? bootstrapStageResults = null,
        FalloutNativeRaceSexSelection? initialCharacter = null)
    {
        if (_configured)
            throw new InvalidOperationException("Native opening stage driver was already configured.");
        if (!controls.ResultDriven || transitions.Transitions.Count != 0)
            throw new NotSupportedException("Native opening requires original result-driven stage declarations; predicted controls/transitions are refused.");
        _configured = true;
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _raceSexContract = raceSexContract ?? throw new ArgumentNullException(nameof(raceSexContract));
        _skillCatalog = FalloutNativeTagSkillResolver.ResolveSkills(pluginStack);
        _pluginStack = pluginStack ?? throw new ArgumentNullException(nameof(pluginStack));
        _controls = controls;
        _sourceQuestEditorId = restore?.State.QuestEditorId ?? initialQuestEditorId;
        _sourceControls = restore is null ? initialControls ?? FalloutPlayerControlState.AllEnabled : FalloutNativeCampaignSave.RestorePlayerControls(restore.State);
        _savePath = Path.GetFullPath(savePath ?? throw new ArgumentNullException(nameof(savePath)));
        _saveCompatibilityId = string.IsNullOrWhiteSpace(saveCompatibilityId)
            ? throw new ArgumentException(
                "Native save compatibility identity is required.", nameof(saveCompatibilityId))
            : saveCompatibilityId;
        _activeCell = restore?.State.ActiveCell ?? initialCell;
        _playerName = restore?.State.PlayerName ?? FalloutGameSettingStrings.Read(pluginStack, "sDefaultPlayerName");
        _character = restore?.State.Character ?? initialCharacter ?? raceSexContract.Initial;
        FalloutNativeRaceSexResolver.Validate(raceSexContract, _character);
        _playerActorValues = new(pluginStack, restore?.State.PlayerActorValues);
        _tagSkills = new(pluginStack, _skillCatalog, snapshot: restore?.State.TagSkillSlots);
        _traits = restore?.State.Traits ?? [];
        FalloutTraitMenuCatalogue.Validate(pluginStack, _traits);
        _lipConfiguration = lipConfiguration;
        _imageSpaceState = imageSpaceState;
        _quests = quests;
        _scripts = scripts;
        _scripts.References!.PlayerTraitSelection = () => _traits.Select(trait => _pluginStack.RuntimeFormKey(trait.RuntimeFormId)).ToArray();
        BindSourceManualSaves();
        if (restore is not null) _scripts.ScriptManualSaves.RestoreOrder(restore.SaveRequestLoad ??
            throw new InvalidDataException("Current save has no actual file/source queue handoff."));
        _scripts.References!.BindActorAlert(_pluginStack.RuntimeFormKey(0x14), _player.Activity);
        _restoreFinishedSpeech = restore?.State.FinishedSpeech;
        _restoreFinishedSpeechStage = restore?.State.FinishedSpeechStage;
        _restoreStageResults = restore?.State.QuestStageResults ?? bootstrapStageResults;
        _restoreStageResultFailure = restore?.State.StageResultFailure;
        _restoreTerminalResults = restore?.State.TerminalResults;
        _restorePlayerPackageAudio = restore?.State.PlayerPackageAudio;
        _inventory = inventory;
        _globals = globals;
        _scripts.References!.BindPlayerAppearance(() => PlayerCreationState);
        _scriptHost = new((quest, stage) =>
        {
            return () => RequestSourceStage(quest, stage);
        }, name =>
        {
            if (name.Equals("Health", StringComparison.OrdinalIgnoreCase)) return Vitals.ExactHitPoints;
            if (name.Equals("RadiationRads", StringComparison.OrdinalIgnoreCase)) return Vitals.RadiationRads;
            if (name.Equals("ActionPoints", StringComparison.OrdinalIgnoreCase)) return Vitals.ActionPoints;
            if (name.Equals("XP", StringComparison.OrdinalIgnoreCase)) return Vitals.ExperiencePoints;
            return IsSpecial(name) ? _playerActorValues.ReadCurrent(FalloutPlayerActorValues.SpecialValue(name)) :
                _playerSkills.IsSkill(name) ? _playerSkills.ReadSkill(name, FalloutActorValueRead.Current) : _playerSkills.Value(name);
        }, ReadPlayerActorValue: ReadPlayerActorValue, ChangePlayerActorValue: ChangePlayerActorValue,
            Inventory: InventoryCommands, ResetPlayerHealth: () => _vitals.ResetHealth(), CurrentPackage: CurrentActorPackage,
            Sitting: ActorSitting, TagSkills: _tagSkills, RewardXp: RewardPlayerExperience, GameTime: gameTime,
            IsPcSleeping: _player.IsPcSleeping, Sleeping: ActorSleeping, KnockedState: ActorKnockedState);
        _captureScripts = captureScripts;
        _playerSkills = new(pluginStack, () => Special, IsPlayerTagSkill, () => _traits, globals, inventory,
            raceSexContract.Player, () => _scripts.References!.ActorRace(pluginStack.RuntimeFormKey(0x14)), () => _scripts.Session.Hardcore,
            () => _scripts.References!.AcquiredPerks(pluginStack.RuntimeFormKey(0x14)), _playerActorValues,
            perk => _scripts.References!.PerkRank(pluginStack.RuntimeFormKey(0x14), perk));
        if (restore is not null)
            _playerSkills.RestoreValues((restore.State.PlayerProgress ??
                throw new InvalidDataException("Current player progress is absent.")).Skills);
        _playerActorValues.BindConstantModifiers(_playerSkills.Modifiers);
        _vitals = FalloutPlayerVitals.FromActorValues(pluginStack, _playerActorValues, restore?.State.Vitals);
        _playerSkills.BindAbilityConditions(PlayerProgressCondition);
        _experience = new(pluginStack, _vitals, () => _playerSkills.PerkEntries);
        InitializePlayerProgress(restore is null ? null : restore.State.PlayerProgress ??
            throw new InvalidDataException("Current player progress is absent."));
        ConfigureExperienceNotifications(restore is null ? null : restore.State.ExperienceNotifications ??
            throw new InvalidDataException("Current experience notifications are absent."),
            BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(sizeof(ulong))));
        ConfigureInterfaceActivationFrames(restore is null ? null : restore.State.InterfaceActivationFrames ??
            throw new InvalidDataException("Current interface frame continuation is absent."));
        ConfigureSourceCombatGroups(restore is null ? null : restore.State.CombatGroups ??
            throw new InvalidDataException("Current combat group continuation is absent."));
        ConfigureSourceActorPerception(restore is null ? null : restore.State.ActorPerception ??
            throw new InvalidDataException("Current actor perception continuation is absent."));
        ConfigureSourceActorProcesses(restore is null ? null : restore.State.ActorProcesses ??
            throw new InvalidDataException("Current actor process continuation is absent."));
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
        _imageSpacePresenter = imageSpacePresenter;
        // Attachment observes existing authoritative state. A bootstrap or
        // cold result history never becomes another SetStage invocation, even
        // when the original QUST admits deliberate repeated stage requests.
        if (restore is null && bootstrapStageResults is null)
            _initialStageResultRequest = (FalloutDialogueTopic.Find(_pluginStack, "QUST", initialQuestEditorId).FormKey, initialStage);
        Name = "NativeOpeningStageDriver";
        Synchronize();
    }

    internal void CompleteBlocker(string blocker)
    {
        // The actual completed menu/movie/speech owner already retired its
        // state. It cannot lend a predicted destination or rerun stage effects.
        if (string.IsNullOrWhiteSpace(blocker)) throw new ArgumentException("Source presentation owner is absent.", nameof(blocker));
        if (_configured) Synchronize();
    }

    private void OpenVigorMenu(int total)
    {
        RequireLevelUpMenuFree("SPECIAL menu");
        if (_vigorEntry is not null || _specialBookEntry is not null || BlockingExecutionError is not null)
            throw new InvalidOperationException("SPECIAL menu cannot open while its owner is busy or failed.");
        var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("SPECIAL menu owned source is absent.");
        _specialMenuSource = FalloutExecutableStringTable.ReadLoveTesterSource(content.FalloutExecutablePath);
        _specialMenuContract = _specialMenuSource.Allocate(total);
        _specialMenuContract.Validate(Special, allowUnspent: true);
        _vigorEntry = new RuntimeNativeVigorEntry();
        AddChild(_vigorEntry);
        _vigorEntry.Accepted += AcceptSpecial;
        _vigorEntry.Configure(_specialMenuSource, _specialMenuContract, Special, _pluginStack, _imageSpacePresenter());
        _player.SetModalInput(true);
        GD.Print(
            $"OPENNV_NATIVE_VIGOR_OPEN stage={Stage} total={total} " +
            $"menu={_specialMenuSource.MenuId} source=compiled-menu-request presentation=owned-love-tester-menu parity=unverified");
    }

    public override void _Process(double delta)
    {
        if (BlockingExecutionFault is not null) return;
        try
        {
            DrainSourceManualSaves();
            if (!CanProcess()) return; // Save preparation may have acquired this producer during the current callback.
            if (BlockingExecutionFault is not null) return;
            RefreshRadioStations();
            _ingestibles.Advance(delta);
            _stageResults?.Continue();
            _playerPackage?.Advance(delta);
            _scripts.References!.UnloadedPackages?.Advance(delta);
            AdvanceSourceActorPerception(delta);
            AdvanceSourceCombatGroups(delta);
            AdvanceNativePlayerInCurrentFrame();
            if (!CanProcess()) return; // Native advancement can acquire a modal gameplay pause in this same frame.
        }
        catch (Exception error)
        {
            RetainDriverFailure(error);
            GD.PushError($"OPENNV_NATIVE_PLAYER_PACKAGE_DIVERGENCE: {error.Message}");
            return;
        }
        if (_controls is not null && _controls.Quests.ContainsKey(QuestEditorId) && !_moviePlaying && _nameEntry is null && _raceSexEntry is null && _vigorEntry is null && _specialBookEntry is null &&
            _tagSkillEntry is null && _traitEntry is null && _recipeMenu is null && _barterMenu is null && _levelUpEntry is null)
        {
            try { _scripts.AdvanceClaimed(_controls.Stage(QuestEditorId, Stage).Quest, delta, _scriptHost); }
            catch (Exception error)
            {
                RetainDriverFailure(error);
                GD.PushError($"OPENNV_NATIVE_QUEST_SCRIPT_DIVERGENCE quest={QuestEditorId} stage={Stage}: {error.Message}");
                return;
            }
        }
    }

    internal void InitializeOwnedState()
    {
        _playerPackage = new RuntimeNativePlayerPackage(_pluginStack, _player, _scripts.Session, _scripts.References!, () => _activeCell,
            _restorePlayerPackageAudio, (program, committed) =>
                (_resultScripts ?? throw new InvalidOperationException("Player package result VM is absent."))
                    .ExecutePlayerPackageEvent(program, committed));
        _scripts.References!.UnloadedPackages = new(_pluginStack, _scripts.References, _quests, _gameTime,
            _globals, ExecutePackageEvent, () => SourcePlayerLevel, ActorSitting);
        _speech = new RuntimeNativeSpeech();
        _speech.PrepareSubtitle = subtitle => (PrepareSubtitle ??
            throw new NotSupportedException("Source subtitle presentation is absent."))(subtitle);
        _speech.SayToCompleted += receipt => (SayToCompleted ??
            throw new NotSupportedException("Source SayToDone event dispatch is absent."))(receipt);
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
        AttachSourceActorPerception();
        ConfigureDetectionAndFinishedSpeech();
        ApplyEnteredActorCommands();
    }

    private void Synchronize()
    {
        ApplyEnteredActorCommands();
        if (BlockingExecutionError is not null) return;
        _player.ApplySourceControls(PlayerControls);
        GD.Print(
            $"OPENNV_NATIVE_OPENING_STAGE quest={QuestEditorId} stage={Stage} " +
            $"movement={PlayerControls.Movement} looking={PlayerControls.Looking} " +
            $"pipBoy={PlayerControls.PipBoy} fighting={PlayerControls.Fighting} " +
            $"timer=original-script-variable-owner pending={string.Join(',', PendingBlockers)} " +
            "source=actual-shared-quest-result-state");
    }

    private void ApplyEnteredActorCommands()
    {
        if (!IsInsideTree() || BlockingExecutionError is not null || _initialStageResultRequest is null) return;
        try
        {
            var stage = _initialStageResultRequest.Value;
            _initialStageResultRequest = null;
            (_stageResults ?? throw new InvalidOperationException("Quest stage owner is absent.")).Enter(stage.Quest, stage.Stage);
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
        if (_scripts.ScriptManualSaves.Order.Writing is null)
            throw new InvalidOperationException("Persistent campaign writes require their actual ordered head lease.");
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
        RequireLevelUpMenuFree("Player name menu");
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
                    source.FalloutExecutablePath, plugin);
            else _raceMenuDefaultModel = FalloutExecutableStringTable.ReadNativeRaceMenuModel(source.FalloutExecutablePath);
            _raceMenuDevicesRead = true;
        }
        return _raceMenuDevices?.ModelFor(command) ?? (command == "showracemenu" ? _raceMenuDefaultModel! :
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
        RequireLevelUpMenuFree("Player appearance menu");
        var modelPath = RaceMenuModel(_raceMenuCommand);
        var entry = new RuntimeNativeRaceSexEntry();
        AddChild(entry);
        entry.Accepted += AcceptCharacter;
        entry.Failed += error =>
        {
            ExecutionError = error.Message;
            if (!ReferenceEquals(_raceSexEntry, entry)) return;
            _raceSexEntry = null;
            entry.ReleasePause();
            entry.QueueFree();
        };
        try
        {
            entry.Configure(_raceSexContract, _character, _pluginStack, _imageSpacePresenter(), modelPath);
            _raceSexEntry = entry;
        }
        catch
        {
            entry.ReleasePause();
            entry.QueueFree();
            throw;
        }
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
        if (_controls.Quests.ContainsKey(QuestEditorId)) _scripts.ExecuteClaimedMenu(_controls.Stage(QuestEditorId, Stage).Quest, 1036, _scriptHost);
    }

    private void AcceptSpecial(FalloutNativeSpecialState state)
    {
        try { AcceptSpecialCore(state); }
        catch (Exception error)
        {
            RetainDriverFailure(error);
            GD.PushError($"OPENNV_NATIVE_VIGOR_DIVERGENCE {error.Message}");
        }
    }

    private void AcceptSpecialCore(FalloutNativeSpecialState state)
    {
        (_specialMenuContract ?? throw new NotSupportedException("SPECIAL tester acceptance has no source contract.")).Validate(state);
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
            "source=player-input-selected-source-menu");
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
        RequireLevelUpMenuFree("Tag skill menu");
        var request = _tagMenuRequest ??
            throw new NotSupportedException("Tag menu has no source SetTagSkills request.");
        _activeTagSkillContract = new(_skillCatalog, request.TotalCount);
        _tagSkillEntry = new RuntimeNativeTagSkillEntry();
        AddChild(_tagSkillEntry);
        _tagSkillEntry.Accepted += AcceptTagSkills;
        _tagSkillEntry.Failed += error => ExecutionError = error.Message;
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
            $"choices={_skillCatalog.Count} required={request.TotalCount} initial={request.ShowInitialTaggedSkills} " +
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
        RequireLevelUpMenuFree("Trait menu");
        var contract = FalloutTraitMenuCatalogue.Read(_pluginStack);
        _traitEntry = new RuntimeNativeTraitEntry();
        AddChild(_traitEntry);
        _traitEntry.Accepted += AcceptTraits;
        _traitEntry.Failed += error => ExecutionError = error.Message;
        try { _traitEntry.Configure(_pluginStack, contract, Traits, PlayerLevel, EvaluateMessageCondition); }
        catch
        {
            _traitEntry.Accepted -= AcceptTraits;
            _traitEntry.ReleasePause();
            _traitEntry.QueueFree();
            _traitEntry = null;
            throw;
        }
        _player.SetModalInput(true);
        GD.Print(
            $"OPENNV_NATIVE_TRAITS_OPEN stage={Stage} " +
            $"choices={contract.Traits.Count} maximum=" +
            $"{contract.MaximumTraits} " +
            "source=live-showtraitmenu-perk presentation=menus/trait_menu.xml");
    }

    private void AcceptTraits(IReadOnlyList<FalloutNativeTraitIdentity> selection)
    {
        try
        {
            FalloutTraitMenuCatalogue.Validate(_pluginStack, selection);
            var player = _pluginStack.RuntimeFormKey(0x14);
            var selectedForms = selection.Select(value => _pluginStack.RuntimeFormKey(value.RuntimeFormId)).ToArray();
            var selected = selectedForms.ToHashSet();
            foreach (var previous in Traits.Select(value => _pluginStack.RuntimeFormKey(value.RuntimeFormId)).Where(form => !selected.Contains(form)))
                _scripts.References!.SetPerkRank(player, previous, 0);
            foreach (var trait in selectedForms) _scripts.References!.SetPerkRank(player, trait, 1);
            _traits = selection.OrderBy(value => value.RuntimeFormId).ToArray();
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            RetainDriverFailure(error);
            GD.PushError($"OPENNV_NATIVE_TRAIT_EFFECT_DIVERGENCE: {error.Message}");
            throw;
        }
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
