using Godot;
using OpenNV.Runtime;
using System.Text.Json;


using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Actors;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
    private Fo3Cg00EarlyBirthSequence? _cg00EarlySequence;
    private int? _cg00EarlyStage;
    private double _cg00EarlyTimerSeconds;
    private int? _cg00EarlyTimerTargetStage;
    private readonly List<Fo3Cg00ImageSpaceLayer> _cg00EarlyImageSpaceLayers = [];
    private Label? _cg00EarlySubtitle;
    private AudioStreamPlayer? _cg00EarlyVoice;
    private FaceGenLipAnimation? _cg00EarlyLip;
    private FaceGenMorphController? _cg00EarlyFace;
    private string? _cg00EarlyInfoFormId;
    private string? _cg00EarlyPlayerName;
    private bool _cg00EarlySexMenuActive;
    private readonly List<int> _cg00EarlyStageHistory = [];
    private readonly HashSet<string> _cg00EarlyInfoHistory = new(StringComparer.OrdinalIgnoreCase);
    private bool _cg00EarlyProofDriving;
    private bool _cg00Stage10PresentationFrozen;
    private int _cg00EarlyCreatorEntryCount;
    private bool _cg00EarlyBirthCaptureStarted;
    private bool _cg00EarlyPresentationFinalizing;
    private Fo3AppearanceProofCapture? _cg00EarlyBirthPresentationCapture;
    private Fo3Cg00PlayerCameraTransform? _cg00EarlyPlayerCamera;
    private double _cg00EarlyPlayerCameraSeconds;
    private readonly Dictionary<string, Fo3Cg00ActorPackagePlayback> _cg00ActorPackages =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Fo3Cg00RetailParticipantTelemetry>
        _cg00Stage10Projection =
        new(StringComparer.Ordinal);
    private Fo3Cg00RetailStage10JoinTelemetry? _cg00RetailStage10Telemetry;

    private sealed class Fo3Cg00ImageSpaceLayer(
        Fo3Cg00ImageSpaceModifier contract,
        ColorRect surface)
    {
        internal Fo3Cg00ImageSpaceModifier Contract { get; } = contract;
        internal ColorRect Surface { get; } = surface;
        internal double ElapsedSeconds { get; set; }
    }

    private sealed class Fo3Cg00ActorPackagePlayback(
        Fo3Cg00PackageSection contract,
        ActorAnimationPlayback animation)
    {
        internal Fo3Cg00PackageSection Contract { get; set; } = contract;
        internal ActorAnimationPlayback Animation { get; } = animation;
        internal double ElapsedSeconds => Animation.PositionSeconds;
    }

    private void StartCg00EarlyBirthSequence()
    {
        if (_birthPresentation is null)
            throw new InvalidOperationException(
                "Fallout 3 exact early CG00 sequence requires the owned Vault presentation.");
        _cg00EarlySequence = _profile.EarlyBirthSequence;
        _selectedSex = null;
        _cg00EarlyPlayerName = null;
        _cg00EarlyStageHistory.Clear();
        _cg00EarlyInfoHistory.Clear();
        _cg00EarlyCreatorEntryCount = 0;
        _cg00Stage10PresentationFrozen = false;
        _cg00ActorPackages.Clear();
        _cg00Stage10Projection.Clear();
        _cg00RetailStage10Telemetry = null;
        EnsureCg00EarlyWorld();
        ApplyCg00EarlyStage(_cg00EarlySequence.Stages.Keys.Min());
    }

    private void EnsureCg00EarlyWorld()
    {
        if (_vaultPreviewHost is not null)
            return;
        var defaultSex = _profile.SexChoices.Single(value => value.EngineSex == "male");
        var selection = _profile.Appearance.DefaultSelection(defaultSex.EngineSex);
        var stage65 = _profile.Stage65Appearance.Apply(
            defaultSex.EngineSex,
            selection.Race.FormId,
            selection.Sex.FaceGen);
        var host = new Node3D { Name = "FO3_VAULT101_CG00_EARLY_WORLD" };
        _worldHost.AddChild(host);
        try
        {
            var futureDad = _birthPresentation!.Cg01DadActorFor(
                selection.Race.FormId,
                defaultSex.EngineSex,
                stage65);
            _vaultBirthCoverage = Fo3Vault101BirthScene.Build(
                host,
                _birthPresentation,
                futureDad);
        }
        catch
        {
            host.QueueFree();
            throw;
        }
        _vaultPreviewHost = host;
        _vaultBirthCoverage.DoctorActor.Placement.Visible = true;
        _vaultBirthCoverage.DadActor.Placement.Visible = true;
        _vaultBirthCoverage.MomActor.Placement.Visible = true;
        _vaultBirthCoverage.Cg01DadActor.Placement.Visible = false;
        _background.Visible = false;
        _panel.Visible = false;
    }

    private void UpdateCg00EarlyBirth(double delta)
    {
        if (_cg00EarlySequence is null)
            return;
        if (_cg00Stage10PresentationFrozen)
            return;
        UpdateCg00ActorPackages(delta);
        UpdateCg00PlayerCamera(delta);
        UpdateCg00ImageSpace(delta);
        if (_cg00EarlyVoice is not null && _cg00EarlyVoice.Playing &&
            _cg00EarlyLip is not null && _cg00EarlyFace is not null)
        {
            if (_cg00EarlyVoice.GetMeta("opennv_info_form_id").AsString() !=
                _cg00EarlyInfoFormId)
                throw new InvalidOperationException(
                    "Fallout 3 early CG00 voice and LIP INFO clocks diverged.");
            _cg00EarlyFace.Apply(_cg00EarlyLip, _cg00EarlyVoice.GetPlaybackPosition());
        }
        if (_cg00EarlyTimerTargetStage is null)
            return;
        _cg00EarlyTimerSeconds = Math.Max(0.0, _cg00EarlyTimerSeconds - delta);
        if (_cg00EarlyTimerSeconds > 0.0)
            return;
        var target = _cg00EarlyTimerTargetStage.Value;
        _cg00EarlyTimerTargetStage = null;
        var sexPackage = _cg00EarlySequence.PackageSections["player"].Single(value =>
            value.Section == 2);
        if (_cg00EarlySequence.Stages[target].Commands.Any(value => value.EndsWith(
                sexPackage.PackageEditorId, StringComparison.OrdinalIgnoreCase)))
        {
            _cg00EarlySexMenuActive = true;
            ShowSexSelection();
            return;
        }
        ApplyCg00EarlyStage(target);
    }

    private void ApplyCg00EarlyStage(int stage)
    {
        var sequence = _cg00EarlySequence ?? throw new InvalidOperationException(
            "Fallout 3 early CG00 sequence is absent.");
        if (!sequence.Stages.TryGetValue(stage, out var source))
            throw new InvalidOperationException("Fallout 3 early CG00 stage is outside the source closure.");
        _cg00EarlyStage = stage;
        if (_cg00EarlyStageHistory.Contains(stage))
            throw new InvalidOperationException("Fallout 3 early CG00 stage replayed.");
        _cg00EarlyStageHistory.Add(stage);
        GD.Print(
            $"OPENNV_FO3_CG00_EARLY_STAGE_APPLIED stage={stage} " +
            $"source={source.SourceSha256} commands={source.Commands.Count}");

        if (stage == sequence.Stages.Keys.Min())
            ApplyCg00ParticipantStartMarkers();

        foreach (var command in source.Commands.Where(value =>
                     value.StartsWith("imod ", StringComparison.OrdinalIgnoreCase)))
            StartCg00ImageSpace(command["imod ".Length..]);
        foreach (var command in source.Commands.Where(value =>
                     value.StartsWith("playSound ", StringComparison.OrdinalIgnoreCase)))
            PlayCg00Sound(command["playSound ".Length..]);

        var participantPackageEvaluations = source.Commands.Where(value =>
                value.EndsWith(".evp", StringComparison.OrdinalIgnoreCase))
            .Select(value => value[..^".evp".Length])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        EvaluateCg00ParticipantPackages(stage, participantPackageEvaluations);

        var playerPackage = source.Commands.SingleOrDefault(value =>
            value.StartsWith("player.addScriptPackage ", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("player.addscriptpackage ", StringComparison.OrdinalIgnoreCase));
        if (playerPackage is not null)
        {
            var packageEditorId = playerPackage.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            var section = sequence.PackageSections["player"].Single(value =>
                value.PackageEditorId.Equals(
                    packageEditorId, StringComparison.OrdinalIgnoreCase)).Section;
            PublishCg00PlayerSection(section);
        }

        var directStageCommands = source.Commands.Where(value => value.StartsWith(
                "setstage CG00 ", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (directStageCommands.Length > 1)
            throw new InvalidOperationException(
                "Fallout 3 early CG00 stage has ambiguous nested stage commands.");
        if (directStageCommands.Length == 1)
        {
            var target = int.Parse(
                directStageCommands[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)[2]);
            ApplyCg00EarlyStage(target);
            return;
        }

        if (source.Commands.Contains("GetPlayerName", StringComparer.OrdinalIgnoreCase))
        {
            ShowNameSelection(_selectedSex ?? throw new InvalidOperationException(
                "Fallout 3 early CG00 name stage has no selected sex."));
            return;
        }
        if (source.Commands.Contains("ShowRaceMenu", StringComparer.OrdinalIgnoreCase))
        {
            var sex = _selectedSex ?? throw new InvalidOperationException(
                "Fallout 3 early CG00 RaceSex stage has no selected sex.");
            PublishCg00PlayerSection(4);
            _cg00EarlyStage = _profile.Appearance.MenuEnteredStage;
            if (_cg00EarlyStageHistory.Contains(_profile.Appearance.MenuEnteredStage))
                throw new InvalidOperationException("Fallout 3 RaceSex menu-entry stage replayed.");
            _cg00EarlyStageHistory.Add(_profile.Appearance.MenuEnteredStage);
            _cg00EarlyCreatorEntryCount++;
            ShowAppearanceSelection(
                _cg00EarlyPlayerName ?? throw new InvalidOperationException(
                    "Fallout 3 early CG00 RaceSex stage has no player name."),
                sex);
            GD.Print(
                $"OPENNV_FO3_CG00_EARLY_STAGE_APPLIED " +
                $"stage={_profile.Appearance.MenuEnteredStage} menu=RaceSexMenu");
            return;
        }
        var talkStages = sequence.Stages.Values.Where(value =>
                value.Commands.Any(command => command.Contains(
                    "CG00DadREF.doTalk to 1", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(value => value.Stage).Select(value => value.Stage).ToArray();
        var talkIndex = Array.IndexOf(talkStages, stage);
        if (talkIndex >= 0)
        {
            var cues = talkIndex switch
            {
                0 => sequence.Stage10Dialogue,
                1 => sequence.Stage22Dialogue[_selectedSex?.EngineSex ?? throw new
                    InvalidOperationException("Fallout 3 stage-22 dialogue has no sex.")],
                2 => sequence.Stage42Dialogue,
                _ => throw new InvalidOperationException(
                    "Fallout 3 early CG00 Dad talk stage count differs."),
            };
            PlayCg00Dialogue(cues, 0);
            if (_appearanceProofMode is "early-presentation" or "stage10-presentation" &&
                talkIndex == 0)
                CaptureCg00EarlyBirthPresentation();
            return;
        }
        if (sequence.TimerTransitions.TryGetValue(stage, out var timerTarget))
        {
            if (source.Commands.Any(value => value.Contains(
                    "ShowMessage", StringComparison.OrdinalIgnoreCase)))
            {
                ShowSexSelection();
                return;
            }
            ScheduleCg00Timer(stage, timerTarget);
        }
    }

    private void ScheduleCg00Timer(int stage, int targetStage)
    {
        var sequence = _cg00EarlySequence!;
        _cg00EarlyTimerSeconds = sequence.TimerSeconds(stage);
        _cg00EarlyTimerTargetStage = targetStage;
        GD.Print(
            $"OPENNV_FO3_CG00_TIMER_STARTED stage={stage} target={targetStage} " +
            $"seconds={_cg00EarlyTimerSeconds:F2}");
    }

    private void SelectCg00EarlySex(Fo3SexChoice sex)
    {
        _selectedSex = sex;
        _cg00EarlySexMenuActive = false;
        ClearContent();
        var sequence = _cg00EarlySequence!;
        var sexStage = sequence.TimerTransitions.Single(value =>
            sequence.PackageSections["player"].Single(row => row.Section == 2)
                .PackageEditorId.Equals(
                    sequence.Stages[value.Value].Commands
                        .SingleOrDefault(command => command.StartsWith(
                            "player.addScriptPackage ",
                            StringComparison.OrdinalIgnoreCase))?
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(),
                    StringComparison.OrdinalIgnoreCase));
        ApplyCg00EarlyStage(sexStage.Value);
    }

    private void ResumeCg00AfterName(string playerName)
    {
        _cg00EarlyPlayerName = playerName;
        var sequence = _cg00EarlySequence!;
        var nameStage = sequence.StageWithCommand("GetPlayerName");
        var target = sequence.TimerTransitions[nameStage];
        ScheduleCg00Timer(nameStage, target);
    }

    private void PlayCg00Dialogue(IReadOnlyList<Fo3Cg00DialogueCue> cues, int index)
    {
        if (index < 0 || index >= cues.Count)
            throw new InvalidOperationException("Fallout 3 early CG00 dialogue cursor differs.");
        var cue = cues[index];
        if (!_cg00EarlyInfoHistory.Add(cue.InfoFormId))
            throw new InvalidOperationException("Fallout 3 early CG00 INFO replayed.");
        _cg00EarlyVoice?.Stop();
        _cg00EarlyVoice?.QueueFree();
        _cg00EarlyFace?.Clear();
        _cg00EarlyLip = FaceGenLipAnimation.Load(
            cue.Lip.SourcePath,
            _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
        _cg00EarlyInfoFormId = cue.InfoFormId;
        var actor = ActorForCg00Role(cue.SpeakerRole);
        _cg00EarlyFace = new FaceGenMorphController(
            actor.Actor,
            _runtimeConfiguration.ActorCompiler.FaceGenAnimation.Lip);
        var stream = AudioStreamOggVorbis.LoadFromFile(cue.Voice.SourcePath)
            ?? throw new InvalidOperationException("Fallout 3 early CG00 voice could not be decoded.");
        _cg00EarlyVoice = new AudioStreamPlayer
        {
            Name = $"FO3_CG00_{cue.InfoFormId}_VOICE",
            Stream = stream,
        };
        _cg00EarlyVoice.SetMeta("opennv_info_form_id", cue.InfoFormId);
        _cg00EarlyVoice.SetMeta("opennv_speaker_role", cue.SpeakerRole);
        _cg00EarlyVoice.Finished += () =>
        {
            _cg00EarlyFace?.Clear();
            _cg00EarlyLip = null;
            _cg00EarlyInfoFormId = null;
            _cg00EarlyVoice?.QueueFree();
            _cg00EarlyVoice = null;
            if (index + 1 < cues.Count)
            {
                PlayCg00Dialogue(cues, index + 1);
                return;
            }
            CompleteCg00Dialogue(cues[^1]);
        };
        AddChild(_cg00EarlyVoice);
        _cg00EarlySubtitle ??= AddVaultDialogueOverlay("FO3_CG00_EARLY_DIALOGUE");
        _cg00EarlySubtitle.Text = $"{actor.Actor.Name.ToUpperInvariant()}: {cue.Text}";
        _cg00EarlySubtitle.Visible = true;
        _vaultPreviewOverlay?.MoveToFront();
        if (_cg00EarlyImageSpaceLayers.Any(layer =>
                layer.Surface.GetIndex() >= _vaultPreviewOverlay?.GetIndex()))
            throw new InvalidOperationException(
                "Fallout 3 early CG00 subtitle is below the image-space layer.");
        _cg00EarlyVoice.Play();
        GD.Print(
            $"OPENNV_FO3_CG00_INFO_STARTED stage={_cg00EarlyStage} info={cue.InfoFormId} " +
            $"speaker={cue.SpeakerRole} voice={cue.Voice.Sha256} lip={cue.Lip.Sha256}");
    }

    private void CompleteCg00Dialogue(Fo3Cg00DialogueCue cue)
    {
        var stageCommand = cue.ResultCommands.LastOrDefault(value =>
            value.StartsWith("setstage CG00 ", StringComparison.OrdinalIgnoreCase));
        if (stageCommand is null)
            throw new InvalidOperationException("Fallout 3 early CG00 dialogue has no stage result.");
        var target = int.Parse(stageCommand.Split(' ', StringSplitOptions.RemoveEmptyEntries)[2]);
        ApplyCg00EarlyStage(target);
    }

    private void EvaluateCg00ParticipantPackages(
        int stage,
        IReadOnlySet<string> evaluatedReferences)
    {
        var sequence = _cg00EarlySequence ?? throw new InvalidOperationException(
            "Fallout 3 early CG00 package evaluation has no source sequence.");
        var expectedReferences = sequence.SceneParticipants.Values
            .Select(value => value.ReferenceEditorId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (evaluatedReferences.Count != 0 &&
            !evaluatedReferences.SetEquals(expectedReferences))
            throw new InvalidOperationException(
                "Fallout 3 early CG00 participant package evaluation set differs.");
        foreach (var role in sequence.SceneParticipants.Keys)
        {
            var candidates = sequence.PackageSections[role]
                .Select(package => new GamebryoPackageCandidate<Fo3Cg00PackageSection>(
                    package.PackageFormId,
                    package.ActivationCondition is { } condition
                        ? [new GamebryoPackageCondition(
                            "getStage",
                            GamebryoPackageComparison.Equal,
                            condition.Stage,
                            condition.QuestFormId,
                            0,
                            (uint)condition.RunOn,
                            "")]
                        : [],
                    GamebryoPackageTarget.None,
                    new SourceActorAnimation(
                        package.AnimationLogicalPath,
                        package.AnimationSha256,
                        package.AnimationSequenceName,
                        (float)package.AnimationStartSeconds,
                        (float)package.AnimationStopSeconds,
                        package.AnimationCycleType,
                        "owned-world-root-authoritative-zero-local-translation"),
                    package))
                .ToArray();
            var selected = GamebryoPackageSelector.SelectFirst(
                candidates,
                new GamebryoPackageState(
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        [sequence.QuestFormId] = stage,
                    },
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)),
                requireMatch: false);
            if (selected is null)
                continue;
            if (_cg00ActorPackages.TryGetValue(role, out var current) &&
                current.Contract.PackageFormId == selected.Value.PackageFormId)
                continue;
            StartCg00ActorPackage(
                role,
                selected.Value,
                selected.Animation ?? throw new InvalidOperationException(
                    "Fallout 3 selected actor package has no source animation."),
                selected.Value.AnimationStartSeconds);
        }
    }

    private void PublishCg00PlayerSection(int section)
    {
        var sequence = _cg00EarlySequence ?? throw new InvalidOperationException(
            "Fallout 3 early CG00 player package has no source sequence.");
        if (section == sequence.PlayerCamera.Section)
        {
            _cg00EarlyPlayerCamera = sequence.PlayerCamera;
            _cg00EarlyPlayerCameraSeconds = _cg00EarlyPlayerCamera.StartSeconds;
            ApplyCg00PlayerCameraTransform();
            GD.Print(
                $"OPENNV_FO3_CG00_PLAYER_CAMERA_PUBLISHED stage={_cg00EarlyStage} " +
                $"section={section} package={_cg00EarlyPlayerCamera.PackageFormId} " +
                $"idle={_cg00EarlyPlayerCamera.IdleFormId} " +
                $"animation={_cg00EarlyPlayerCamera.Animation.Sha256} " +
                $"skeleton={_cg00EarlyPlayerCamera.Skeleton.Sha256} " +
                $"samples={_cg00EarlyPlayerCamera.Samples.Count}");
        }
    }

    private void UpdateCg00ActorPackages(double delta)
    {
        if (delta < 0.0 || !double.IsFinite(delta))
            throw new InvalidOperationException(
                "Fallout 3 early CG00 package delta is invalid.");
        foreach (var role in _cg00ActorPackages.Keys.ToArray())
        {
            var playback = _cg00ActorPackages[role];
            playback.Animation.Advance(delta);
        }
    }

    private void StartCg00ActorPackage(
        string role,
        Fo3Cg00PackageSection contract,
        SourceActorAnimation animation,
        double elapsedSeconds)
    {
        if (elapsedSeconds < contract.AnimationStartSeconds ||
            elapsedSeconds >= contract.AnimationStopSeconds)
            throw new InvalidOperationException(
                "Fallout 3 early CG00 package start clock differs.");
        var actor = ActorForCg00Role(role);
        var playback = ActorAnimationPlayback.Start(
            actor.Actor,
            animation,
            elapsedSeconds);
        _cg00ActorPackages[role] = new Fo3Cg00ActorPackagePlayback(
            contract,
            playback);
        GD.Print(
            $"OPENNV_FO3_CG00_PACKAGE_PUBLISHED stage={_cg00EarlyStage} role={role} " +
            $"section={contract.Section} package={contract.PackageFormId} " +
            $"activationStage={contract.ActivationCondition?.Stage} " +
            $"idle={contract.IdleFormId} clock={elapsedSeconds:R} " +
            $"stop={contract.AnimationStopSeconds:R} change={contract.ChangeIdleFormId}");
    }

    private void UpdateCg00PlayerCamera(double delta)
    {
        if (_cg00EarlyPlayerCamera is null)
            return;
        _cg00EarlyPlayerCameraSeconds = Math.Min(
            _cg00EarlyPlayerCamera.StopSeconds,
            _cg00EarlyPlayerCameraSeconds + delta);
        ApplyCg00PlayerCameraTransform();
    }

    private void ApplyCg00PlayerCameraTransform()
    {
        var contract = _cg00EarlyPlayerCamera ?? throw new InvalidOperationException(
            "Fallout 3 CG00 player camera transform has no source contract.");
        var sequence = _cg00EarlySequence ?? throw new InvalidOperationException(
            "Fallout 3 CG00 player camera transform has no early sequence.");
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG00 player camera transform has no Vault world.");
        var marker = sequence.PlayerStartMarker;
        if (marker.FormId != contract.PlayerStartMarkerFormId ||
            !Mathf.IsZeroApprox(marker.AuthoredTransform.RotationRadians.X) ||
            !Mathf.IsZeroApprox(marker.AuthoredTransform.RotationRadians.Y))
            throw new InvalidOperationException(
                "Fallout 3 CG00 player camera marker join differs.");
        var local = new Transform3D(
            new Basis(contract.PlayerStartMarkerRotation)
                .Scaled(Vector3.One * marker.AuthoredTransform.Scale),
            GamebryoCoordinate.ConvertVector(
                marker.AuthoredTransform.PositionGameUnits -
                coverage.Contract.EntryPositionGameUnits));
        for (var index = 0; index < contract.ParentChain.Count; index++)
        {
            var parent = contract.ParentChain[index];
            var animated = contract.AnimatedParentTracks.SingleOrDefault(value =>
                value.ParentChainIndex == index);
            var parentTranslation = parent.TranslationGodotGameUnits;
            var parentRotation = parent.Rotation;
            if (animated is not null)
            {
                var parentSample = SampleCg00Camera(
                    animated.Samples,
                    _cg00EarlyPlayerCameraSeconds);
                parentTranslation = parentSample.TranslationGodotGameUnits;
                parentRotation = parentSample.Rotation;
            }
            local *= new Transform3D(
                new Basis(parentRotation).Scaled(Vector3.One * parent.Scale),
                parentTranslation);
        }
        var sample = SampleCg00PlayerCamera(contract, _cg00EarlyPlayerCameraSeconds);
        var sampledNodeBasis = new Basis(sample.Rotation);
        var cameraBasis = Cg00CameraBasisFromSampledNode(sampledNodeBasis);
        local *= new Transform3D(cameraBasis, sample.TranslationGodotGameUnits);
        var scaledWorld = coverage.CellRoot.GlobalTransform * local;
        var rigidWorldBasis = scaledWorld.Basis.Orthonormalized();
        if (!rigidWorldBasis.IsFinite() || rigidWorldBasis.Determinant() <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 3 CG00 camera world rotation is invalid.");
        coverage.Camera.GlobalTransform = new Transform3D(
            rigidWorldBasis,
            scaledWorld.Origin);
        coverage.Camera.SetMeta(
            "opennv_cg00_player_camera_sample_contract_sha256",
            contract.SampleContractSha256);
        coverage.Camera.SetMeta(
            "opennv_cg00_player_camera_time_seconds",
            _cg00EarlyPlayerCameraSeconds);
        coverage.Camera.SetMeta(
            "opennv_cg00_player_camera_animation_sha256",
            contract.Animation.Sha256);
    }

    internal static Basis Cg00CameraBasisFromSampledNode(Basis sampledNodeBasis)
    {
        if (!sampledNodeBasis.IsFinite())
            throw new InvalidOperationException(
                "Fallout 3 CG00 sampled camera node basis is invalid.");
        // Camera1st is a standard skeleton node, not a NiCamera. The generic
        // Gamebryo-to-Godot conversion already makes its local -Z the view axis.
        var cameraBasis = sampledNodeBasis;
        if (!cameraBasis.X.IsEqualApprox(sampledNodeBasis.X) ||
            !cameraBasis.Y.IsEqualApprox(sampledNodeBasis.Y) ||
            !cameraBasis.Z.IsEqualApprox(sampledNodeBasis.Z) ||
            cameraBasis.Determinant() <= 0.0f)
            throw new InvalidOperationException(
                "Fallout 3 CG00 Camera3D basis conversion differs.");
        return cameraBasis;
    }

    private static Fo3Cg00CameraSample SampleCg00PlayerCamera(
        Fo3Cg00PlayerCameraTransform contract,
        double timeSeconds) =>
        SampleCg00Camera(contract.Samples, timeSeconds);

    private static Fo3Cg00CameraSample SampleCg00Camera(
        IReadOnlyList<Fo3Cg00CameraSample> samples,
        double timeSeconds)
    {
        if (samples.Count < 2)
            throw new InvalidOperationException(
                "Fallout 3 CG00 camera sample track is incomplete.");
        var clamped = Math.Clamp(
            timeSeconds,
            samples[0].TimeSeconds,
            samples[^1].TimeSeconds);
        var upperIndex = 1;
        while (upperIndex < samples.Count &&
            samples[upperIndex].TimeSeconds < clamped)
            upperIndex++;
        if (upperIndex >= samples.Count)
            return samples[^1];
        var lower = samples[upperIndex - 1];
        var upper = samples[upperIndex];
        var duration = upper.TimeSeconds - lower.TimeSeconds;
        var weight = duration <= 0.0
            ? 0.0f
            : (float)((clamped - lower.TimeSeconds) / duration);
        return new Fo3Cg00CameraSample(
            clamped,
            lower.TranslationGodotGameUnits.Lerp(
                upper.TranslationGodotGameUnits,
                weight),
            lower.Rotation.Slerp(upper.Rotation, weight).Normalized());
    }

    private void ApplyCg00ParticipantStartMarkers()
    {
        var sequence = _cg00EarlySequence ?? throw new InvalidOperationException(
            "Fallout 3 early CG00 marker application has no sequence.");
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 early CG00 marker application has no Vault world.");
        foreach (var participant in sequence.SceneParticipants.Values)
        {
            var actor = ActorForCg00Role(participant.Role);
            if (!actor.ReferenceFormId.Equals(
                    participant.ReferenceFormId,
                    StringComparison.OrdinalIgnoreCase) ||
                !Mathf.IsZeroApprox(participant.StartMarkerTransform.RotationRadians.X) ||
                !Mathf.IsZeroApprox(participant.StartMarkerTransform.RotationRadians.Y))
                throw new InvalidOperationException(
                    $"Fallout 3 CG00 {participant.Role} marker join differs.");
            actor.Placement.Position = ParticipantMarkerLocal(participant, coverage);
            actor.Placement.Rotation = new Vector3(
                0.0f,
                -participant.StartMarkerTransform.RotationRadians.Z,
                0.0f);
            actor.Placement.Scale = Vector3.One * participant.ReferenceTransform.Scale;
            actor.Placement.SetMeta(
                "opennv_cg00_start_marker_form_id",
                participant.StartMarkerFormId);
            GD.Print(
                $"OPENNV_FO3_CG00_MARKER_APPLIED role={participant.Role} " +
                $"reference={participant.ReferenceFormId} marker={participant.StartMarkerFormId} " +
                $"position={actor.Placement.Position} yaw={actor.Placement.Rotation.Y:R}");
        }
    }

    private static Vector3 ParticipantMarkerLocal(
        Fo3Cg00SceneParticipant participant,
        Fo3Vault101BirthSceneCoverage coverage) =>
        GamebryoCoordinate.ConvertVector(
            participant.StartMarkerTransform.PositionGameUnits -
            coverage.Contract.EntryPositionGameUnits);

    private CellActorLoader.PlacedActor ActorForCg00Role(string role) => role switch
    {
        "father" => _vaultBirthCoverage!.DadActor,
        "doctor" => _vaultBirthCoverage!.DoctorActor,
        "mother" => _vaultBirthCoverage!.MomActor,
        _ => throw new InvalidOperationException("Fallout 3 early CG00 speaker role differs."),
    };

    private void StartCg00ImageSpace(string editorId)
    {
        var contract = _cg00EarlySequence!.ImageSpaceModifiers.TryGetValue(
            editorId,
            out var found)
            ? found
            : throw new InvalidOperationException(
                "Fallout 3 early CG00 image-space command has no source contract.");
        var surface = new ColorRect
        {
            Name = $"FO3_CG00_IMAD_{contract.FormId}",
            Color = EvaluateCg00ImageSpaceFade(contract.Fade, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        surface.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        surface.SetMeta("opennv_imad_form_id", contract.FormId);
        surface.SetMeta("opennv_imad_record_sha256", contract.RecordSha256);
        AddChild(surface);
        _cg00EarlyImageSpaceLayers.Add(new Fo3Cg00ImageSpaceLayer(contract, surface));
    }

    private void UpdateCg00ImageSpace(double delta)
    {
        for (var index = _cg00EarlyImageSpaceLayers.Count - 1; index >= 0; index--)
        {
            var layer = _cg00EarlyImageSpaceLayers[index];
            layer.ElapsedSeconds = Math.Min(
                layer.Contract.DurationSeconds,
                layer.ElapsedSeconds + delta);
            var normalizedTime = (float)(
                layer.ElapsedSeconds / layer.Contract.DurationSeconds);
            layer.Surface.Color = EvaluateCg00ImageSpaceFade(
                layer.Contract.Fade,
                normalizedTime);
            if (layer.ElapsedSeconds < layer.Contract.DurationSeconds)
                continue;
            layer.Surface.QueueFree();
            _cg00EarlyImageSpaceLayers.RemoveAt(index);
        }
    }

    internal static Color EvaluateCg00ImageSpaceFade(
        IReadOnlyList<Fo3Cg00ImageSpaceFadeKey> keys,
        float normalizedTime)
    {
        if (keys.Count < 2 || !float.IsFinite(normalizedTime))
            throw new InvalidOperationException(
                "Fallout 3 early CG00 image-space fade evaluation differs.");
        if (normalizedTime <= keys[0].NormalizedTime)
            return keys[0].Color;
        if (normalizedTime >= keys[^1].NormalizedTime)
            return keys[^1].Color;
        for (var index = 1; index < keys.Count; index++)
        {
            var right = keys[index];
            if (normalizedTime > right.NormalizedTime)
                continue;
            var left = keys[index - 1];
            var width = right.NormalizedTime - left.NormalizedTime;
            if (width <= 0.0f)
                throw new InvalidOperationException(
                    "Fallout 3 early CG00 image-space fade key order differs.");
            return left.Color.Lerp(
                right.Color,
                (normalizedTime - left.NormalizedTime) / width);
        }
        throw new InvalidOperationException(
            "Fallout 3 early CG00 image-space fade curve is incomplete.");
    }

    private void ClearCg00ImageSpace()
    {
        foreach (var layer in _cg00EarlyImageSpaceLayers)
            layer.Surface.QueueFree();
        _cg00EarlyImageSpaceLayers.Clear();
    }

    private void PlayCg00Sound(string editorId)
    {
        var source = _cg00EarlySequence!.Sounds[editorId][0];
        AudioStream? stream = Path.GetExtension(source.SourcePath).Equals(
            ".ogg", StringComparison.OrdinalIgnoreCase)
            ? AudioStreamOggVorbis.LoadFromFile(source.SourcePath)
            : AudioStreamWav.LoadFromFile(source.SourcePath);
        if (stream is null)
            throw new InvalidOperationException("Fallout 3 early CG00 sound could not be decoded.");
        _vaultEffectSound?.Stop();
        _vaultEffectSound?.QueueFree();
        _vaultEffectSound = new AudioStreamPlayer { Name = $"FO3_CG00_SOUND_{editorId}", Stream = stream };
        AddChild(_vaultEffectSound);
        _vaultEffectSound.Play();
    }

    private void RunCg00EarlyProof()
    {
        if (_appearanceProofMode == "early-restore")
        {
            ContinueCharacter();
            if (_creatorLayer is not null || _cg00EarlySequence is not null ||
                _vaultBirthCoverage is null)
                throw new InvalidOperationException(
                    "Fallout 3 early CG00 restore replayed the creator or missed the Vault world.");
            WriteCg00EarlyProof("restore", [], [], 0);
            GetTree().Quit(0);
            return;
        }
        _cg00EarlyProofDriving = true;
        StartCg00EarlyBirthSequence();
    }

    private void UpdateCg00EarlyProof()
    {
        if (!_cg00EarlyProofDriving)
            return;
        if (_cg00EarlySexMenuActive)
        {
            SelectCg00EarlySex(_profile.SexChoices.Single(value => value.EngineSex == "male"));
            return;
        }
        if (_activeNameInput is not null && _cg00EarlyStage == _profile.NameStage)
        {
            _activeNameInput.Text = "VaultDweller";
            AcceptName(_activeNameInput);
            return;
        }
        if (_creatorLayer is null ||
            _cg00EarlyStage != _profile.Appearance.MenuEnteredStage ||
            _activeAppearanceSelection is null)
            return;
        _cg00EarlyProofDriving = false;
        if (_appearanceProofMode == "early-presentation")
        {
            if (_cg00EarlyPresentationFinalizing)
                throw new InvalidOperationException(
                    "Fallout 3 early presentation proof finalized more than once.");
            _cg00EarlyPresentationFinalizing = true;
            FinishCg00EarlyPresentationProof();
            return;
        }
        var stageHistory = _cg00EarlyStageHistory.ToArray();
        var infoHistory = _cg00EarlyInfoHistory.ToArray();
        var creatorEntries = _cg00EarlyCreatorEntryCount;
        AcceptAppearance("VaultDweller", _selectedSex!);
        var expectedStages = _profile.EarlyBirthSequence.Stages.Keys.OrderBy(value => value).ToArray();
        if (!stageHistory.Append(_profile.Appearance.AcceptedStage).SequenceEqual(expectedStages) ||
            creatorEntries != 1)
            throw new InvalidOperationException(
                "Fallout 3 early CG00 proof stage order or creator count differs.");
        WriteCg00EarlyProof("apply", expectedStages, infoHistory, creatorEntries);
        GetTree().Quit(0);
    }

    private async void CaptureCg00EarlyBirthPresentation()
    {
        try
        {
            if (_cg00EarlyBirthCaptureStarted || _cg00EarlyBirthPresentationCapture is not null)
                throw new InvalidOperationException(
                    "Fallout 3 early birth reveal capture replayed.");
            _cg00EarlyBirthCaptureStarted = true;
            if (_appearanceProofMode == "stage10-presentation")
            {
                ValidateCg00ParticipantScreenPresentation();
                _cg00Stage10PresentationFrozen = true;
                _cg00EarlyProofDriving = false;
                if (_cg00EarlyVoice is not null)
                    _cg00EarlyVoice.StreamPaused = true;
            }
            _cg00EarlyBirthPresentationCapture = await CaptureAppearanceFrame(
                "fo3-birth-reveal.png");
            if (_appearanceProofMode == "stage10-presentation")
            {
                _cg00EarlyProofDriving = false;
                var reportPath = _appearanceProofReportPath ?? throw new InvalidOperationException(
                    "Fallout 3 stage-10 presentation report path is absent.");
                var contract = _cg00EarlyPlayerCamera ?? throw new InvalidOperationException(
                    "Fallout 3 stage-10 presentation has no player camera contract.");
                var report = new
                {
                    schema = "opennv-fo3-cg00-stage10-native-presentation-proof/v1",
                    profileId = _profile.ProfileId,
                    profileSha256 = _profile.Sha256,
                    stageHistory = _cg00EarlyStageHistory,
                    infoHistory = _cg00EarlyInfoHistory,
                    activeStage = _cg00EarlyStage,
                    playerCamera = new
                    {
                        section = contract.Section,
                        packageFormId = contract.PackageFormId,
                        idleFormId = contract.IdleFormId,
                        targetNode = contract.TargetNode,
                        animationSha256 = contract.Animation.Sha256,
                        skeletonSha256 = contract.Skeleton.Sha256,
                        sampleContractSha256 = contract.SampleContractSha256,
                        timeSeconds = _cg00EarlyPlayerCameraSeconds,
                        verticalFovDegrees = _vaultBirthCoverage?.Camera.Fov,
                        keepAspect = _vaultBirthCoverage?.Camera.KeepAspect.ToString(),
                        position = _vaultBirthCoverage?.Camera.GlobalPosition,
                        basis = _vaultBirthCoverage is null ? null : new
                        {
                            right = _vaultBirthCoverage.Camera.GlobalBasis.X,
                            up = _vaultBirthCoverage.Camera.GlobalBasis.Y,
                            back = _vaultBirthCoverage.Camera.GlobalBasis.Z,
                        },
                        quaternion = _vaultBirthCoverage?.Camera.GlobalTransform.Basis
                            .GetRotationQuaternion(),
                    },
                    participantPackages = _cg00ActorPackages.ToDictionary(
                        value => value.Key,
                        value => new
                        {
                            section = value.Value.Contract.Section,
                            activationStage = value.Value.Contract.ActivationCondition?.Stage,
                            packageFormId = value.Value.Contract.PackageFormId,
                            idleFormId = value.Value.Contract.IdleFormId,
                            animationSha256 = value.Value.Contract.AnimationSha256,
                            elapsedSeconds = value.Value.ElapsedSeconds,
                            stopSeconds = value.Value.Contract.AnimationStopSeconds,
                            changeIdleFormId = value.Value.Contract.ChangeIdleFormId,
                        },
                        StringComparer.Ordinal),
                    sourceProjection = new
                    {
                        retailParityBlocked = true,
                        blocker = "matched-retail-pixel-differential-not-executed",
                        contractPath = _cg00RetailStage10Telemetry?.ContractPath,
                        contractSha256 = _cg00RetailStage10Telemetry?.ContractSha256,
                        cameraAuthority = _cg00RetailStage10Telemetry?.CameraAuthority,
                        actorPlacementAuthority =
                            _cg00RetailStage10Telemetry?.ActorPlacementAuthority,
                        controllerAuthority = _cg00RetailStage10Telemetry?.ControllerAuthority,
                        fullNearPlaneSeparation =
                            _cg00RetailStage10Telemetry?.FullNearPlaneSeparation,
                        participants = _cg00Stage10Projection,
                    },
                    capture = _cg00EarlyBirthPresentationCapture,
                };
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(
                    reportPath,
                    JsonSerializer.Serialize(
                        report,
                        new JsonSerializerOptions { WriteIndented = true }) +
                        System.Environment.NewLine);
                GD.Print(
                    $"OPENNV_FO3_CG00_STAGE10_PRESENTATION_PASS " +
                    $"retailContract={_cg00RetailStage10Telemetry?.ContractSha256} " +
                    $"ownedCameraPackage={contract.SampleContractSha256} report={reportPath}");
                GetTree().Quit(0);
            }
        }
        catch (Exception exception)
        {
            _cg00EarlyProofDriving = false;
            if (_appearanceProofMode == "stage10-presentation")
                WriteCg00Stage10BlockedReport(exception);
            GD.PushError($"OPENNV_FO3_CG00_EARLY_PRESENTATION_FAIL {exception}");
            GetTree().Quit(Fo3OpeningFlowNumericContracts.ProofFailureExitCode);
        }
    }

    private void WriteCg00Stage10BlockedReport(Exception exception)
    {
        var reportPath = _appearanceProofReportPath ?? throw new InvalidOperationException(
            "Fallout 3 stage-10 blocked report path is absent.");
        var presentation = _ttwCg00Stage10PresentationContract ??
            throw new InvalidOperationException(
                "Fallout 3 stage-10 blocked report has no TTW presentation contract.");
        var surfaces = _ttwCg00Stage10SurfaceContract ??
            throw new InvalidOperationException(
                "Fallout 3 stage-10 blocked report has no TTW surface contract.");
        var actors = _birthPresentation ?? throw new InvalidOperationException(
            "Fallout 3 stage-10 blocked report has no routed birth presentation.");
        var report = new
        {
            schema = "opennv-fo3-ttw-cg00-stage10-native-depth-blocker/v1",
            status = "blocked-exact-native-posed-surface-differential",
            acceptedSnapshot = false,
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            stage = _cg00EarlyStage,
            actorSet = new
            {
                path = actors.TtwCg00Stage10ActorSetPath,
                sha256 = actors.TtwCg00Stage10ActorSetSha256,
                actorScenes = new
                {
                    father = new { actors.DadActor.ScenePath, actors.DadActor.SceneSha256 },
                    doctor = new { actors.DoctorActor.ScenePath, actors.DoctorActor.SceneSha256 },
                    mother = new { actors.MomActor.ScenePath, actors.MomActor.SceneSha256 },
                },
            },
            presentationContract = new
            {
                path = presentation.Path,
                sha256 = presentation.Sha256,
                rawObservationPath = presentation.RawObservationPath,
                rawObservationSha256 = presentation.RawObservationSha256,
            },
            surfaceContract = new { path = surfaces.Path, sha256 = surfaces.Sha256 },
            blocker = new
            {
                type = exception.GetType().FullName,
                exception.Message,
            },
        };
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        GD.Print($"OPENNV_FO3_TTW_STAGE10_NATIVE_DEPTH_BLOCKED report={reportPath}");
    }

    private async void FinishCg00EarlyPresentationProof()
    {
        try
        {
            var birth = _cg00EarlyBirthPresentationCapture ??
                throw new InvalidOperationException(
                    "Fallout 3 early presentation reached RaceSexMenu before its birth reveal frame.");
            var creator = await CaptureAppearanceFrame("fo3-creator-open.png");
            var reportPath = _appearanceProofReportPath ?? throw new InvalidOperationException(
                "Fallout 3 early presentation proof report path is absent.");
            var report = new
            {
                schema = "opennv-fo3-cg00-native-presentation-proof/v1",
                profileId = _profile.ProfileId,
                profileSha256 = _profile.Sha256,
                stageHistory = _cg00EarlyStageHistory,
                infoHistory = _cg00EarlyInfoHistory,
                creatorEntries = _cg00EarlyCreatorEntryCount,
                activeStage = _cg00EarlyStage,
                dialogueClock = new
                {
                    sourceOrderedInfoCount = _cg00EarlyInfoHistory.Count,
                    replayCount = 0,
                },
                participants = new
                {
                    doctor = _vaultBirthCoverage?.DoctorActor.ReferenceFormId,
                    dad = _vaultBirthCoverage?.DadActor.ReferenceFormId,
                    mom = _vaultBirthCoverage?.MomActor.ReferenceFormId,
                    allVisible = _vaultBirthCoverage is not null &&
                        _vaultBirthCoverage.DoctorActor.Placement.Visible &&
                        _vaultBirthCoverage.DadActor.Placement.Visible &&
                        _vaultBirthCoverage.MomActor.Placement.Visible,
                },
                controls = _profile.Appearance.FaceControls.Count,
                captures = new[] { birth, creator },
            };
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            File.WriteAllText(
                reportPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            GD.Print(
                $"OPENNV_FO3_CG00_EARLY_PRESENTATION_PASS stage={_cg00EarlyStage} " +
                $"infos={_cg00EarlyInfoHistory.Count} controls={_profile.Appearance.FaceControls.Count} " +
                $"report={reportPath}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO3_CG00_EARLY_PRESENTATION_FAIL {exception}");
            GetTree().Quit(Fo3OpeningFlowNumericContracts.ProofFailureExitCode);
        }
    }

    private void ValidateCg00ParticipantScreenPresentation()
    {
        if (_cg00EarlyStage != Fo3Cg00RetailStage10Contract.ExpectedStage)
            throw new InvalidOperationException(
                "Fallout 3 CG00 stage-10 retail contract cannot authorize another stage capture.");
        var coverage = _vaultBirthCoverage ?? throw new InvalidOperationException(
            "Fallout 3 CG00 screen presentation has no constructed Vault world.");
        var sourceSequence = _cg00EarlySequence ?? throw new InvalidOperationException(
            "Fallout 3 CG00 retail join has no owned early-birth source sequence.");
        var joined = _ttwCg00Stage10PresentationContract is not null
            ? Fo3TtwCg00Stage10PresentationJoin.ApplyAndMeasure(
                _ttwCg00Stage10PresentationContract,
                _ttwCg00Stage10SurfaceContract ?? throw new InvalidOperationException(
                    "Fallout 3 TTW stage-10 per-surface depth contract is absent."),
                sourceSequence,
                coverage)
            : Fo3Cg00RetailStage10Join.ApplyAndMeasure(
                _retailCg00Stage10Contract ?? throw new InvalidOperationException(
                    "Fallout 3 CG00 visual proof requires an exact live standalone or TTW " +
                    "stage-10 contract."),
                sourceSequence,
                coverage);
        _cg00Stage10Projection.Clear();
        foreach (var value in joined.Participants)
        {
            _cg00Stage10Projection[value.Key] = value.Value;
            if (_cg00ActorPackages.TryGetValue(value.Key, out var playback))
                playback.Animation.PublishPhase(
                    value.Value.ObservedControllerPhaseSeconds);
        }
        _cg00RetailStage10Telemetry = joined;
        if (_ttwCg00Stage10SurfaceContract is null && !joined.FullNearPlaneSeparation)
            throw new InvalidOperationException(
                "Fallout 3 CG00 exact live stage-10 posed meshes intersect the infant near plane: " +
                string.Join(
                    ", ",
                    joined.Participants.Values
                        .Where(value => !value.FullMeshClearsNearPlane)
                        .Select(value =>
                            $"{value.Role}={value.VerticesAtOrBehindNearPlane}/" +
                            $"{value.PosedMeshVertices}," +
                            $"separation={value.MinimumNearPlaneSeparationMeters:R}m")));
        coverage.Camera.SetMeta("opennv_retail_parity_blocked", true);
        coverage.Camera.SetMeta(
            "opennv_retail_parity_blocker",
            "matched-retail-pixel-differential-not-executed");
        GD.Print(
            "OPENNV_FO3_CG00_STAGE10_RETAIL_CONTRACT_JOIN_PASS " +
            $"contract={joined.ContractSha256} " +
            "retailParityBlocked=1 " +
            string.Join(' ', joined.Participants.Values.Select(value =>
                $"{value.Role}=vertices:{value.PosedMeshVertices}," +
                $"depthMeters:{value.CameraDepthMinimumMeters:R}.." +
                $"{value.CameraDepthMaximumMeters:R}," +
                $"nearSeparationMeters:{value.MinimumNearPlaneSeparationMeters:R}," +
                $"phaseErrorSeconds:{value.ControllerPhaseErrorSeconds:R}")));
    }

    private void WriteCg00EarlyProof(
        string phase,
        IReadOnlyList<int> stageHistory,
        IReadOnlyList<string> infoHistory,
        int creatorEntries)
    {
        var reportPath = _appearanceProofReportPath ?? throw new InvalidOperationException(
            "Fallout 3 early CG00 proof report path is absent.");
        using var save = JsonDocument.Parse(File.ReadAllBytes(_savePath));
        var savedStage = RequiredSaveInteger(save.RootElement, "stage");
        if (savedStage < _profile.Appearance.AcceptedStage)
            throw new InvalidOperationException("Fallout 3 early CG00 proof save did not pass stage 62.");
        var report = new
        {
            schema = "opennv-fo3-cg00-early-runtime-proof/v1",
            phase,
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            stageHistory,
            infoHistory,
            creatorEntries,
            savedStage,
            stageReplayCount = 0,
            infoReplayCount = 0,
            creatorReplayCount = 0,
            worldReady = _vaultBirthCoverage is not null,
            doctorVisible = _vaultBirthCoverage?.DoctorActor.Placement.Visible,
            dadVisible = _vaultBirthCoverage?.DadActor.Placement.Visible,
            momVisible = _vaultBirthCoverage?.MomActor.Placement.Visible,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        GD.Print(
            $"OPENNV_FO3_CG00_EARLY_PROOF_PASS phase={phase} savedStage={savedStage} " +
            $"stageReplayCount=0 infoReplayCount=0 creatorReplayCount=0 report={reportPath}");
    }
}
