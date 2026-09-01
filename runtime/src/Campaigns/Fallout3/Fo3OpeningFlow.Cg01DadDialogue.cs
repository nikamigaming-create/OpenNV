using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
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

}
