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
    private FalloutOpeningStageMachine _machine = null!;
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
    private FalloutNativeSpecialState _special = null!;
    private FalloutPlayerVitals _vitals = null!;
    private FalloutPlayerSkills _playerSkills = null!;
    private IReadOnlyList<FalloutNativeSkillIdentity> _tagSkills = [];
    private IReadOnlyList<FalloutNativeTraitIdentity> _traits = [];
    private RuntimeNativePlayerNameEntry? _nameEntry;
    private RuntimeNativeRaceSexEntry? _raceSexEntry;
    private RuntimeNativeVigorEntry? _vigorEntry;
    private RuntimeNativeTagSkillEntry? _tagSkillEntry;
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
    internal string? ExecutionError { get; private set; }
    private readonly List<object> _headTrackingCommands = [];
    internal object[] HeadTrackingCommands => _headTrackingCommands.ToArray();

    internal string QuestEditorId => _machine.QuestEditorId;
    internal short Stage => _machine.Stage;
    internal float? TimerSeconds => _machine.TimerSeconds;
    internal IReadOnlyCollection<string> PendingBlockers => _machine.PendingBlockers;
    internal FalloutFormKey ActiveCell => _activeCell;
    internal bool HasCampaignSave => File.Exists(_savePath);
    internal string PlayerName => _playerName;
    internal int PlayerLevel => SourcePlayerLevel;
    internal FalloutNativeSpecialState Special => _special;
    internal GameplayVitals Vitals => _vitals.State;
    internal object? SpeechState => _speech?.State;
    internal object? PlayerPackageState => _playerPackage?.State;
    internal object? CharacterCreationState => _raceSexEntry?.State;
    internal object? VigorState => _vigorEntry?.State;

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
        short initialStage)
    {
        if (_machine is not null)
            throw new InvalidOperationException("Native opening stage driver was already configured.");
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _openingGrant = openingGrant ?? throw new ArgumentNullException(nameof(openingGrant));
        _raceSexContract = raceSexContract ?? throw new ArgumentNullException(nameof(raceSexContract));
        _vigorContract = vigorContract ?? throw new ArgumentNullException(nameof(vigorContract));
        _tagSkillContract = tagSkillContract ?? throw new ArgumentNullException(nameof(tagSkillContract));
        _traitFarewellContract = traitFarewellContract ??
            throw new ArgumentNullException(nameof(traitFarewellContract));
        _pluginStack = pluginStack ?? throw new ArgumentNullException(nameof(pluginStack));
        _controls = controls;
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
        _special = restore?.State.Special ?? vigorContract.Initial;
        _vitals = new(pluginStack, raceSexContract.Player, _special, restore?.State.Vitals);
        if (restore is not null)
            FalloutNativeVigorResolver.Validate(vigorContract, _special, allowUnspent: !restore.State.CharacterCreationComplete);
        _tagSkills = restore?.State.TagSkills ?? [];
        if (restore is not null)
            FalloutNativeTagSkillResolver.Validate(tagSkillContract, _tagSkills, allowUnspent: !restore.State.CharacterCreationComplete);
        _traits = restore?.State.Traits ?? [];
        FalloutNativeTraitFarewellResolver.ValidateTraits(traitFarewellContract, _traits);
        _lipConfiguration = lipConfiguration;
        _imageSpaceState = imageSpaceState;
        _quests = quests;
        _scripts = scripts;
        _scriptHost = new((quest, stage) =>
        {
            var source = _controls.Quests.Values.SelectMany(values => values.Values)
                .SingleOrDefault(value => value.Quest == quest && value.Stage == stage);
            return () =>
            {
                if (source is null)
                    (_stageResults ?? throw new InvalidOperationException("Quest stage result owner is absent.")).Enter(quest, stage);
                else
                {
                    _machine!.EnterScriptStage(source.QuestEditorId, stage);
                    Synchronize();
                }
            };
        }, name =>
        {
            if (name.Equals("Health", StringComparison.OrdinalIgnoreCase)) return Vitals.HitPoints;
            if (name.Equals("ActionPoints", StringComparison.OrdinalIgnoreCase)) return Vitals.ActionPoints;
            if (name.Equals("XP", StringComparison.OrdinalIgnoreCase)) return Vitals.ExperiencePoints;
            return _playerSkills.Value(name);
        });
        _inventory = inventory;
        _captureScripts = captureScripts;
        _globals = globals;
        _playerSkills = new(pluginStack, () => _special, IsPlayerTagSkill, () => _traits, globals, inventory,
            raceSexContract.Player, () => pluginStack.RuntimeFormKey(_character.RaceRuntimeFormId), () => _scripts.Session.Hardcore);
        _gameTime = gameTime;
        _skyLighting = skyLighting;
        _restoringEnteredStage = restore is not null;
        _imageSpacePresenter = imageSpacePresenter;
        _machine = new FalloutOpeningStageMachine(
            new(transitions.Transitions.Where(transition => transition.Kind != "stage-script" || transition.Blockers.Count != 0)
                .Select(transition => transition.Kind == "stage-script" ? transition with { Kind = "script-wait" } : transition).ToArray()),
            controls,
            restore?.State.QuestEditorId ?? initialQuestEditorId,
            restore?.State.Stage ?? initialStage,
            restore is null
                ? null
                : FalloutNativeCampaignSave.RestorePlayerControls(restore.State));
        Name = "NativeOpeningStageDriver";
        Synchronize();
    }

    internal void CompleteBlocker(string blocker)
    {
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
        if (_vigorEntry is not null || ExecutionError is not null)
            throw new InvalidOperationException("SPECIAL menu cannot open while its owner is busy or failed.");
        _specialMenuContract = _vigorContract with { RequiredTotal = total };
        _vigorEntry = new RuntimeNativeVigorEntry();
        AddChild(_vigorEntry);
        _vigorEntry.Accepted += AcceptSpecial;
        _vigorEntry.Configure(_specialMenuContract, _special, _pluginStack, _imageSpacePresenter());
        _player.SetModalInput(true);
        GD.Print(
            $"OPENNV_NATIVE_VIGOR_OPEN stage={_machine.Stage} total={total} " +
            $"reference={_vigorContract.TesterReference.FormKey} " +
            "source=live-player-vigor-scripts presentation=owned-love-tester-menu parity=unverified");
    }

    public override void _Process(double delta)
    {
        if (ExecutionError is not null) return;
        try
        {
            _stageResults?.Continue();
            if (_saveRequested) SaveCurrentState();
            _playerPackage?.Advance(delta);
            foreach (var expired in _imageSpaceState.Advance(delta))
                GD.Print($"OPENNV_NATIVE_IMAD_EXPIRED source={expired.Form} duration={expired.Duration:R} owner=gameplay-clock");
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or FileNotFoundException or InvalidOperationException or KeyNotFoundException or OverflowException)
        {
            ExecutionError = error.Message;
            GD.PushError($"OPENNV_NATIVE_PLAYER_PACKAGE_DIVERGENCE: {error.Message}");
            return;
        }
        if (!_moviePlaying && _nameEntry is null && _raceSexEntry is null && _vigorEntry is null &&
            _tagSkillEntry is null && _traitEntry is null)
        {
            try { _scripts.AdvanceClaimed(_controls.Stage(QuestEditorId, Stage).Quest, delta, _scriptHost); }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or InvalidOperationException or KeyNotFoundException or OverflowException)
            {
                ExecutionError = error.Message;
                GD.PushError($"OPENNV_NATIVE_QUEST_SCRIPT_DIVERGENCE quest={QuestEditorId} stage={Stage}: {error.Message}");
                return;
            }
        }
    }

    public override void _Ready()
    {
        _playerPackage = new RuntimeNativePlayerPackage(_pluginStack, _player);
        _speech = new RuntimeNativeSpeech();
        _speech.InfoCompleted += _ =>
        {
            if (!_speech.Active && _speechStage == $"{_machine.QuestEditorId}:{_machine.Stage}" &&
                _machine.PendingBlockers.Contains("sayto", StringComparer.OrdinalIgnoreCase))
            {
                _machine.CompleteDialogueSpeech();
                Synchronize();
            }
        };
        _speech.Configure(_pluginStack, _lipConfiguration, quest => _quests.Stage(quest), condition =>
        {
            if (condition.RunOn == 1 && condition.Function == 70 && condition.Reference == 0)
            {
                if (condition.Argument1 > 1) throw new InvalidDataException("GetIsSex has an invalid sex argument.");
                return (condition.Argument1 == 1) == _character.Female ? 1 : 0;
            }
            if (condition.RunOn == 0 && condition.Function is 59 or 79 or 546) return _quests.Evaluate(condition);
            throw new NotSupportedException($"Dialogue condition {condition.Function} RunOn {condition.RunOn} has no actor/quest owner.");
        }, _scripts.SaidInfos);
        AddChild(_speech);
        ConfigureConversation();
        ApplyEnteredActorCommands();
    }

    private void Synchronize()
    {
        ApplyEnteredActorCommands();
        if (ExecutionError is not null) return;
        _player.ApplySourceControls(_machine.ControlState);
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
        if (!IsInsideTree() || ExecutionError is not null) return;
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
            ExecutionError = error.Message;
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
        if (!File.Exists(_savePath))
            throw new InvalidOperationException(
                "Native world state has no prior save.");
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

    private void SynchronizeNameEntry()
    {
        var pending = _machine.PendingBlockers.Contains(
            "getplayername", StringComparer.OrdinalIgnoreCase);
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
            $"OPENNV_NATIVE_NAME_ENTRY_OPEN quest={_machine.QuestEditorId} stage={_machine.Stage} " +
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

    private void SynchronizeRaceSexEntry()
    {
        var pending = _machine.PendingBlockers.Contains(
            "showracemenu", StringComparer.OrdinalIgnoreCase);
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
            return;
        _raceSexEntry = new RuntimeNativeRaceSexEntry();
        AddChild(_raceSexEntry);
        _raceSexEntry.Accepted += AcceptCharacter;
        _raceSexEntry.Failed += error => ExecutionError = error.Message;
        _raceSexEntry.Configure(_raceSexContract, _character, _pluginStack, _imageSpacePresenter());
        GD.Print(
            $"OPENNV_NATIVE_RACESEX_OPEN quest={_machine.QuestEditorId} stage={_machine.Stage} " +
            $"race={_character.RaceEditorId}/{_character.RaceRuntimeFormId:x8} " +
            "source=player-race-hair-eyes-ctl presentation=owned-rendered-menu parity=unverified");
    }

    private void AcceptCharacter(FalloutNativeRaceSexSelection selection)
    {
        FalloutNativeRaceSexResolver.Validate(_raceSexContract, selection);
        _character = selection;
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
        CompleteBlocker("showracemenu");
        // The existing modal handoff delivers the source MenuMode event here.
        // Exact scheduling while the menu is open remains a separate owner.
        _scripts.ExecuteClaimedMenu(_controls.Stage(QuestEditorId, Stage).Quest, 1036, _scriptHost);
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
        _special = state;
        _vitals.SetSpecial(state);
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
            $"OPENNV_NATIVE_SPECIAL_ACCEPTED total={_special.Values.Sum()} " +
            $"values={string.Join(',', _special.Values)} stage={_machine.Stage} " +
            "source=configured-player-input-live-vigor-contract");
        Synchronize();
    }

    private void SynchronizeTagSkillEntry()
    {
        var pending = _machine.PendingBlockers.Contains(
            "settagskills", StringComparer.OrdinalIgnoreCase);
        if (!pending)
        {
            if (_tagSkillEntry is not null)
            {
                _tagSkillEntry.QueueFree();
                _tagSkillEntry = null;
            }
            return;
        }
        if (_tagSkillEntry is not null)
            return;
        _tagSkillEntry = new RuntimeNativeTagSkillEntry();
        AddChild(_tagSkillEntry);
        _tagSkillEntry.Accepted += AcceptTagSkills;
        _tagSkillEntry.Configure(_tagSkillContract, _tagSkills);
        _player.SetModalInput(true);
        GD.Print(
            $"OPENNV_NATIVE_TAG_SKILLS_OPEN stage={_machine.Stage} " +
            $"choices={_tagSkillContract.Skills.Count} required={_tagSkillContract.RequiredCount} " +
            "source=live-settagskills-avif presentation=first-party-functional");
    }

    private void AcceptTagSkills(IReadOnlyList<FalloutNativeSkillIdentity> selection)
    {
        FalloutNativeTagSkillResolver.Validate(_tagSkillContract, selection);
        _tagSkills = selection.ToArray();
        if (_tagSkillEntry is not null)
        {
            _tagSkillEntry.Accepted -= AcceptTagSkills;
            _tagSkillEntry.QueueFree();
            _tagSkillEntry = null;
        }
        _player.SetModalInput(false);
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        GD.Print(
            $"OPENNV_NATIVE_TAG_SKILLS_ACCEPTED skills=" +
            $"{string.Join(',', _tagSkills.Select(value => value.EditorId))} " +
            "source=configured-player-input-live-avif-contract");
        CompleteBlocker("settagskills");
    }

    private void SynchronizeTraitEntry()
    {
        var pending = _machine.PendingBlockers.Contains(
            "showtraitmenu", StringComparer.OrdinalIgnoreCase);
        if (!pending)
        {
            if (_traitEntry is not null)
            {
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
        _traitEntry.Configure(_traitFarewellContract, _traits);
        _player.SetModalInput(true);
        GD.Print(
            $"OPENNV_NATIVE_TRAITS_OPEN stage={_machine.Stage} " +
            $"choices={_traitFarewellContract.Traits.Count} maximum=" +
            $"{_traitFarewellContract.MaximumTraits} " +
            "source=live-showtraitmenu-perk presentation=first-party-functional");
    }

    private void AcceptTraits(IReadOnlyList<FalloutNativeTraitIdentity> selection)
    {
        FalloutNativeTraitFarewellResolver.ValidateTraits(_traitFarewellContract, selection);
        _traits = selection.OrderBy(value => value.RuntimeFormId).ToArray();
        if (_traitEntry is not null)
        {
            _traitEntry.Accepted -= AcceptTraits;
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
