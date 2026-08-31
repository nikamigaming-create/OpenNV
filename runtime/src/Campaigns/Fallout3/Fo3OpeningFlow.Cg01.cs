using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;


using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

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

    private void BeginCg01DadDialogue(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        bool resumed)
    {
        _vaultPreviewOverlay?.QueueFree();
        var subtitle = AddVaultDialogueOverlay("FO3_CG01_STAGE5_DAD_DIALOGUE");
        subtitle.SetMeta("opennv_speaker_reference_form_id", stage5.Dad.Reference.FormId);
        var cues = _profile.Cg01Stage10Transition.DialogueFor(context.Sex.EngineSex);
        PlayCg01DadCue(stage5, context, cues, 0, subtitle);
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_DIALOGUE_STARTED stage={stage5.ActiveStage} " +
            $"sex={context.Sex.EngineSex} cues={cues.Count} resumed={(resumed ? 1 : 0)} " +
            "movieReplayed=0");
    }

    private void PlayCg01DadCue(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01RuntimeContext context,
        IReadOnlyList<Fo3Cg01DadSpeechCue> cues,
        int index,
        Button subtitle)
    {
        if (index < 0 || index >= cues.Count)
            throw new InvalidOperationException("Fallout 3 CG01 Dad dialogue cursor differs.");
        var cue = cues[index];
        var speaker = subtitle.GetMeta("opennv_speaker_reference_form_id").AsString();
        if (!speaker.Equals(stage5.Dad.Reference.FormId, StringComparison.OrdinalIgnoreCase) ||
            _cg01DadDialogueGeometry is null ||
            !_cg01DadDialogueGeometry.FrustumIntersection)
            throw new InvalidOperationException(
                "Fallout 3 CG01 subtitle or camera subject differs from Dad.");
        var publishedSpeakerIdle = PublishCg01DadSpeakerIdle(cue);
        _vaultDialogueVoice?.Stop();
        _vaultDialogueVoice?.QueueFree();
        ClearCg01DadLip();
        var stream = AudioStreamOggVorbis.LoadFromFile(cue.Response.Voice.SourcePath)
            ?? throw new InvalidOperationException(
                $"Fallout 3 CG01 Dad voice could not be decoded: " +
                cue.Response.Voice.LogicalPath);
        var durationSeconds = stream.GetLength();
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new InvalidOperationException("Fallout 3 CG01 Dad voice has no duration.");
        _activeCg01DadLip = FaceGenLipAnimation.Load(
            cue.Response.Lip.SourcePath,
            RuntimeConfiguration.Load().ActorCompiler.FaceGenAnimation.Lip);
        _activeCg01DadInfoFormId = cue.InfoFormId;
        _cg01DadLipSampleLogged = false;
        _vaultDialogueVoice = new AudioStreamPlayer
        {
            Name = $"Fallout3Cg01DadVoice{cue.Sequence}",
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
            if (index + 1 < cues.Count)
            {
                var timer = GetTree().CreateTimer(cue.DadTimerAfterSeconds);
                timer.Timeout += () => PlayCg01DadCue(
                    stage5,
                    context,
                    cues,
                    index + 1,
                    subtitle);
                GD.Print(
                    $"OPENNV_FO3_CG01_DAD_TIMER_SET info={cue.InfoFormId} " +
                    $"seconds={cue.DadTimerAfterSeconds:F1}");
                return;
            }
            CompleteCg01DadDialogue(stage5, context, cues, subtitle);
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
                "Fallout 3 CG01 audio, LIP, and speaker idle do not own the same INFO.");
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_CUE_STARTED sequence={cue.Sequence} " +
            $"info={cue.InfoFormId} duration={durationSeconds:F3} " +
            $"voice={cue.Response.Voice.LogicalPath} lip={cue.Response.Lip.LogicalPath}");
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_LIP_LOADED info={cue.InfoFormId} " +
            $"frames={_activeCg01DadLip.FrameCount} " +
            $"startFrame={_activeCg01DadLip.StartFrame} " +
            $"metadata=0x{_activeCg01DadLip.MetadataWord:x8} " +
            $"actor={_vaultBirthCoverage?.Cg01DadActor.ReferenceFormId}");
        if (_cg01ProofCapturePath is not null && cue.Sequence == 1)
            CaptureCg01DadCue(cue, publishedSpeakerIdle, subtitle);
    }

    private async void CaptureCg01DadCue(
        Fo3Cg01DadSpeechCue cue,
        ActorModelSlice.LoadedAnimation publishedSpeakerIdle,
        Button subtitle)
    {
        try
        {
            for (var frame = 0;
                 frame < Fo3OpeningFlowNumericContracts.Cg01CaptureWarmupFrames;
                 frame++)
                await ToSignal(
                    RenderingServer.Singleton,
                    RenderingServer.SignalName.FramePostDraw);
            var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
                "Fallout 3 CG01 capture has no owned world.");
            if (_cg01ProofCaptureCompleted ||
                _background.Visible ||
                _panel.Visible ||
                _introLayer is not null ||
                _video is not null ||
                !coverage.Cg01DadActor.Placement.Visible ||
                coverage.DoctorActor.Placement.Visible ||
                coverage.DadActor.Placement.Visible ||
                _cg01DadDialogueGeometry is null ||
                !_cg01DadDialogueGeometry.FrustumIntersection ||
                _vaultDialogueVoice is null ||
                !_vaultDialogueVoice.Playing ||
                _activeCg01DadLip is null ||
                _activeCg01DadInfoFormId != cue.InfoFormId ||
                !subtitle.Visible ||
                publishedSpeakerIdle.Player.CurrentAnimation.ToString() !=
                    publishedSpeakerIdle.RuntimeName)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 capture presentation is blank, stale, or unsynchronized.");
            var path = _cg01ProofCapturePath ?? throw new InvalidOperationException(
                "Fallout 3 CG01 capture path is absent.");
            var image = GetViewport().GetTexture().GetImage();
            image.Convert(Image.Format.Rgba8);
            var data = image.GetData();
            var pixels = image.GetWidth() * image.GetHeight();
            if (pixels <= 0 ||
                data.Length != pixels * Fo3OpeningFlowNumericContracts.CaptureBytesPerPixel)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 capture viewport is empty.");
            var minimum = byte.MaxValue;
            var maximum = byte.MinValue;
            for (var offset = 0;
                 offset < data.Length;
                 offset += Fo3OpeningFlowNumericContracts.CaptureBytesPerPixel)
            {
                for (var channel = 0;
                     channel < Fo3OpeningFlowNumericContracts.CaptureRgbChannels;
                     channel++)
                {
                    minimum = Math.Min(minimum, data[offset + channel]);
                    maximum = Math.Max(maximum, data[offset + channel]);
                }
            }
            var rgbSpan = maximum - minimum;
            if (rgbSpan <= 0)
                throw new InvalidOperationException(
                    "Fallout 3 CG01 capture contains one blank color.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var saveError = image.SavePng(path);
            if (saveError != Error.Ok)
                throw new InvalidOperationException(
                    $"Fallout 3 CG01 capture could not be saved: {saveError}.");
            using var stream = File.OpenRead(path);
            _cg01ProofCaptureSha256 = Convert.ToHexString(
                SHA256.HashData(stream)).ToLowerInvariant();
            _cg01ProofCaptureInfoFormId = cue.InfoFormId;
            _cg01ProofCaptureSpeakerIdleFormId = cue.SpeakerIdle.FormId;
            _cg01ProofCaptureWidth = image.GetWidth();
            _cg01ProofCaptureHeight = image.GetHeight();
            _cg01ProofCaptureRgbSpan = rgbSpan;
            _cg01ProofCaptureCompleted = true;
            GD.Print(
                $"OPENNV_FO3_CG01_COHERENT_CAPTURE_READY path={path} " +
                $"sha256={_cg01ProofCaptureSha256} info={cue.InfoFormId} " +
                $"idle={cue.SpeakerIdle.FormId} size={image.GetWidth()}x{image.GetHeight()} " +
                $"rgbSpan={rgbSpan} shellVisible=0 movieVisible=0 frustum=1 " +
                "audioLipIdleSynchronized=1");
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO3_CG01_COHERENT_CAPTURE_FAIL {exception.Message}");
            GetTree().Quit(Fo3OpeningFlowNumericContracts.ProofFailureExitCode);
        }
    }

    private ActorModelSlice.LoadedAnimation PublishCg01DadSpeakerIdle(
        Fo3Cg01DadSpeechCue cue) =>
        PublishCg01DadSpeakerIdle(
            cue.Sequence,
            cue.InfoFormId,
            cue.SpeakerIdle,
            stage12Response: false);

    private ActorModelSlice.LoadedAnimation PublishCg01DadSpeakerIdle(
        Fo3Cg01Stage12DadResponseCue cue) =>
        PublishCg01DadSpeakerIdle(
            cue.Sequence,
            cue.InfoFormId,
            cue.SpeakerIdle,
            stage12Response: true);

    private ActorModelSlice.LoadedAnimation PublishCg01DadSpeakerIdle(
        int sequence,
        string infoFormId,
        Fo3Cg01DadSpeakerIdle speakerIdle,
        bool stage12Response)
    {
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 Dad speaker idle has no owned actor scene.");
        var expectedAnimations = stage12Response
            ? coverage.Cg01DadAppearance.Stage12DialogueAnimations
            : coverage.Cg01DadAppearance.DialogueAnimations;
        var expected = expectedAnimations.Single(value =>
            value.Sequence == sequence &&
            value.InfoFormId.Equals(infoFormId, StringComparison.OrdinalIgnoreCase));
        if (!Fo3Cg01Stage10Transition.SpeakerIdleEquals(
                expected.SpeakerIdle,
                speakerIdle))
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad INFO speaker-idle source differs from the actor derivative.");
        var loaded = coverage.Cg01DadActor.Actor.LoadedAnimations.Single(value =>
            ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                ActorModelSlice.NormalizeAnimationPath(speakerIdle.ModelPath),
                StringComparison.OrdinalIgnoreCase) &&
            value.SourceSha256.Equals(
                speakerIdle.SourceSha256,
                StringComparison.OrdinalIgnoreCase));
        foreach (var player in coverage.Cg01DadActor.Actor.LoadedAnimations
                     .Select(value => value.Player).Distinct())
            player.Stop();
        loaded.Player.Play(loaded.RuntimeName);
        loaded.Player.Advance(0.0);
        if (loaded.Player.CurrentAnimation.ToString() != loaded.RuntimeName ||
            _cg01DadPublishedSpeakerIdleInfoFormIds.Contains(
                infoFormId,
                StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad speaker idle was not published exactly once.");
        _cg01DadPublishedSpeakerIdleInfoFormIds.Add(infoFormId);
        _cg01DadDialogueGeometry = CellReferenceLedger.MeasureGeometry(
            coverage.Cg01DadActor.Actor.Root,
            coverage.Camera,
            coverage.Cg01DadGrounding.GroundedBounds.GetCenter());
        if (!_cg01DadDialogueGeometry.RenderLayerVisible ||
            !_cg01DadDialogueGeometry.AabbValid ||
            !_cg01DadDialogueGeometry.FrustumIntersection ||
            _cg01DadDialogueGeometry.Surfaces != coverage.Cg01DadAppearance.Actor.Surfaces)
            throw new InvalidOperationException(
                "Fallout 3 CG01 Dad speaker-idle pose is outside the active camera.");
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_SPEAKER_IDLE_PUBLISHED sequence={sequence} " +
            $"info={infoFormId} idle={speakerIdle.FormId} " +
            $"path={speakerIdle.ModelPath} sha256={speakerIdle.SourceSha256} " +
            $"stage12Response={(stage12Response ? 1 : 0)} " +
            $"runtime={loaded.RuntimeName} channels={loaded.Channels} " +
            $"frustum=1 surfaces={_cg01DadDialogueGeometry.Surfaces}");
        return loaded;
    }

    private void UpdateCg01DadLip()
    {
        if (_vaultDialogueVoice is null ||
            !_vaultDialogueVoice.Playing ||
            _activeCg01DadLip is null ||
            _cg01DadFace is null)
            return;
        var seconds = _vaultDialogueVoice.GetPlaybackPosition();
        if (_vaultDialogueVoice.GetMeta("opennv_info_form_id").AsString() !=
                _activeCg01DadInfoFormId)
            throw new InvalidOperationException(
                "Fallout 3 CG01 audio and LIP clock INFO identities diverged.");
        var dominant = _cg01DadFace.Apply(_activeCg01DadLip, seconds);
        if (_cg01DadLipSampleLogged || dominant.Value == 0.0f)
            return;
        _cg01DadLipSampleLogged = true;
        _cg01DadLipCueSamples++;
        GD.Print(
            $"OPENNV_FO3_CG01_DAD_LIP_SAMPLE info={_activeCg01DadInfoFormId} " +
            $"seconds={seconds:F3} target={dominant.Target} value={dominant.Value:F6}");
    }

    private void ClearCg01DadLip()
    {
        _cg01DadFace?.Clear();
        _activeCg01DadLip = null;
        _activeCg01DadInfoFormId = null;
        _cg01DadLipSampleLogged = false;
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
        ApplyCg01DadPackage(_profile.Cg01PostStage14Transition.LeaveRoomPackage, stage5);
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
        var travel = GamebryoPackageTravel.ArriveAtSourceTarget(
            package.FormId,
            placement,
            coverage.Cg01DadActor.Placement.Transform,
            GamebryoPackageTravel.ExactArrivalToleranceCellUnits);
        travel.Publish(coverage.Cg01DadActor.Placement);
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
        ApplyCg01DadPackage(_profile.Cg01PostStage14Transition.LeaveRoomPackage, stage5);
        SetCg01WorldReferenceOpen(state.PlayroomDoorReferenceFormId, state.PlayroomDoorOpen);
        SetCg01WorldReferenceLock(
            state.PlayroomDoorReferenceFormId,
            state.PlayroomDoorLockLevel);
        SetCg01WorldReferenceOpen(
            state.PlaypenGateReferenceFormId,
            state.PlaypenGateOpen);
        var world = _cg01ToddlerWorld ?? throw new InvalidOperationException(
            "Fallout 3 CG01 stage-20 restore has no toddler world.");
        if (!world.Player.MovementEnabled ||
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
                Persist();
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
        if (current.ActiveStage == interaction.BookStage && !current.SpecialBookAccepted)
            Book();
        else if (current.TimerAdvancing)
            StartStage50Timer();
    }

    private Node3D Cg01WorldReference(string formId) =>
        (_vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG01 reference has no owned world."))
        .CellRoot.GetChildren().OfType<Node3D>().Single(node =>
            node.HasMeta("opennv_source_form_id") &&
            node.GetMeta("opennv_source_form_id").AsString().Equals(
                formId,
                StringComparison.OrdinalIgnoreCase));

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
