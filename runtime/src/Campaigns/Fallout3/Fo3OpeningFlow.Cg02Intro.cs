using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
    private void StartCg02TransitionMovie(Fo3Cg01OwnedMovie movie)
    {
        if (_video is not null || _ownedVideoMode != Fo3OwnedVideoMode.None)
            throw new InvalidOperationException("Fallout 3 CG02 transition movie is already active.");
        _ownedVideoMode = Fo3OwnedVideoMode.Cg02Transition;
        _introLayer = new Control { Name = "Fallout3OwnedCg02Transition" };
        _introLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_introLayer);
        var black = new ColorRect { Color = Colors.Black };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _introLayer.AddChild(black);
        _video = new VideoStreamPlayer
        {
            Name = "Fallout3OwnedCg02TransitionVideo",
            Stream = new VideoStreamTheora { File = movie.RuntimeOutput },
            Expand = true,
            Loop = false,
            Visible = false,
        };
        _video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _video.Finished += () => CompleteOwnedVideo(false);
        _introLayer.AddChild(_video);
        var skip = Button("SKIP  •  ESC");
        skip.Name = "SkipFallout3OwnedCg02Transition";
        skip.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        skip.Position = new Vector2(
            Fo3OpeningFlowNumericContracts.SkipButtonOffsetXPixels,
            Fo3OpeningFlowNumericContracts.SkipButtonOffsetYPixels);
        skip.Size = new Vector2(
            Fo3OpeningFlowNumericContracts.SkipButtonWidthPixels,
            Fo3OpeningFlowNumericContracts.ButtonMinimumHeightPixels);
        skip.Pressed += () => CompleteOwnedVideo(true);
        _introLayer.AddChild(skip);
        BeginOwnedVideoSurfaceGate();
        _video.Play();
    }

    private void CompleteCg02TransitionMovie(bool skipped)
    {
        if (_ownedVideoMode != Fo3OwnedVideoMode.Cg02Transition)
            return;
        ClearOwnedVideo();
        var begin = _cg02IntroBegin ?? throw new InvalidOperationException(
            "Fallout 3 CG02 intro runtime is absent.");
        _cg02IntroBegin = null;
        begin();
        GD.Print(
            $"OPENNV_FO3_CG02_TRANSITION_MOVIE_COMPLETE " +
            $"mode={(skipped ? "skipped" : "watched")} " +
            $"blocker={_profile.Cg01PostStage14Transition.Stage20Interaction.TimerTransition.DadLead.Completion.Cg02Stage0.NextBoundaryBlocker}");
    }

    private void StartCg02IntroRuntime(
        Fo3Cg02Stage0Transition transition,
        Fo3Cg01ToddlerPlayer player,
        Action completed,
        double? restoredSeconds = null)
    {
        var intro = transition.IntroRuntime ?? throw new InvalidOperationException(
            "Fallout 3 CG02 intro contract is absent.");
        if (_cg02IntroTimerTick is not null || _cg02IntroDialogue.Count != 0)
            throw new InvalidOperationException("Fallout 3 CG02 intro is already active.");
        EnsureCg02IntroActors(intro, player);
        var remaining = restoredSeconds ?? intro.InitialSeconds;
        if (!double.IsFinite(remaining) || remaining <= 0.0 || remaining > intro.InitialSeconds)
            throw new InvalidOperationException("Fallout 3 CG02 restored timer differs.");
        player.SetMeta("opennv_cg02_timer", remaining);
        _cg02IntroTimerTick = delta =>
        {
            remaining = Math.Max(0.0, remaining - delta);
            player.SetMeta("opennv_cg02_timer", remaining);
            if (remaining > 0.0)
                return;
            _cg02IntroTimerTick = null;
            player.SetMeta("opennv_cg02_run_timer", 0);
            PlayCg02IntroSayTo(intro, player, 0, completed);
        };
    }

    private void EnsureCg02IntroActors(
        Fo3Cg02IntroRuntime intro,
        Fo3Cg01ToddlerPlayer player)
    {
        if (_cg02IntroActors.Count != 0)
            return;
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG02 intro world is absent.");
        foreach (var participant in intro.Participants
            .GroupBy(value => value.ReferenceFormId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()))
        {
            using var stream = File.OpenRead(participant.ActorScenePath);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!actual.Equals(participant.ActorSceneSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Fallout 3 CG02 actor scene hash differs: {participant.ReferenceFormId}");
            var actor = CellActorLoader.Load(
                    participant.ActorScenePath,
                    new HashSet<string>([coverage.Contract.CellFormId], StringComparer.OrdinalIgnoreCase),
                    coverage.CellRoot,
                    coverage.Contract.EntryPositionGameUnits,
                    _runtimeConfiguration,
                    proofEnableInitiallyDisabled: false,
                    materializeInitiallyDisabled: true)
                ?? throw new InvalidOperationException(
                    $"Fallout 3 CG02 actor is disabled: {participant.ReferenceFormId}");
            if (actor.ReferenceFormId != participant.ReferenceFormId ||
                actor.BaseFormId != participant.BaseFormId)
                throw new InvalidOperationException(
                    $"Fallout 3 CG02 actor identity differs: {participant.ReferenceFormId}");
            if (!actor.InitiallyDisabled)
                actor.Placement.LookAt(player.GlobalPosition, Vector3.Up);
            actor.Placement.SetMeta("opennv_looks_at_player", 1);
            _cg02IntroActors.Add(participant.ReferenceFormId, actor);
        }
        var overseer = intro.DadSpeechRuntime?.OverseerSpeechRuntime;
        if (overseer is not null && !_cg02IntroActors.ContainsKey(
                overseer.OverseerReferenceFormId))
        {
            using var stream = File.OpenRead(overseer.ActorScenePath);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!actual.Equals(overseer.ActorSceneSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Fallout 3 CG02 Overseer actor scene hash differs.");
            var actor = CellActorLoader.Load(
                    overseer.ActorScenePath,
                    new HashSet<string>([coverage.Contract.CellFormId],
                        StringComparer.OrdinalIgnoreCase),
                    coverage.CellRoot,
                    coverage.Contract.EntryPositionGameUnits,
                    _runtimeConfiguration,
                    proofEnableInitiallyDisabled: false,
                    materializeInitiallyDisabled: true)
                ?? throw new InvalidOperationException(
                    "Fallout 3 CG02 Overseer actor is disabled.");
            if (actor.ReferenceFormId != overseer.OverseerReferenceFormId ||
                actor.BaseFormId != overseer.OverseerBaseFormId)
                throw new InvalidOperationException(
                    "Fallout 3 CG02 Overseer actor identity differs.");
            actor.Placement.LookAt(player.GlobalPosition, Vector3.Up);
            actor.Placement.SetMeta("opennv_looks_at_player", 1);
            _cg02IntroActors.Add(overseer.OverseerReferenceFormId, actor);
        }
    }

    private void PlayCg02IntroSayTo(
        Fo3Cg02IntroRuntime intro,
        Fo3Cg01ToddlerPlayer player,
        int phase,
        Action completed)
    {
        foreach (var sound in intro.Sounds.Where(value => value.Phase == phase))
        {
            var stream = AudioStreamWav.LoadFromFile(sound.SourcePath)
                ?? throw new InvalidOperationException(
                    $"Fallout 3 CG02 sound could not be decoded: {sound.FormId}");
            var source = new AudioStreamPlayer
            {
                Name = $"Fallout3Cg02IntroSound{sound.Sequence}",
                Stream = stream,
            };
            source.SetMeta("opennv_source_form_id", sound.FormId);
            source.Finished += source.QueueFree;
            AddChild(source);
            _cg02IntroSounds.Add(source);
            source.Play();
        }
        foreach (var participant in intro.Participants
            .Where(value => value.Phase == phase &&
                (value.EngineSex is null || value.EngineSex ==
                    (_selectedSex ?? throw new InvalidOperationException(
                        "Fallout 3 CG02 player sex is absent.")).EngineSex))
            .OrderBy(value => value.SequenceInPhase))
        {
            var actor = _cg02IntroActors[participant.ReferenceFormId];
            if (participant.SpeakerIdleLogicalPath is not null)
            {
                var animation = actor.Actor.LoadedAnimations.Single(value =>
                    ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                        ActorModelSlice.NormalizeAnimationPath(
                            participant.SpeakerIdleLogicalPath),
                        StringComparison.OrdinalIgnoreCase));
                _cg02IntroAnimations[participant.ReferenceFormId] =
                    ActorAnimationPlayback.Start(actor.Actor, animation);
            }
            var voice = new AudioStreamPlayer
            {
                Name = $"Fallout3Cg02IntroVoice{participant.Sequence}",
            };
            AddChild(voice);
            var dialogue = new GamebryoDialoguePlayback(
                voice,
                _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
            _cg02IntroDialogue.Add(dialogue);
            dialogue.Start(
                new SourceDialogueLine(
                    participant.InfoFormId,
                    participant.Response.Index,
                    participant.ReferenceFormId,
                    participant.Response.Text,
                    new SourceDialogueAsset(
                        participant.Response.Voice.LogicalPath,
                        participant.Response.Voice.SourcePath,
                        participant.Response.Voice.Sha256),
                    new SourceDialogueAsset(
                        participant.Response.Lip.LogicalPath,
                        participant.Response.Lip.SourcePath,
                        participant.Response.Lip.Sha256)),
                new FaceGenMorphController(
                    actor.Actor,
                    _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip),
                () =>
                {
                    if (participant.QuestVariableEffects.Count == 0)
                        return;
                    foreach (var (variable, value) in participant.QuestVariableEffects)
                        player.SetMeta(
                            variable.Equals("runTimer", StringComparison.OrdinalIgnoreCase)
                                ? "opennv_cg02_run_timer"
                                : $"opennv_cg02_{variable.ToLowerInvariant()}",
                            value);
                    if (participant.ResultEffectCount > participant.QuestVariableEffects.Count)
                    {
                        actor.Placement.SetMeta("opennv_variable01", 1);
                        actor.Placement.SetMeta("opennv_evaluate_package", 1);
                    }
                    if (phase < intro.Participants.Max(value => value.Phase))
                    {
                        Callable.From(() => PlayCg02IntroSayTo(
                            intro, player, phase + 1, completed)).CallDeferred();
                        return;
                    }
                    player.SetMeta("opennv_cg02_stage", intro.TargetStage);
                    player.SetMeta("opennv_cg02_intro", 0);
                    player.SetMeta("opennv_cg02_run_timer", 0);
                    foreach (var command in intro.Stage6Commands)
                    {
                        if (command.Kind == "setOpenState")
                        {
                            SetCg01WorldReferenceOpen(
                                command.ReferenceFormId, command.Value != 0);
                            continue;
                        }
                        var placement = _cg02IntroActors.TryGetValue(
                            command.ReferenceFormId, out var loadedActor)
                            ? loadedActor.Placement
                            : Cg01WorldReference(command.ReferenceFormId);
                        if (command.Kind == "setActorVariable")
                            placement.SetMeta(
                                $"opennv_{command.Variable!.ToLowerInvariant()}",
                                command.Value);
                        else
                            placement.LookAt(player.GlobalPosition, Vector3.Up);
                    }
                    completed();
                });
        }
    }

    private void StartCg02DadSpeechRuntime(
        Fo3Cg02DadSpeechRuntime speech,
        Fo3Cg01ToddlerPlayer player,
        IReadOnlyCollection<string> appliedInfoFormIds,
        Action<string> cueCompleted,
        Action completed)
    {
        if (!_cg02IntroActors.TryGetValue(speech.DadReferenceFormId, out var dad))
            throw new InvalidOperationException("Fallout 3 CG02 Dad actor is absent.");
        var sex = (_selectedSex ?? throw new InvalidOperationException(
            "Fallout 3 CG02 player sex is absent.")).EngineSex;
        var cues = speech.Cues
            .Where(value => value.EngineSex is null || value.EngineSex == sex)
            .OrderBy(value => value.Sequence)
            .ToArray();
        if (cues.Length != 2 || cues[0].Sequence != 0 || cues[1].Sequence != 1)
            throw new InvalidOperationException("Fallout 3 CG02 Dad cue selection differs.");
        var next = Array.FindIndex(cues, value => !appliedInfoFormIds.Contains(
            value.InfoFormId, StringComparer.OrdinalIgnoreCase));
        Play(next < 0 ? cues.Length : next);

        void Play(int index)
        {
            if (index == cues.Length)
            {
                player.SetMeta("opennv_cg02_stage", speech.TargetStage);
                foreach (var command in speech.Stage7Commands)
                {
                    var placement = _cg02IntroActors.TryGetValue(
                        command.ReferenceFormId, out var actor)
                        ? actor.Placement
                        : Cg01WorldReference(command.ReferenceFormId);
                    if (command.Kind == "evaluatePackage")
                    {
                        placement.SetMeta("opennv_evaluate_package", 1);
                        continue;
                    }
                    placement.SetMeta(
                        $"opennv_{command.Variable.ToLowerInvariant()}",
                        command.Value);
                }
                completed();
                return;
            }
            var cue = cues[index];
            var animation = dad.Actor.LoadedAnimations.Single(value =>
                ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                    ActorModelSlice.NormalizeAnimationPath(cue.SpeakerIdleLogicalPath),
                    StringComparison.OrdinalIgnoreCase) &&
                value.SourceSha256.Equals(
                    cue.SpeakerIdleSourceSha256,
                    StringComparison.OrdinalIgnoreCase));
            _cg02IntroAnimations[speech.DadReferenceFormId] =
                ActorAnimationPlayback.Start(dad.Actor, animation);
            var voice = new AudioStreamPlayer
            {
                Name = $"Fallout3Cg02DadVoice{cue.Sequence}",
            };
            AddChild(voice);
            var dialogue = new GamebryoDialoguePlayback(
                voice,
                _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
            _cg02IntroDialogue.Add(dialogue);
            dialogue.Start(
                new SourceDialogueLine(
                    cue.InfoFormId,
                    cue.Response.Index,
                    speech.DadReferenceFormId,
                    cue.Response.Text,
                    new SourceDialogueAsset(
                        cue.Response.Voice.LogicalPath,
                        cue.Response.Voice.SourcePath,
                        cue.Response.Voice.Sha256),
                    new SourceDialogueAsset(
                        cue.Response.Lip.LogicalPath,
                        cue.Response.Lip.SourcePath,
                        cue.Response.Lip.Sha256)),
                new FaceGenMorphController(
                    dad.Actor,
                    _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip),
                () =>
                {
                    cueCompleted(cue.InfoFormId);
                    if (cue.TargetStage is not null && cue.TargetStage != speech.TargetStage)
                        throw new InvalidOperationException(
                            "Fallout 3 CG02 Dad result stage differs.");
                    Play(index + 1);
                });
        }
    }

    private void StartCg02OverseerSpeechRuntime(
        Fo3Cg02OverseerSpeechRuntime speech,
        Fo3Cg01ToddlerPlayer player,
        IReadOnlyCollection<string> appliedInfoFormIds,
        Action<string, int, int?> cueCompleted,
        Action completed)
    {
        if (!_cg02IntroActors.TryGetValue(
                speech.OverseerReferenceFormId, out var overseer))
            throw new InvalidOperationException("Fallout 3 CG02 Overseer actor is absent.");
        var sex = (_selectedSex ?? throw new InvalidOperationException(
            "Fallout 3 CG02 player sex is absent.")).EngineSex;
        var cues = speech.Cues
            .Where(value => value.EngineSex is null || value.EngineSex == sex)
            .OrderBy(value => value.Sequence).ToArray();
        if (cues.Length != 4 ||
            !cues.Select(value => value.Sequence).SequenceEqual([0, 1, 2, 3]))
            throw new InvalidOperationException(
                "Fallout 3 CG02 Overseer cue selection differs.");
        var next = Array.FindIndex(cues, value => !appliedInfoFormIds.Contains(
            value.InfoFormId, StringComparer.OrdinalIgnoreCase));
        Play(next < 0 ? cues.Length : next);

        void Play(int index)
        {
            if (index == cues.Length)
            {
                completed();
                return;
            }
            var cue = cues[index];
            if (cue.SpeakerIdleLogicalPath is not null)
            {
                var animation = overseer.Actor.LoadedAnimations.Single(value =>
                    ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                        ActorModelSlice.NormalizeAnimationPath(
                            cue.SpeakerIdleLogicalPath),
                        StringComparison.OrdinalIgnoreCase) &&
                    value.SourceSha256.Equals(
                        cue.SpeakerIdleSourceSha256,
                        StringComparison.OrdinalIgnoreCase));
                _cg02IntroAnimations[speech.OverseerReferenceFormId] =
                    ActorAnimationPlayback.Start(overseer.Actor, animation);
            }
            var voice = new AudioStreamPlayer
            {
                Name = $"Fallout3Cg02OverseerVoice{cue.Sequence}",
            };
            AddChild(voice);
            var dialogue = new GamebryoDialoguePlayback(
                voice,
                _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
            _cg02IntroDialogue.Add(dialogue);
            dialogue.Start(
                new SourceDialogueLine(
                    cue.InfoFormId,
                    cue.Response.Index,
                    speech.OverseerReferenceFormId,
                    cue.Response.Text,
                    new SourceDialogueAsset(
                        cue.Response.Voice.LogicalPath,
                        cue.Response.Voice.SourcePath,
                        cue.Response.Voice.Sha256),
                    new SourceDialogueAsset(
                        cue.Response.Lip.LogicalPath,
                        cue.Response.Lip.SourcePath,
                        cue.Response.Lip.Sha256)),
                new FaceGenMorphController(
                    overseer.Actor,
                    _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip),
                () =>
                {
                    var appliedCommands = 0;
                    int? activeStage = null;
                    foreach (var command in cue.Effects)
                    {
                        Apply(command);
                        appliedCommands++;
                        if (command.Kind != "setStage")
                            continue;
                        activeStage = (int)command.Value;
                        player.SetMeta("opennv_cg02_stage", activeStage.Value);
                        foreach (var nested in speech.StageResults[activeStage.Value])
                        {
                            Apply(nested);
                            appliedCommands++;
                        }
                    }
                    cueCompleted(cue.InfoFormId, appliedCommands, activeStage);
                    Play(index + 1);
                });
        }

        void Apply(Fo3Cg02OverseerCommand command)
        {
            Node3D Actor(string formId) => _cg02IntroActors.TryGetValue(
                    formId, out var actor)
                ? actor.Placement
                : Cg01WorldReference(formId);
            switch (command.Kind)
            {
                case "setStage":
                    break;
                case "setActorVariable":
                    Actor(command.ReferenceFormId).SetMeta(
                        $"opennv_{command.Variable.ToLowerInvariant()}", command.Value);
                    break;
                case "evaluatePackage":
                    Actor(command.ReferenceFormId).SetMeta(
                        "opennv_evaluate_package", 1);
                    break;
                case "lookAt":
                    var target = command.TargetReferenceFormId ==
                        speech.PlayerReferenceFormId
                        ? player.GlobalPosition
                        : Actor(command.TargetReferenceFormId).GlobalPosition;
                    Actor(command.ReferenceFormId).LookAt(target, Vector3.Up);
                    Actor(command.ReferenceFormId).SetMeta("opennv_looks_at_player", 1);
                    break;
                case "stopLook":
                    Actor(command.ReferenceFormId).SetMeta("opennv_looks_at_player", 0);
                    break;
                case "addItem":
                    player.SetMeta($"opennv_item_{command.ItemFormId}", command.Count);
                    break;
                case "resetPipboyManager":
                    player.SetMeta("opennv_reset_pipboy_manager", 1);
                    break;
                case "addAchievement":
                    player.SetMeta("opennv_achievement", command.Value);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Fallout 3 CG02 Overseer command is unsupported: {command.Kind}");
            }
        }
    }

}
