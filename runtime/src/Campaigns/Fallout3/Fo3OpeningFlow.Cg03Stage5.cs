using System.Security.Cryptography;
using Godot;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal sealed record Fo3Cg03Stage5Progress(
    int Stage,
    double TimerRemainingSeconds,
    bool TimerAdvancing,
    string? AppliedInfoFormId,
    string AppliedPackageFormId,
    int AppliedCommandCount,
    string NextBoundaryBlocker);

internal partial class Fo3OpeningFlow
{
    private void StartCg03Stage5Runtime(
        Fo3Cg03Stage5Runtime runtime,
        Fo3Cg01ToddlerPlayer player,
        int restoredStage,
        double restoredSeconds,
        bool sourceApplied,
        Action<Fo3Cg03Stage5Progress> progress)
    {
        var dad = EnsureCg03Dad(runtime);
        ApplyCg03Stage5SourceState(runtime, player, dad);
        if (restoredStage == runtime.SpeechStage)
        {
            dad.Placement.SetMeta("opennv_active_package_form_id",
                runtime.DadTalkPackageFormId);
            return;
        }
        if (restoredStage != runtime.SourceStage)
            throw new InvalidOperationException(
                "Fallout 3 CG03 Dad speech stage differs.");
        dad.Placement.SetMeta("opennv_active_package_form_id",
            runtime.DadHoldPackageFormId);

        void BeginTimer()
        {
            var remaining = restoredSeconds > 0.0
                ? restoredSeconds
                : runtime.TimerSeconds;
            progress(new Fo3Cg03Stage5Progress(
                runtime.SourceStage, remaining, true, null,
                runtime.DadHoldPackageFormId,
                0,
                runtime.NextBoundaryBlocker));
            _cg03DadSpeechTick = delta =>
            {
                remaining = Math.Max(0.0, remaining - delta);
                progress(new Fo3Cg03Stage5Progress(
                    runtime.SourceStage, remaining, remaining > 0.0, null,
                    runtime.DadHoldPackageFormId, 0,
                    runtime.NextBoundaryBlocker));
                if (remaining > 0.0)
                    return;
                _cg03DadSpeechTick = null;
                PlayCg03DadSpeech(runtime, player, dad, progress);
            };
        }

        if (restoredSeconds > 0.0)
            BeginTimer();
        else
        {
            if (!sourceApplied)
                progress(new Fo3Cg03Stage5Progress(
                    runtime.SourceStage, 0.0, false, null,
                    runtime.DadHoldPackageFormId,
                    runtime.Stage5CommandCount,
                    runtime.NextBoundaryBlocker));
            _cg03TransitionBegin = BeginTimer;
            StartCg03TransitionMovie(runtime.Movie);
        }
    }

    private CellActorLoader.PlacedActor EnsureCg03Dad(Fo3Cg03Stage5Runtime runtime)
    {
        if (_cg02IntroActors.TryGetValue(runtime.DadReferenceFormId, out var existing))
            return existing;
        using var stream = File.OpenRead(runtime.DadActorScenePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actual.Equals(runtime.DadActorSceneSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Fallout 3 CG03 Dad actor scene hash differs.");
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG03 Dad world is absent.");
        var actor = CellActorLoader.Load(
                runtime.DadActorScenePath,
                new HashSet<string>([coverage.Contract.CellFormId],
                    StringComparer.OrdinalIgnoreCase),
                coverage.CellRoot,
                coverage.Contract.EntryPositionGameUnits,
                _runtimeConfiguration,
                proofEnableInitiallyDisabled: false,
                materializeInitiallyDisabled: true)
            ?? throw new InvalidOperationException(
                "Fallout 3 CG03 Dad actor is absent.");
        if (actor.ReferenceFormId != runtime.DadReferenceFormId ||
            actor.BaseFormId != runtime.DadBaseFormId)
            throw new InvalidOperationException(
                "Fallout 3 CG03 Dad actor identity differs.");
        _cg02IntroActors.Add(actor.ReferenceFormId, actor);
        return actor;
    }

    private void ApplyCg03Stage5SourceState(
        Fo3Cg03Stage5Runtime runtime,
        Fo3Cg01ToddlerPlayer player,
        CellActorLoader.PlacedActor dad)
    {
        dad.Placement.Visible = true;
        dad.Placement.ProcessMode = ProcessModeEnum.Inherit;
        dad.Placement.SetMeta("opennv_ignore_crime", 1);
        player.SetMeta("opennv_location_specific_load_screens_only", 1);
        player.SetMeta("opennv_in_char_gen", 1);
        player.SetMeta("opennv_player_controls_disabled", 1);
        player.SetMeta("opennv_health_reset", 1);
        player.SetMeta("opennv_player_young", 1);
        player.SetMeta("opennv_inventory_cleared", 1);
        player.SetMeta($"opennv_cg03_item_{runtime.VaultSuitFormId}", 1);
        player.SetMeta($"opennv_cg03_item_{runtime.PipBoyFormId}", 1);
        player.SetMeta("opennv_equipped_item_form_id", runtime.VaultSuitFormId);
        player.SetMeta("opennv_equipped_pipboy_form_id", runtime.PipBoyFormId);
        var radio = Cg01WorldReference(runtime.RadioReferenceFormId);
        radio.Visible = true;
        radio.ProcessMode = ProcessModeEnum.Inherit;
        Cg01WorldReference(runtime.Cg02HiddenPlaneReferenceFormId).Visible = true;
        Cg01WorldReference(runtime.Cg03HiddenPlaneReferenceFormId).Visible = false;
    }

    private void PlayCg03DadSpeech(
        Fo3Cg03Stage5Runtime runtime,
        Fo3Cg01ToddlerPlayer player,
        CellActorLoader.PlacedActor dad,
        Action<Fo3Cg03Stage5Progress> progress)
    {
        var sex = (_selectedSex ?? throw new InvalidOperationException(
            "Fallout 3 CG03 player sex is absent.")).EngineSex;
        var cue = runtime.Cues.Single(value => value.EngineSex == sex);
        var animation = dad.Actor.LoadedAnimations.Single(value =>
            ActorModelSlice.NormalizeAnimationPath(value.LogicalPath).Equals(
                ActorModelSlice.NormalizeAnimationPath(
                    cue.SpeakerIdleLogicalPath),
                StringComparison.OrdinalIgnoreCase) &&
            value.SourceSha256.Equals(cue.SpeakerIdleSourceSha256,
                StringComparison.OrdinalIgnoreCase));
        _cg02IntroAnimations[runtime.DadReferenceFormId] =
            ActorAnimationPlayback.Start(dad.Actor, animation);
        var voice = new AudioStreamPlayer { Name = "Fallout3Cg03DadSpeechVoice" };
        AddChild(voice);
        var dialogue = new GamebryoDialoguePlayback(
            voice, _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
        _cg02IntroDialogue.Add(dialogue);
        dialogue.Start(
            new SourceDialogueLine(
                cue.InfoFormId,
                cue.Response.Index,
                runtime.DadBaseFormId,
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
                dad.Placement.SetMeta("opennv_dotalk", 0);
                dad.Placement.SetMeta("opennv_evaluate_package", 1);
                dad.Placement.SetMeta("opennv_active_package_form_id",
                    runtime.DadTalkPackageFormId);
                player.SetMeta("opennv_cg03_stage", runtime.SpeechStage);
                player.SetMeta($"opennv_quest_stage_{runtime.QuestFormId}",
                    runtime.SpeechStage);
                progress(new Fo3Cg03Stage5Progress(
                    runtime.SpeechStage, 0.0, false, cue.InfoFormId,
                    runtime.DadTalkPackageFormId,
                    runtime.Stage6CommandCount + 1,
                    runtime.NextBoundaryBlocker));
            });
    }

    private void StartCg03TransitionMovie(Fo3Cg01OwnedMovie movie)
    {
        if (_video is not null || _ownedVideoMode != Fo3OwnedVideoMode.None)
            throw new InvalidOperationException(
                "Fallout 3 CG03 transition movie is already active.");
        _ownedVideoMode = Fo3OwnedVideoMode.Cg03Transition;
        _introLayer = new Control { Name = "Fallout3OwnedCg03Transition" };
        _introLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_introLayer);
        var black = new ColorRect { Color = Colors.Black };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _introLayer.AddChild(black);
        _video = new VideoStreamPlayer
        {
            Name = "Fallout3OwnedCg03TransitionVideo",
            Stream = new VideoStreamTheora { File = movie.RuntimeOutput },
            Expand = true,
            Loop = false,
            Visible = false,
        };
        _video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _video.Finished += () => CompleteOwnedVideo(false);
        _introLayer.AddChild(_video);
        var skip = Button("SKIP  •  ESC");
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

    private void CompleteCg03TransitionMovie(bool skipped)
    {
        if (_ownedVideoMode != Fo3OwnedVideoMode.Cg03Transition)
            return;
        ClearOwnedVideo();
        var begin = _cg03TransitionBegin ?? throw new InvalidOperationException(
            "Fallout 3 CG03 transition continuation is absent.");
        _cg03TransitionBegin = null;
        begin();
        GD.Print(
            $"OPENNV_FO3_CG03_TRANSITION_MOVIE_COMPLETE " +
            $"mode={(skipped ? "skipped" : "watched")}");
    }
}
