using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.Presentation.Ui;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver : Node
{
    private RuntimeNativePlayer _player = null!;
    private FalloutOpeningStageMachine _machine = null!;
    private FalloutOpeningInventoryGrant _openingGrant = null!;
    private FalloutNativeRaceSexContract _raceSexContract = null!;
    private FalloutNativeVigorContract _vigorContract = null!;
    private FalloutNativeTagSkillContract _tagSkillContract = null!;
    private FalloutNativeTraitFarewellContract _traitFarewellContract = null!;
    private FalloutPluginStack _pluginStack = null!;
    private string _savePath = string.Empty;
    private string _saveCompatibilityId = string.Empty;
    private FalloutFormKey _activeCell;
    private string _playerName = string.Empty;
    private FalloutNativeRaceSexSelection _character = null!;
    private FalloutNativeSpecialState _special = null!;
    private IReadOnlyList<FalloutNativeSkillIdentity> _tagSkills = [];
    private IReadOnlyList<FalloutNativeTraitIdentity> _traits = [];
    private RuntimeNativePlayerNameEntry? _nameEntry;
    private RuntimeNativeRaceSexEntry? _raceSexEntry;
    private RuntimeNativeVigorEntry? _vigorEntry;
    private RuntimeNativePsychHandoffEntry? _psychEntry;
    private RuntimeNativeTagSkillEntry? _tagSkillEntry;
    private RuntimeNativeTraitEntry? _traitEntry;
    private RuntimeNativeFarewellEntry? _farewellEntry;
    private float? _farewellSeconds;
    private FalloutOpeningInventoryGrant? _completedGrant;
    private bool _stage200Saved;
    private FalloutOpeningControlGraph _controls = null!;
    private bool _moviePlaying;
    private RuntimeNativeSpeech? _speech;
    private FaceGenLipConfiguration _lipConfiguration = null!;
    private string? _speechStage;
    private RuntimeNativePlayerPackage? _playerPackage;
    private FalloutImageSpaceState _imageSpaceState = null!;
    private FalloutQuestState _quests = null!;
    private FalloutPlayerInventory _inventory = null!;
    private Func<FalloutQuestScriptsSnapshot?> _captureScripts = null!;
    internal string? ExecutionError { get; private set; }

    internal string QuestEditorId => _machine.QuestEditorId;
    internal short Stage => _machine.Stage;
    internal float? TimerSeconds => _machine.TimerSeconds;
    internal IReadOnlyCollection<string> PendingBlockers => _machine.PendingBlockers;
    internal FalloutFormKey ActiveCell => _activeCell;
    internal object? SpeechState => _speech?.State;
    internal object? PlayerPackageState => _playerPackage?.State;
    internal object? CharacterCreationState => _raceSexEntry?.State;

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
        FalloutPlayerInventory inventory,
        Func<FalloutQuestScriptsSnapshot?> captureScripts,
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
        _stage200Saved = restore is not null;
        _playerName = restore?.State.PlayerName ?? FalloutDialogueTopic.Text(
            FalloutDialogueTopic.Find(pluginStack, "GMST", "sDefaultPlayerName")
                .ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span);
        _character = restore?.State.Character ?? raceSexContract.Initial;
        FalloutNativeRaceSexResolver.Validate(raceSexContract, _character);
        _special = restore?.State.Special ?? vigorContract.Initial;
        if (restore is not null)
            FalloutNativeVigorResolver.Validate(vigorContract, _special);
        _tagSkills = restore?.State.TagSkills ?? [];
        if (restore is not null)
            FalloutNativeTagSkillResolver.Validate(tagSkillContract, _tagSkills);
        _traits = restore?.State.Traits ?? [];
        FalloutNativeTraitFarewellResolver.ValidateTraits(traitFarewellContract, _traits);
        if (restore is not null)
            _completedGrant = FalloutNativeTraitFarewellResolver.ResolveGrant(
                traitFarewellContract,
                openingGrant,
                _tagSkills);
        _lipConfiguration = lipConfiguration;
        _imageSpaceState = imageSpaceState;
        _quests = quests;
        _inventory = inventory;
        _captureScripts = captureScripts;
        _machine = new FalloutOpeningStageMachine(
            transitions,
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

    internal void EnterVigorTrigger()
    {
        if (_machine.QuestEditorId != FalloutNativeCampaignSave.OpeningQuestEditorId ||
            _machine.Stage != _vigorContract.TriggerFromStage)
            throw new InvalidOperationException(
                $"Native Vigor trigger entered at unsupported stage {_machine.QuestEditorId}:{_machine.Stage}.");
        _machine.EnterSourceStage(
            FalloutNativeCampaignSave.OpeningQuestEditorId,
            _vigorContract.TesterStage);
        GD.Print(
            $"OPENNV_NATIVE_VIGOR_TRIGGER stage={_vigorContract.TriggerFromStage}->" +
            $"{_vigorContract.TesterStage} reference={_vigorContract.TriggerReference.FormKey} " +
            "source=live-trigger-script-xprm");
        Synchronize();
    }

    internal void ActivateVigorTester()
    {
        if (_machine.QuestEditorId != FalloutNativeCampaignSave.OpeningQuestEditorId ||
            _machine.Stage != _vigorContract.TesterStage || _vigorEntry is not null)
        {
            GD.Print(
                $"OPENNV_NATIVE_VIGOR_ACTIVATE accepted=false stage={_machine.QuestEditorId}:{_machine.Stage}");
            return;
        }
        _vigorEntry = new RuntimeNativeVigorEntry();
        AddChild(_vigorEntry);
        _vigorEntry.Accepted += AcceptSpecial;
        _vigorEntry.Configure(_vigorContract, _special);
        _player.SetModalInput(true);
        GD.Print(
            $"OPENNV_NATIVE_VIGOR_OPEN stage={_machine.Stage} total={_vigorContract.RequiredTotal} " +
            $"reference={_vigorContract.TesterReference.FormKey} " +
            "source=live-player-vigor-scripts presentation=first-party-functional");
    }

    internal void EnterFarewellTrigger()
    {
        if (_machine.QuestEditorId != FalloutNativeCampaignSave.OpeningQuestEditorId ||
            _machine.Stage != _traitFarewellContract.ExitTriggerFromStage)
            throw new InvalidOperationException(
                $"Native farewell trigger entered at unsupported stage {_machine.QuestEditorId}:{_machine.Stage}.");
        _machine.EnterSourceStage(
            FalloutNativeCampaignSave.OpeningQuestEditorId,
            _traitFarewellContract.FarewellStage);
        GD.Print(
            $"OPENNV_NATIVE_FAREWELL_TRIGGER stage={_traitFarewellContract.ExitTriggerFromStage}->" +
            $"{_traitFarewellContract.FarewellStage} reference=" +
            $"{_traitFarewellContract.ExitTriggerReference.FormKey} " +
            "source=live-trigger-script-xprm");
        Synchronize();
    }

    public override void _Process(double delta)
    {
        if (ExecutionError is not null) return;
        try
        {
            _playerPackage?.Advance(delta);
            foreach (var expired in _imageSpaceState.Advance(delta))
                GD.Print($"OPENNV_NATIVE_IMAD_EXPIRED source={expired.Form} duration={expired.Duration:R} owner=gameplay-clock");
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or FileNotFoundException)
        {
            ExecutionError = error.Message;
            GD.PushError($"OPENNV_NATIVE_PLAYER_PACKAGE_DIVERGENCE: {error.Message}");
            return;
        }
        var changed = _machine.AdvanceTime((float)delta);
        if (_farewellSeconds is not null)
        {
            _farewellSeconds -= (float)delta;
            if (_farewellSeconds <= 0.0f)
            {
                _farewellSeconds = null;
                _machine.EnterSourceStage(
                    FalloutNativeCampaignSave.OpeningQuestEditorId,
                    _traitFarewellContract.CompletedStage);
                changed = true;
            }
        }
        if (changed)
            Synchronize();
    }

    public override void _Ready()
    {
        _playerPackage = new RuntimeNativePlayerPackage(_pluginStack, _player);
        _speech = new RuntimeNativeSpeech();
        _speech.Configure(_pluginStack, _lipConfiguration, (quest, stage) =>
        {
            _machine.CompleteDialogueResult(quest, stage);
            Synchronize();
        }, quest => quest == _controls.Stage(_machine.QuestEditorId, _machine.Stage).Quest
            ? _machine.Stage : throw new NotSupportedException($"Quest state {quest} is not published by an active owner."));
        AddChild(_speech);
        ApplyEnteredActorCommands();
        SynchronizeMovie();
        SynchronizeSpeech();
    }

    private void SynchronizeMovie()
    {
        if (!IsInsideTree() || _moviePlaying || !_machine.PendingBlockers.Contains("playbink", StringComparer.OrdinalIgnoreCase))
            return;
        var commands = new Queue<FalloutMovieCommand>(FalloutMovieCommand.FromScript(
            _controls.Stage(_machine.QuestEditorId, _machine.Stage).Source));
        if (commands.Count == 0)
            throw new InvalidDataException("Pending PlayBink has no decoded source command.");
        _moviePlaying = true;
        void PlayNext()
        {
            if (commands.Count == 0)
            {
                _moviePlaying = false;
                CompleteBlocker("playbink");
                return;
            }
            var movie = new NativeGamebryoMovie();
            movie.Configure(commands.Dequeue(), _ => Callable.From(PlayNext).CallDeferred());
            AddChild(movie);
        }
        PlayNext();
    }

    private void SynchronizeSpeech()
    {
        if (_speech is null || !_machine.PendingBlockers.Contains("sayto", StringComparer.OrdinalIgnoreCase))
            return;
        var key = $"{_machine.QuestEditorId}:{_machine.Stage}";
        if (_speechStage == key) return;
        _speechStage = key;
        var commands = FalloutDialogueTopic.SayToCommands(_controls.Stage(_machine.QuestEditorId, _machine.Stage).Source);
        if (commands.Count != 1) throw new NotSupportedException("Pending SayTo requires exactly one source speaker command.");
        _speech.Start(commands[0]);
    }

    private void Synchronize()
    {
        ApplyEnteredActorCommands();
        if (ExecutionError is not null) return;
        _player.ApplySourceControls(_machine.ControlState);
        SynchronizeMovie();
        SynchronizeSpeech();
        SynchronizeNameEntry();
        SynchronizeRaceSexEntry();
        SynchronizePsychHandoff();
        SynchronizeTagSkillEntry();
        SynchronizeTraitEntry();
        SynchronizeFarewellEntry();
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
            var transform = _player.GlobalTransform;
            var rotation = transform.Basis.GetRotationQuaternion().Normalized();
            var completedGrant = _inventory.Capture();
            var scriptState = _captureScripts();
            var state = FalloutNativeCampaignSave.Capture(
                _saveCompatibilityId,
                _activeCell,
                completedGrant,
                _playerName,
                _character,
                _vigorContract,
                _special,
                _tagSkillContract,
                _tagSkills,
                _traitFarewellContract,
                _traits,
                _machine.ControlState,
                [transform.Origin.X, transform.Origin.Y, transform.Origin.Z],
                [rotation.X, rotation.Y, rotation.Z, rotation.W],
                scriptState is null ? null : _quests.Capture(), scriptState);
            FalloutNativeCampaignSave.Write(_savePath, state);
            _stage200Saved = true;
            GD.Print(
                $"OPENNV_NATIVE_OPENING_SAVED stage={state.QuestEditorId}:{state.Stage} " +
                $"items={state.Inventory.Count} equipped={state.EquippedRuntimeFormIds.Count} " +
                $"save={_savePath} source=live-qust-info-records writes=save-only");
        }
    }

    private void ApplyEnteredActorCommands()
    {
        if (!IsInsideTree() || ExecutionError is not null) return;
        try
        {
            while (_machine.TryTakeEnteredStage(out var stage))
            {
                _quests.EnterStage(stage!.Quest, stage.Stage);
                var packages = FalloutScriptPackageCommands.Read(stage!.Source).ToDictionary(command => command.Line);
                var imageSpaces = FalloutImageSpaceCommands.Read(stage.Source).ToDictionary(command => command.Line);
                var lines = FalloutDialogueTopic.CodeLines(stage.Source).ToArray();
                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    if (imageSpaces.TryGetValue(lineIndex, out var imageSpace))
                    {
                        var record = FalloutDialogueTopic.Find(_pluginStack, "IMAD", imageSpace.EditorId);
                        if (imageSpace.Apply) _imageSpaceState.Apply(FalloutImageSpaceModifierReader.Read(record));
                        else _imageSpaceState.Remove(record.FormKey);
                        GD.Print($"OPENNV_NATIVE_IMAD_COMMAND source={record.FormKey} apply={imageSpace.Apply} " +
                            "owner=source-script-imad cinematicFade=bound otherChannels=reported-unbound parity=unmeasured");
                    }
                    if (packages.TryGetValue(lineIndex, out var package))
                    {
                        if (!package.ActorEditorId.Equals("player", StringComparison.OrdinalIgnoreCase))
                            throw new NotSupportedException($"Actor {package.ActorEditorId} script package simulation is unbound.");
                        _playerPackage!.Apply(package.PackageEditorId);
                    }
                    foreach (var command in FalloutActorIdleCommands.Read(lines[lineIndex]))
                    {
                        var reference = FalloutDialogueTopic.Find(_pluginStack, "ACHR", command.ActorEditorId);
                        var actors = GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
                            .Where(actor => actor.Appearance.Reference == reference.FormKey).ToArray();
                        if (actors.Length != 1)
                            throw new InvalidDataException($"PlayIdle source actor {reference.FormKey} has {actors.Length} runtime owners.");
                        actors[0].PlayIdle(_pluginStack, command.IdleEditorId);
                    }
                }
            }
        }
        catch (Exception error) when (error is NotSupportedException or InvalidDataException or FileNotFoundException)
        {
            ExecutionError = error.Message;
            GD.PushError($"OPENNV_NATIVE_STAGE_DIVERGENCE quest={QuestEditorId} stage={Stage}: {error.Message}");
        }
    }

    internal FalloutNativeCampaignState PersistWorldState(FalloutFormKey activeCell)
    {
        if (!_stage200Saved)
            throw new InvalidOperationException(
                "Native world state cannot persist before the opening completes.");
        var restore = FalloutNativeCampaignSave.Read(
            _savePath,
            _saveCompatibilityId,
            _pluginStack,
            _vigorContract,
            _tagSkillContract,
            _openingGrant,
            _traitFarewellContract);
        var transform = _player.GlobalTransform;
        var rotation = transform.Basis.GetRotationQuaternion().Normalized();
        var state = FalloutNativeCampaignSave.WithWorldState(
            restore.State,
            activeCell,
            [transform.Origin.X, transform.Origin.Y, transform.Origin.Z],
            [rotation.X, rotation.Y, rotation.Z, rotation.W]);
        FalloutNativeCampaignSave.Write(_savePath, state);
        _activeCell = activeCell;
        GD.Print(
            $"OPENNV_NATIVE_WORLD_SAVED cell={activeCell} stage={state.QuestEditorId}:{state.Stage} " +
            $"save={_savePath} owner=native-campaign-state");
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
        _raceSexEntry.Configure(_raceSexContract, _character, _pluginStack);
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
    }

    private void AcceptSpecial(FalloutNativeSpecialState state)
    {
        FalloutNativeVigorResolver.Validate(_vigorContract, state);
        _special = state;
        if (_vigorEntry is not null)
        {
            _vigorEntry.Accepted -= AcceptSpecial;
            _vigorEntry.QueueFree();
            _vigorEntry = null;
        }
        _player.SetModalInput(false);
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        _machine.EnterSourceStage(
            FalloutNativeCampaignSave.OpeningQuestEditorId,
            _vigorContract.CompletedStage);
        GD.Print(
            $"OPENNV_NATIVE_SPECIAL_ACCEPTED total={_special.Values.Sum()} " +
            $"values={string.Join(',', _special.Values)} stage={_vigorContract.CompletedStage} " +
            "source=configured-player-input-live-vigor-contract");
        Synchronize();
    }

    private void SynchronizePsychHandoff()
    {
        var pending = _machine.QuestEditorId == FalloutNativeCampaignSave.OpeningQuestEditorId &&
            _machine.Stage == _tagSkillContract.PsychStage;
        if (!pending)
        {
            if (_psychEntry is not null)
            {
                _psychEntry.QueueFree();
                _psychEntry = null;
            }
            return;
        }
        if (_psychEntry is not null)
            return;
        _psychEntry = new RuntimeNativePsychHandoffEntry();
        AddChild(_psychEntry);
        _psychEntry.Accepted += AcceptPsychHandoff;
        _psychEntry.Configure();
        _player.SetModalInput(true);
        GD.Print(
            $"OPENNV_NATIVE_PSYCH_HANDOFF_OPEN stage={_machine.Stage} " +
            $"terminal={_tagSkillContract.PsychCompletedStage} " +
            "source=live-info-terminal-results presentation=first-party-functional");
    }

    private void AcceptPsychHandoff()
    {
        if (_psychEntry is not null)
        {
            _psychEntry.Accepted -= AcceptPsychHandoff;
            _psychEntry.QueueFree();
            _psychEntry = null;
        }
        _player.SetModalInput(false);
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        _machine.EnterSourceStage(
            FalloutNativeCampaignSave.OpeningQuestEditorId,
            _tagSkillContract.PsychCompletedStage);
        GD.Print(
            $"OPENNV_NATIVE_PSYCH_HANDOFF_ACCEPTED stage={_tagSkillContract.PsychCompletedStage} " +
            "boundary=questionnaire-presentation-unsupported");
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

    private void SynchronizeFarewellEntry()
    {
        var pending = _machine.QuestEditorId == FalloutNativeCampaignSave.OpeningQuestEditorId &&
            _machine.Stage == _traitFarewellContract.FarewellStage &&
            _farewellSeconds is null;
        if (!pending)
        {
            if (_farewellEntry is not null)
            {
                _farewellEntry.QueueFree();
                _farewellEntry = null;
            }
            return;
        }
        if (_farewellEntry is not null)
            return;
        _completedGrant = FalloutNativeTraitFarewellResolver.ResolveGrant(
            _traitFarewellContract,
            _openingGrant,
            _tagSkills);
        _farewellEntry = new RuntimeNativeFarewellEntry();
        AddChild(_farewellEntry);
        _farewellEntry.Accepted += AcceptFarewell;
        _farewellEntry.Configure(_completedGrant);
        _player.SetModalInput(true);
        GD.Print(
            $"OPENNV_NATIVE_FAREWELL_OPEN stage={_machine.Stage} " +
            $"items={_completedGrant.Inventory.Items.Count} " +
            "source=live-info-tag-branches presentation=first-party-functional");
    }

    private void AcceptFarewell()
    {
        if (_farewellSeconds is not null) return;
        _inventory.AddGrant(_completedGrant ?? throw new InvalidOperationException("Farewell grant was not resolved."));
        if (_farewellEntry is not null)
        {
            _farewellEntry.Accepted -= AcceptFarewell;
            _farewellEntry.QueueFree();
            _farewellEntry = null;
        }
        _player.SetModalInput(false);
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
        _farewellSeconds = _traitFarewellContract.CompletionDelaySeconds;
        GD.Print(
            $"OPENNV_NATIVE_FAREWELL_ACCEPTED delay={_farewellSeconds:R} " +
            "source=live-vgenerictimer-event3");
    }
}
