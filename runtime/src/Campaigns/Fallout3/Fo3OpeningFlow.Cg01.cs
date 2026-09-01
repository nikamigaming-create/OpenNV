using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;


using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;
using OpenNV.Runtime.World.Interactions;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
    private void ApplyCg01AfterStage100(Fo3Cg01RuntimeContext context)
    {
        var state = _profile.Cg01Stage0Transition.Apply(context.Stage100);
        EnsureCg01VaultScene(context);
        ApplyCg01Stage5Presentation(state, context.Stage65);
        PersistCg01Transition(
            context.PlayerName,
            context.Sex,
            context.Selection,
            context.Section4Package,
            context.Stage65,
            context.Stage80,
            context.Stage85,
            context.Stage90,
            context.Stage100,
            state);
        StartCg01TransitionMovie(state, context);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE0_STAGE5_APPLIED quest={state.ActiveQuestFormId} " +
            $"stage={state.ActiveStage} commands={state.AppliedCommandCount} " +
            $"trace={string.Join(',', state.AppliedExecutionTrace)} " +
            $"dad={state.Dad.Reference.FormId} dadEnabled=1 " +
            $"nextDad={state.NextDad.Reference.FormId} nextDadEnabled=1 " +
            $"playerScale={state.Player.Scale:F1} movieRequested=1 " +
            $"nextApplied=0 blocker={state.NextBoundary.Blocker}");
    }

    private void ApplyStage100Presentation(Fo3Stage100State stage100)
    {
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 stage-100 Dad has no owned Vault 101 scene.");
        if (!coverage.DadActor.ReferenceFormId.Equals(
                stage100.DisabledDad.FormId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fallout 3 stage-100 Dad identity differs.");
        coverage.DadActor.Placement.Visible = false;
        coverage.DadActor.Placement.ProcessMode = ProcessModeEnum.Disabled;
    }

    private void ApplyCg01Stage5Presentation(
        Fo3Cg01Stage0State stage5,
        Fo3Stage65AppearanceState stage65)
    {
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 stage-5 presentation has no owned Vault 101 scene.");
        var appearance = coverage.Cg01DadAppearance;
        var actorContract = appearance.Actor;
        var rawMarker = actorContract.StartMarkerPositionGodotGameUnits;
        var groundedMarker = coverage.Cg01DadGrounding.PresentationPlacementGodotGameUnits;
        if (!coverage.Cg01DadActor.ReferenceFormId.Equals(
                stage5.Dad.Reference.FormId,
                StringComparison.OrdinalIgnoreCase) ||
            !coverage.Cg01DadActor.BaseFormId.Equals(
                stage5.Dad.Reference.BaseFormId,
                StringComparison.OrdinalIgnoreCase) ||
            !coverage.Cg01DadGrounding.AuthoredPlacementGodotGameUnits.IsEqualApprox(
                rawMarker) ||
            !coverage.Cg01DadActor.Placement.Position.IsEqualApprox(groundedMarker) ||
            !Mathf.IsEqualApprox(groundedMarker.X, rawMarker.X) ||
            !Mathf.IsEqualApprox(groundedMarker.Z, rawMarker.Z) ||
            !Mathf.IsEqualApprox(
                groundedMarker.Y,
                rawMarker.Y +
                    coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits) ||
            !coverage.Cg01DadActor.Placement.Quaternion.IsEqualApprox(
                actorContract.StartMarkerRotationGodotQuaternion))
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-5 Dad actor or MoveTo marker differs.");
        var matchedAppearance = stage65.Parents.Single(value =>
            value.ReferenceFormId.Equals(
                stage5.Dad.Reference.FormId,
                StringComparison.OrdinalIgnoreCase));
        if (actorContract.RaceFormId != matchedAppearance.RaceFormId ||
            appearance.SymmetricGeometrySha256 !=
                matchedAppearance.SymmetricGeometrySha256 ||
            appearance.AsymmetricGeometrySha256 !=
                matchedAppearance.AsymmetricGeometrySha256 ||
            appearance.SymmetricTextureSha256 !=
                matchedAppearance.SymmetricTextureSha256)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad stage-65 geometry was not applied before visibility.");
        coverage.DoctorActor.Placement.Visible = false;
        coverage.DoctorActor.Placement.ProcessMode = ProcessModeEnum.Disabled;
        coverage.DadActor.Placement.Visible = false;
        coverage.DadActor.Placement.ProcessMode = ProcessModeEnum.Disabled;
        coverage.Cg01DadActor.Placement.Visible = true;
        coverage.Cg01DadActor.Placement.ProcessMode = ProcessModeEnum.Inherit;
        ActivateCg01DadDialogueCamera(stage5, coverage);
        _cg01DadFace ??= new FaceGenMorphController(
            coverage.Cg01DadActor.Actor,
            RuntimeConfiguration.Load().ActorCompiler.FaceGenAnimation.Lip);
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_PRESENTED reference={stage5.Dad.Reference.FormId} " +
            $"base={stage5.Dad.Reference.BaseFormId} marker={stage5.Dad.MoveTargetFormId} " +
            $"rawMarker={rawMarker} groundedMarker={groundedMarker} " +
            $"groundingDelta={coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits:F6} " +
            $"enabled={(stage5.Dad.Enabled ? 1 : 0)} previousDoctorVisible=0 " +
            $"previousCg00DadVisible=0 " +
            $"appearance=source-stage65-match-race-50-percent-facegen-applied " +
            $"matchedRace={matchedAppearance.RaceFormId} " +
            $"matchedFace={matchedAppearance.SymmetricGeometrySha256}");
    }

    private void ActivateCg01DadDialogueCamera(
        Fo3Cg01Stage0State stage5,
        Fo3Vault101BirthSceneCoverage coverage)
    {
        var playerMarker = stage5.Player.Transform.PositionGameUnits;
        var playerMarkerLocal = GamebryoCoordinate.ConvertVector(
            new Vector3(
                (float)playerMarker.X,
                (float)playerMarker.Y,
                (float)playerMarker.Z) - coverage.Contract.EntryPositionGameUnits);
        var camera = coverage.Camera;
        camera.GlobalPosition = coverage.CellRoot.ToGlobal(playerMarkerLocal) +
            _profile.Cg01ToddlerWorld.DesktopCameraOffsetMeters;
        camera.Fov = _profile.Cg01ToddlerWorld.VerticalFovDegrees;
        camera.Near = _profile.Cg01ToddlerWorld.NearGameUnits *
            coverage.Contract.UnitsToMeters;
        camera.LookAt(coverage.Cg01DadGrounding.GroundedBounds.GetCenter(), Vector3.Up);
        camera.Current = true;
        _cg01DadDialogueGeometry = CellReferenceLedger.MeasureGeometry(
            coverage.Cg01DadActor.Actor.Root,
            camera,
            coverage.Cg01DadGrounding.GroundedBounds.GetCenter());
        if (!camera.IsCurrent() ||
            !coverage.Cg01DadActor.Placement.Visible ||
            coverage.DoctorActor.Placement.Visible ||
            coverage.DadActor.Placement.Visible ||
            !_cg01DadDialogueGeometry.RenderLayerVisible ||
            !_cg01DadDialogueGeometry.AabbValid ||
            !_cg01DadDialogueGeometry.FrustumIntersection ||
            _cg01DadDialogueGeometry.Surfaces !=
                coverage.Cg01DadAppearance.Actor.Surfaces ||
            _cg01DadDialogueGeometry.Vertices <= 0 ||
            _cg01DadDialogueGeometry.Triangles <= 0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad is not the active-camera dialogue subject.");
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_CAMERA_READY camera={camera.Name} " +
            $"position={camera.GlobalPosition} target=" +
            $"{coverage.Cg01DadGrounding.GroundedBounds.GetCenter()} " +
            $"frustum=1 surfaces={_cg01DadDialogueGeometry.Surfaces}");
    }

    private void StartCg01TransitionMovie(
        Fo3Cg01Stage0State state,
        Fo3Cg01RuntimeContext? context = null)
    {
        if (_video is not null || _ownedVideoMode != Fo3OwnedVideoMode.None)
            throw new InvalidOperationException("Fallout 3 CG01 transition movie is already active.");
        _ownedVideoMode = Fo3OwnedVideoMode.Cg01Transition;
        _activeCg01MovieState = state;
        _activeCg01MovieContext = context;
        _introLayer = new Control { Name = "Fallout3OwnedCg01Transition" };
        _introLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_introLayer);
        var black = new ColorRect { Color = Colors.Black };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _introLayer.AddChild(black);
        _video = new VideoStreamPlayer
        {
            Name = "Fallout3OwnedCg01TransitionVideo",
            Stream = new VideoStreamTheora { File = state.TransitionMovie.RuntimeOutput },
            Expand = true,
            Loop = false,
            Visible = false,
        };
        _video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _video.Finished += () => CompleteOwnedVideo(false);
        _introLayer.AddChild(_video);
        var skip = Button("SKIP  •  ESC");
        skip.Name = "SkipFallout3OwnedCg01Transition";
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
        GD.Print(
            $"OPENNV_FO3_CG01_TRANSITION_MOVIE_STARTED path={state.TransitionMovie.LogicalPath} " +
            $"runtime={state.TransitionMovie.RuntimeOutput} requestCount=1 escapeSkip=1");
    }

    private void CompleteCg01TransitionMovie(bool skipped)
    {
        if (_ownedVideoMode != Fo3OwnedVideoMode.Cg01Transition ||
            _activeCg01MovieState is null)
            return;
        var state = _activeCg01MovieState;
        var context = _activeCg01MovieContext;
        _activeCg01MovieState = null;
        _activeCg01MovieContext = null;
        ClearOwnedVideo();
        if (!_ownedVideoCleared || _video is not null || _introLayer is not null)
            throw new InvalidOperationException(
                "Fallout 3 CG01 movie surface survived transition completion.");
        if (_cg01ProofMode == "apply")
            _cg01ProofMovieEscapeSkipped = skipped;
        BeginCg01DadDialogue(
            state,
            context ?? throw new InvalidOperationException(
                "Fallout 3 CG01 Dad dialogue has no runtime context."),
            resumed: false);
        GD.Print(
            $"OPENNV_FO3_CG01_TRANSITION_MOVIE_COMPLETE " +
            $"mode={(skipped ? "skipped" : "watched")} stage={state.ActiveStage} " +
            $"nextApplied=0 blocker={state.NextBoundary.Blocker}");
    }

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

    private void CompleteCg01DadDialogue(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        IReadOnlyList<Fo3Cg01DadSpeechCue> cues,
        Button subtitle)
    {
        RestoreCg01DadPrimaryIdle();
        HideVaultDialogue(subtitle);
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        var state = _profile.Cg01Stage10Transition.Apply(stage5, context.Sex.EngineSex);
        if (!state.AppliedInfoFormIds.SequenceEqual(cues.Select(value => value.InfoFormId)))
            throw new InvalidOperationException("Fallout 3 CG01 applied INFO sequence differs.");
        PersistCg01Stage10Transition(context, stage5, state);
        BeginCg01ToddlerWorld(
            stage5,
            context,
            state,
            restored: null,
            acceptanceProof: _cg01ProofMode == "apply");
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE10_APPLIED quest={state.ActiveQuestFormId} " +
            $"stage={state.ActiveStage} infos={string.Join(',', state.AppliedInfoFormIds)} " +
            $"commands={state.AppliedCommandCount} dadTimer={state.DadTimerSeconds:F1} " +
            $"objective={state.DisplayedObjectiveIndex} tutorial={state.TutorialQuestStage} " +
            $"autosave={state.AutosaveRequestCount} toddlerWorld=1 " +
            $"blocker={state.NextBoundary.Blocker}");
    }

    private void RestoreCg01DadPrimaryIdle()
    {
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 Dad primary idle has no owned actor scene.");
        var primary = coverage.Cg01DadActor.Actor.LoadedAnimations.Single(value =>
            ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                ActorModelSlice.NormalizeAnimationPath(
                    coverage.Cg01DadAppearance.Actor.IdleAnimationPath),
                StringComparison.OrdinalIgnoreCase));
        foreach (var player in coverage.Cg01DadActor.Actor.LoadedAnimations
                     .Select(value => value.Player).Distinct())
            player.Stop();
        primary.Player.Play(primary.RuntimeName);
        primary.Player.Advance(0.0);
        if (primary.Player.CurrentAnimation.ToString() != primary.RuntimeName)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad primary idle was not restored after dialogue.");
    }

    private void BeginCg01ToddlerWorld(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01ToddlerWorldState? restored,
        bool acceptanceProof,
        Fo3Cg01Stage14State? restoredStage14 = null,
        Fo3Cg01Stage20State? restoredStage20 = null)
    {
        if (_cg01ToddlerWorld is not null)
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler world is already active.");
        EnsureCg01VaultScene(context);

        if (restored is null)
            ShowCg01PostStage10Boundary(stage10, resumed: false);
        var scene = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 toddler world scene is absent after preparation.");
        _cg01ToddlerWorld = Fo3Cg01ToddlerWorldRuntime.Build(
            _vaultPreviewHost ?? throw new InvalidOperationException(
                "Fallout 3 CG01 toddler world host is absent."),
            scene,
            _profile.Cg01ToddlerWorld,
            stage5,
            stage10,
            _profile.Cg01Stage12Transition,
            restored,
            player => CompleteCg01ToddlerTrigger(
                stage5,
                context,
                stage10,
                player,
                acceptanceProof));

        if (restored is not null)
        {
            var restoredRuntime = _cg01ToddlerWorld.State(triggerEntered: true);
            if (!restoredRuntime.PlayerPositionMeters.IsEqualApprox(
                    restored.PlayerPositionMeters) ||
                !restoredRuntime.PlayerRotation.IsEqualApprox(restored.PlayerRotation) ||
                restoredRuntime.AuthoredCollisionBodies != restored.AuthoredCollisionBodies)
                throw new InvalidOperationException(
                    "Restored Fallout 3 CG01 toddler body differs.");
            var restoredStage12 = _profile.Cg01Stage12Transition.ApplyAuthoredTrigger(
                stage10,
                _profile.Cg01Stage12Transition.Trigger.ReferenceFormId,
                actionReferenceWasPlayer: true);
            if (restoredStage14 is not null)
            {
                var expectedStage14 = _profile.Cg01Stage12DadResponse.Apply(restoredStage12);
                if (restoredStage14.SourceStage != expectedStage14.SourceStage ||
                    restoredStage14.ActiveQuestFormId != expectedStage14.ActiveQuestFormId ||
                    restoredStage14.ActiveQuestEditorId != expectedStage14.ActiveQuestEditorId ||
                    restoredStage14.ActiveStage != expectedStage14.ActiveStage ||
                    !restoredStage14.AppliedInfoFormIds.SequenceEqual(
                        expectedStage14.AppliedInfoFormIds) ||
                    restoredStage14.DadTalking != expectedStage14.DadTalking ||
                    restoredStage14.DadLooksAtPlayer != expectedStage14.DadLooksAtPlayer ||
                    restoredStage14.DadPackageEvaluated !=
                        expectedStage14.DadPackageEvaluated ||
                    restoredStage14.AccountedCommandCount !=
                        expectedStage14.AccountedCommandCount ||
                    restoredStage14.AppliedCommandCount !=
                        expectedStage14.AppliedCommandCount ||
                    restoredStage14.NextBoundary != expectedStage14.NextBoundary)
                    throw new InvalidOperationException(
                        "Restored Fallout 3 CG01 stage-14 Dad response differs.");
                var dad = scene.Cg01DadActor.Placement;
                dad.SetMeta("opennv_talking", restoredStage14.DadTalking);
                dad.SetMeta(
                    "opennv_look_target",
                    restoredStage14.DadLooksAtPlayer ? "player" : "");
                dad.SetMeta(
                    "opennv_package_evaluated",
                    restoredStage14.DadPackageEvaluated);
                if (dad.GetMeta("opennv_talking").AsInt32() !=
                        restoredStage14.DadTalking ||
                    dad.GetMeta("opennv_look_target").AsString() != "player" ||
                    !dad.GetMeta("opennv_package_evaluated").AsBool())
                    throw new InvalidOperationException(
                        "Restored Fallout 3 CG01 Dad runtime state differs.");
                if (_cg01DadPublishedSpeakerIdleInfoFormIds.Count != 0 ||
                    _cg01DadLipCueSamples != 0 ||
                    _vaultDialogueVoice is not null ||
                    _activeCg01DadLip is not null)
                    throw new InvalidOperationException(
                        "Restored Fallout 3 CG01 Dad response replayed presentation effects.");
            }
            if (acceptanceProof)
            {
                if (restoredStage14 is null)
                    throw new InvalidOperationException(
                        "Fallout 3 CG01 restore proof has no saved stage-14 Dad response.");
                WriteCg01ProofReport(
                    stage5,
                    stage10,
                    restoredStage12,
                    restoredStage14,
                    restoredRuntime,
                    context.Sex.EngineSex,
                    "restore",
                    movieSurfaceRequested: false,
                    escapeSkipped: false,
                    movieReplayed: false,
                    dialoguePlayed: false);
                GD.Print(
                    $"OPENNV_FO3_CG01_TODDLER_WORLD_PROOF_RESTORE " +
                    $"stage={restoredStage14.ActiveStage} physicalEntry=1 " +
                    $"collisionBodies={restoredRuntime.AuthoredCollisionBodies} " +
                    "movieReplayed=0 dialogueReplayed=0 stage12ResponseReplayed=0 " +
                    "transitionEffectsReplayed=0 packageEffectsReplayed=0");
                GetTree().Quit(0);
                return;
            }
            if (restoredStage20 is not null)
            {
                if (restoredStage14 is null)
                    throw new InvalidOperationException(
                        "Fallout 3 CG01 stage-20 restore has no stage-14 state.");
                RestoreCg01Stage20World(
                    context, stage5, stage10, restoredStage12, restored,
                    restoredStage14, restoredStage20);
            }
            else if (restoredStage14 is not null)
                BeginCg01PostStage14Transition(
                    stage5,
                    context,
                    stage10,
                    restoredStage12,
                    restored,
                    restoredStage14);
            else
                ShowCg01PostStage12Boundary(restoredStage12, resumed: true);
            return;
        }

        GD.Print(
            $"OPENNV_FO3_CG01_TODDLER_WORLD_READY cell={_profile.Cg01ToddlerWorld.CellFormId} " +
            $"marker={_profile.Cg01ToddlerWorld.PlayerStartMarkerFormId} " +
            $"scale={_profile.Cg01ToddlerWorld.PlayerScale:F1} " +
            $"collisionBodies={_cg01ToddlerWorld.AuthoredCollisionBodies} " +
            $"trigger={_profile.Cg01ToddlerWorld.TriggerReferenceFormId} visualBody=0");
        if (!acceptanceProof)
            return;
        _cg01ToddlerWorld.Player.BeginConfiguredInputAcceptance();
        var start = _profile.Cg01ToddlerWorld.PlayerStartTransform.PositionGameUnits;
        var trigger = _profile.Cg01Stage12Transition.Trigger.SourceTransform.PositionGameUnits;
        var distanceGameUnits = new Vector3(
            (float)(trigger.X - start.X),
            (float)(trigger.Y - start.Y),
            (float)(trigger.Z - start.Z)).Length();
        var timeoutSeconds =
            distanceGameUnits * _birthPresentation!.UnitsToMeters /
            _profile.Cg01ToddlerWorld.MoveSpeedMetersPerSecond *
            Fo3OpeningFlowNumericContracts.Cg01ProofTimeoutMultiplier;
        GetTree().CreateTimer(timeoutSeconds).Timeout += () =>
        {
            if (_cg01ToddlerWorld?.Player.MovementEnabled != true)
                return;
            var player = _cg01ToddlerWorld.Player;
            player.CancelConfiguredInputAcceptance();
            GD.PushError(
                "OPENNV_FO3_CG01_TODDLER_WORLD_PROOF_FAIL physical trigger was not entered " +
                $"frames={player.AcceptancePhysicsFrames} " +
                $"travel={player.AcceptanceHorizontalTravelMeters:F3} " +
                $"position={player.GlobalPosition}");
            GetTree().Quit(Fo3OpeningFlowNumericContracts.ProofFailureExitCode);
        };
    }

    private void EnsureCg01VaultScene(Fo3Cg01RuntimeContext context)
    {
        if (_vaultBirthCoverage is not null)
            return;
        var presentation = _birthPresentation ?? throw new InvalidOperationException(
            "Fallout 3 CG01 world has no owned Vault 101 presentation.");
        var previewHost = new Node3D { Name = "FO3_VAULT101_CG01_WORLD" };
        _worldHost.AddChild(previewHost);
        try
        {
            var cg01DadAppearance = presentation.Cg01DadActorFor(
                context.Selection.Race.FormId,
                context.Sex.EngineSex,
                context.Stage65);
            _vaultBirthCoverage = Fo3Vault101BirthScene.Build(
                previewHost,
                presentation,
                cg01DadAppearance);
        }
        catch
        {
            previewHost.QueueFree();
            throw;
        }
        _vaultPreviewHost = previewHost;
        _background.Visible = false;
        _panel.Visible = false;
        ApplyStage100Presentation(context.Stage100);
        GD.Print(
            $"OPENNV_FO3_CG01_WORLD_PRESENTATION_READY cell={presentation.CellFormId} " +
            $"references={_vaultBirthCoverage.PlacedReferences} " +
            $"models={_vaultBirthCoverage.LoadedAssets} " +
            $"collisionBodies={_vaultBirthCoverage.AuthoredCollisionBodies} " +
            $"cg01DadPrepared=1 cg01Dad={_vaultBirthCoverage.Cg01DadActor.ReferenceFormId} " +
            "cg01DadVisible=0 stage5PresentationApplied=0 " +
            "appearance=source-stage65-match-race-50-percent-facegen-applied");
    }

    private void CompleteCg01ToddlerTrigger(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01ToddlerPlayer player,
        bool acceptanceProof)
    {
        var runtime = _cg01ToddlerWorld ?? throw new InvalidOperationException(
            "Fallout 3 CG01 toddler trigger has no active world.");
        if (player != runtime.Player)
            throw new InvalidOperationException(
                "Fallout 3 CG01 toddler trigger actor differs.");
        var stage12 = _profile.Cg01Stage12Transition.ApplyAuthoredTrigger(
            stage10,
            runtime.Contract.TriggerReferenceFormId,
            actionReferenceWasPlayer: true);
        var toddlerState = runtime.State(triggerEntered: true);
        PersistCg01Stage12Transition(context, stage5, stage10, stage12, toddlerState);
        BeginCg01Stage12DadResponse(
            stage5,
            context,
            stage10,
            stage12,
            toddlerState,
            acceptanceProof);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE12_APPLIED_PHYSICAL_TRIGGER stage={stage12.ActiveStage} " +
            $"trigger={stage12.TriggerReferenceFormId} physicalEntry=1 movementEnabled=0");
    }

    private void BeginCg01Stage12DadResponse(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerState,
        bool acceptanceProof)
    {
        _vaultPreviewOverlay?.QueueFree();
        var subtitle = AddVaultDialogueOverlay("FO3_CG01_STAGE12_DAD_RESPONSE");
        subtitle.SetMeta("opennv_speaker_reference_form_id", stage5.Dad.Reference.FormId);
        PlayCg01Stage12DadResponseCue(
            stage5,
            context,
            stage10,
            stage12,
            toddlerState,
            _profile.Cg01Stage12DadResponse.Cues,
            0,
            subtitle,
            acceptanceProof);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE12_DAD_RESPONSE_STARTED stage={stage12.ActiveStage} " +
            $"cues={_profile.Cg01Stage12DadResponse.Cues.Count} physicalEntry=1");
    }

    private void PlayCg01Stage12DadResponseCue(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerState,
        IReadOnlyList<Fo3Cg01Stage12DadResponseCue> cues,
        int index,
        Button subtitle,
        bool acceptanceProof)
    {
        if (index < 0 || index >= cues.Count)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response cursor differs.");
        var cue = cues[index];
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 stage-12 Dad response has no owned actor scene.");
        var speaker = subtitle.GetMeta("opennv_speaker_reference_form_id").AsString();
        if (!speaker.Equals(stage5.Dad.Reference.FormId, StringComparison.OrdinalIgnoreCase) ||
            !coverage.Cg01DadActor.Placement.Visible)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 response speaker differs from visible Dad.");
        var publishedSpeakerIdle = PublishCg01DadSpeakerIdle(cue);
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        ClearCg01DadLip();
        var stream = AudioStreamOggVorbis.LoadFromFile(cue.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 CG01 stage-12 Dad voice could not be decoded: " +
                cue.Response.Voice.LogicalPath);
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad voice has no duration.");
        _activeCg01DadLip = FaceGenLipAnimation.Load(
            cue.Response.Lip.SourcePath,
            RuntimeConfiguration.Load().ActorCompiler.FaceGenAnimation.Lip);
        _activeCg01DadInfoFormId = cue.InfoFormId;
        _cg01DadLipSampleLogged = false;
        coverage.Cg01DadActor.Placement.SetMeta("opennv_talking", 1);
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = $"Fallout3Cg01Stage12DadVoice{cue.Sequence}",
            Stream = stream,
        };
        _vaultDialogueVoice.SetMeta("opennv_info_form_id", cue.InfoFormId);
        _vaultDialogueVoice.SetMeta("opennv_speaker_reference_form_id", speaker);
        _vaultDialogueVoice.SetMeta(
            "opennv_speaker_idle_form_id",
            cue.SpeakerIdle.FormId);
        _vaultDialogueVoice.Finished += () =>
        {
            ClearCg01DadLip();
            _vaultDialogueVoice?.QueueFree();
            _vaultDialogueVoice = null;
            coverage.Cg01DadActor.Placement.SetMeta("opennv_talking", 0);
            coverage.Cg01DadActor.Placement.SetMeta("opennv_look_target", "player");
            if (index + 1 < cues.Count)
            {
                Callable.From(() => PlayCg01Stage12DadResponseCue(
                    stage5,
                    context,
                    stage10,
                    stage12,
                    toddlerState,
                    cues,
                    index + 1,
                    subtitle,
                    acceptanceProof)).CallDeferred();
                return;
            }
            CompleteCg01Stage12DadResponse(
                stage5,
                context,
                stage10,
                stage12,
                toddlerState,
                cues,
                subtitle,
                acceptanceProof);
        };
        AddChild(_vaultDialogueVoice);
        ShowVaultDialogue(
            subtitle,
            _vaultBirthCoverage?.Cg01DadActor.Actor.Name ??
                throw new InvalidOperationException("Fallout 3 CG01 Dad actor is unavailable."),
            cue.Response.Text);
        _vaultDialogueVoice.Play();
        if (_vaultDialogueVoice.GetMeta("opennv_info_form_id").AsString() !=
                _activeCg01DadInfoFormId ||
            _vaultDialogueVoice.GetMeta("opennv_speaker_reference_form_id").AsString() !=
                speaker ||
            _vaultDialogueVoice.GetMeta("opennv_speaker_idle_form_id").AsString() !=
                cue.SpeakerIdle.FormId ||
            publishedSpeakerIdle.Player.CurrentAnimation.ToString() !=
                publishedSpeakerIdle.RuntimeName)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 audio, LIP, and idle INFO identities diverged.");
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE12_DAD_CUE_STARTED sequence={cue.Sequence} " +
            $"info={cue.InfoFormId} duration={durationSeconds:F3} " +
            $"voice={cue.Response.Voice.LogicalPath} lip={cue.Response.Lip.LogicalPath} " +
            $"targetStage={(cue.TargetStage?.ToString() ?? "none")}");
    }

    private void CompleteCg01Stage12DadResponse(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerState,
        IReadOnlyList<Fo3Cg01Stage12DadResponseCue> cues,
        Button subtitle,
        bool acceptanceProof)
    {
        RestoreCg01DadPrimaryIdle();
        HideVaultDialogue(subtitle);
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        var stage14 = _profile.Cg01Stage12DadResponse.Apply(stage12);
        if (!stage14.AppliedInfoFormIds.SequenceEqual(cues.Select(value => value.InfoFormId)) ||
            cues[^1].TargetStage != stage14.ActiveStage)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-12 Dad response application differs.");
        var dad = (_vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 stage-14 package evaluation has no owned actor scene."))
            .Cg01DadActor.Placement;
        dad.SetMeta("opennv_talking", stage14.DadTalking);
        dad.SetMeta("opennv_look_target", stage14.DadLooksAtPlayer ? "player" : "");
        dad.SetMeta("opennv_package_evaluated", stage14.DadPackageEvaluated);
        PersistCg01Stage14Response(
            context,
            stage5,
            stage10,
            stage12,
            toddlerState,
            stage14);
        if (acceptanceProof)
        {
            WriteCg01ProofReport(
                stage5,
                stage10,
                stage12,
                stage14,
                toddlerState,
                context.Sex.EngineSex,
                "apply",
                movieSurfaceRequested: true,
                escapeSkipped: _cg01ProofMovieEscapeSkipped,
                movieReplayed: false,
                dialoguePlayed: true);
            GD.Print(
                $"OPENNV_FO3_CG01_STAGE14_PROOF_APPLY stage={stage14.ActiveStage} " +
                $"infos={string.Join(',', stage14.AppliedInfoFormIds)} physicalEntry=1 " +
                $"packageEvaluated={(stage14.DadPackageEvaluated ? 1 : 0)} " +
                "movementEnabled=0");
            GetTree().Quit(0);
            return;
        }
        BeginCg01PostStage14Transition(
            stage5,
            context,
            stage10,
            stage12,
            toddlerState,
            stage14);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE14_APPLIED quest={stage14.ActiveQuestFormId} " +
            $"stage={stage14.ActiveStage} infos={string.Join(',', stage14.AppliedInfoFormIds)} " +
            $"packageEvaluated={(stage14.DadPackageEvaluated ? 1 : 0)} " +
            $"blocker={stage14.NextBoundary.Blocker}");
    }

    private void ShowCg01PostStage10Boundary(Fo3Cg01Stage10State state, bool resumed)
    {
        _vaultPreviewOverlay?.QueueFree();
        var overlay = new PanelContainer
        {
            Name = "FO3_CG01_POST_STAGE10_BOUNDARY",
            AnchorLeft = 0.0f,
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = Fo3OpeningFlowNumericContracts.BoundaryHorizontalInsetPixels,
            OffsetTop = Fo3OpeningFlowNumericContracts.BoundaryTopOffsetPixels,
            OffsetRight = -Fo3OpeningFlowNumericContracts.BoundaryHorizontalInsetPixels,
            OffsetBottom = Fo3OpeningFlowNumericContracts.BoundaryBottomOffsetPixels,
        };
        overlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(
                0.0f,
                0.0f,
                0.0f,
                Fo3OpeningFlowNumericContracts.BoundaryPanelAlpha),
            BorderColor = _profile.InterfaceColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        });
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, Fo3OpeningFlowNumericContracts.SeparationPixels);
        overlay.AddChild(margin);
        var content = new VBoxContainer();
        margin.AddChild(content);
        content.AddChild(Label(
            $"{state.ActiveQuestEditorId}  •  STAGE {state.ActiveStage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        content.AddChild(Label(
            $"OBJECTIVE: {_profile.Cg01Stage12Transition.ObjectiveText}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        content.AddChild(Label(
            "Dad's two source-authored cues completed. Move with W/A/S/D to enter the exact " +
            "owned walk trigger. The physical body has no prepared toddler visual yet.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var exit = Button("RETURN TO MAIN MENU");
        exit.Pressed += ExitVault101Preview;
        content.AddChild(exit);
        AddChild(overlay);
        _vaultPreviewOverlay = overlay;
        Callable.From(exit.GrabFocus).CallDeferred();
        if (resumed)
        {
            GD.Print(
                $"OPENNV_FO3_CG01_COLD_RESTORE quest={state.ActiveQuestFormId} " +
                $"stage={state.ActiveStage} commands={state.AppliedCommandCount} " +
                $"movieReplayed=0 dialogueReplayed=0 transitionEffectsReplayed=0 " +
                $"nextApplied=0 blocker={state.NextBoundary.Blocker}");
        }
    }

    private void ShowCg01PostStage12Boundary(Fo3Cg01Stage12State state, bool resumed)
    {
        _vaultPreviewOverlay?.QueueFree();
        var overlay = new PanelContainer
        {
            Name = "FO3_CG01_POST_STAGE12_BOUNDARY",
            AnchorLeft = 0.0f,
            AnchorTop = 1.0f,
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = Fo3OpeningFlowNumericContracts.BoundaryHorizontalInsetPixels,
            OffsetTop = Fo3OpeningFlowNumericContracts.BoundaryTopOffsetPixels,
            OffsetRight = -Fo3OpeningFlowNumericContracts.BoundaryHorizontalInsetPixels,
            OffsetBottom = Fo3OpeningFlowNumericContracts.BoundaryBottomOffsetPixels,
        };
        overlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(
                0.0f,
                0.0f,
                0.0f,
                Fo3OpeningFlowNumericContracts.BoundaryPanelAlpha),
            BorderColor = _profile.InterfaceColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        });
        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, Fo3OpeningFlowNumericContracts.SeparationPixels);
        overlay.AddChild(margin);
        var content = new VBoxContainer();
        margin.AddChild(content);
        content.AddChild(Label(
            $"{state.ActiveQuestEditorId}  •  STAGE {state.ActiveStage}",
            Fo3OpeningFlowNumericContracts.TitleFontPixels));
        content.AddChild(Label(
            $"OBJECTIVE COMPLETE: {_profile.Cg01Stage12Transition.ObjectiveText}",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        content.AddChild(Label(
            "The physical toddler body entered the owned Dad trigger and the exact stage-12 " +
            "commands are saved. Dad's response and the wider Vault route remain stopped.",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var exit = Button("RETURN TO MAIN MENU");
        exit.Pressed += ExitVault101Preview;
        content.AddChild(exit);
        AddChild(overlay);
        _vaultPreviewOverlay = overlay;
        Callable.From(exit.GrabFocus).CallDeferred();
        if (resumed)
        {
            GD.Print(
                $"OPENNV_FO3_CG01_STAGE12_COLD_RESTORE quest={state.ActiveQuestFormId} " +
                $"stage={state.ActiveStage} trigger={state.TriggerReferenceFormId} " +
                "transitionEffectsReplayed=0 nextApplied=0 " +
                $"blocker={state.NextBoundary.Blocker}");
        }
    }

    private void BeginCg01PostStage14Transition(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerState,
        Fo3Cg01Stage14State stage14)
    {
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        ApplyCg01DadPackage(_profile.Cg01PostStage14Transition.CloseGatePackage, stage5);
        SetCg01WorldReferenceOpen(
            _profile.Cg01PostStage14Transition.PlaypenGateReferenceFormId,
            false);
        var cues = _profile.Cg01PostStage14Transition.SelectCues(context.Sex.EngineSex);
        var subtitle = AddVaultDialogueOverlay("FO3_CG01_STAGE16_DAD_RESPONSE");
        PlayCg01PostStage14Cue(
            stage5,
            context,
            stage10,
            stage12,
            toddlerState,
            stage14,
            cues,
            0,
            subtitle);
    }

    private void PlayCg01PostStage14Cue(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerState,
        Fo3Cg01Stage14State stage14,
        IReadOnlyList<Fo3Cg01PostStage14Cue> cues,
        int index,
        Button subtitle)
    {
        var cue = cues[index];
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 stage-16 Dad response has no owned world.");
        RestoreCg01DadPrimaryIdle();
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        ClearCg01DadLip();
        var stream = AudioStreamOggVorbis.LoadFromFile(cue.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad voice could not be decoded: {cue.InfoFormId}");
        _activeCg01DadLip = FaceGenLipAnimation.Load(
            cue.Response.Lip.SourcePath,
            RuntimeConfiguration.Load().ActorCompiler.FaceGenAnimation.Lip);
        _activeCg01DadInfoFormId = cue.InfoFormId;
        coverage.Cg01DadActor.Placement.SetMeta("opennv_talking", 1);
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = $"Fallout3Cg01Stage16DadVoice{cue.Sequence}",
            Stream = stream,
        };
        _vaultDialogueVoice.SetMeta("opennv_info_form_id", cue.InfoFormId);
        _vaultDialogueVoice.Finished += () =>
        {
            ClearCg01DadLip();
            _vaultDialogueVoice?.QueueFree();
            _vaultDialogueVoice = null;
            coverage.Cg01DadActor.Placement.SetMeta("opennv_talking", 0);
            if (index + 1 < cues.Count)
            {
                Callable.From(() => PlayCg01PostStage14Cue(
                    stage5,
                    context,
                    stage10,
                    stage12,
                    toddlerState,
                    stage14,
                    cues,
                    index + 1,
                    subtitle)).CallDeferred();
                return;
            }
            CompleteCg01PostStage14Transition(
                stage5,
                context,
                stage10,
                stage12,
                toddlerState,
                stage14,
                subtitle);
        };
        AddChild(_vaultDialogueVoice);
        ShowVaultDialogue(subtitle, coverage.Cg01DadActor.Actor.Name, cue.Response.Text);
        _vaultDialogueVoice.Play();
    }

    private void CompleteCg01PostStage14Transition(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerState,
        Fo3Cg01Stage14State stage14,
        Button subtitle)
    {
        HideVaultDialogue(subtitle);
        _vaultPreviewOverlay?.QueueFree();
        _vaultPreviewOverlay = null;
        var state = _profile.Cg01PostStage14Transition.Apply(
            stage14,
            context.Sex.EngineSex);
        ApplyCg01DadPackage(_profile.Cg01PostStage14Transition.CloseDoorPackage, stage5);
        SetCg01WorldReferenceOpen(state.PlayroomDoorReferenceFormId, false);
        SetCg01WorldReferenceLock(state.PlayroomDoorReferenceFormId, state.PlayroomDoorLockLevel);
        SetCg01WorldReferenceOpen(state.PlaypenGateReferenceFormId, false);
        var timer = _profile.Cg01PostStage14Transition.Stage20Interaction.TimerTransition;
        ApplyCg01DadPackage(
            state.ActiveStage >= timer.DadLead.SayToDoneStage
                ? timer.DadLead.LeadTravel.Package
                : state.ActiveStage >= timer.CompletionStage
                    ? timer.DadReturnPackage
                : _profile.Cg01PostStage14Transition.LeaveRoomPackage,
            stage5);
        (_cg01ToddlerWorld ?? throw new InvalidOperationException(
            "Fallout 3 CG01 stage-20 world is absent."))
            .Player.EnableMovementAtSourceStage();
        var stage20World = _cg01ToddlerWorld.State(triggerEntered: true);
        InstallCg01Stage20Interactions(
            context, stage5, stage10, stage12, stage20World, stage14, state);
        PersistCg01Stage20Transition(
            context,
            stage5,
            stage10,
            stage12,
            stage20World,
            stage14,
            state);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE20_APPLIED quest={state.ActiveQuestFormId} " +
            $"stage={state.ActiveStage} packages={string.Join(',', state.AppliedPackageFormIds)} " +
            $"infos={string.Join(',', state.AppliedInfoFormIds)} movement=1 " +
            $"objective={state.DisplayedObjectiveIndex} blocker={state.NextBoundary.Blocker}");
    }

    private void ApplyCg01DadPackage(
        Fo3Cg01PostStage14Package package,
        Fo3Cg01Stage0State stage5)
    {
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 Dad package has no owned world.");
        var placement = Cg01DadPackagePlacement(package, stage5, coverage);
        var travel = GamebryoPackageTravel.ArriveAtSourceTarget(
            package.FormId,
            placement,
            coverage.Cg01DadActor.Placement.Transform,
            GamebryoPackageTravel.ExactArrivalToleranceCellUnits);
        travel.Publish(coverage.Cg01DadActor.Placement);
    }

    private static SourcePackagePlacement Cg01DadPackagePlacement(
        Fo3Cg01PostStage14Package package,
        Fo3Cg01Stage0State stage5,
        Fo3Vault101BirthSceneCoverage coverage)
    {
        var source = package.TargetTransform;
        var placement = GamebryoPackagePlacement.FromPlanarGameReferenceMarker(
            package.TargetFormId,
            new Vector3(
                (float)source.PositionGameUnits.X,
                (float)source.PositionGameUnits.Y,
                (float)source.PositionGameUnits.Z),
            new Vector3(
                (float)source.RotationRadians.X,
                (float)source.RotationRadians.Y,
                (float)source.RotationRadians.Z),
            (float)stage5.Dad.Reference.SourceTransform.Scale,
            coverage.Contract.EntryPositionGameUnits);
        placement = placement with
        {
            SourceTransform = GamebryoPackagePlacement.AdjustSupportHeight(
                placement.SourceTransform,
                coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits),
        };
        return placement;
    }

    private void StartCg01DadSourceTravel(
        Fo3Cg01DadLeadSequence sequence,
        Fo3Cg01DadTravelPackage source,
        Fo3Cg01Transform start,
        Fo3Cg01Stage0State stage5,
        Action arrived)
    {
        if (_cg01DadPackageTravelTick is not null)
            throw new InvalidOperationException("Fallout 3 CG01 Dad travel is already active.");
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 Dad travel has no owned world.");
        var target = Cg01DadPackagePlacement(source.Package, stage5, coverage);
        var path = sequence.Navigation.FindPath(
                new Vector3((float)start.PositionGameUnits.X, (float)start.PositionGameUnits.Y,
                    (float)start.PositionGameUnits.Z),
                new Vector3((float)source.Package.TargetTransform.PositionGameUnits.X,
                    (float)source.Package.TargetTransform.PositionGameUnits.Y,
                    (float)source.Package.TargetTransform.PositionGameUnits.Z))
            .Select(position => GamebryoCoordinate.ConvertVector(
                position - coverage.Contract.EntryPositionGameUnits) +
                Vector3.Up * coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits)
            .ToArray();
        var travel = GamebryoPackageTravel.Start(
            source.Package.FormId,
            target,
            coverage.Cg01DadActor.Placement.Transform,
            path,
            sequence.LocomotionSpeedGameUnitsPerSecond,
            GamebryoPackageTravel.ExactArrivalToleranceCellUnits);
        var animation = coverage.Cg01DadActor.Actor.LoadedAnimations.Single(value =>
            ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                ActorModelSlice.NormalizeAnimationPath(sequence.LocomotionLogicalPath),
                StringComparison.OrdinalIgnoreCase) &&
            value.SourceSha256.Equals(sequence.LocomotionSha256,
                StringComparison.OrdinalIgnoreCase));
        animation.Player.Play(animation.RuntimeName);
        animation.Player.Advance(0.0);
        travel.Publish(coverage.Cg01DadActor.Placement);
        if (travel.Arrived)
        {
            RestoreCg01DadPrimaryIdle();
            arrived();
            return;
        }
        _cg01DadPackageTravelTick = delta =>
        {
            var completed = travel.Advance(delta);
            travel.Publish(coverage.Cg01DadActor.Placement);
            if (!completed)
                return;
            _cg01DadPackageTravelTick = null;
            RestoreCg01DadPrimaryIdle();
            arrived();
        };
    }

    private void RestoreCg01Stage20World(
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage0State stage5,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerWorld,
        Fo3Cg01Stage14State stage14,
        Fo3Cg01Stage20State state)
    {
        var timer = _profile.Cg01PostStage14Transition.Stage20Interaction.TimerTransition;
        var completion = timer.DadLead.Completion;
        var completed = state.ActiveQuestFormId.Equals(
            completion.NextQuestFormId, StringComparison.OrdinalIgnoreCase);
        var progressStage = completed ? completion.TargetStage : state.ActiveStage;
        ApplyCg01DadPackage(
            progressStage >= timer.DadLead.SayToDoneStage
                ? timer.DadLead.LeadTravel.Package
                : progressStage >= timer.CompletionStage
                    ? timer.DadReturnPackage
                    : _profile.Cg01PostStage14Transition.LeaveRoomPackage,
            stage5);
        SetCg01WorldReferenceOpen(state.PlayroomDoorReferenceFormId, state.PlayroomDoorOpen);
        SetCg01WorldReferenceLock(
            state.PlayroomDoorReferenceFormId,
            state.PlayroomDoorLockLevel);
        SetCg01WorldReferenceOpen(
            state.PlaypenGateReferenceFormId,
            state.PlaypenGateOpen);
        if (progressStage >= _profile.Cg01PostStage14Transition.Stage20Interaction
                .TimerTransition.CompletionStage)
        {
            SetCg01WorldReferenceLock(
                timer.MainDoorReferenceFormId,
                progressStage >= timer.DadLead.SayToDoneStage
                    ? 0
                    : timer.MainDoorLockLevel);
            SetCg01WorldReferenceOpen(timer.MainDoorReferenceFormId, timer.MainDoorOpen);
        }
        var world = _cg01ToddlerWorld ?? throw new InvalidOperationException(
            "Fallout 3 CG01 stage-20 restore has no toddler world.");
        if (world.Player.MovementEnabled != state.PlayerMovementEnabled ||
            _vaultDialogueVoice is not null ||
            _activeCg01DadLip is not null)
            throw new InvalidOperationException(
                "Fallout 3 CG01 stage-20 restored runtime differs.");
        InstallCg01Stage20Interactions(
            context, stage5, stage10, stage12, toddlerWorld, stage14, state);
        GD.Print(
            $"OPENNV_FO3_CG01_STAGE20_COLD_RESTORE quest={state.ActiveQuestFormId} " +
            $"stage={state.ActiveStage} packages={string.Join(',', state.AppliedPackageFormIds)} " +
            "dialogueReplayed=0 packageTravelReplayed=0 movement=1 " +
            $"blocker={state.NextBoundary.Blocker}");
    }

    private void InstallCg01Stage20Interactions(
        Fo3Cg01RuntimeContext context,
        Fo3Cg01Stage0State stage5,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01ToddlerWorldState toddlerWorld,
        Fo3Cg01Stage14State stage14,
        Fo3Cg01Stage20State initial)
    {
        var current = initial;
        var interaction = _profile.Cg01PostStage14Transition.Stage20Interaction;
        void Persist() => PersistCg01Stage20Transition(
            context, stage5, stage10, stage12,
            (_cg01ToddlerWorld ?? throw new InvalidOperationException(
                "Fallout 3 CG01 interaction world is absent.")).State(triggerEntered: true),
            stage14, current);
        void InstallBirthday(
            Fo3Cg02BirthdayInteractionsRuntime birthday,
            Fo3Cg01ToddlerPlayer player)
        {
            var cake = birthday.CakeRuntime ?? throw new InvalidOperationException(
                "Fallout 3 CG02 cake runtime is absent.");
            var butch = birthday.ButchRuntime ?? throw new InvalidOperationException(
                "Fallout 3 CG02 Butch runtime is absent.");
            var postIntercom = butch.PostIntercomRuntime ??
                throw new InvalidOperationException(
                    "Fallout 3 CG02 post-intercom runtime is absent.");
            var reactorGift = postIntercom.ReactorGiftRuntime ??
                throw new InvalidOperationException(
                    "Fallout 3 CG02 reactor-gift runtime is absent.");
            var picture = reactorGift.PictureRuntime;
            var pictureCompletion = picture.CompletionRuntime;
            var jonasGift = reactorGift.Participants.Single(value =>
                value.ReferenceFormId.Equals(postIntercom.JonasReferenceFormId,
                    StringComparison.OrdinalIgnoreCase));
            var dadGift = reactorGift.Participants.Single(value =>
                value.ReferenceFormId.Equals(postIntercom.DadReferenceFormId,
                    StringComparison.OrdinalIgnoreCase));
            if (current.ActiveQuestFormId.Equals(
                    pictureCompletion.NextQuestFormId,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (current.ActiveStage != pictureCompletion.NextQuestTargetStage ||
                    !current.Cg02AdultVaultSuitEquipped)
                    throw new InvalidOperationException(
                        "Fallout 3 CG03 completion handoff state differs.");
                _vaultBirthCoverage!.Cg01DadActor.Placement.Visible = false;
                _vaultBirthCoverage.Cg01DadActor.Placement.ProcessMode =
                    ProcessModeEnum.Disabled;
                var restoredBeatrice = Cg01WorldReference(
                    pictureCompletion.BeatriceReferenceFormId);
                restoredBeatrice.Visible = false;
                restoredBeatrice.ProcessMode = ProcessModeEnum.Disabled;
                player.SetMeta("opennv_pipboy_radio_on", false);
                player.SetMeta("opennv_inventory_cleared", 1);
                player.SetMeta(
                    $"opennv_cg03_item_{pictureCompletion.AdultVaultSuitFormId}", 1);
                player.SetMeta("opennv_equipped_item_form_id",
                    pictureCompletion.AdultVaultSuitFormId);
                player.SetMeta("opennv_age_race_delta", 1);
                if (current.Cg02SkillBookTransferred)
                    player.SetMeta(
                        $"opennv_reference_item_{pictureCompletion.NextDresserReferenceFormId}_" +
                        pictureCompletion.SkillBookFormId, 1);
                return;
            }
            player.SetMeta($"opennv_quest_stage_{current.ActiveQuestFormId}",
                current.ActiveStage);
            bool InfoAppliedAtStage(int stage) => birthday.Participants
                .SelectMany(value => value.Nodes.Values)
                .Where(node => current.AppliedInfoFormIds.Contains(
                    node.InfoFormId, StringComparer.OrdinalIgnoreCase))
                .SelectMany(node => node.Effects)
                .Any(effect => effect.Kind == "setStage" && effect.Stage == stage);
            bool InfoRemovedItem(string formId) => birthday.Participants
                .SelectMany(value => value.Nodes.Values)
                .Where(node => current.AppliedInfoFormIds.Contains(
                    node.InfoFormId, StringComparer.OrdinalIgnoreCase))
                .SelectMany(node => node.Effects)
                .Any(effect => effect.Kind == "removeItem" &&
                    effect.FormId.Equals(formId, StringComparison.OrdinalIgnoreCase));
            var sweetrollCount = InfoAppliedAtStage(butch.SourceStage) &&
                !InfoRemovedItem(butch.SweetrollFormId) ? 1 : 0;
            player.SetMeta($"opennv_cg02_item_{butch.SweetrollFormId}", sweetrollCount);

            void StartDadToIntercomTravel()
            {
                var target = postIntercom.DadToIntercomPackage.TargetTransform ??
                    throw new InvalidOperationException(
                        "Fallout 3 CG02 intercom package target is absent.");
                var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 CG02 intercom travel world is absent.");
                var local = coverage.Cg01DadActor.Placement.Transform.Origin -
                    Vector3.Up * coverage.Cg01DadGrounding.VerticalCorrectionGodotGameUnits;
                var sourceStart = coverage.Contract.EntryPositionGameUnits +
                    new Vector3(local.X, -local.Z, local.Y);
                var start = target with
                {
                    PositionGameUnits = new Fo3Cg01Vector3(
                        sourceStart.X, sourceStart.Y, sourceStart.Z),
                };
                var package = new Fo3Cg01DadTravelPackage(
                    new Fo3Cg01PostStage14Package(
                        postIntercom.DadToIntercomPackage.FormId,
                        postIntercom.DadToIntercomPackage.FormId,
                        postIntercom.DadToIntercomPackage.TargetFormId,
                        target,
                        postIntercom.DadToIntercomPackage.RadiusGameUnits,
                        null),
                    [], postIntercom.SourceStage, null, []);
                StartCg01DadSourceTravel(
                    interaction.TimerTransition.DadLead, package, start, stage5,
                    () => coverage.Cg01DadActor.Placement.SetMeta(
                        "opennv_active_package_form_id",
                        postIntercom.DadTalkToJonasPackage.FormId));
            }

            CellActorLoader.PlacedActor EnsureJonas()
            {
                if (_cg02IntroActors.TryGetValue(
                        postIntercom.JonasReferenceFormId, out var existing))
                    return existing;
                using var stream = File.OpenRead(postIntercom.JonasActorScenePath);
                var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (!hash.Equals(postIntercom.JonasActorSceneSha256,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Fallout 3 CG02 Jonas actor scene hash differs.");
                var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 CG02 Jonas world is absent.");
                var actor = CellActorLoader.Load(
                        postIntercom.JonasActorScenePath,
                        new HashSet<string>([coverage.Contract.CellFormId],
                            StringComparer.OrdinalIgnoreCase), coverage.CellRoot,
                        coverage.Contract.EntryPositionGameUnits,
                        _runtimeConfiguration, proofEnableInitiallyDisabled: false,
                        materializeInitiallyDisabled: true)
                    ?? throw new InvalidOperationException(
                        "Fallout 3 CG02 Jonas actor is absent.");
                if (actor.ReferenceFormId != postIntercom.JonasReferenceFormId ||
                    actor.BaseFormId != postIntercom.JonasBaseFormId)
                    throw new InvalidOperationException(
                        "Fallout 3 CG02 Jonas actor identity differs.");
                _cg02IntroActors.Add(actor.ReferenceFormId, actor);
                return actor;
            }

            void ApplyPostIntercomStage(int stage)
            {
                var commands = postIntercom.StageResults[stage];
                foreach (var command in commands)
                {
                    var target = string.IsNullOrEmpty(command.ReferenceFormId)
                        ? null
                        : _cg02IntroActors.TryGetValue(command.ReferenceFormId,
                            out var actor) ? actor.Placement
                        : Cg01WorldReference(command.ReferenceFormId);
                    switch (command.Kind)
                    {
                        case "setQuestVariable":
                            player.SetMeta($"opennv_cg02_{command.Variable.ToLowerInvariant()}",
                                command.Value);
                            break;
                        case "evaluatePackage":
                            target!.SetMeta("opennv_evaluate_package", 1);
                            break;
                        case "clearTalkingActivatorActor":
                            target!.SetMeta("opennv_talking_activator_actor", "");
                            break;
                        case "enable":
                            target!.Visible = true;
                            target.ProcessMode = ProcessModeEnum.Inherit;
                            target.SetMeta("opennv_enabled", 1);
                            break;
                        case "ignoreCrime":
                            target!.SetMeta("opennv_ignore_crime", command.Value);
                            break;
                        case "setObjectiveDisplayed":
                            player.SetMeta("opennv_cg02_objective_displayed",
                                command.ObjectiveIndex);
                            break;
                        case "setObjectiveCompleted":
                            player.SetMeta("opennv_cg02_objective_completed",
                                command.ObjectiveIndex);
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Fallout 3 CG02 post-intercom command is unsupported: {command.Kind}");
                    }
                }
                current = current with
                {
                    ActiveStage = stage,
                    DisplayedObjectiveIndex = commands
                        .Where(value => value.Kind == "setObjectiveDisplayed" &&
                            value.Value != 0)
                        .Select(value => value.ObjectiveIndex)
                        .DefaultIfEmpty(current.DisplayedObjectiveIndex).Last(),
                    AccountedCommandCount = current.AccountedCommandCount + commands.Count,
                    AppliedCommandCount = current.AppliedCommandCount + commands.Count,
                    NextBoundary = new Fo3Cg01Stage12Boundary(false,
                        stage == postIntercom.TargetStage
                            ? postIntercom.NextBoundaryBlocker
                            : birthday.NextBoundaryBlocker),
                };
                player.SetMeta("opennv_cg02_stage", stage);
                player.SetMeta($"opennv_quest_stage_{current.ActiveQuestFormId}", stage);
                Persist();
            }

            void PlayPostIntercomCue(Fo3Cg02PostIntercomCue cue, Action completed)
            {
                if (current.AppliedInfoFormIds.Contains(
                        cue.InfoFormId, StringComparer.OrdinalIgnoreCase))
                {
                    completed();
                    return;
                }
                var speaker = cue.SpeakerBaseFormId.Equals(
                        postIntercom.JonasBaseFormId, StringComparison.OrdinalIgnoreCase)
                    ? EnsureJonas()
                    : _vaultBirthCoverage!.Cg01DadActor;
                GamebryoDialoguePlayback.ValidateOrderedLines(cue.Responses.Select(
                    response => new SourceDialogueLine(cue.InfoFormId, response.Index,
                        cue.SpeakerBaseFormId, response.Text,
                        new SourceDialogueAsset(response.Voice.LogicalPath,
                            response.Voice.SourcePath, response.Voice.Sha256),
                        new SourceDialogueAsset(response.Lip.LogicalPath,
                            response.Lip.SourcePath, response.Lip.Sha256))).ToArray());
                PlayLine(0);
                void PlayLine(int index)
                {
                    if (index == cue.Responses.Count)
                    {
                        current = current with
                        {
                            AppliedInfoFormIds =
                                current.AppliedInfoFormIds.Append(cue.InfoFormId).ToArray(),
                            AccountedCommandCount = current.AccountedCommandCount +
                                (cue.TargetStage is null ? 0 : 1),
                            AppliedCommandCount = current.AppliedCommandCount +
                                (cue.TargetStage is null ? 0 : 1),
                        };
                        if (cue.TargetStage is { } targetStage)
                            ApplyPostIntercomStage(targetStage);
                        else
                            Persist();
                        completed();
                        return;
                    }
                    var response = cue.Responses[index];
                    var voice = new AudioStreamPlayer
                    {
                        Name = $"Fallout3Cg02PostIntercomVoice{cue.InfoFormId}_{response.Index}",
                    };
                    AddChild(voice);
                    var dialogue = new GamebryoDialoguePlayback(
                        voice, _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
                    _cg02IntroDialogue.Add(dialogue);
                    dialogue.Start(new SourceDialogueLine(cue.InfoFormId, response.Index,
                            cue.SpeakerBaseFormId, response.Text,
                            new SourceDialogueAsset(response.Voice.LogicalPath,
                                response.Voice.SourcePath, response.Voice.Sha256),
                            new SourceDialogueAsset(response.Lip.LogicalPath,
                                response.Lip.SourcePath, response.Lip.Sha256)),
                        new FaceGenMorphController(speaker.Actor,
                            _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip),
                        () => PlayLine(index + 1));
                }
            }

            void ActivateIntercom()
            {
                if (current.ActiveStage != postIntercom.SourceStage)
                    return;
                var sex = (_selectedSex ?? throw new InvalidOperationException(
                    "Fallout 3 CG02 post-intercom player sex is absent.")).EngineSex;
                current = current with
                {
                    AppliedPackageFormIds =
                    current.AppliedPackageFormIds.Contains(
                        postIntercom.DadTalkToJonasPackage.FormId,
                        StringComparer.OrdinalIgnoreCase)
                        ? current.AppliedPackageFormIds
                        : current.AppliedPackageFormIds.Append(
                            postIntercom.DadTalkToJonasPackage.FormId).ToArray()
                };
                var dadCall = postIntercom.Cues.Single(value =>
                    value.TargetStage == postIntercom.AnswerStage);
                var jonasReply = postIntercom.Cues.Single(value =>
                    value.SpeakerBaseFormId.Equals(postIntercom.JonasBaseFormId,
                        StringComparison.OrdinalIgnoreCase));
                var goodbye = postIntercom.Cues.Single(value => value.EngineSex == sex);
                PlayPostIntercomCue(dadCall, () => PlayPostIntercomCue(jonasReply,
                    () => PlayPostIntercomCue(goodbye, () =>
                    {
                        var dad = _vaultBirthCoverage!.Cg01DadActor.Placement;
                        dad.SetMeta("opennv_active_package_form_id",
                            postIntercom.DadToPlayerPackage.FormId);
                        current = current with
                        {
                            AppliedPackageFormIds =
                            current.AppliedPackageFormIds.Append(
                                postIntercom.DadToPlayerPackage.FormId).ToArray()
                        };
                        Persist();
                    })));
            }

            void ActivateDadPostIntercom()
            {
                if (current.ActiveStage != postIntercom.GoodbyeStage)
                    return;
                var greeting = postIntercom.Cues.Single(value =>
                    value.TargetStage == postIntercom.TargetStage);
                PlayPostIntercomCue(greeting, () => { });
            }

            void ExecuteReactorGiftStageCommands(int stage)
            {
                var commands = reactorGift.StageResults[stage];
                foreach (var command in commands)
                {
                    switch (command.Kind)
                    {
                        case "removeItem":
                            (_cg02IntroActors.TryGetValue(command.ReferenceFormId,
                                out var removeActor) ? removeActor.Placement :
                                _vaultBirthCoverage!.Cg01DadActor.Placement).SetMeta(
                                    $"opennv_item_{command.ItemFormId}", 0);
                            break;
                        case "moveToReference":
                            {
                                var source = command.TargetTransform ??
                                    throw new InvalidOperationException(
                                        "Fallout 3 CG02 reactor-gift move target is absent.");
                                var package = new Fo3Cg01PostStage14Package(
                                    command.TargetFormId, command.TargetFormId,
                                    command.TargetFormId, source, 0, null);
                                var coverage = _vaultBirthCoverage!;
                                var placement = Cg01DadPackagePlacement(
                                    package, stage5, coverage);
                                GamebryoPackageTravel.ArriveAtSourceTarget(
                                    command.TargetFormId, placement,
                                    coverage.Cg01DadActor.Placement.Transform,
                                    GamebryoPackageTravel.ExactArrivalToleranceCellUnits)
                                    .Publish(coverage.Cg01DadActor.Placement);
                                break;
                            }
                        case "setOpenState":
                            SetCg01WorldReferenceOpen(
                                command.ReferenceFormId, command.Value != 0);
                            break;
                        case "lock":
                            SetCg01WorldReferenceLock(
                                command.ReferenceFormId, command.Value);
                            break;
                        case "addItem":
                            player.SetMeta($"opennv_cg02_item_{command.ItemFormId}",
                                player.GetMeta(
                                    $"opennv_cg02_item_{command.ItemFormId}", 0).AsInt32() +
                                command.Count);
                            break;
                        case "equipItem":
                            player.SetMeta("opennv_equipped_item_form_id",
                                command.ItemFormId);
                            break;
                        case "unlock":
                            SetCg01WorldReferenceLock(command.ReferenceFormId, 0);
                            break;
                        case "enablePlayerControls":
                            player.SetMeta("opennv_enabled_player_controls",
                                string.Join(',', command.Arguments));
                            break;
                        case "setObjectiveCompleted":
                            player.SetMeta("opennv_cg02_objective_completed",
                                command.ObjectiveIndex);
                            break;
                        case "setObjectiveDisplayed":
                            player.SetMeta("opennv_cg02_objective_displayed",
                                command.ObjectiveIndex);
                            break;
                        case "setStage":
                            player.SetMeta("opennv_tutorial_quest_form_id",
                                command.QuestFormId);
                            player.SetMeta("opennv_tutorial_stage", command.Stage);
                            break;
                        case "enable":
                            {
                                var enabled = Cg01WorldReference(command.ReferenceFormId);
                                enabled.Visible = true;
                                enabled.ProcessMode = ProcessModeEnum.Inherit;
                                enabled.SetMeta("opennv_enabled", 1);
                                break;
                            }
                        case "evaluatePackage":
                            (_cg02IntroActors.TryGetValue(command.ReferenceFormId,
                                out var packageActor) ? packageActor.Placement :
                                Cg01WorldReference(command.ReferenceFormId)).SetMeta(
                                    "opennv_evaluate_package", 1);
                            break;
                        case "setQuestObject":
                            player.SetMeta(
                                $"opennv_quest_object_{command.ItemFormId}",
                                command.Value);
                            break;
                        default:
                            throw new InvalidOperationException(
                                $"Fallout 3 CG02 reactor-gift command is unsupported: {command.Kind}");
                    }
                }
            }

            void ApplyReactorGiftStage(int stage)
            {
                var commands = reactorGift.StageResults.TryGetValue(stage,
                    out var preparedCommands) ? preparedCommands : [];
                if (commands.Count != 0)
                    ExecuteReactorGiftStageCommands(stage);
                IReadOnlyList<string> packages = stage switch
                {
                    var value when value == reactorGift.JonasStage =>
                        [reactorGift.JonasGreetPackageFormId],
                    var value when value == reactorGift.TargetStage =>
                        [reactorGift.DadGreetPackageFormId,
                         reactorGift.DadToRangePackageFormId,
                         reactorGift.JonasWaitPackageFormId],
                    var value when value == reactorGift.RangeStage =>
                        [reactorGift.DadWaitPackageFormId],
                    var value when value == reactorGift.HitStage => [],
                    var value when value == reactorGift.CombatStage =>
                        [reactorGift.Combatant.PackageFormId],
                    var value when value == reactorGift.DeathStage => [],
                    var value when value == reactorGift.CompletionStage => [],
                    _ => throw new InvalidOperationException(
                        "Fallout 3 CG02 reactor-gift stage differs."),
                };
                current = current with
                {
                    ActiveStage = stage,
                    AppliedPackageFormIds = current.AppliedPackageFormIds
                        .Concat(packages).ToArray(),
                    DisplayedObjectiveIndex = commands
                        .Where(value => value.Kind == "setObjectiveDisplayed" &&
                            value.Value != 0)
                        .Select(value => value.ObjectiveIndex)
                        .DefaultIfEmpty(current.DisplayedObjectiveIndex).Last(),
                    AccountedCommandCount = current.AccountedCommandCount +
                        commands.Count + 1 + (stage == reactorGift.CompletionStage
                            ? picture.SourceStageCommandCount : 0),
                    AppliedCommandCount = current.AppliedCommandCount +
                        commands.Count + 1 + (stage == reactorGift.CompletionStage
                            ? picture.SourceStageCommandCount : 0),
                    NextBoundary = new Fo3Cg01Stage12Boundary(false,
                        stage == reactorGift.CompletionStage
                            ? reactorGift.NextBoundaryBlocker
                            : postIntercom.NextBoundaryBlocker),
                };
                player.SetMeta("opennv_cg02_stage", stage);
                if (stage == reactorGift.CompletionStage)
                {
                    player.SetMeta("opennv_cg02_objective_displayed",
                        picture.ObjectiveIndex);
                    player.SetMeta($"opennv_quest_stage_{current.ActiveQuestFormId}",
                        stage);
                }
                if (stage == reactorGift.CombatStage &&
                    !current.CombatHealthByReferenceFormId.ContainsKey(
                        reactorGift.Combatant.ReferenceFormId))
                {
                    current = current with
                    {
                        CombatHealthByReferenceFormId =
                            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                            {
                                [reactorGift.Combatant.ReferenceFormId] =
                                    reactorGift.Combatant.MaximumHealth,
                            },
                    };
                    var combatant = Cg01WorldReference(
                        reactorGift.Combatant.ReferenceFormId);
                    combatant.SetMeta("opennv_active_package_form_id",
                        reactorGift.Combatant.PackageFormId);
                    combatant.SetMeta("opennv_package_target_form_id",
                        reactorGift.Combatant.PackageTargetFormId);
                    combatant.SetMeta("opennv_package_radius_game_units",
                        reactorGift.Combatant.PackageRadiusGameUnits);
                    combatant.SetMeta("opennv_current_health",
                        reactorGift.Combatant.MaximumHealth);
                }
                Persist();
                if (stage == reactorGift.HitStage)
                    StartReactorGiftParticipant(dadGift);
            }

            void StartReactorGiftParticipant(Fo3Cg02BirthdayParticipant participant)
            {
                StartCg02BirthdayInteraction(
                    participant, player, (infoFormId, targetStage) =>
                    {
                        if (current.AppliedInfoFormIds.Contains(
                                infoFormId, StringComparer.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                "Fallout 3 CG02 reactor-gift INFO replay differs.");
                        current = current with
                        {
                            AppliedInfoFormIds = current.AppliedInfoFormIds
                                .Append(infoFormId).ToArray(),
                            AccountedCommandCount = current.AccountedCommandCount +
                                participant.Nodes[infoFormId].Effects.Count(value =>
                                    value.Kind != "setStage"),
                            AppliedCommandCount = current.AppliedCommandCount +
                                participant.Nodes[infoFormId].Effects.Count(value =>
                                    value.Kind != "setStage"),
                        };
                        if (targetStage is { } stage)
                            ApplyReactorGiftStage(stage);
                        else
                            Persist();
                    });
            }

            void CompletePictureSequence()
            {
                var transferredBook = player.GetMeta(
                    $"opennv_cg02_item_{pictureCompletion.SkillBookFormId}", 0)
                    .AsInt32() > 0;
                _vaultBirthCoverage!.Cg01DadActor.Placement.Visible = false;
                _vaultBirthCoverage.Cg01DadActor.Placement.ProcessMode =
                    ProcessModeEnum.Disabled;
                var beatrice = Cg01WorldReference(
                    pictureCompletion.BeatriceReferenceFormId);
                beatrice.Visible = false;
                beatrice.ProcessMode = ProcessModeEnum.Disabled;
                player.ConfigureSourceFormActivations(null);
                player.ClearSourceHitscan();
                player.SetMeta("opennv_pipboy_radio_on", false);
                player.SetMeta("opennv_inventory_cleared", 1);
                player.SetMeta(
                    $"opennv_cg02_item_{pictureCompletion.SkillBookFormId}", 0);
                if (transferredBook)
                    player.SetMeta(
                        $"opennv_reference_item_{pictureCompletion.NextDresserReferenceFormId}_" +
                        pictureCompletion.SkillBookFormId, 1);
                player.SetMeta(
                    $"opennv_cg03_item_{pictureCompletion.AdultVaultSuitFormId}", 1);
                player.SetMeta("opennv_equipped_item_form_id",
                    pictureCompletion.AdultVaultSuitFormId);
                player.SetMeta("opennv_age_race_delta", 1);
                player.MoveToSourceTransform(
                    pictureCompletion.NextQuestStartTransform,
                    _vaultBirthCoverage.Contract);
                current = current with
                {
                    ActiveQuestFormId = pictureCompletion.NextQuestFormId,
                    ActiveQuestEditorId = pictureCompletion.NextQuestEditorId,
                    ActiveStage = pictureCompletion.NextQuestTargetStage,
                    TimerRemainingSeconds = 0.0,
                    TimerAdvancing = false,
                    Cg02PictureImageSpaceElapsedSeconds =
                        pictureCompletion.ImageSpaceModifier.DurationSeconds,
                    Cg02PictureSoundStarted = true,
                    PlayerMovementEnabled = false,
                    Cg02SkillBookTransferred = transferredBook,
                    Cg02AdultVaultSuitEquipped = true,
                    AccountedCommandCount = current.AccountedCommandCount +
                        pictureCompletion.Stage100CommandCount +
                        pictureCompletion.NextQuestStage0CommandCount,
                    AppliedCommandCount = current.AppliedCommandCount +
                        pictureCompletion.Stage100CommandCount +
                        pictureCompletion.NextQuestStage0CommandCount,
                    NextBoundary = new Fo3Cg01Stage12Boundary(
                        false, pictureCompletion.NextBoundaryBlocker),
                };
                player.SetMeta("opennv_active_quest_form_id",
                    pictureCompletion.NextQuestFormId);
                player.SetMeta("opennv_cg03_stage",
                    pictureCompletion.NextQuestTargetStage);
                player.SetMeta(
                    $"opennv_quest_stage_{pictureCompletion.NextQuestFormId}",
                    pictureCompletion.NextQuestTargetStage);
                Persist();
            }

            void CompletionProgress(Fo3Cg02CompletionProgress progress)
            {
                var stageChanged = current.ActiveStage != progress.Stage;
                current = current with
                {
                    ActiveStage = progress.Stage,
                    TimerRemainingSeconds = progress.TimerRemainingSeconds,
                    TimerAdvancing = progress.TimerAdvancing,
                    Cg02PictureImageSpaceElapsedSeconds =
                        progress.ImageSpaceElapsedSeconds,
                    Cg02PictureSoundStarted = progress.SoundStarted,
                    AccountedCommandCount = current.AccountedCommandCount +
                        (stageChanged ? pictureCompletion.Stage98CommandCount : 0),
                    AppliedCommandCount = current.AppliedCommandCount +
                        (stageChanged ? pictureCompletion.Stage98CommandCount : 0),
                };
                player.SetMeta("opennv_cg02_stage", progress.Stage);
                player.SetMeta("opennv_cg02_timer", progress.TimerRemainingSeconds);
                player.SetMeta("opennv_cg02_run_timer",
                    progress.TimerAdvancing ? 1 : 0);
                Persist();
            }

            void StartPictureCompletion()
            {
                StartCg02CompletionTimer(
                    pictureCompletion, current.ActiveStage,
                    current.TimerRemainingSeconds,
                    current.Cg02PictureImageSpaceElapsedSeconds,
                    current.Cg02PictureSoundStarted,
                    CompletionProgress, CompletePictureSequence);
            }

            void StartPictureJonas()
            {
                if (current.AppliedInfoFormIds.Contains(
                        picture.JonasInfoFormId, StringComparer.OrdinalIgnoreCase))
                    return;
                StartCg02BirthdayInteraction(jonasGift, player,
                    (infoFormId, targetStage) =>
                    {
                        if (targetStage is not null || !infoFormId.Equals(
                                picture.JonasInfoFormId,
                                StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                "Fallout 3 CG02 picture Jonas result differs.");
                        current = current with
                        {
                            ActiveStage = picture.TimerStage,
                            AppliedInfoFormIds = current.AppliedInfoFormIds
                                .Append(infoFormId).ToArray(),
                            TimerRemainingSeconds =
                                pictureCompletion.Stage95TimerSeconds,
                            TimerAdvancing = true,
                            AccountedCommandCount = current.AccountedCommandCount + 1 +
                                pictureCompletion.Stage95CommandCount,
                            AppliedCommandCount = current.AppliedCommandCount + 1 +
                                pictureCompletion.Stage95CommandCount,
                            NextBoundary = new Fo3Cg01Stage12Boundary(
                                false, picture.NextBoundaryBlocker),
                        };
                        player.SetMeta("opennv_cg02_stage", picture.TimerStage);
                        player.SetMeta(
                            $"opennv_quest_stage_{current.ActiveQuestFormId}",
                            picture.TimerStage);
                        player.SetMeta("opennv_objectives_completed", true);
                        player.SetMeta("opennv_equipped_item_form_id", "");
                        player.SetMeta("opennv_cg02_timer",
                            pictureCompletion.Stage95TimerSeconds);
                        player.SetMeta("opennv_cg02_run_timer", 1);
                        Persist();
                        StartPictureCompletion();
                    });
            }

            void ApplyPictureStage()
            {
                if (current.ActiveStage != picture.SourceStage)
                    return;
                player.StopAtAuthoredTrigger();
                _vaultBirthCoverage!.Cg01DadActor.Placement.SetMeta(
                    "opennv_dotalk", picture.PictureDadTalkValue);
                current = current with
                {
                    ActiveStage = picture.PictureStage,
                    PlayerMovementEnabled = false,
                    DisplayedObjectiveIndex = picture.ObjectiveIndex,
                    AccountedCommandCount = current.AccountedCommandCount +
                        picture.PictureStageCommandCount,
                    AppliedCommandCount = current.AppliedCommandCount +
                        picture.PictureStageCommandCount,
                    NextBoundary = new Fo3Cg01Stage12Boundary(
                        false, picture.NextBoundaryBlocker),
                };
                player.SetMeta("opennv_cg02_objective_completed",
                    picture.ObjectiveIndex);
                player.SetMeta("opennv_cg02_stage", picture.PictureStage);
                player.SetMeta($"opennv_quest_stage_{current.ActiveQuestFormId}",
                    picture.PictureStage);
                Persist();
                StartPictureJonas();
            }

            void PicturePackageCompleted(string packageFormId)
            {
                if (current.AppliedPackageFormIds.Contains(
                        packageFormId, StringComparer.OrdinalIgnoreCase))
                    return;
                var package = picture.Packages.Single(value =>
                    value.FormId.Equals(packageFormId,
                        StringComparison.OrdinalIgnoreCase));
                var actor = package.ActorReferenceFormId.Equals(
                        postIntercom.DadReferenceFormId,
                        StringComparison.OrdinalIgnoreCase)
                    ? _vaultBirthCoverage!.Cg01DadActor.Placement
                    : EnsureJonas().Placement;
                actor.SetMeta("opennv_picture_ready", 1);
                if (package.ActorReferenceFormId.Equals(
                        postIntercom.DadReferenceFormId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    actor.SetMeta("opennv_dotalk", picture.DadTalkValue);
                    actor.SetMeta("opennv_timer", picture.DadTimerSeconds);
                }
                current = current with
                {
                    AppliedPackageFormIds = current.AppliedPackageFormIds
                        .Append(package.FormId).ToArray(),
                    AccountedCommandCount = current.AccountedCommandCount +
                        package.CompletionCommandCount,
                    AppliedCommandCount = current.AppliedCommandCount +
                        package.CompletionCommandCount,
                };
                Persist();
            }

            void StartPicturePositioning()
            {
                var dad = _vaultBirthCoverage!.Cg01DadActor;
                var jonas = EnsureJonas();
                StartCg02PicturePositioning(
                    picture, interaction.TimerTransition.DadLead, player,
                    dad, jonas, () => current.AppliedPackageFormIds,
                    PicturePackageCompleted, ApplyPictureStage);
                if (!current.AppliedInfoFormIds.Contains(
                        picture.DadInfoFormId, StringComparer.OrdinalIgnoreCase))
                    StartReactorGiftParticipant(dadGift);
            }

            void ApplyTargetHit(string targetReferenceFormId)
            {
                if (current.ActiveStage != reactorGift.RangeStage ||
                    current.Cg02TargetHitFormIds.Count >= reactorGift.RequiredHitCount)
                    return;
                var target = Cg01WorldReference(targetReferenceFormId);
                target.SetMeta("opennv_animation_group",
                    reactorGift.TargetAnimationGroup);
                current = current with
                {
                    Cg02TargetHitFormIds = current.Cg02TargetHitFormIds
                        .Append(targetReferenceFormId).ToArray(),
                };
                player.SetMeta("opennv_cg02_target_count",
                    current.Cg02TargetHitFormIds.Count);
                player.SetMeta("opennv_tutorial_stage",
                    reactorGift.TutorialHitStage);
                if (current.Cg02TargetHitFormIds.Count == reactorGift.RequiredHitCount)
                    ApplyReactorGiftStage(reactorGift.HitStage);
                else
                    Persist();
            }

            void ApplyCombatHit()
            {
                if (current.ActiveStage != reactorGift.CombatStage ||
                    !current.CombatHealthByReferenceFormId.TryGetValue(
                        reactorGift.Combatant.ReferenceFormId, out var health))
                    return;
                var outcome = GamebryoRangedCombat.ApplyHit(
                    new GamebryoRangedAttack(
                        reactorGift.Combatant.WeaponFormId,
                        reactorGift.Combatant.AmmunitionFormId,
                        reactorGift.Combatant.WeaponDamage),
                    player.GetMeta("opennv_equipped_item_form_id", "").AsString(),
                    new GamebryoCombatantState(
                        reactorGift.Combatant.ReferenceFormId,
                        reactorGift.Combatant.MaximumHealth,
                        health,
                        current.DeadCombatReferenceFormIds.Contains(
                            reactorGift.Combatant.ReferenceFormId,
                            StringComparer.OrdinalIgnoreCase)));
                current = current with
                {
                    CombatHealthByReferenceFormId =
                        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                        {
                            [outcome.Target.ReferenceFormId] =
                                outcome.Target.CurrentHealth,
                        },
                    DeadCombatReferenceFormIds = outcome.Target.Dead
                        ? [outcome.Target.ReferenceFormId]
                        : current.DeadCombatReferenceFormIds,
                };
                var combatant = Cg01WorldReference(outcome.Target.ReferenceFormId);
                combatant.SetMeta("opennv_combat_target_form_id",
                    reactorGift.Combatant.PlayerReferenceFormId);
                combatant.SetMeta("opennv_current_health", outcome.Target.CurrentHealth);
                combatant.SetMeta("opennv_dead", outcome.Target.Dead ? 1 : 0);
                if (outcome.Died)
                    ApplyReactorGiftStage(reactorGift.DeathStage);
                else
                    Persist();
            }

            void ApplyStage35()
            {
                foreach (var command in butch.Stage35Commands)
                {
                    if (command.Kind == "evaluatePackage")
                        (_cg02IntroActors.TryGetValue(command.ReferenceFormId,
                            out var packageActor) ? packageActor.Placement :
                            Cg01WorldReference(command.ReferenceFormId))
                            .SetMeta("opennv_evaluate_package", 1);
                    else if (command.Kind == "setTalkingActivatorActor")
                        Cg01WorldReference(command.ReferenceFormId).SetMeta(
                            "opennv_talking_activator_actor",
                            command.ActorReferenceFormId);
                    else if (command.Kind == "setQuestVariable")
                        player.SetMeta(
                            $"opennv_cg02_{command.Variable.ToLowerInvariant()}",
                            command.Value);
                    else
                        throw new InvalidOperationException(
                            $"Fallout 3 CG02 stage-35 command is unsupported: " +
                            command.Kind);
                }
                current = current with
                {
                    ActiveStage = butch.IntercomStage,
                    TimerRemainingSeconds = 0.0,
                    TimerAdvancing = false,
                    AccountedCommandCount = current.AccountedCommandCount +
                        butch.Stage35Commands.Count,
                    AppliedCommandCount = current.AppliedCommandCount +
                        butch.Stage35Commands.Count,
                    AppliedPackageFormIds = current.AppliedPackageFormIds.Append(
                        postIntercom.DadToIntercomPackage.FormId).ToArray(),
                };
                player.SetMeta("opennv_cg02_stage", butch.IntercomStage);
                _cg02ButchTimerTick = null;
                Persist();
                EnsureJonas();
                var dad = _vaultBirthCoverage!.Cg01DadActor.Placement;
                dad.SetMeta("opennv_active_package_form_id",
                    postIntercom.DadToIntercomPackage.FormId);
                StartDadToIntercomTravel();
            }
            void StartIntercomTimer(double remainingSeconds)
            {
                if (_cg02ButchTimerTick is not null)
                    return;
                _cg02ButchTimerTick = delta =>
                {
                    var remaining = Math.Max(
                        0.0, current.TimerRemainingSeconds - delta);
                    current = current with { TimerRemainingSeconds = remaining };
                    if (remaining > 0.0)
                    {
                        Persist();
                        return;
                    }
                    ApplyStage35();
                };
                current = current with
                {
                    TimerRemainingSeconds = remainingSeconds,
                    TimerAdvancing = true,
                };
                player.SetMeta("opennv_cg02_timer", remainingSeconds);
                Persist();
            }
            void CakeStageChanged(int stage, string? packageFormId)
            {
                if (current.ActiveStage == stage)
                    return;
                var commandCount = stage == cake.TriggerStage
                    ? cake.Stage15CommandCount + 1
                    : cake.PackageResultCommandCount + cake.Stage16CommandCount;
                current = current with
                {
                    ActiveStage = stage,
                    AppliedPackageFormIds = packageFormId is null
                        ? current.AppliedPackageFormIds
                        : current.AppliedPackageFormIds.Append(packageFormId).ToArray(),
                    AccountedCommandCount = current.AccountedCommandCount + commandCount,
                    AppliedCommandCount = current.AppliedCommandCount + commandCount,
                    NextBoundary = new Fo3Cg01Stage12Boundary(
                        false, cake.NextBoundaryBlocker),
                };
                player.SetMeta("opennv_cg02_stage", stage);
                Persist();
            }
            void CakeCueCompleted(Fo3Cg02CakeCue cue)
            {
                if (current.AppliedInfoFormIds.Contains(
                        cue.InfoFormId, StringComparer.OrdinalIgnoreCase))
                    return;
                current = current with
                {
                    AppliedInfoFormIds = current.AppliedInfoFormIds
                        .Append(cue.InfoFormId).ToArray(),
                    AccountedCommandCount = current.AccountedCommandCount +
                        cue.Effects.Count,
                    AppliedCommandCount = current.AppliedCommandCount +
                        cue.Effects.Count,
                };
                Persist();
            }
            void StartCake() => StartCg02CakeRuntime(
                cake, player, CakeStageChanged, CakeCueCompleted,
                current.AppliedInfoFormIds,
                current.AppliedPackageFormIds.Contains(
                    cake.PackageFormId, StringComparer.OrdinalIgnoreCase));
            var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
                "Fallout 3 CG02 cake trigger world is absent.");
            var triggerName = $"SOURCE_TRIGGER_{cake.TriggerReferenceFormId}";
            if (!coverage.CellRoot.HasNode(triggerName))
            {
                var source = cake.TriggerTransform;
                var trigger = new Area3D
                {
                    Name = triggerName,
                    Position = GamebryoCoordinate.ConvertVector(
                        new Vector3((float)source.PositionGameUnits.X,
                            (float)source.PositionGameUnits.Y,
                            (float)source.PositionGameUnits.Z) -
                        coverage.Contract.EntryPositionGameUnits),
                    Rotation = new Vector3(0.0f, -(float)source.RotationRadians.Z, 0.0f),
                    Scale = Vector3.One * (float)source.Scale,
                    CollisionLayer = 0,
                    CollisionMask = player.SourceBodyCollisionLayer,
                    Monitoring = true,
                };
                trigger.SetMeta("opennv_source_form_id", cake.TriggerReferenceFormId);
                trigger.AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D
                    {
                        Size = new Vector3(
                            (float)cake.TriggerDimensionsGameUnits.X,
                            (float)cake.TriggerDimensionsGameUnits.Z,
                            (float)cake.TriggerDimensionsGameUnits.Y),
                    },
                });
                trigger.BodyEntered += body =>
                {
                    if (body == player && _cg02CakePackageTick is null &&
                        !current.AppliedPackageFormIds.Contains(
                            cake.PackageFormId, StringComparer.OrdinalIgnoreCase))
                        StartCake();
                };
                coverage.CellRoot.AddChild(trigger);
            }
            foreach (var participant in birthday.Participants)
            {
                var actor = EnsureCg02BirthdayActor(participant);
                var bodyName = $"SOURCE_ACTIVATION_{participant.ReferenceFormId}";
                if (actor.Placement.HasNode(bodyName))
                    continue;
                var bounds = actor.Actor.Bounds;
                var body = new StaticBody3D
                {
                    Name = bodyName,
                    Position = bounds.GetCenter(),
                    CollisionLayer = player.SourceActivationCollisionLayer,
                    CollisionMask = 0,
                };
                body.SetMeta("opennv_source_form_id", participant.ReferenceFormId);
                body.AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D { Size = bounds.Size },
                });
                actor.Placement.AddChild(body);
            }
            foreach (var effect in birthday.Participants
                .SelectMany(value => value.Nodes.Values)
                .Where(node => current.AppliedInfoFormIds.Contains(
                    node.InfoFormId, StringComparer.OrdinalIgnoreCase))
                .SelectMany(node => node.Effects))
            {
                if (effect.Kind == "setQuestVariable")
                    player.SetMeta(
                        $"opennv_cg02_{effect.Variable.ToLowerInvariant()}",
                        effect.Value);
                else if (effect.Kind == "setActorVariable")
                    _cg02IntroActors[effect.ReferenceFormId].Placement.SetMeta(
                        $"opennv_{effect.Variable.ToLowerInvariant()}", effect.Value);
                else if (effect.Kind == "evaluatePackage")
                    (_cg02IntroActors.TryGetValue(effect.ReferenceFormId,
                        out var packageActor) ? packageActor.Placement :
                        Cg01WorldReference(effect.ReferenceFormId))
                        .SetMeta("opennv_evaluate_package", 1);
                else if (effect.Kind == "startCombat")
                {
                    EnsureCg02BirthdayActor(birthday.Participants.Single(value =>
                        value.ReferenceFormId.Equals(butch.ReferenceFormId,
                            StringComparison.OrdinalIgnoreCase))).Placement.SetMeta(
                        "opennv_combat_target", effect.Target);
                    (_cg02IntroActors.TryGetValue(effect.ReferenceFormId,
                        out var responder) ? responder.Placement :
                        Cg01WorldReference(effect.ReferenceFormId))
                        .SetMeta("opennv_evaluate_package", 1);
                    player.SetMeta("opennv_cg02_combat_runtime_blocker",
                        butch.NextBoundaryBlocker);
                }
            }
            void StartButchPackageIfEligible()
            {
                var butchActor = EnsureCg02BirthdayActor(
                    birthday.Participants.Single(value =>
                        value.ReferenceFormId.Equals(butch.ReferenceFormId,
                            StringComparison.OrdinalIgnoreCase)));
                var eligible = current.AppliedPackageFormIds.Contains(
                        cake.PackageFormId, StringComparer.OrdinalIgnoreCase) &&
                    InfoAppliedAtStage(butch.SourceStage) &&
                    current.ActiveStage != butch.SceneDoneStage &&
                    current.ActiveStage != butch.IntercomStage;
                if (!eligible)
                    return;
                butchActor.Placement.SetMeta(
                    "opennv_active_package_form_id", butch.FindPlayerPackageFormId);
                if (!current.AppliedPackageFormIds.Contains(
                        butch.FindPlayerPackageFormId,
                        StringComparer.OrdinalIgnoreCase))
                {
                    current = current with
                    {
                        AppliedPackageFormIds = current.AppliedPackageFormIds
                            .Append(butch.FindPlayerPackageFormId).ToArray(),
                    };
                    Persist();
                }
                if (current.AppliedPackageFormIds.Count(value => value.Equals(
                        butch.FindPlayerPackageFormId,
                        StringComparison.OrdinalIgnoreCase)) > 1)
                    return;
                _cg02ButchPackageTick ??= _ =>
                {
                    if (butchActor.Placement.GlobalPosition.DistanceTo(
                            player.GlobalPosition) >
                        butch.FindPlayerRadiusGameUnits *
                            _runtimeConfiguration.World.GameUnitsToMeters)
                        return;
                    _cg02ButchPackageTick = null;
                    var paul = birthday.Participants.Single(value =>
                        value.DisplayName.Equals("Paul Hannon",
                            StringComparison.OrdinalIgnoreCase));
                    EnsureCg02BirthdayActor(paul).Placement.SetMeta(
                        "opennv_evaluate_package", 1);
                    current = current with
                    {
                        AppliedPackageFormIds = current.AppliedPackageFormIds
                            .Append(butch.FindPlayerPackageFormId).ToArray(),
                        AccountedCommandCount = current.AccountedCommandCount +
                            butch.FindPlayerResultCommandCount,
                        AppliedCommandCount = current.AppliedCommandCount +
                            butch.FindPlayerResultCommandCount,
                    };
                    Persist();
                };
            }
            var activations = birthday.Participants.ToDictionary(
                participant => participant.ReferenceFormId,
                participant => (Action)(() => StartCg02BirthdayInteraction(
                    participant,
                    player,
                    (infoFormId, targetStage) =>
                    {
                        if (current.AppliedInfoFormIds.Contains(
                                infoFormId, StringComparer.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                "Fallout 3 CG02 birthday INFO replay differs.");
                        var completedNode = participant.Nodes[infoFormId];
                        var appliedCommands = completedNode.Effects.Count(effect =>
                            effect.Kind != "sourceConditional");
                        int? effectiveStage = targetStage;
                        if (targetStage is not null)
                        {
                            if (targetStage == cake.TriggerStage)
                                StartCake();
                            else if (birthday.StageResults.TryGetValue(
                                targetStage.Value, out var result))
                            {
                                player.SetMeta(
                                    $"opennv_cg02_{result.Kind.ToLowerInvariant()}_{result.FormId}",
                                    result.Count);
                                if (result.Kind == "addItem")
                                    player.SetMeta(
                                        $"opennv_cg02_item_{result.FormId}",
                                        result.Count);
                                player.SetMeta("opennv_cg02_stage", targetStage.Value);
                                appliedCommands += result.CommandCount;
                                if (result.AggregateStage is not null)
                                {
                                    if (result.AggregateStage != butch.AggregateStage)
                                        throw new InvalidOperationException(
                                            "Fallout 3 CG02 aggregate stage differs.");
                                    appliedCommands++;
                                    effectiveStage = result.AggregateStage;
                                    StartIntercomTimer(butch.AggregateTimerSeconds);
                                }
                            }
                            else if (targetStage == butch.SceneDoneStage)
                                appliedCommands = 1;
                            else
                                throw new InvalidOperationException(
                                    "Fallout 3 CG02 birthday stage is unsupported.");
                        }
                        current = current with
                        {
                            ActiveStage = targetStage == cake.TriggerStage
                                ? current.ActiveStage
                                : effectiveStage ?? current.ActiveStage,
                            AppliedInfoFormIds = current.AppliedInfoFormIds
                                .Append(infoFormId).ToArray(),
                            AccountedCommandCount = current.AccountedCommandCount +
                                appliedCommands,
                            AppliedCommandCount = current.AppliedCommandCount +
                                appliedCommands,
                            NextBoundary = new Fo3Cg01Stage12Boundary(
                                false, birthday.NextBoundaryBlocker),
                        };
                        Persist();
                        StartButchPackageIfEligible();
                    })),
                StringComparer.OrdinalIgnoreCase);
            activations[postIntercom.IntercomReferenceFormId] = ActivateIntercom;
            activations[postIntercom.JonasReferenceFormId] = () =>
            {
                if (current.ActiveStage == reactorGift.SourceStage)
                    StartReactorGiftParticipant(jonasGift);
            };
            activations[postIntercom.DadReferenceFormId] = () =>
            {
                if (current.ActiveStage == postIntercom.GoodbyeStage)
                    ActivateDadPostIntercom();
                else if (current.ActiveStage == reactorGift.JonasStage ||
                         current.ActiveStage == reactorGift.TargetStage ||
                         current.ActiveStage == reactorGift.HitStage ||
                         current.ActiveStage == reactorGift.DeathStage)
                    StartReactorGiftParticipant(dadGift);
            };
            player.ConfigureSourceFormActivations(activations);
            var sourceHits = reactorGift.TargetReferenceFormIds.ToDictionary(
                formId => formId,
                formId => (Action)(() => ApplyTargetHit(formId)),
                StringComparer.OrdinalIgnoreCase);
            sourceHits.Add(reactorGift.Combatant.ReferenceFormId, ApplyCombatHit);
            player.ConfigureSourceHitscan(
                _runtimeConfiguration.Player.DesktopInput.Fire.Action,
                _runtimeConfiguration.Player.FireRayDistanceMeters,
                reactorGift.RequiredWeaponFormId,
                sourceHits);
            if (current.ActiveStage >= postIntercom.SourceStage)
            {
                EnsureJonas();
                var activePackage = current.ActiveStage >= postIntercom.GoodbyeStage
                    ? postIntercom.DadToPlayerPackage.FormId
                    : current.ActiveStage >= postIntercom.AnswerStage
                        ? postIntercom.DadTalkToJonasPackage.FormId
                        : postIntercom.DadToIntercomPackage.FormId;
                _vaultBirthCoverage!.Cg01DadActor.Placement.SetMeta(
                    "opennv_active_package_form_id", activePackage);
                if (current.ActiveStage == postIntercom.SourceStage &&
                    _cg01DadPackageTravelTick is null)
                    StartDadToIntercomTravel();
            }
            if (current.ActiveStage >= reactorGift.JonasStage)
                ExecuteReactorGiftStageCommands(reactorGift.JonasStage);
            if (current.ActiveStage >= reactorGift.TargetStage)
                ExecuteReactorGiftStageCommands(reactorGift.TargetStage);
            if (current.ActiveStage >= reactorGift.RangeStage)
                ExecuteReactorGiftStageCommands(reactorGift.RangeStage);
            if (current.ActiveStage >= reactorGift.HitStage)
                ExecuteReactorGiftStageCommands(reactorGift.HitStage);
            if (current.ActiveStage >= reactorGift.CombatStage)
                ExecuteReactorGiftStageCommands(reactorGift.CombatStage);
            if (current.ActiveStage >= reactorGift.DeathStage)
                ExecuteReactorGiftStageCommands(reactorGift.DeathStage);
            if (current.ActiveStage == picture.SourceStage)
                StartPicturePositioning();
            else if (current.ActiveStage == picture.PictureStage)
            {
                StartPicturePositioning();
                StartPictureJonas();
            }
            else if (current.ActiveStage == pictureCompletion.TimerStage ||
                     current.ActiveStage == pictureCompletion.FlashStage)
                StartPictureCompletion();
            foreach (var targetReferenceFormId in current.Cg02TargetHitFormIds.Distinct(
                StringComparer.OrdinalIgnoreCase))
                Cg01WorldReference(targetReferenceFormId).SetMeta(
                    "opennv_animation_group", reactorGift.TargetAnimationGroup);
            player.SetMeta("opennv_cg02_target_count",
                current.Cg02TargetHitFormIds.Count);
            if (current.CombatHealthByReferenceFormId.TryGetValue(
                    reactorGift.Combatant.ReferenceFormId, out var restoredHealth))
            {
                var restoredCombatant = Cg01WorldReference(
                    reactorGift.Combatant.ReferenceFormId);
                restoredCombatant.SetMeta("opennv_current_health", restoredHealth);
                restoredCombatant.SetMeta("opennv_dead",
                    current.DeadCombatReferenceFormIds.Contains(
                        reactorGift.Combatant.ReferenceFormId,
                        StringComparer.OrdinalIgnoreCase) ? 1 : 0);
                restoredCombatant.SetMeta("opennv_active_package_form_id",
                    reactorGift.Combatant.PackageFormId);
                restoredCombatant.SetMeta("opennv_package_target_form_id",
                    reactorGift.Combatant.PackageTargetFormId);
                restoredCombatant.SetMeta("opennv_package_radius_game_units",
                    reactorGift.Combatant.PackageRadiusGameUnits);
                if (restoredHealth < reactorGift.Combatant.MaximumHealth)
                    restoredCombatant.SetMeta("opennv_combat_target_form_id",
                        reactorGift.Combatant.PlayerReferenceFormId);
            }
            StartButchPackageIfEligible();
            if ((current.ActiveStage == butch.AggregateStage ||
                 current.ActiveStage == butch.SceneDoneStage) &&
                current.TimerAdvancing)
                StartIntercomTimer(current.TimerRemainingSeconds);
            if (current.ActiveStage == cake.TriggerStage &&
                !current.AppliedPackageFormIds.Contains(
                    cake.PackageFormId, StringComparer.OrdinalIgnoreCase))
                StartCake();
            else if (current.ActiveStage == cake.TargetStage &&
                cake.Cues.Any(cue => !current.AppliedInfoFormIds.Contains(
                    cue.InfoFormId, StringComparer.OrdinalIgnoreCase)))
                StartCake();
        }
        void StartDadParty(
            Fo3Cg02DadPartyRuntime party,
            Fo3Cg01ToddlerPlayer player)
        {
            StartCg02DadPartyRuntime(
                party, player, current.AppliedInfoFormIds,
                (infoFormId, appliedCommands) =>
                {
                    current = current with
                    {
                        ActiveStage = party.TargetStage,
                        AppliedInfoFormIds = current.AppliedInfoFormIds
                            .Append(infoFormId).ToArray(),
                        AccountedCommandCount = current.AccountedCommandCount +
                            appliedCommands,
                        AppliedCommandCount = current.AppliedCommandCount +
                            appliedCommands,
                        NextBoundary = new Fo3Cg01Stage12Boundary(
                            false, party.NextBoundaryBlocker),
                    };
                    Persist();
                    InstallBirthday(
                        party.BirthdayInteractionsRuntime ??
                            throw new InvalidOperationException(
                                "Fallout 3 CG02 birthday interactions are absent."),
                        player);
                });
        }
        void StartOverseer(
            Fo3Cg02OverseerSpeechRuntime speech,
            Fo3Cg01ToddlerPlayer player)
        {
            StartCg02OverseerSpeechRuntime(
                speech,
                player,
                current.AppliedInfoFormIds,
                (infoFormId, appliedCommands, activeStage) =>
                {
                    current = current with
                    {
                        ActiveStage = activeStage ?? current.ActiveStage,
                        AppliedInfoFormIds = current.AppliedInfoFormIds
                            .Append(infoFormId).ToArray(),
                        AccountedCommandCount = current.AccountedCommandCount +
                            appliedCommands,
                        AppliedCommandCount = current.AppliedCommandCount +
                            appliedCommands,
                    };
                    Persist();
                },
                () =>
                {
                    if (current.ActiveStage != speech.TargetStage)
                        throw new InvalidOperationException(
                            "Fallout 3 CG02 Overseer completion stage differs.");
                    current = current with
                    {
                        NextBoundary = new Fo3Cg01Stage12Boundary(
                            false, speech.NextBoundaryBlocker),
                    };
                    Persist();
                    StartDadParty(
                        speech.DadPartyRuntime ?? throw new InvalidOperationException(
                            "Fallout 3 CG02 Dad party contract is absent."),
                        player);
                });
        }
        void StartStage50Timer()
        {
            if (!current.TimerAdvancing ||
                current.ActiveStage != interaction.TimerTransition.SourceStage ||
                _cg01Stage50TimerTick is not null)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 stage-50 timer start differs.");
            _cg01Stage50TimerTick = delta =>
            {
                var remaining = Math.Max(0.0, current.TimerRemainingSeconds - delta);
                current = current with { TimerRemainingSeconds = remaining };
                if (remaining > 0.0)
                {
                    Persist();
                    return;
                }
                var applied = interaction.TimerTransition.ExecuteTargetResult();
                current = current with
                {
                    ActiveStage = interaction.TimerTransition.TargetStage,
                    TimerAdvancing = false,
                    AccountedCommandCount = current.AccountedCommandCount + applied,
                    AppliedCommandCount = current.AppliedCommandCount + applied
                };
                (_vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 CG01 stage-70 Dad world is absent."))
                    .Cg01DadActor.Placement.SetMeta("opennv_package_evaluated", true);
                _cg01Stage50TimerTick = null;
                ApplyCg01DadPackage(interaction.TimerTransition.DadReturnPackage, stage5);
                var completionApplied = interaction.TimerTransition.ExecuteCompletionResult();
                SetCg01WorldReferenceLock(current.PlayroomDoorReferenceFormId, 0);
                SetCg01WorldReferenceOpen(current.PlayroomDoorReferenceFormId, true);
                SetCg01WorldReferenceLock(
                    interaction.TimerTransition.MainDoorReferenceFormId,
                    interaction.TimerTransition.MainDoorLockLevel);
                SetCg01WorldReferenceOpen(
                    interaction.TimerTransition.MainDoorReferenceFormId,
                    interaction.TimerTransition.MainDoorOpen);
                current = current with
                {
                    ActiveStage = interaction.TimerTransition.CompletionStage,
                    AppliedPackageFormIds = current.AppliedPackageFormIds
                        .Append(interaction.TimerTransition.DadReturnPackage.FormId).ToArray(),
                    PlayroomDoorOpen = true,
                    PlayroomDoorLockLevel = 0,
                    AccountedCommandCount = current.AccountedCommandCount + completionApplied,
                    AppliedCommandCount = current.AppliedCommandCount + completionApplied
                };
                var dialogueDelay = GetTree().CreateTimer(
                    interaction.TimerTransition.DialogueDelaySeconds);
                dialogueDelay.Timeout += () => PlayCg01DadReturnCue(
                    interaction.TimerTransition.DialogueCues,
                    0,
                    targetStage =>
                    {
                        if (targetStage is null)
                            return true;
                        var sequence = interaction.TimerTransition.DadLead;
                        current = current with { ActiveStage = targetStage.Value };
                        if (targetStage == sequence.BibleTravel.SourceStage)
                        {
                            var applied = ExecuteSourceCommands(
                                sequence.BibleTravel.StageCommands);
                            current = current with
                            {
                                AccountedCommandCount = current.AccountedCommandCount + applied,
                                AppliedCommandCount = current.AppliedCommandCount + applied,
                            };
                            StartCg01DadSourceTravel(
                                sequence,
                                sequence.BibleTravel,
                                interaction.TimerTransition.DadReturnPackage.TargetTransform,
                                stage5,
                                () =>
                                {
                                    var completionApplied = ExecuteSourceCommands(
                                        sequence.BibleTravel.CompletionCommands);
                                    current = current with
                                    {
                                        ActiveStage = sequence.BibleTravel.CompletionStage!.Value,
                                        AppliedPackageFormIds = current.AppliedPackageFormIds
                                            .Append(sequence.BibleTravel.Package.FormId).ToArray(),
                                        AccountedCommandCount = current.AccountedCommandCount + completionApplied,
                                        AppliedCommandCount = current.AppliedCommandCount + completionApplied,
                                    };
                                    PlayCg01DadReturnCue(
                                        interaction.TimerTransition.DialogueCues,
                                        1,
                                        HandleDadReturnStage);
                                });
                            return false;
                        }
                        return HandleDadReturnStage(targetStage);

                        bool HandleDadReturnStage(int? stage)
                        {
                            if (stage is null)
                                return true;
                            current = current with { ActiveStage = stage.Value };
                            if (stage != interaction.TimerTransition.DialogueTargetStage)
                                return true;
                            var leadApplied = ExecuteSourceCommands(sequence.LeadTravel.StageCommands);
                            SetCg01WorldReferenceLock(
                                sequence.UnlockedDoorReferenceFormId, 0);
                            current = current with
                            {
                                AccountedCommandCount = current.AccountedCommandCount + leadApplied,
                                AppliedCommandCount = current.AppliedCommandCount + leadApplied,
                            };
                            StartCg01DadSourceTravel(
                                sequence,
                                sequence.LeadTravel,
                                sequence.BibleTravel.Package.TargetTransform,
                                stage5,
                                () =>
                                {
                                    if (current.ActiveStage == sequence.SayToDoneStage)
                                        Persist();
                                });
                            var sayDoneApplied = ExecuteSourceCommands(sequence.SayToDoneCommands);
                            current = current with
                            {
                                ActiveStage = sequence.SayToDoneStage,
                                DisplayedObjectiveIndex = sequence.DisplayedObjectiveIndex,
                                AppliedPackageFormIds = current.AppliedPackageFormIds
                                    .Append(sequence.LeadTravel.Package.FormId).ToArray(),
                                AccountedCommandCount = current.AccountedCommandCount + sayDoneApplied,
                                AppliedCommandCount = current.AppliedCommandCount + sayDoneApplied,
                            };
                            return false;
                        }
                    });
            };
        }
        void Gate()
        {
            if (current.ActiveStage != interaction.SourceStage)
                return;
            var applied = interaction.ExecuteStageResult(interaction.GateStage);
            SetCg01WorldReferenceOpen(interaction.GateReferenceFormId, true);
            current = current with
            {
                ActiveStage = interaction.GateStage,
                PlaypenGateOpen = true,
                DisplayedObjectiveIndex = interaction.GateStage,
                AccountedCommandCount = current.AccountedCommandCount + applied,
                AppliedCommandCount = current.AppliedCommandCount + applied
            };
            Persist();
        }
        void Exit()
        {
            if (current.ActiveStage != interaction.GateStage)
                return;
            var applied = interaction.ExecuteStageResult(interaction.ExitStage);
            current = current with
            {
                ActiveStage = interaction.ExitStage,
                DisplayedObjectiveIndex = interaction.ExitStage,
                AccountedCommandCount = current.AccountedCommandCount + applied,
                AppliedCommandCount = current.AppliedCommandCount + applied
            };
            Persist();
        }
        void Book()
        {
            if (current.ActiveStage != interaction.ExitStage &&
                    current.ActiveStage != interaction.BookStage ||
                current.ActiveStage == interaction.BookStage && current.SpecialBookAccepted)
                return;
            if (current.ActiveStage < interaction.BookStage)
            {
                var applied = interaction.ExecuteStageResult(interaction.BookStage);
                current = current with
                {
                    ActiveStage = interaction.BookStage,
                    AccountedCommandCount = current.AccountedCommandCount + applied,
                    AppliedCommandCount = current.AppliedCommandCount + applied
                };
                Cg01WorldReference(interaction.BookReferenceFormId)
                    .SetMeta("opennv_special_book_menu_points", interaction.MenuPoints);
                Persist();
            }
            if (_cg01SpecialBookMenu is not null)
                throw new InvalidOperationException(
                    "Fallout 3 SPECIAL book menu is already active.");
            _cg01SpecialBookMenu = new Fo3SpecialBookMenuRuntime(
                interaction,
                (_cg01ToddlerWorld ?? throw new InvalidOperationException(
                    "Fallout 3 SPECIAL input owner is absent.")).Contract,
                current.SpecialValues,
                values =>
                {
                    current = current with { SpecialValues = values };
                    Persist();
                },
                values =>
                {
                    current = current with
                    {
                        SpecialValues = values,
                        SpecialBookAccepted = true,
                        TimerRemainingSeconds = interaction.TimerTransition.InitialSeconds,
                        TimerAdvancing = true
                    };
                    _cg01SpecialBookMenu = null;
                    Persist();
                    StartStage50Timer();
                });
            _cg01SpecialBookMenu.Open(
                Cg01WorldReference(interaction.BookReferenceFormId),
                (_cg01ToddlerWorld ?? throw new InvalidOperationException(
                    "Fallout 3 SPECIAL player is absent.")).Player);
        }
        (_cg01ToddlerWorld ?? throw new InvalidOperationException(
            "Fallout 3 CG01 interaction world is absent."))
            .InstallStage20Interactions(
                _vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 CG01 interaction scene is absent."),
                interaction, Gate, Exit, Book);
        (_cg01ToddlerWorld ?? throw new InvalidOperationException(
            "Fallout 3 CG01 Dad-lead world is absent."))
            .InstallDadLeadEndTrigger(
                _vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 CG01 Dad-lead scene is absent."),
                interaction.TimerTransition.DadLead.EndTrigger,
                () =>
                {
                    var trigger = interaction.TimerTransition.DadLead.EndTrigger;
                    if (current.ActiveStage != trigger.SourceStage)
                        return;
                    var completion = interaction.TimerTransition.DadLead.Completion;
                    current = current with
                    {
                        ActiveStage = trigger.TargetStage,
                        TimerRemainingSeconds = completion.TimerInitialSeconds,
                        TimerAdvancing = true,
                        AccountedCommandCount = current.AccountedCommandCount + 1 +
                            completion.Stage90CommandCount,
                        AppliedCommandCount = current.AppliedCommandCount + 1 +
                            completion.Stage90CommandCount,
                    };
                    var stage90World = _cg01ToddlerWorld ??
                        throw new InvalidOperationException(
                            "Fallout 3 CG01 stage-90 player is absent.");
                    stage90World.Player.SetMeta("opennv_objectives_completed", true);
                    stage90World.Player.SetMeta("opennv_auto_display_objectives", false);
                    stage90World.Player.SetMeta("opennv_quest_updates_enabled", false);
                    StartStage90ImageSpace(completion.ImageSpaceModifier);
                    StartStage90Sound(completion.Sound);
                    _cg01Stage90TimerTick = delta =>
                    {
                        current = current with
                        {
                            TimerRemainingSeconds = Math.Max(
                                0.0, current.TimerRemainingSeconds - delta),
                        };
                        if (current.TimerRemainingSeconds > 0.0)
                            return;
                        _cg01Stage90TimerTick = null;
                        current = current with
                        {
                            ActiveQuestFormId = completion.NextQuestFormId,
                            ActiveQuestEditorId = completion.NextQuestEditorId,
                            ActiveStage = completion.Cg02Stage0.TargetStage,
                            TimerRemainingSeconds = completion.Cg02Stage0.IntroRuntime?.InitialSeconds
                                ?? throw new InvalidOperationException(
                                    "Fallout 3 CG02 intro timer contract is absent."),
                            TimerAdvancing = true,
                            ImageSpaceElapsedSeconds = Math.Min(
                                completion.ImageSpaceModifier.DurationSeconds,
                                _stage90ImageSpaceElapsedSeconds + delta),
                            Stage90SoundStarted = true,
                            AccountedCommandCount = current.AccountedCommandCount +
                                completion.Stage100CommandCount +
                                completion.Cg02Stage0.Stage5CommandCount +
                                completion.Cg02Stage0.Stage0CommandCount,
                            AppliedCommandCount = current.AppliedCommandCount +
                                completion.Stage100CommandCount +
                                completion.Cg02Stage0.Stage5CommandCount +
                                completion.Cg02Stage0.Stage0CommandCount,
                            NextBoundary = new Fo3Cg01Stage12Boundary(
                                false, completion.NextBoundaryBlocker),
                        };
                        var world = _cg01ToddlerWorld ?? throw new InvalidOperationException(
                            "Fallout 3 CG01 completion player is absent.");
                        world.Player.ApplySourceScale(completion.PlayerScale);
                        ApplyCg02Stage5State(world.Player, completion.Cg02Stage0);
                        world.Player.StopAtAuthoredTrigger();
                        world.Player.MoveToSourceTransform(
                            completion.Cg02Stage0.PlayerMoveTransform,
                            (_vaultBirthCoverage ?? throw new InvalidOperationException(
                                "Fallout 3 CG02 player move scene is absent.")).Contract);
                        world.Player.SetMeta("opennv_player_toddler", completion.PlayerToddler);
                        world.Player.SetMeta("opennv_no_activation_sound", false);
                        var dad = _vaultBirthCoverage?.Cg01DadActor.Placement ??
                            throw new InvalidOperationException(
                                "Fallout 3 CG01 completion Dad is absent.");
                        if (!dad.GetMeta("opennv_source_form_id").AsString().Equals(
                                completion.DisabledDadReferenceFormId,
                                StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException(
                                "Fallout 3 CG01 completion Dad identity differs.");
                        dad.Visible = false;
                        dad.ProcessMode = ProcessModeEnum.Disabled;
                        dad.SetMeta("opennv_enabled", 0);
                        _cg02IntroBegin = () => StartCg02IntroRuntime(
                            completion.Cg02Stage0,
                            world.Player,
                            () =>
                            {
                                var intro = completion.Cg02Stage0.IntroRuntime!;
                                current = current with
                                {
                                    ActiveStage = intro.TargetStage,
                                    TimerRemainingSeconds = 0.0,
                                    TimerAdvancing = false,
                                    AccountedCommandCount = current.AccountedCommandCount +
                                        intro.FinalCommandCount,
                                    AppliedCommandCount = current.AppliedCommandCount +
                                        intro.FinalCommandCount,
                                };
                                Persist();
                                var speech = intro.DadSpeechRuntime ??
                                    throw new InvalidOperationException(
                                        "Fallout 3 CG02 Dad speech contract is absent.");
                                StartCg02DadSpeechRuntime(
                                    speech,
                                    world.Player,
                                    current.AppliedInfoFormIds,
                                    infoFormId =>
                                    {
                                        current = current with
                                        {
                                            AppliedInfoFormIds = current.AppliedInfoFormIds
                                                .Append(infoFormId).ToArray(),
                                        };
                                        Persist();
                                    },
                                    () =>
                                    {
                                        current = current with
                                        {
                                            ActiveStage = speech.TargetStage,
                                            AccountedCommandCount = current.AccountedCommandCount +
                                                speech.FinalCommandCount,
                                            AppliedCommandCount = current.AppliedCommandCount +
                                                speech.FinalCommandCount,
                                            NextBoundary = new Fo3Cg01Stage12Boundary(
                                                false, speech.NextBoundaryBlocker),
                                        };
                                        Persist();
                                        StartOverseer(
                                            speech.OverseerSpeechRuntime ??
                                                throw new InvalidOperationException(
                                                    "Fallout 3 CG02 Overseer speech is absent."),
                                            world.Player);
                                    });
                            },
                            current.TimerRemainingSeconds);
                        Persist();
                        StartCg02TransitionMovie(completion.Cg02Stage0.TransitionMovie);
                    };
                });
        var restoredCompletion = interaction.TimerTransition.DadLead.Completion;
        var restoredOverseer = restoredCompletion.Cg02Stage0.IntroRuntime?
            .DadSpeechRuntime?.OverseerSpeechRuntime;
        var restoredParty = restoredOverseer?.DadPartyRuntime;
        var restoredBirthday = restoredParty?.BirthdayInteractionsRuntime;
        var restoredPost = restoredBirthday?.ButchRuntime?.PostIntercomRuntime;
        var restoredGift = restoredPost?.ReactorGiftRuntime;
        if (current.ActiveQuestFormId.Equals(
                restoredCompletion.NextQuestFormId, StringComparison.OrdinalIgnoreCase) &&
            (current.ActiveStage == restoredCompletion.Cg02Stage0.TargetStage ||
             current.ActiveStage == restoredCompletion.Cg02Stage0.IntroRuntime?.TargetStage ||
             current.ActiveStage == restoredCompletion.Cg02Stage0.IntroRuntime?
                 .DadSpeechRuntime?.TargetStage ||
             restoredOverseer is not null &&
                 (current.ActiveStage == restoredOverseer.TargetStage ||
                  restoredOverseer.StageResults.ContainsKey(current.ActiveStage)) ||
             current.ActiveStage == restoredParty?.TargetStage ||
             restoredBirthday?.StageResults.ContainsKey(current.ActiveStage) == true ||
             restoredBirthday?.CakeRuntime is { } restoredCake &&
                 (current.ActiveStage == restoredCake.TriggerStage ||
                  current.ActiveStage == restoredCake.TargetStage) ||
             restoredBirthday?.ButchRuntime is { } restoredButch &&
                 (current.ActiveStage == restoredButch.SceneDoneStage ||
                  current.ActiveStage == restoredButch.AggregateStage ||
                  current.ActiveStage == restoredButch.IntercomStage) ||
             restoredPost is not null &&
                 (current.ActiveStage == restoredPost.AnswerStage ||
                  current.ActiveStage == restoredPost.GoodbyeStage ||
                  current.ActiveStage == restoredPost.TargetStage) ||
             restoredGift is not null &&
                 (current.ActiveStage == restoredGift.JonasStage ||
                  current.ActiveStage == restoredGift.TargetStage ||
                  current.ActiveStage == restoredGift.RangeStage ||
                  current.ActiveStage == restoredGift.HitStage ||
                  current.ActiveStage == restoredGift.CombatStage ||
                  current.ActiveStage == restoredGift.DeathStage ||
                  current.ActiveStage == restoredGift.CompletionStage ||
                  current.ActiveStage == restoredGift.PictureRuntime.PictureStage ||
                  current.ActiveStage == restoredGift.PictureRuntime.TimerStage ||
                  current.ActiveStage == restoredGift.PictureRuntime
                      .CompletionRuntime.FlashStage) ||
             restoredGift is not null &&
                 current.ActiveQuestFormId.Equals(
                     restoredGift.PictureRuntime.CompletionRuntime.NextQuestFormId,
                     StringComparison.OrdinalIgnoreCase) &&
                 current.ActiveStage == restoredGift.PictureRuntime
                     .CompletionRuntime.NextQuestTargetStage))
        {
            (_cg01ToddlerWorld ?? throw new InvalidOperationException(
                "Fallout 3 CG01 restored completion player is absent."))
                .Player.ApplySourceScale(restoredCompletion.PlayerScale);
            var restoredPlayer = _cg01ToddlerWorld.Player;
            restoredPlayer.SetMeta("opennv_player_toddler", restoredCompletion.PlayerToddler);
            restoredPlayer.SetMeta("opennv_no_activation_sound", false);
            restoredPlayer.SetMeta("opennv_objectives_completed", true);
            restoredPlayer.SetMeta("opennv_auto_display_objectives", false);
            restoredPlayer.SetMeta("opennv_quest_updates_enabled", false);
            ApplyCg02Stage5State(restoredPlayer, restoredCompletion.Cg02Stage0);
            restoredPlayer.StopAtAuthoredTrigger();
            restoredPlayer.MoveToSourceTransform(
                restoredCompletion.Cg02Stage0.PlayerMoveTransform,
                (_vaultBirthCoverage ?? throw new InvalidOperationException(
                    "Fallout 3 restored CG02 player move scene is absent.")).Contract);
            if (current.ImageSpaceElapsedSeconds <
                restoredCompletion.ImageSpaceModifier.DurationSeconds)
            {
                StartStage90ImageSpace(restoredCompletion.ImageSpaceModifier);
                _stage90ImageSpaceElapsedSeconds = current.ImageSpaceElapsedSeconds;
            }
            var dad = (_vaultBirthCoverage ?? throw new InvalidOperationException(
                "Fallout 3 CG01 restored completion Dad is absent."))
                .Cg01DadActor.Placement;
            dad.Visible = false;
            dad.ProcessMode = ProcessModeEnum.Disabled;
            dad.SetMeta("opennv_enabled", 0);
            EnsureCg02IntroActors(
                restoredCompletion.Cg02Stage0.IntroRuntime ??
                    throw new InvalidOperationException(
                        "Fallout 3 restored CG02 intro is absent."),
                restoredPlayer);
            if (current.TimerAdvancing)
            {
                StartCg02IntroRuntime(
                    restoredCompletion.Cg02Stage0,
                    restoredPlayer,
                    () =>
                    {
                        var intro = restoredCompletion.Cg02Stage0.IntroRuntime!;
                        current = current with
                        {
                            ActiveStage = intro.TargetStage,
                            TimerRemainingSeconds = 0.0,
                            TimerAdvancing = false,
                            AccountedCommandCount = current.AccountedCommandCount +
                                intro.FinalCommandCount,
                            AppliedCommandCount = current.AppliedCommandCount +
                                intro.FinalCommandCount,
                        };
                        Persist();
                        var speech = intro.DadSpeechRuntime ??
                            throw new InvalidOperationException(
                                "Fallout 3 restored CG02 Dad speech is absent.");
                        StartCg02DadSpeechRuntime(
                            speech,
                            restoredPlayer,
                            current.AppliedInfoFormIds,
                            infoFormId =>
                            {
                                current = current with
                                {
                                    AppliedInfoFormIds = current.AppliedInfoFormIds
                                        .Append(infoFormId).ToArray(),
                                };
                                Persist();
                            },
                            () =>
                            {
                                current = current with
                                {
                                    ActiveStage = speech.TargetStage,
                                    AccountedCommandCount = current.AccountedCommandCount +
                                        speech.FinalCommandCount,
                                    AppliedCommandCount = current.AppliedCommandCount +
                                        speech.FinalCommandCount,
                                    NextBoundary = new Fo3Cg01Stage12Boundary(
                                        false, speech.NextBoundaryBlocker),
                                };
                                Persist();
                                StartOverseer(
                                    speech.OverseerSpeechRuntime ??
                                        throw new InvalidOperationException(
                                            "Fallout 3 restored CG02 Overseer speech is absent."),
                                    restoredPlayer);
                            });
                    },
                    current.TimerRemainingSeconds);
            }
            else if (current.ActiveStage ==
                restoredCompletion.Cg02Stage0.IntroRuntime?.TargetStage)
            {
                var speech = restoredCompletion.Cg02Stage0.IntroRuntime.DadSpeechRuntime ??
                    throw new InvalidOperationException(
                        "Fallout 3 restored CG02 Dad speech is absent.");
                StartCg02DadSpeechRuntime(
                    speech,
                    restoredPlayer,
                    current.AppliedInfoFormIds,
                    infoFormId =>
                    {
                        current = current with
                        {
                            AppliedInfoFormIds = current.AppliedInfoFormIds
                                .Append(infoFormId).ToArray(),
                        };
                        Persist();
                    },
                    () =>
                    {
                        current = current with
                        {
                            ActiveStage = speech.TargetStage,
                            AccountedCommandCount = current.AccountedCommandCount +
                                speech.FinalCommandCount,
                            AppliedCommandCount = current.AppliedCommandCount +
                                speech.FinalCommandCount,
                            NextBoundary = new Fo3Cg01Stage12Boundary(
                                false, speech.NextBoundaryBlocker),
                        };
                        Persist();
                        StartOverseer(
                            speech.OverseerSpeechRuntime ??
                                throw new InvalidOperationException(
                                    "Fallout 3 restored CG02 Overseer speech is absent."),
                            restoredPlayer);
                    });
            }
            else if (restoredOverseer is not null &&
                (current.ActiveStage == restoredOverseer.SourceStage ||
                 current.ActiveStage == restoredOverseer.TargetStage ||
                 restoredOverseer.StageResults.ContainsKey(current.ActiveStage)))
            {
                if (current.ActiveStage != restoredOverseer.TargetStage)
                    StartOverseer(restoredOverseer, restoredPlayer);
                else
                    StartDadParty(
                        restoredParty ?? throw new InvalidOperationException(
                            "Fallout 3 restored CG02 Dad party is absent."),
                        restoredPlayer);
            }
            else if (restoredBirthday is not null &&
                (restoredBirthday.StageResults.ContainsKey(current.ActiveStage) ||
                 restoredBirthday.CakeRuntime is { } cake &&
                    (current.ActiveStage == cake.TriggerStage ||
                     current.ActiveStage == cake.TargetStage) ||
                 restoredBirthday.ButchRuntime is { } butch &&
                    (current.ActiveStage == butch.SceneDoneStage ||
                     current.ActiveStage == butch.AggregateStage ||
                     current.ActiveStage == butch.IntercomStage) ||
                 restoredPost is not null &&
                    (current.ActiveStage == restoredPost.AnswerStage ||
                     current.ActiveStage == restoredPost.GoodbyeStage ||
                     current.ActiveStage == restoredPost.TargetStage) ||
                 restoredGift is not null &&
                    (current.ActiveStage == restoredGift.JonasStage ||
                     current.ActiveStage == restoredGift.TargetStage ||
                     current.ActiveStage == restoredGift.RangeStage ||
                     current.ActiveStage == restoredGift.HitStage ||
                     current.ActiveStage == restoredGift.CombatStage ||
                     current.ActiveStage == restoredGift.DeathStage ||
                     current.ActiveStage == restoredGift.CompletionStage ||
                     current.ActiveStage == restoredGift.PictureRuntime.PictureStage ||
                     current.ActiveStage == restoredGift.PictureRuntime.TimerStage ||
                     current.ActiveStage == restoredGift.PictureRuntime
                         .CompletionRuntime.FlashStage) ||
                 restoredGift is not null &&
                    current.ActiveQuestFormId.Equals(
                        restoredGift.PictureRuntime.CompletionRuntime.NextQuestFormId,
                        StringComparison.OrdinalIgnoreCase) &&
                    current.ActiveStage == restoredGift.PictureRuntime
                        .CompletionRuntime.NextQuestTargetStage))
            {
                InstallBirthday(restoredBirthday, restoredPlayer);
            }
        }
        if (current.ActiveStage == interaction.BookStage && !current.SpecialBookAccepted)
            Book();
        else if (current.TimerAdvancing)
            StartStage50Timer();
    }

    private static void ApplyCg02Stage5State(
        Fo3Cg01ToddlerPlayer player,
        Fo3Cg02Stage0Transition transition)
    {
        player.SetMeta("opennv_cg02_stage", transition.TargetStage);
        player.SetMeta("opennv_cg02_player_marker", transition.PlayerMoveReferenceFormId);
        player.SetMeta("opennv_cg02_game_time", JsonSerializer.Serialize(transition.GameTime));
        player.SetMeta("opennv_cg02_player_young", transition.PlayerYoung);
        player.SetMeta("opennv_cg02_age_race_years", transition.AgeRaceYears);
        player.SetMeta("opennv_cg02_inventory", JsonSerializer.Serialize(transition.Inventory));
        player.SetMeta("opennv_cg02_actor_state", JsonSerializer.Serialize(transition.Actors));
        player.SetMeta("opennv_cg02_timer", transition.TimerInitialSeconds);
        player.SetMeta("opennv_cg02_run_timer", transition.RunTimerValue);
        player.SetMeta("opennv_cg02_intro", transition.IntroValue);
        player.SetMeta(
            "opennv_cg02_disabled_controls",
            JsonSerializer.Serialize(transition.DisabledPlayerControls));
    }

    private Node3D Cg01WorldReference(string formId) =>
        (_vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 reference has no owned world."))
        .CellRoot.GetChildren().OfType<Node3D>().Single(node =>
            node.HasMeta("opennv_source_form_id") &&
            node.GetMeta("opennv_source_form_id").AsString().Equals(
                formId,
                StringComparison.OrdinalIgnoreCase));

    private void PlayCg01DadReturnCue(
        IReadOnlyList<Fo3Cg01DadReturnCue> cues,
        int index,
        Func<int?, bool> completed)
    {
        var cue = cues[index];
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 Dad-return dialogue has no owned world.");
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        ClearCg01DadLip();
        var stream = AudioStreamOggVorbis.LoadFromFile(cue.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad-return voice could not be decoded: {cue.InfoFormId}");
        _activeCg01DadLip = FaceGenLipAnimation.Load(
            cue.Response.Lip.SourcePath,
            RuntimeConfiguration.Load().ActorCompiler.FaceGenAnimation.Lip);
        _activeCg01DadInfoFormId = cue.InfoFormId;
        coverage.Cg01DadActor.Placement.SetMeta("opennv_talking", 1);
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = $"Fallout3Cg01DadReturnVoice{index}",
            Stream = stream,
        };
        _vaultDialogueVoice.SetMeta("opennv_info_form_id", cue.InfoFormId);
        _vaultDialogueVoice.Finished += () =>
        {
            ClearCg01DadLip();
            _vaultDialogueVoice?.QueueFree();
            _vaultDialogueVoice = null;
            coverage.Cg01DadActor.Placement.SetMeta("opennv_talking", 0);
            if (cue.TargetStage is not null)
            {
                var result = GamebryoDialoguePlayback.RequireStageResult(
                    "setStage", cue.TargetQuestFormId, cue.TargetStage);
                if (!completed(result.Stage))
                    return;
            }
            else if (!completed(null))
                return;
            if (index + 1 < cues.Count)
                Callable.From(() => PlayCg01DadReturnCue(
                    cues, index + 1, completed)).CallDeferred();
        };
        AddChild(_vaultDialogueVoice);
        _vaultDialogueVoice.Play();
    }

    private static int ExecuteSourceCommands(
        IReadOnlyList<SourceGamebryoStageCommand<string>> commands)
    {
        var applied = 0;
        GamebryoStageCommandExecutor.ExecuteAll(commands, command =>
        {
            applied++;
            return applied == command.SourceIndex + 1;
        });
        return applied;
    }

    private void SetCg01WorldReferenceOpen(string formId, bool open)
    {
        var reference = Cg01WorldReference(formId);
        reference.SetMeta("opennv_open_state", open ? 1 : 0);
    }

    private void SetCg01WorldReferenceLock(string formId, int lockLevel)
    {
        var reference = Cg01WorldReference(formId);
        reference.SetMeta("opennv_lock_level", lockLevel);
    }
}
