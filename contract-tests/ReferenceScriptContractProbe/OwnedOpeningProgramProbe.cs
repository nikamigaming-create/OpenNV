using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;
using OpenNV.Runtime.World.Cells;

// Disposable command/record preflight. Movie, voice and menu completion are
// explicit fixture callbacks. This does not supply ordinary gameplay evidence.
internal static class OwnedOpeningProgramProbe
{
    internal static void Run(string root)
    {
        RuntimeLiveContentSource.Configure(root, RuntimeLiveContentSource.FalloutNewVegasGame);
        using var content = RuntimeLiveContentSource.Current!;
        using var records = FalloutPluginStack.Load(content.PluginSources);
        var cell = FalloutCellSceneReader.Read(records, FalloutDialogueTopic.Find(records, "CELL", "GSDocMitchellHouse").FormKey);
        using var world = new FalloutReferenceWorld(records); world.LoadCell(cell);
        var quests = new FalloutQuestState(records);
        var globals = FalloutGlobalState.Read(records);
        var inventory = new FalloutPlayerInventory();
        var intro = FalloutDialogueTopic.Find(records, "QUST", "VCG00").FormKey;
        var opening = FalloutDialogueTopic.Find(records, "QUST", "VCG01").FormKey;
        var claims = new HashSet<FalloutFormKey> { intro, opening };
        var scripts = new FalloutQuestScripts(records, quests, claims, inventory, globals, references: world);
        var sky = new FalloutSkyLightingState(records, FalloutGameSettingFloats.Read(records, "fDaytimeColorExtension"));
        sky.EnterCell(cell.Cell);
        var imageSpace = new FalloutImageSpaceState();
        var settings = FalloutInstallationSettings.Read(content);
        var race = FalloutNativeRaceSexResolver.Resolve(records);
        var character = race.Initial;
        var controls = FalloutPlayerControlState.AllEnabled;
        var spoken = new Queue<(FalloutDialogueInfo Info, FalloutFormKey Speaker)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var messages = new List<string>();
        var movies = 0; var namesEntered = 0; var raceMenus = 0; var idles = 0;
        var moviePending = false; var racePending = false; var saveRequested = false;
        FalloutQuestStages? stages = null;
        FalloutReferenceScripts? executor = null;
        FalloutQuestScriptHost? questHost = null;
        float Evaluate(FalloutCondition condition)
        {
            if (FalloutPlatformConditions.Evaluate(condition) is { } platform) return platform;
            if (condition.RunOn == 1 && condition.Function == 70)
                return (condition.Argument1 == 1) == character.Female ? 1 : 0;
            return quests.Evaluate(condition);
        }
        void Apply(FalloutReferenceScriptEffect effect)
        {
            switch (effect.Kind)
            {
                case FalloutReferenceEffectKind.SetStage: stages!.Enter(effect.Target!.Value, effect.Stage); break;
                case FalloutReferenceEffectKind.PlayerControls:
                    controls = new FalloutPlayerControlCommand(effect.Enable, effect.Controls!).Apply(controls); break;
                case FalloutReferenceEffectKind.RemoveItem:
                    if (records.RuntimeFormId(effect.Target!.Value) != 0x14) throw new NotSupportedException("Opening fixture non-player removal.");
                    inventory.Remove(effect.Argument!.Value, effect.Value, effect.Enable); break;
                case FalloutReferenceEffectKind.ImageSpace:
                    if (effect.Enable) imageSpace.Apply(FalloutImageSpaceModifierReader.Read(records.GetEffective(effect.Target!.Value)));
                    else imageSpace.Remove(effect.Target!.Value);
                    break;
                case FalloutReferenceEffectKind.ReferenceEnable: break; // Applied by the shared world owner.
                case FalloutReferenceEffectKind.EvaluatePackages:
                    if (!cell.References.Any(reference => reference.FormKey == effect.Target))
                        throw new InvalidDataException("Opening package evaluation lost its resident actor.");
                    break;
                case FalloutReferenceEffectKind.HeadTracking:
                    _ = FalloutHeadTrackingPrograms.RequiresProcess(records.GetEffective(effect.Target!.Value), cell.Cell.FormKey,
                        cell.References.Any(reference => reference.FormKey == effect.Target)); break;
                case FalloutReferenceEffectKind.ScriptPackage:
                    var package = FalloutScriptPackage.Read(records.GetEffective(effect.Argument!.Value));
                    if (package.Procedure != 6 || package.LocationType != 3) throw new NotSupportedException("Opening fixture player package procedure.");
                    break;
                case FalloutReferenceEffectKind.SayTo:
                    var speaker = records.GetEffective(effect.Target!.Value);
                    var npc = FalloutDialogueTopic.RequiredForm(speaker, "NAME");
                    var info = FalloutDialogueTopic.Read(records, effect.Topic!.Value).Select(npc, scripts.SaidInfos,
                        quest => quests.Stage(quest), Evaluate) ?? throw new InvalidDataException("Opening source has no eligible voice INFO.");
                    if ((info.Flags & 4) != 0) scripts.SaidInfos.Add(info.Record.FormKey);
                    executor!.ExecuteResult(info, speaker.FormKey, true);
                    spoken.Enqueue((info, speaker.FormKey));
                    break;
                case FalloutReferenceEffectKind.Message:
                    messages.Add(FalloutDialogueTopic.Text(records.GetEffective(effect.Target!.Value)
                        .ReadSubrecords().Single(field => field.Signature == "EDID").Data.Span));
                    var owner = records.GetEffective(effect.Source);
                    scripts.ShowMessage(effect.Target!.Value, FalloutScriptLocals.AttachedScript(records, owner)?.FormKey,
                        owner.Signature == "QUST" ? null : owner.FormKey); break;
                case FalloutReferenceEffectKind.AutoDisplayObjectives: scripts.Session.AutoDisplayObjectives = effect.Enable; break;
                default: throw new NotSupportedException($"Opening fixture effect {effect.Kind} is unbound.");
            }
        }
        void Native(FalloutFormKey source, FalloutScriptBindings bindings, string command, IReadOnlyList<string> arguments)
        {
            var split = command.Split('.');
            var target = split.Length == 1 ? source : bindings.Reference(split[0]);
            var operation = split[^1].ToLowerInvariant(); names.Add(operation);
            switch (operation)
            {
                case "playbink":
                    var movie = FalloutMovieCommand.FromScript(command + " " + string.Join(' ', arguments)).Single();
                    if (!content.TryResolve("video/" + movie.FileName, null, out _)) throw new FileNotFoundException("Opening movie.");
                    movies++; moviePending = true; break;
                case "forceweather": sky.ForceWeather(bindings.Form(arguments.Single()).FormKey); break;
                case "releaseweatheroverride": sky.ReleaseWeatherOverride(); break;
                case "playmusic":
                    var music = bindings.Form(arguments.Single());
                    if (music.Signature != "MUSC" || music.ReadSubrecords().Any(field => field.Signature != "EDID"))
                        throw new NotSupportedException("Opening music requires streaming.");
                    break;
                case "moveto":
                    if (records.RuntimeFormId(target) != 0x14 || !cell.References.Any(reference => reference.FormKey == bindings.Reference(arguments.Single())))
                        throw new InvalidDataException("Opening MoveTo is not a resident player destination.");
                    break;
                case "setscale": if (arguments.Single() != "1") throw new NotSupportedException("Opening fixture scale."); break;
                case "clearscreensplatter" or "removescriptpackage": break;
                case "unequipitem": world.UnequipItem(target, bindings.Form(arguments.Single()).FormKey, 1, globals); break;
                case "playidle":
                    // Names in this opcode are strings; they do not require SCRO.
                    var idle = FalloutActorIdleSource.Resolve(records, FalloutDialogueTopic.Find(records, "IDLE", arguments.Single().Trim('"')));
                    if (!content.TryRead(idle.AnimationPath, null, out _, out _)) throw new FileNotFoundException("Opening idle.");
                    idles++; break;
                case "resetai":
                    if (!cell.References.Any(reference => reference.FormKey == target)) throw new InvalidDataException("Opening ResetAI lost its actor.");
                    break;
                case "getplayername": namesEntered++; break;
                case "showracemenu": raceMenus++; racePending = true; break;
                case "sexchange":
                    var female = arguments[0].Equals("female", StringComparison.OrdinalIgnoreCase);
                    if (female != character.Female)
                    {
                        var creation = new FalloutNativeCharacterCreation(records, race, character, settings);
                        creation.ChangeIdentity(character.RaceRuntimeFormId, female); character = creation.Selection;
                    }
                    break;
                case "autosave": saveRequested = true; break;
                default: throw new NotSupportedException($"Opening native command {command} has no preflight binding.");
            }
        }
        executor = new(records, world, quests, new((_, _) => false, Apply, scripts.MessageResults.Take,
            ActorValue: (_, _) => 5, Globals: globals, Command: Native));
        stages = new(records, quests, executor.StageSteps, Evaluate, () => !moviePending);
        questHost = new((quest, stage) => () => stages.Enter(quest, stage), _ => 5, executor.ExecuteProgram);
        scripts.Host = questHost;
        stages.Enter(intro, 0);
        if (!moviePending || quests.Stage(intro) != 0 || quests.StageDone(opening, 0))
            throw new InvalidDataException($"Opening source passed its pending movie: pending={moviePending} intro={quests.Stage(intro)} openingEntered={quests.StageDone(opening, 0)}.");
        // Explicit lab playback completion, never used by the native product.
        moviePending = false; stages.Continue();
        for (var frame = 0; frame < 6000 && quests.Stage(opening) < 55; frame++)
        {
            while (spoken.TryDequeue(out var voice)) executor.ExecuteResult(voice.Info, voice.Speaker, false);
            while (scripts.TryTakeMessage(out var message)) scripts.MessageResults.Select(message!.Request!, 0);
            if (racePending) { racePending = false; scripts.ExecuteClaimedMenu(opening, 1036, questHost); }
            scripts.AdvanceClaimed(quests.StageDone(opening, 0) ? opening : intro, 1d / 60, questHost);
            world.AdvanceEnableChanges(1d / 60, FalloutReferenceFadeSettings.Read(settings), _ => false);
        }
        if (quests.Stage(opening) != 55 || !saveRequested || movies != 1 || namesEntered != 1 || raceMenus != 1 || idles == 0 ||
            !controls.Movement || controls.PipBoy || !scripts.Session.AutoDisplayObjectives || sky.ForcedWeather is not null)
            throw new InvalidDataException($"Opening preflight did not reach its source autosave: stage={quests.Stage(opening)} commands={string.Join(',', names)}");
        if (!messages.Contains("CGTutorialMovement") || !messages.Contains("CGTutorialMovementRun") || messages.Contains("CGTutorialMovementXbox"))
            throw new InvalidDataException("Opening source platform conditions selected the wrong movement tutorial.");
        scripts.Capture().Validate();
        Console.WriteLine($"OPENNV_OWNED_OPENING_PROGRAM_PASS stage=55 nativeCommandKinds={names.Count} sourceResults=true sourceTimers=true movieSuspension=true name=true raceMenu=true autosaveRequest=true ordinaryPresentation=unverified");
    }
}
