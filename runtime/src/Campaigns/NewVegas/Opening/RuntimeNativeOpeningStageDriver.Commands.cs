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
    internal FalloutFormKey? SourceMusic => _music;
    private bool _saveRequested;
    private readonly HashSet<CanvasItem> _screenSplatters = [];

    private void ApplyNativeSourceCommand(FalloutFormKey source, FalloutScriptBindings bindings, string command, IReadOnlyList<string> arguments)
    {
        var parts = command.Split('.');
        var operation = parts[^1].ToLowerInvariant();
        var target = parts.Length == 2 ? bindings.Reference(parts[0]) : source;
        RuntimeNativeNpc Actor() => GetTree().Root.FindChildren("*", "", true, false).OfType<RuntimeNativeNpc>()
            .Single(actor => actor.Appearance.Reference == target);
        switch (operation)
        {
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
                break;
            case "clearscreensplatter" when parts.Length == 1 && arguments.Count == 0:
                foreach (var splatter in _screenSplatters)
                    if (GodotObject.IsInstanceValid(splatter)) splatter.QueueFree();
                _screenSplatters.Clear();
                break;
            case "unequipitem" when arguments.Count == 1:
                var removedEquipment = bindings.Form(arguments[0]).FormKey;
                if (_pluginStack.RuntimeFormId(target) == 0x14) _inventory.Unequip(_pluginStack, removedEquipment);
                else
                {
                    if (_scripts.References!.IsResident(target))
                        throw new NotSupportedException("Resident NPC equipment changes require the actor mesh/animation refresh owner.");
                    _scripts.References.UnequipItem(target, removedEquipment, SourcePlayerLevel, _globals);
                }
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
                Actor().EvaluatePackages(true);
                break;
            case "getplayername" when parts.Length == 1 && arguments.Count == 0:
                SynchronizeNameEntry();
                if (_nameEntry is null) throw new NotSupportedException("Name input has no active menu owner.");
                break;
            case "showracemenu" when parts.Length == 1 && arguments.Count == 0:
                SynchronizeRaceSexEntry();
                if (_raceSexEntry is null) throw new NotSupportedException("Race input has no active menu owner.");
                break;
            case "settagskills" when parts.Length == 1 && arguments.Count == 2:
                if (arguments[0] != _tagSkillContract.RequiredCount.ToString(System.Globalization.CultureInfo.InvariantCulture) || arguments[1] != "1")
                    throw new NotSupportedException("Tag menu parameters have no owned creation contract.");
                SynchronizeTagSkillEntry();
                if (_tagSkillEntry is null) throw new NotSupportedException("Tag input has no active menu owner.");
                break;
            case "showtraitmenu" when parts.Length == 1 && arguments.Count == 0:
                SynchronizeTraitEntry();
                if (_traitEntry is null) throw new NotSupportedException("Trait input has no active menu owner.");
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
            case "moveto" when arguments.Count == 1 && _pluginStack.RuntimeFormId(target) == 0x14:
                var marker = bindings.Reference(arguments[0]);
                var reference = FalloutCellSceneReader.Read(_pluginStack, _activeCell).References.SingleOrDefault(reference => reference.FormKey == marker)
                    ?? throw new NotSupportedException("Cross-cell scripted MoveTo needs a world transition owner.");
                _player.Teleport(new(GamebryoCoordinate.ConvertReferenceEuler(new(reference.RotationRadians[0], reference.RotationRadians[1], reference.RotationRadians[2]), reference.Scale),
                    GamebryoCoordinate.ConvertVector(new(reference.Position[0], reference.Position[1], reference.Position[2])) * _player.UnitsToMeters));
                break;
            case "setscale" when arguments.Count == 1 && _pluginStack.RuntimeFormId(target) == 0x14:
                if (!float.TryParse(arguments[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale) || !float.IsFinite(scale) || scale <= 0)
                    throw new InvalidDataException("Player source scale is invalid.");
                _player.Scale = Vector3.One * scale;
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
        if (_moviePlaying || _player.FurnitureActive || _conversation?.Active == true || _speech?.Active == true ||
            _nameEntry is not null || _raceSexEntry is not null || _vigorEntry is not null || _tagSkillEntry is not null || _traitEntry is not null)
            throw new NotSupportedException("Saving an active movie, furniture, speech or menu requires continuation state.");
        var transform = _player.GlobalTransform;
        var rotation = transform.Basis.GetRotationQuaternion().Normalized();
        var complete = _quests.IsCompleted(FalloutDialogueTopic.Find(_pluginStack, "QUST", FalloutNativeCampaignSave.OpeningQuestEditorId).FormKey);
        var state = FalloutNativeCampaignSave.Capture(_saveCompatibilityId, activeCell, _inventory.Capture(), _playerName, _character,
            _vigorContract, _special, _tagSkillContract, _tagSkills, _traitFarewellContract, _traits, _machine.ControlState,
            [transform.Origin.X, transform.Origin.Y, transform.Origin.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W],
            _quests.Capture(), _captureScripts(), _globals?.Capture(), _gameTime?.Capture(), _skyLighting?.Capture(), _scripts.References?.Capture(),
            QuestEditorId, Stage, complete, _player.ViewPitchRadians);
        return state with { Vitals = Vitals, WeaponHandling = _player.CaptureWeaponHandling() };
    }
}
