using System.Buffers.Binary;
using Godot;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.Presentation.Ui;
using OpenNV.Runtime.World.Actors;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutRadioHudDeclaration? _radioHudDeclaration;
    private FalloutCaravanMinimumNotice? _caravanMinimumNotice;
    internal FalloutFormKey? SourceMusic => _scriptMusic?.SourceForm;
    private bool _saveRequested;
    private CanvasLayer? _recipeLayer;
    private NativeOwnedRecipeMenu? _recipeMenu;
    private CanvasLayer? _barterLayer;
    private NativeOwnedBarterMenu? _barterMenu;
    private Action? _releaseRecipeInput;
    private Action? _releaseBarterInput;
    private readonly HashSet<CanvasItem> _screenSplatters = [];
    private string? SaveContinuationBlocker =>
        _player.VatsSaveBlocker is { } vats ? vats :
        PlayerAdvancementSaveBlocker is { } advancement ? advancement :
        _moviePlaying ? "movie" : _player.FurnitureActive ? "furniture" :
        _conversation?.Active == true ? "conversation" : _speech is { CanCaptureState: false } ? "speech" :
        _nameEntry is not null ? "name-menu" : _raceSexEntry is not null ? "race-menu" :
        _specialBookEntry is not null ? "special-book-menu" :
        _vigorEntry is not null ? "special-menu" : _tagSkillEntry is not null ? "tag-menu" :
        _traitEntry is not null ? "trait-menu" : _recipeMenu is not null ? "recipe-menu" :
        _barterMenu is not null ? "barter-menu" : _caravanMenu is not null ? "caravan-menu" :
        TerminalSaveBlocker is { } terminal ? terminal :
        _scripts.References?.PendingProcedureCaptureCount > 0 ? "actor-procedure-initialization" :
        _scripts.References?.PendingCorpseEquipmentCaptureCount > 0 ? "actor-corpse-continuation" :
        _scripts.References?.TransientExplosions.SaveBlocker ?? StageResultsSaveBlocker;
    internal object SaveRequestState => new
    {
        requested = _saveRequested,
        transientExplosions = _scripts.References?.TransientExplosions.State,
        sourceManual = _scripts.ScriptManualSaves.Receipt,
        sourceManualDeferredBy = _scripts.ScriptManualSaves.DeferredBy,
        deferredBy = !_saveRequested ? null : _scripts.References!.PlayerMoves.Pending ? "player-move" : SaveContinuationBlocker ?? SourceAnimationSoundSaveBlocker,
        activeContinuationSaving = "source-radio-pcm;other-continuations-unbound"
    };

    internal void ApplyNativeSourceCommand(FalloutFormKey source, FalloutScriptBindings bindings, string command, IReadOnlyList<string> arguments)
    {
        var parts = command.Split('.');
        var operation = parts[^1].ToLowerInvariant();
        var target = parts.Length == 2 ? bindings.Reference(parts[0]) : source;
        switch (operation)
        {
            case "addtopic" when parts.Length == 1 && arguments.Count == 1:
                var topic = bindings.Form(arguments[0]);
                if (topic.Signature != "DIAL") throw new InvalidDataException("AddTopic argument is not a DIAL topic.");
                _scripts.AddedTopics.Add(topic.FormKey);
                break;
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
                var idleActor = SceneGraph.NodeTraversal.SelfAndDescendants<Node3D>(GetTree().Root)
                    .Where(node => node.IsInsideTree() && !node.IsQueuedForDeletion() && (node switch
                    {
                        RuntimeNativeNpc npc => npc.Appearance.Reference == target,
                        RuntimeNativeCreature creature => creature.Appearance.Reference == target,
                        _ => false
                    })).Single();
                if (idleActor is RuntimeNativeNpc idleNpc) idleNpc.PlayIdle(_pluginStack, arguments[0].Trim('"'));
                else ((RuntimeNativeCreature)idleActor).PlayIdle(_pluginStack, arguments[0].Trim('"'));
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
            case "showcaravanmenu" when parts.Length is 1 or 2 && arguments.Count == 3:
                if (!double.TryParse(arguments[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var aiType) ||
                    !double.TryParse(arguments[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var betPercentage))
                    throw new InvalidDataException("ShowCaravanMenu numeric arguments were not resolved.");
                var request = FalloutCaravanCommandRequest.Read(_pluginStack, target, bindings.Form(arguments[0]), aiType, betPercentage);
                if (_pluginStack.RuntimeFormId(target) == 0x14) break;
                if (_pluginStack.GetEffective(target).Signature is not ("ACHR" or "ACRE"))
                    throw new NotSupportedException("Caravan opponent reference needs its source actor resolution owner.");
                var cards = (_scripts.References ?? throw new InvalidOperationException("Caravan collection has no world owner.")).PlayerCaravanCards;
                if (!cards.MeetsMatchMinimum())
                {
                    var noticeSource = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Caravan notice source is absent.");
                    _caravanMinimumNotice ??= FalloutExecutableStringTable.ReadCaravanMinimumNotice(noticeSource.FalloutExecutablePath);
                    _ = FalloutGameSettingStrings.Read(_pluginStack, _caravanMinimumNotice.Setting);
                    if (!noticeSource.TryRead("textures/" + _caravanMinimumNotice.Icon, null, out var icon, out _) || icon.Length == 0)
                        throw new FileNotFoundException("Caravan notice source icon is missing.", _caravanMinimumNotice.Icon);
                    var sound = FalloutSoundRecordReader.Find(_pluginStack, _caravanMinimumNotice.SoundEditorId);
                    var notice = new FalloutHudEvent(FalloutHudEventKind.ExecutableNotice, target, 0, Text: _caravanMinimumNotice.Setting);
                    _inventory.Notifications.RequirePublish([notice]);
                    _inventory.Notifications.Publish([notice]);
                    // Retail schedules the notice before requesting the UI sound.
                    // A sound fault remains visible without replaying that prefix.
                    _scripts.Sounds.Play(_pluginStack.RuntimeFormKey(0x14), sound.FormKey, systemSound: true);
                    break;
                }
                OpenCaravanMenu(request);
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
            case "enablefasttravel" or "enablefast" when parts.Length == 1:
                FastTravel.Apply(arguments);
                break;
            case "playmusic" when parts.Length == 1 && arguments.Count == 1:
                MusicPlayer.Play(bindings.Form(arguments[0]).FormKey);
                break;
            case "forceweather" or "fw" when parts.Length == 1:
                var overrideWeather = FalloutSkyLightingState.ReadForceWeatherOverride(arguments);
                (_skyLighting ?? throw new InvalidOperationException("Sky state owner is absent."))
                    .ForceWeather(bindings.Form(arguments[0]).FormKey, overrideWeather);
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

    private FalloutNativeCampaignState CaptureCurrentState(FalloutFormKey activeCell, bool validate = true) =>
        CaptureCurrentStateCore(activeCell, validate, null);

    private FalloutNativeCampaignState CaptureCurrentStateCore(FalloutFormKey activeCell, bool validate,
        IReadOnlyList<FalloutReferenceSnapshot>? references)
    {
        if (Vitals.HitPoints <= 0)
            throw new InvalidOperationException("Cannot replace a playable save after player death.");
        _scripts.ScriptManualSaves.RequireCapture();
        _scripts.References!.PlayerMoves.RequireSettled();
        if (SaveContinuationBlocker is { } blocker)
            throw new NotSupportedException($"Saving {blocker} requires continuation state.");
        _playerAbilities.Synchronize();
        var vitals = Vitals;
        var transform = _player.GlobalTransform;
        var sourceCell = FalloutCellSceneReader.ReadDefinition(_pluginStack, activeCell);
        if (sourceCell.Worldspace is { } world)
        {
            _saveExteriorGrid ??= new(_pluginStack);
            activeCell = _saveExteriorGrid.SpatialCell(world,
                transform.Origin.X / _player.UnitsToMeters, -transform.Origin.Z / _player.UnitsToMeters);
        }
        var rotation = transform.Basis.Orthonormalized().GetRotationQuaternion().Normalized();
        var complete = _tagSkillContract is null ? !_scripts.Session.InCharGen :
            _quests.IsCompleted(FalloutDialogueTopic.Find(_pluginStack, "QUST", FalloutNativeCampaignSave.OpeningQuestEditorId).FormKey);
        var state = FalloutNativeCampaignSave.Capture(_saveCompatibilityId, activeCell, _inventory.Capture(), _playerName, _character,
            _vigorContract, Special, _tagSkillContract, _tagSkills.Selection, _traitFarewellContract, _traits, PlayerControls,
            [transform.Origin.X, transform.Origin.Y, transform.Origin.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W],
            _quests.Capture(), _captureScripts(), _globals?.Capture(), _gameTime?.Capture(), _skyLighting?.Capture(), references ?? _scripts.References?.Capture(),
            QuestEditorId, Stage, complete, _player.ViewPitchRadians, _playerActorValues.Capture(), _tagSkills.Capture(),
            _scripts.References!.CaptureDetection(), _speech?.CaptureState(), CaptureFinishedSpeechStage(),
            CaptureStageResults(), _stageResultDriverFailure, CaptureTerminalResults(), _skillCatalog, _playerPackage!.CaptureAudio(),
            playerSkillValues: _playerSkills.CaptureValues(), playerAbilityScripts: _playerAbilities.Capture(),
            transientExplosions: _scripts.References.TransientExplosions.Capture(), fastTravel: FastTravel.State, validate: validate,
            playerSneaking: _player.Activity.Sneaking);
        return state with
        {
            Vitals = vitals,
            WeaponHandling = _player.CaptureWeaponHandling(),
            Ingestibles = _ingestibles.Capture(),
            ActorOverrides = _scripts.References!.CaptureActorOverrides(),
            FactionRelations = _scripts.References.CaptureFactionRelations(),
            Reputations = _scripts.References.CaptureReputations(),
            PlayerNotes = _scripts.References.PlayerNotes.Capture(),
            PlayerCaravanCards = _scripts.References.PlayerCaravanCards.Capture(),
            DroppedItems = _scripts.References.CaptureDroppedItems(),
            PlayerProjectileLaunch = _player.CaptureProjectileLaunch(),
            PlayerAdvancement = _advancement?.CaptureSettled(),
            EncounterZones = _scripts.References.CaptureEncounterZones(),
            ExplosionExposure = _player.CaptureExplosionExposure(),
            Music = _scriptMusic?.Capture()
        };
    }

    private FalloutExteriorGrid? _saveExteriorGrid;

    private void OpenRecipeMenu(FalloutFormKey categoryForm)
    {
        if (_recipeMenu is not null) throw new InvalidOperationException("A recipe menu is already active.");
        if (_barterMenu is not null || _levelUpMenu is not null || _player.FurnitureActive || _conversation?.Active == true || _speech?.Active == true ||
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
        try
        {
            _releaseRecipeInput = AcquireInteractionInput();
            layer.TreeExiting += CloseRecipeMenu;
            menu.TreeExiting += CloseRecipeMenu;
            AddChild(layer);
            layer.AddChild(menu);
        }
        catch
        {
            CloseRecipeMenu();
            if (menu.GetParent() is null) menu.Free();
            if (!layer.IsInsideTree()) layer.Free();
            throw;
        }
        GD.Print($"OPENNV_NATIVE_RECIPE_MENU_OPEN category={category.Form} recipes={recipes.Count} source=RCCT-RCPE owner=shared-inventory parity=unverified");
    }

    internal float EvaluateRecipeCondition(FalloutCondition condition) =>
        SharedConditions.Evaluate(condition, new(_pluginStack.RuntimeFormKey(0x14), _pluginStack.RuntimeFormKey(0x14),
            LinkedReference: _scripts.References!.GetLinkedRef));

    internal float EvaluateMessageCondition(FalloutCondition condition) => EvaluateRecipeCondition(condition);

    internal float EvaluateTargetCondition(FalloutFormKey targetReference, FalloutCondition condition) =>
        SharedConditions.Evaluate(condition, new(targetReference, _pluginStack.RuntimeFormKey(0x14),
            _scripts.References!.GetLinkedRef));
    private void CloseRecipeMenu()
    {
        if (_recipeMenu is null) return;
        var layer = _recipeLayer;
        _recipeLayer = null;
        _recipeMenu = null;
        var release = _releaseRecipeInput;
        _releaseRecipeInput = null;
        try { release?.Invoke(); }
        finally { if (layer is not null && !layer.IsQueuedForDeletion()) layer.QueueFree(); }
    }

    private void OpenBarterMenu(FalloutFormKey actorReference, int discount)
    {
        if (_barterMenu is not null) throw new InvalidOperationException("A barter menu is already active.");
        if (_moviePlaying || _levelUpMenu is not null || _player.FurnitureActive || _recipeMenu is not null || _nameEntry is not null ||
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
        try
        {
            _releaseBarterInput = AcquireInteractionInput();
            layer.TreeExiting += CloseBarterMenu;
            menu.TreeExiting += CloseBarterMenu;
            AddChild(layer);
            layer.AddChild(menu);
        }
        catch
        {
            CloseBarterMenu();
            if (menu.GetParent() is null) menu.Free();
            if (!layer.IsInsideTree()) layer.Free();
            throw;
        }
        GD.Print($"OPENNV_NATIVE_BARTER_MENU_OPEN actor={actorReference} merchant={merchantContainer} discount={discount} " +
            $"items={_inventory.Items.Count}/{merchantInventory.Items.Count} source=ACHR-XMRC-GMST owner=shared-inventory " +
            "price-modifiers-restock-matched-ui-physical-xr-acceptance=unverified");
    }

    private void CloseBarterMenu()
    {
        if (_barterMenu is null) return;
        var layer = _barterLayer;
        _barterLayer = null;
        _barterMenu = null;
        var release = _releaseBarterInput;
        _releaseBarterInput = null;
        try { release?.Invoke(); }
        finally { if (layer is not null && !layer.IsQueuedForDeletion()) layer.QueueFree(); }
    }

    private Action AcquireInteractionInput()
    {
        var previousMouse = Input.MouseMode;
        var release = _player.AcquireModalInput();
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
        var released = false;
        return () =>
        {
            if (released) return;
            released = true;
            try { release(); }
            finally
            {
                if (DisplayServer.GetName() != "headless") Input.MouseMode = previousMouse;
            }
        };
    }
}
