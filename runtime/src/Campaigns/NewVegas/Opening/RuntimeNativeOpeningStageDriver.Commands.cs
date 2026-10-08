using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutFormKey? _music;
    private FalloutRadioHudDeclaration? _radioHudDeclaration;
    internal FalloutFormKey? SourceMusic => _music;
    private bool _saveRequested;
    private CanvasLayer? _recipeLayer;
    private NativeOwnedRecipeMenu? _recipeMenu;
    private CanvasLayer? _barterLayer;
    private NativeOwnedBarterMenu? _barterMenu;
    private readonly HashSet<CanvasItem> _screenSplatters = [];
    private string? SaveContinuationBlocker =>
        _moviePlaying ? "movie" : _player.FurnitureActive ? "furniture" :
        _conversation?.Active == true ? "conversation" : _speech is { CanCaptureState: false } ? "speech" :
        _nameEntry is not null ? "name-menu" : _raceSexEntry is not null ? "race-menu" :
        _specialBookEntry is not null ? "special-book-menu" :
        _vigorEntry is not null ? "special-menu" : _tagSkillEntry is not null ? "tag-menu" :
        _traitEntry is not null ? "trait-menu" : _recipeMenu is not null ? "recipe-menu" :
        _barterMenu is not null ? "barter-menu" :
        TerminalSaveBlocker is { } terminal ? terminal :
        _scripts.References?.PendingProcedureCaptureCount > 0 ? "actor-procedure-initialization" :
        StageResultsSaveBlocker;
    internal object SaveRequestState => new
    {
        requested = _saveRequested,
        sourceManual = _scripts.ScriptManualSaves.Receipt,
        sourceManualDeferredBy = _scripts.ScriptManualSaves.DeferredBy,
        deferredBy = _scripts.References!.PlayerMoves.Pending ? "player-move" : SaveContinuationBlocker ?? SourceAnimationSoundSaveBlocker,
        activeContinuationSaving = "source-radio-pcm;other-continuations-unbound"
    };

    internal void ApplyNativeSourceCommand(FalloutFormKey source, FalloutScriptBindings bindings, string command, IReadOnlyList<string> arguments)
    {
        var parts = command.Split('.');
        var operation = parts[^1].ToLowerInvariant();
        var target = parts.Length == 2 ? bindings.Reference(parts[0]) : source;
        RuntimeNativeNpc Actor() => GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
            .Single(actor => actor.Appearance.Reference == target);
        switch (operation)
        {
            case "startradioconversation" when arguments.Count <= 1:
                _speech!.StartRadioConversation(target, arguments.Count == 0 ? null : bindings.Form(arguments[0]).FormKey);
                break;
            case "stopcombatalarmonactor" or "scaonactor" when arguments.Count == 0:
                _scripts.References!.StopCombatAlarmOnActor(target);
                break;
            case "forceradiostationupdate" or "frsu" when parts.Length == 1 && arguments.Count == 0:
                RefreshRadioStations(force: true);
                break;
            case "sexchange" when arguments.Count <= 2 && (parts.Length == 1 || _pluginStack.RuntimeFormId(target) == 0x14):
                var female = arguments.Count == 0 ? !_character.Female : arguments[0].ToLowerInvariant() switch
                {
                    "male" or "0" => false,
                    "female" or "1" => true,
                    _ => throw new InvalidDataException("SexChange target sex is invalid."),
                };
                var resetAppearance = arguments.Count == 2 ? arguments[1] switch
                {
                    "0" => false,
                    "1" => true,
                    _ => throw new InvalidDataException("SexChange reset flag is invalid."),
                } : false;
                if (female == _character.Female) break;
                if (resetAppearance)
                {
                    var content = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Character content owner is absent.");
                    var creation = new FalloutNativeCharacterCreation(_pluginStack, _raceSexContract, _character, FalloutInstallationSettings.Read(content));
                    creation.ChangeIdentity(_character.RaceRuntimeFormId, female);
                    _character = creation.Selection;
                }
                else _character = _raceSexContract.Select(_character.RaceRuntimeFormId, female, _character) with { Face = _character.Face };
                FalloutNativeRaceSexResolver.Validate(_raceSexContract, _character);
                _characterRevision++;
                break;
            case "clearscreensplatter" when parts.Length == 1 && arguments.Count == 0:
                foreach (var splatter in _screenSplatters)
                    if (GodotObject.IsInstanceValid(splatter)) splatter.QueueFree();
                _screenSplatters.Clear();
                break;
            case "removescriptpackage" when arguments.Count == 0 && _pluginStack.RuntimeFormId(target) == 0x14:
                _playerPackage!.Apply(null);
                break;
            case "playidle" when arguments.Count == 1:
                // PlayIdle's animation name is encoded as a string argument,
                // not a SCRO form slot in the result program.
                Actor().PlayIdle(_pluginStack, arguments[0].Trim('"'));
                break;
            case "resetai" when arguments.Count == 0:
                EvaluateActorPackages(target, true);
                break;
            case "getplayername" when parts.Length == 1 && arguments.Count == 0:
                SynchronizeNameEntry(sourceRequested: true);
                if (_nameEntry is null) throw new NotSupportedException("Name input has no active menu owner.");
                break;
            case "showracemenu" or "ttw_showgeneprojector" when parts.Length == 1 && arguments.Count == 0:
                SynchronizeRaceSexEntry(sourceRequested: true, sourceCommand: operation);
                if (_raceSexEntry is null) throw new NotSupportedException("Race input has no active menu owner.");
                break;
            case "settagskills" when parts.Length == 1:
                var tagMenuRequest = FalloutTagSkillMenuRequest.Read(arguments);
                if (_tagSkillEntry is not null) throw new InvalidOperationException("A tag menu already owns player input.");
                _tagMenuRequest = tagMenuRequest;
                SynchronizeTagSkillEntry(sourceRequested: true);
                if (_tagSkillEntry is null) throw new NotSupportedException("Tag input has no active menu owner.");
                break;
            case "showtraitmenu" when parts.Length == 1 && arguments.Count == 0:
                SynchronizeTraitEntry(sourceRequested: true);
                if (_traitEntry is null) throw new NotSupportedException("Trait input has no active menu owner.");
                break;
            case "showspecialbookmenuparams" or "ssbmp" when parts.Length == 1 && arguments.Count == 1:
                if (!int.TryParse(arguments[0], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var bookBudget))
                    throw new NotSupportedException("SPECIAL book allocation requires an owned integer points parameter.");
                OpenSpecialBookMenu(bookBudget);
                break;
            case "showrecipemenu" when arguments.Count == 1 && (parts.Length == 1 || _pluginStack.RuntimeFormId(target) == 0x14):
                var recipeCategory = bindings.Form(arguments[0]);
                if (recipeCategory.Signature != "RCCT")
                    throw new InvalidDataException("ShowRecipeMenu argument is not an RCCT category.");
                OpenRecipeMenu(recipeCategory.FormKey);
                break;
            case "showbartermenu" or "sbm" when parts.Length is 1 or 2 && arguments.Count == 1:
                if (!int.TryParse(arguments[0], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var discount) || discount is < -100 or > 100)
                    throw new InvalidDataException("ShowBarterMenu discount must be an integer from -100 through 100.");
                OpenBarterMenu(target, discount);
                break;
            case "playbink" when parts.Length == 1:
                if (_moviePlaying) throw new InvalidOperationException("Movie player is already active.");
                var movieCommand = FalloutMovieCommand.FromScript(command + " " + string.Join(' ', arguments)).Single();
                var movie = new NativeGamebryoMovie();
                _moviePlaying = true;
                movie.Configure(movieCommand, _ => Callable.From(() =>
                {
                    _moviePlaying = false;
                    CompleteBlocker("playbink");
                }).CallDeferred());
                AddChild(movie);
                break;
            case "playmusic" when parts.Length == 1 && arguments.Count == 1:
                var music = bindings.Form(arguments[0]);
                if (music.Signature != "MUSC") throw new InvalidDataException("Script music is not MUSC.");
                if (music.ReadSubrecords().Any(field => field.Signature != "EDID"))
                    throw new NotSupportedException("Nonempty script music requires its streaming playback owner.");
                _music = music.FormKey;
                break;
            case "forceweather" when parts.Length == 1 && arguments.Count == 1:
                (_skyLighting ?? throw new InvalidOperationException("Sky state owner is absent.")).ForceWeather(bindings.Form(arguments[0]).FormKey);
                break;
            case "releaseweatheroverride" when parts.Length == 1 && arguments.Count == 0:
                (_skyLighting ?? throw new InvalidOperationException("Sky state owner is absent.")).ReleaseWeatherOverride();
                break;
            case "autosave" when parts.Length == 1 && arguments.Count == 0:
                _saveRequested = true;
                break;
            default:
                throw new NotSupportedException($"Reached native script command {command} ({arguments.Count} arguments) has no owner.");
        }
    }

    private void SaveCurrentState()
    {
        var state = CaptureCurrentState(_activeCell);
        FalloutNativeCampaignSave.Write(_savePath, state);
        _stage200Saved = state.CharacterCreationComplete;
        _saveRequested = false;
        GD.Print($"OPENNV_NATIVE_CAMPAIGN_SAVED quest={QuestEditorId} stage={Stage} creationComplete={state.CharacterCreationComplete} save={_savePath}");
    }

    private FalloutNativeCampaignState CaptureCurrentState(FalloutFormKey activeCell)
    {
        _scripts.ScriptManualSaves.RequireCapture();
        _scripts.References!.PlayerMoves.RequireSettled();
        if (SaveContinuationBlocker is { } blocker)
            throw new NotSupportedException($"Saving {blocker} requires continuation state.");
        var transform = _player.GlobalTransform;
        var rotation = transform.Basis.Orthonormalized().GetRotationQuaternion().Normalized();
        var complete = _tagSkillContract is null ? !_scripts.Session.InCharGen :
            _quests.IsCompleted(FalloutDialogueTopic.Find(_pluginStack, "QUST", FalloutNativeCampaignSave.OpeningQuestEditorId).FormKey);
        var state = FalloutNativeCampaignSave.Capture(_saveCompatibilityId, activeCell, _inventory.Capture(), _playerName, _character,
            _vigorContract, Special, _tagSkillContract, _tagSkills.Selection, _traitFarewellContract, _traits, PlayerControls,
            [transform.Origin.X, transform.Origin.Y, transform.Origin.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W],
            _quests.Capture(), _captureScripts(), _globals?.Capture(), _gameTime?.Capture(), _skyLighting?.Capture(), _scripts.References?.Capture(),
            QuestEditorId, Stage, complete, _player.ViewPitchRadians, _playerActorValues.Capture(), _tagSkills.Capture(),
            _scripts.References!.CaptureDetection(), _speech?.CaptureState(), CaptureFinishedSpeechStage(),
            CaptureStageResults(), _stageResultDriverFailure, CaptureTerminalResults(), _skillCatalog, _playerPackage!.CaptureAudio());
        return state with
        {
            Vitals = Vitals,
            WeaponHandling = _player.CaptureWeaponHandling(),
            Ingestibles = _ingestibles.Capture(),
            ActorOverrides = _scripts.References!.CaptureActorOverrides(),
            FactionRelations = _scripts.References.CaptureFactionRelations(),
            EncounterZones = _scripts.References.CaptureEncounterZones(),
            ExplosionExposure = _player.CaptureExplosionExposure()
        };
    }

    private void OpenRecipeMenu(FalloutFormKey categoryForm)
    {
        if (_recipeMenu is not null) throw new InvalidOperationException("A recipe menu is already active.");
        if (_player.FurnitureActive || _conversation?.Active == true || _speech?.Active == true ||
            _nameEntry is not null || _raceSexEntry is not null || _vigorEntry is not null || _specialBookEntry is not null ||
            _tagSkillEntry is not null || _traitEntry is not null)
            throw new InvalidOperationException("Crafting cannot open while another player interaction owns input.");

        var category = FalloutRecipeCategory.Read(_pluginStack, categoryForm);
        var recipes = FalloutRecipe.ReadCategory(_pluginStack, categoryForm);
        var layer = new CanvasLayer { Layer = 94 };
        var menu = new NativeOwnedRecipeMenu(_pluginStack, _inventory, category, recipes, EvaluateRecipeCondition,
            value => _playerSkills.Value(value), recipe => _inventory.Craft(_pluginStack, recipe, SourcePlayerLevel, _globals),
            CloseRecipeMenu);
        _recipeLayer = layer;
        _recipeMenu = menu;
        AddChild(layer);
        layer.AddChild(menu);
        _player.SetModalInput(true);
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
        GD.Print($"OPENNV_NATIVE_RECIPE_MENU_OPEN category={category.Form} recipes={recipes.Count} source=RCCT-RCPE owner=shared-inventory parity=unverified");
    }

    internal float EvaluateRecipeCondition(FalloutCondition condition)
    {
        if (FalloutPlatformConditions.Evaluate(condition) is { } platform) return platform;
        if (condition.Function is 56 or 58 or 59 or 79 or 420 or 421 or 546) return _quests.Evaluate(condition);
        if (condition.Function == 74)
            return (_globals ?? throw new InvalidOperationException("Recipe condition has no global state owner.")).Get(condition.FormArgument1);
        if (condition.Function == 84) return _scripts.References!.GetDeadCount(condition.FormArgument1);
        if (condition.Function == 492 && condition.RunOn == 2)
            return _scripts.References!.MapMarkerVisibility(condition.Owner.Plugin.AdjustFormId(condition.Reference));
        if (FalloutInventoryConditions.Evaluate(_pluginStack, _inventory, _playerSkills.HasPerk, condition) is { } inventory) return inventory;
        if (!FalloutInventoryConditions.TargetsPlayer(_pluginStack, condition))
            throw new NotSupportedException($"Recipe condition {condition.Owner.FormKey}/{condition.Function} selects run-on actor {condition.RunOn}.");
        return condition.Function switch
        {
            14 => _playerSkills.Value(checked((int)condition.Argument1)),
            67 => FalloutCellQueries.InCell(_pluginStack, _activeCell, condition.FormArgument1) ? 1 : 0,
            69 => condition.FormArgument1 == _scripts.References!.ActorRace(_pluginStack.RuntimeFormKey(0x14)) ? 1 : 0,
            70 when condition.Argument1 <= 1 => _character.Female == (condition.Argument1 == 1) ? 1 : 0,
            72 => condition.FormArgument1 == _raceSexContract.Player ? 1 : 0,
            _ => throw new NotSupportedException($"Recipe condition {condition.Owner.FormKey}/{condition.Function} is unbound."),
        };
    }

    internal float EvaluateMessageCondition(FalloutCondition condition) => condition.Function == 53 && condition.RunOn == 0
        ? (float)_scripts.References!.ReadVariable(_quests, condition.FormArgument1, condition.Argument2)
        : EvaluateRecipeCondition(condition);

    private void CloseRecipeMenu()
    {
        if (_recipeMenu is null) return;
        _recipeLayer?.QueueFree();
        _recipeLayer = null;
        _recipeMenu = null;
        _player.SetModalInput(false);
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private void OpenBarterMenu(FalloutFormKey actorReference, int discount)
    {
        if (_barterMenu is not null) throw new InvalidOperationException("A barter menu is already active.");
        if (_moviePlaying || _player.FurnitureActive || _recipeMenu is not null || _nameEntry is not null ||
            _raceSexEntry is not null || _vigorEntry is not null || _specialBookEntry is not null || _tagSkillEntry is not null || _traitEntry is not null)
            throw new InvalidOperationException("Barter cannot open while another player interaction owns input.");

        var actor = _pluginStack.GetEffective(actorReference);
        if (actor.Signature != "ACHR")
            throw new InvalidDataException($"ShowBarterMenu target {actorReference} is not an actor reference.");
        var merchantLinks = actor.ReadSubrecords().Where(field => field.Signature == "XMRC").ToArray();
        if (merchantLinks.Length != 1 || merchantLinks[0].Data.Length != sizeof(uint))
            throw new NotSupportedException($"Actor {actorReference} has no supported merchant-container reference.");
        var merchantContainer = actor.Plugin.AdjustOptionalFormId(BinaryPrimitives.ReadUInt32LittleEndian(merchantLinks[0].Data.Span))
            ?? throw new InvalidDataException($"Actor {actorReference} has an empty merchant-container reference.");
        var merchantReference = _pluginStack.GetEffective(merchantContainer);
        if (merchantReference.Signature != "REFR" ||
            _pluginStack.GetEffective(FalloutDialogueTopic.RequiredForm(merchantReference, "NAME")).Signature != "CONT")
            throw new InvalidDataException($"Actor {actorReference} XMRC does not resolve to a container reference.");

        var baseActor = _pluginStack.GetEffective(FalloutDialogueTopic.RequiredForm(actor, "NAME"));
        if (baseActor.Signature != "NPC_")
            throw new InvalidDataException($"Merchant actor {baseActor.FormKey} does not use the source NPC_ base type.");
        var fullName = baseActor.ReadSubrecords().Where(field => field.Signature == "FULL").ToArray();
        var editorId = baseActor.ReadSubrecords().Where(field => field.Signature == "EDID").ToArray();
        if (fullName.Length > 1 || editorId.Length != 1)
            throw new InvalidDataException($"Merchant actor {baseActor.FormKey} has ambiguous display identity.");
        var merchantName = fullName.Length == 1 ? FalloutDialogueTopic.Text(fullName[0].Data.Span) :
            FalloutDialogueTopic.Text(editorId[0].Data.Span);
        var caps = FalloutDialogueTopic.Find(_pluginStack, "MISC", "Caps001").FormKey;
        var merchantInventory = (_scripts.References ?? throw new InvalidOperationException("Merchant reference inventory owner is absent."))
            .Inventory(merchantContainer, SourcePlayerLevel, _globals).Contents;
        var pricing = new FalloutBarterPricing(_pluginStack);
        var layer = new CanvasLayer { Layer = 96 };
        var menu = new NativeOwnedBarterMenu(_pluginStack, _inventory, merchantInventory, caps, merchantName, discount,
            pricing, () => _playerSkills.Value("Barter"), transfers => _inventory.Exchange(merchantInventory, transfers),
            CloseBarterMenu);
        _barterLayer = layer; _barterMenu = menu;
        AddChild(layer); layer.AddChild(menu);
        _player.SetModalInput(true);
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
        GD.Print($"OPENNV_NATIVE_BARTER_MENU_OPEN actor={actorReference} merchant={merchantContainer} discount={discount} " +
            $"items={_inventory.Items.Count}/{merchantInventory.Items.Count} source=ACHR-XMRC-GMST owner=shared-inventory " +
            "price-modifiers-restock-matched-ui-physical-xr-acceptance=unverified");
    }

    private void CloseBarterMenu()
    {
        if (_barterMenu is null) return;
        _barterLayer?.QueueFree(); _barterLayer = null; _barterMenu = null;
        var conversationActive = _conversation?.Active == true;
        _player.SetModalInput(conversationActive);
        if (DisplayServer.GetName() != "headless")
            Input.MouseMode = conversationActive ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }
}
