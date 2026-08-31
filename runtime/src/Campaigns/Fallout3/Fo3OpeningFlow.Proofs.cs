using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
    private async void RunAppearanceProof()
    {
        try
        {
            if (_appearanceProofMode is not "apply" and not "restore" ||
                string.IsNullOrWhiteSpace(_appearanceProofReportPath) ||
                string.IsNullOrWhiteSpace(_appearanceProofCaptureRoot) ||
                _birthPresentation is null ||
                DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 3 creator proof requires apply|restore, owned Vault presentation, " +
                    "report/capture paths, and a rendering display driver.");
            if (File.Exists(_appearanceProofReportPath))
                throw new InvalidOperationException(
                    "Fallout 3 creator proof requires a fresh report path.");
            Directory.CreateDirectory(_appearanceProofCaptureRoot);
            if (_appearanceProofMode == "apply")
            {
                if (File.Exists(_savePath))
                    throw new InvalidOperationException(
                        "Fallout 3 creator apply proof requires a fresh save path.");
                var sex = _profile.SexChoices.Single(value => value.EngineSex == "male");
                ShowNameSelection(sex);
                _activeNameInput!.Text = "Lone Wanderer";
                var nameCapture = await CaptureAppearanceFrame("fo3-name-entry.png");
                AcceptName(_activeNameInput);
                _activeAppearanceCategory!.Select(3);
                _activeAppearanceCategory.EmitSignal(
                    OptionButton.SignalName.ItemSelected,
                    3);
                var defaultCapture = await CaptureAppearanceFrame(
                    "fo3-creator-default.png");
                _activeFaceControlSlider!.Value =
                    _profile.Appearance.FaceControl.AcceptanceValue;
                var editedSelection = _activeAppearanceSelection ??
                    throw new InvalidOperationException(
                        "Fallout 3 creator proof did not apply the visible face edit.");
                if (editedSelection.FaceControlValue(
                        _profile.Appearance.FaceControl.SettingEntity) !=
                            _profile.Appearance.FaceControl.AcceptanceValue ||
                    editedSelection.Sex.FaceGen.SymmetricGeometrySha256 ==
                        _profile.Appearance.DefaultSelection("male").Sex.FaceGen
                            .SymmetricGeometrySha256)
                    throw new InvalidOperationException(
                        "Fallout 3 creator proof face edit did not change geometry.");
                var creatorCapture = await CaptureAppearanceFrame("fo3-creator-edited.png");
                var morphDifference = MeasureAppearanceDifference(
                    defaultCapture,
                    creatorCapture);
                AcceptAppearance("Lone Wanderer", sex);
                if (_creatorLayer is not null || _vaultBirthCoverage is null)
                    throw new InvalidOperationException(
                        "Fallout 3 creator acceptance did not reveal the owned Vault room.");
                var persistedSelection = LoadSavedAppearanceSelection();
                if (!_profile.Appearance.FaceControls.All(control =>
                        persistedSelection.FaceControlValue(control.SettingEntity) ==
                            editedSelection.FaceControlValue(control.SettingEntity)) ||
                    persistedSelection.Sex.FaceGen.SymmetricGeometrySha256 !=
                        editedSelection.Sex.FaceGen.SymmetricGeometrySha256)
                    throw new InvalidOperationException(
                        "Fallout 3 creator acceptance did not persist the edited identity.");
                var birthCapture = await CaptureAppearanceFrame("fo3-birth-next-beat.png");
                WriteAppearanceProofReport(
                    "apply",
                    editedSelection,
                    [nameCapture, defaultCapture, creatorCapture, birthCapture],
                    morphDifference,
                    creatorActionsReplayed: false);
                GD.Print(
                    $"OPENNV_FO3_CREATOR_PROOF_APPLY_PASS profile={_profile.ProfileId} " +
                    $"control={_profile.Appearance.FaceControl.SettingEntity} " +
                    $"value={editedSelection.FaceControlValue(
                        _profile.Appearance.FaceControl.SettingEntity):F2} " +
                    $"geometry={editedSelection.Sex.FaceGen.SymmetricGeometrySha256} " +
                    $"save={_savePath}");
                GetTree().Quit(0);
                return;
            }

            if (!File.Exists(_savePath))
                throw new InvalidOperationException(
                    "Fallout 3 creator restore proof save is absent.");
            var restored = LoadSavedAppearanceSelection();
            ContinueCharacter();
            if (_creatorLayer is not null || _vaultBirthCoverage is null)
                throw new InvalidOperationException(
                    "Fallout 3 creator restore did not resume the owned Vault room.");
            var restoreCapture = await CaptureAppearanceFrame("fo3-birth-restored.png");
            WriteAppearanceProofReport(
                "restore",
                restored,
                [restoreCapture],
                morphDifference: null,
                creatorActionsReplayed: false);
            GD.Print(
                $"OPENNV_FO3_CREATOR_PROOF_RESTORE_PASS profile={_profile.ProfileId} " +
                $"control={_profile.Appearance.FaceControl.SettingEntity} " +
                $"value={restored.FaceControlValue(
                    _profile.Appearance.FaceControl.SettingEntity):F2} " +
                $"geometry={restored.Sex.FaceGen.SymmetricGeometrySha256} " +
                $"save={_savePath}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO3_CREATOR_PROOF_FAIL {exception}");
            GetTree().Quit(Fo3OpeningFlowNumericContracts.ProofFailureExitCode);
        }
    }

    private async void RunCharacterGenerationVideo()
    {
        try
        {
            if (_birthPresentation is null || DisplayServer.GetName() == "headless")
                throw new InvalidOperationException(
                    "Fallout 3 character video requires the owned Vault presentation and a rendering display driver.");
            if (File.Exists(_savePath))
                throw new InvalidOperationException(
                    "Fallout 3 character video requires a fresh save path.");
            StartMenuMusic();
            ShowMainMenu();
            await WaitForCharacterVideoDraws(55);
            ShowSexSelection();
            await WaitForCharacterVideoDraws(55);
            var sex = _profile.SexChoices.Single(value => value.EngineSex == "male");
            ShowNameSelection(sex);
            await WaitForCharacterVideoDraws(40);
            _activeNameInput!.Text = "LONE WANDERER";
            await WaitForCharacterVideoDraws(55);
            AcceptName(_activeNameInput);
            var appearanceCategory = _activeAppearanceCategory ??
                throw new InvalidOperationException(
                    "Fallout 3 generated character did not open the appearance categories.");
            var faceControlSlider = _activeFaceControlSlider ??
                throw new InvalidOperationException(
                    "Fallout 3 generated character did not open the live face controls.");
            appearanceCategory.Select(3);
            appearanceCategory.EmitSignal(
                OptionButton.SignalName.ItemSelected,
                3);
            faceControlSlider.Value =
                _profile.Appearance.FaceControl.AcceptanceValue;
            await WaitForCharacterVideoDraws(55);
            _reflectron!.ActivateCreatorModeControl("BODY");
            await WaitForCharacterVideoDraws(55);
            _reflectron.ActivateCreatorModeControl("PROJECTION");
            await WaitForCharacterVideoDraws(55);
            _reflectron.ActivateCreatorModeControl("FACE");
            await WaitForCharacterVideoDraws(55);
            _reflectron.ActivateCreatorModeControl("PROJECTION");
            await WaitForCharacterVideoDraws(55);
            AcceptAppearance("LONE WANDERER", sex);
            if (_creatorLayer is not null || _vaultBirthCoverage is null)
                throw new InvalidOperationException(
                    "Fallout 3 generated character did not enter the Vault 101 birth slice.");
            await WaitForCharacterVideoDraws(180);
            GD.Print(
                $"OPENNV_FO3_CHARACTER_VIDEO_COMPLETE profile={_profile.ProfileId} save={_savePath}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO3_CHARACTER_VIDEO_FAIL {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task WaitForCharacterVideoDraws(int count)
    {
        for (var frame = 0; frame < count; frame++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private Fo3AppearanceSelection LoadSavedAppearanceSelection()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(_savePath));
        var root = document.RootElement;
        if (RequiredSaveString(root, "profileId") != _profile.ProfileId ||
            RequiredSaveString(root, "profileSha256") != _profile.Sha256 ||
            RequiredSaveInteger(root, "stage") != _profile.Appearance.AcceptedStage)
            throw new InvalidOperationException(
                "Fallout 3 creator restore proof save identity/stage differs.");
        _profile.Section4Transition.ValidateSavedState(
            RequiredSaveObject(root, "playerPackage"));
        var sex = RequiredSaveObject(root, "sex");
        var engineSex = RequiredSaveString(sex, "engineSex");
        var appearance = RequiredSaveObject(root, "appearance");
        var selection = _profile.Appearance.ResolveSelection(
            engineSex,
            RequiredSaveString(appearance, "adultRaceFormId"),
            RequiredSaveString(appearance, "childRaceFormId"),
            RequiredSaveString(appearance, "hairFormId"),
            RequiredSaveString(appearance, "eyesFormId"));
        var face = RequiredSaveObject(appearance, "faceGen");
        selection = LoadSavedFaceControls(face, selection);
        if (RequiredSaveString(face, "symmetricGeometrySha256") !=
                selection.Sex.FaceGen.SymmetricGeometrySha256)
            throw new InvalidOperationException(
                "Fallout 3 creator restore proof geometry differs.");
        return selection;
    }

    private async Task<Fo3AppearanceProofCapture> CaptureAppearanceFrame(string fileName)
    {
        for (var frame = 0;
             frame < Fo3OpeningFlowNumericContracts.Cg01CaptureWarmupFrames;
             frame++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (fileName.Contains("birth", StringComparison.Ordinal) &&
            (_creatorLayer is not null || _panel.Visible || _background.Visible ||
             _vaultBirthCoverage is null ||
             !_vaultBirthCoverage.DoctorActor.Placement.Visible ||
             !_vaultBirthCoverage.DadActor.Placement.Visible ||
             !_vaultBirthCoverage.MomActor.Placement.Visible))
            throw new InvalidOperationException(
                "Fallout 3 creator birth capture has stale UI or an absent CG00 participant.");
        if (fileName.Contains("birth", StringComparison.Ordinal) ||
            fileName.Contains("creator", StringComparison.Ordinal))
            ValidateCg00ParticipantScreenPresentation();
        var image = GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        var data = image.GetData();
        if (data.Length == 0)
            throw new InvalidOperationException("Fallout 3 creator capture is empty.");
        var minimum = data.Min();
        var maximum = data.Max();
        if (maximum <= minimum)
            throw new InvalidOperationException("Fallout 3 creator capture is one blank color.");
        var captureRoot = Path.GetFullPath(_appearanceProofCaptureRoot!);
        Directory.CreateDirectory(captureRoot);
        var path = Path.Combine(captureRoot, fileName);
        if (File.Exists(path))
            throw new InvalidOperationException(
                $"Fallout 3 creator capture path is not fresh: {path}");
        var error = image.SavePng(path);
        if (error != Error.Ok)
            throw new InvalidOperationException(
                $"Fallout 3 creator capture could not be saved: {error}.");
        using var stream = File.OpenRead(path);
        var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return new Fo3AppearanceProofCapture(
            path,
            sha256,
            image.GetWidth(),
            image.GetHeight(),
            maximum - minimum);
    }

    private object MeasureAppearanceDifference(
        Fo3AppearanceProofCapture baseline,
        Fo3AppearanceProofCapture edited)
    {
        var baselineImage = Image.LoadFromFile(baseline.Path);
        var editedImage = Image.LoadFromFile(edited.Path);
        if (baselineImage is null || editedImage is null ||
            baselineImage.GetSize() != editedImage.GetSize())
            throw new InvalidOperationException(
                "Fallout 3 creator comparison frames do not share one viewport.");
        baselineImage.Convert(Image.Format.Rgba8);
        editedImage.Convert(Image.Format.Rgba8);
        var baselineData = baselineImage.GetData();
        var editedData = editedImage.GetData();
        var left = baseline.Width * _profile.Appearance.Ui.FaceGrabX /
            Fo3OpeningFlowNumericContracts.SourceUiCanvasWidthPixels;
        var top = baseline.Height * _profile.Appearance.Ui.FaceGrabY /
            Fo3OpeningFlowNumericContracts.SourceUiCanvasHeightPixels;
        var width = baseline.Width * _profile.Appearance.Ui.FaceGrabWidth /
            Fo3OpeningFlowNumericContracts.SourceUiCanvasWidthPixels;
        var height = baseline.Height * _profile.Appearance.Ui.FaceGrabHeight /
            Fo3OpeningFlowNumericContracts.SourceUiCanvasHeightPixels;
        if (width <= 0 || height <= 0 || left + width > baseline.Width ||
            top + height > baseline.Height)
            throw new InvalidOperationException(
                "Fallout 3 creator face comparison region is outside the viewport.");
        long absoluteDifference = 0;
        var changedPixels = 0;
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                var offset = (y * baseline.Width + x) *
                    Fo3OpeningFlowNumericContracts.CaptureBytesPerPixel;
                var pixelChanged = false;
                for (var channel = 0;
                     channel < Fo3OpeningFlowNumericContracts.CaptureRgbChannels;
                     channel++)
                {
                    var difference = Math.Abs(baselineData[offset + channel] -
                        editedData[offset + channel]);
                    absoluteDifference += difference;
                    pixelChanged |= difference > 0;
                }
                if (pixelChanged)
                    changedPixels++;
            }
        }
        if (changedPixels == 0 || absoluteDifference == 0)
            throw new InvalidOperationException(
                "Fallout 3 normalized FaceGen edit produced no visible pixel change.");
        return new
        {
            baselinePath = baseline.Path,
            editedPath = edited.Path,
            region = new[] { left, top, width, height },
            changedPixels,
            absoluteRgbDifference = absoluteDifference,
            meanAbsoluteRgbDifference = absoluteDifference /
                (double)(width * height *
                    Fo3OpeningFlowNumericContracts.CaptureRgbChannels),
        };
    }

    private void WriteAppearanceProofReport(
        string phase,
        Fo3AppearanceSelection selection,
        IReadOnlyList<Fo3AppearanceProofCapture> captures,
        object? morphDifference,
        bool creatorActionsReplayed)
    {
        var preview = _profile.Appearance.PreviewSet.Previews.Single(value =>
            value.RaceFormId.Equals(selection.Race.FormId, StringComparison.OrdinalIgnoreCase) &&
            value.HairFormId.Equals(selection.Hair.FormId, StringComparison.OrdinalIgnoreCase) &&
            value.EyesFormId.Equals(selection.Eyes.FormId, StringComparison.OrdinalIgnoreCase));
        using var document = JsonDocument.Parse(File.ReadAllBytes(_savePath));
        var root = document.RootElement;
        var savedPackage = RequiredSaveObject(root, "playerPackage");
        var activePackage = RequiredSaveBoolean(savedPackage, "active");
        var advancedIntoNextBirthBeat =
            RequiredSaveInteger(root, "stage") == _profile.Appearance.AcceptedStage &&
            activePackage;
        if (!advancedIntoNextBirthBeat)
            throw new InvalidOperationException(
                "Fallout 3 creator proof did not persist the next authored package beat.");
        var report = new
        {
            schema = "opennv-fo3-native-creator-proof/v1",
            phase,
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            sourceUi = new
            {
                canvas = new[]
                {
                    Fo3OpeningFlowNumericContracts.SourceUiCanvasWidthPixels,
                    Fo3OpeningFlowNumericContracts.SourceUiCanvasHeightPixels,
                },
                namePanel = new[]
                {
                    _profile.Appearance.Ui.Name.PanelWidth,
                    _profile.Appearance.Ui.Name.PanelHeight,
                },
                appearancePanel = new[]
                {
                    _profile.Appearance.Ui.PanelX,
                    _profile.Appearance.Ui.PanelY,
                    _profile.Appearance.Ui.PanelWidth,
                    _profile.Appearance.Ui.PanelHeight,
                },
                faceGrab = new[]
                {
                    _profile.Appearance.Ui.FaceGrabX,
                    _profile.Appearance.Ui.FaceGrabY,
                    _profile.Appearance.Ui.FaceGrabWidth,
                    _profile.Appearance.Ui.FaceGrabHeight,
                },
            },
            livePreview = new
            {
                raceFormId = selection.Race.FormId,
                sex = preview.Sex,
                hairFormId = selection.Hair.FormId,
                eyesFormId = selection.Eyes.FormId,
                control = _profile.Appearance.FaceControl.SettingEntity,
                controlAxisSha256 = _profile.Appearance.FaceControl.AxisSha256,
                value = selection.FaceControlValue(
                    _profile.Appearance.FaceControl.SettingEntity),
                controlCount = _profile.Appearance.FaceControls.Count,
                symmetricGeometrySha256 = selection.Sex.FaceGen.SymmetricGeometrySha256,
                disposition = preview.RuntimeDisposition,
                fullBody = preview.FullBody,
                bodyComponentRoles = preview.BodyComponentRoles,
                fullRetailSlidersImplemented = true,
            },
            morphDifference,
            persisted = new
            {
                stage = RequiredSaveInteger(root, "stage"),
                name = RequiredSaveString(root, "playerName"),
                race = RequiredSaveString(RequiredSaveObject(root, "appearance"), "adultRaceFormId"),
                faceControlValues = RequiredSaveArray(
                    RequiredSaveObject(
                        RequiredSaveObject(root, "appearance"),
                        "faceGen"),
                    "geometryControls").EnumerateArray().Select(value => new
                    {
                        settingEntity = RequiredSaveString(value, "settingEntity"),
                        value = RequiredSaveSingle(value, "value"),
                    }).ToArray(),
                creatorActionsReplayed,
                activePackage,
                advancedIntoNextBirthBeat,
            },
            captures = captures.Select(value => new
            {
                path = value.Path,
                sha256 = value.Sha256,
                width = value.Width,
                height = value.Height,
                rgbSpan = value.RgbSpan,
            }),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_appearanceProofReportPath!)!);
        File.WriteAllText(
            _appearanceProofReportPath!,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
    }

    private void RunCg01Proof()
    {
        if (_cg01ProofMode is not "apply" and not "restore" ||
            string.IsNullOrWhiteSpace(_cg01ProofReportPath) ||
            _birthPresentation is null)
            throw new InvalidOperationException("Fallout 3 CG01 proof configuration differs.");
        if (_cg01ProofCapturePath is not null &&
            (_cg01ProofMode != "apply" ||
             DisplayServer.GetName() == "headless" ||
             File.Exists(_cg01ProofCapturePath) ||
             !Path.GetExtension(_cg01ProofCapturePath).Equals(
                 ".png",
                 StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Fallout 3 CG01 capture requires a fresh PNG and a rendering display driver.");
        var sex = _profile.SexChoices.Single(value => value.EngineSex == "male");
        var selection = _profile.Appearance.DefaultSelection(sex.EngineSex);
        var package = _profile.Section4Transition.Activate();
        var stage65 = _profile.Stage65Appearance.Apply(
            sex.EngineSex,
            selection.Race.FormId,
            selection.Sex.FaceGen);
        var stage80 = _profile.Stage80Transition.Apply(sex.EngineSex, stage65);
        var stage85 = _profile.Stage85Transition.Apply(stage80);
        var stage90 = _profile.Stage90Transition.Apply(stage85);
        var stage100 = _profile.Stage100Transition.Apply(stage90, 0.0);
        var cg01 = _profile.Cg01Stage0Transition.Apply(stage100);
        if (_cg01ProofMode == "apply")
        {
            if (File.Exists(_savePath))
                throw new InvalidOperationException(
                    "Fallout 3 CG01 apply proof requires a fresh save path.");
            var context = new Fo3Cg01RuntimeContext(
                _profile.Appearance.PlayerEditorId,
                sex,
                selection,
                package,
                stage65,
                stage80,
                stage85,
                stage90,
                stage100);
            EnsureCg01VaultScene(context);
            ApplyCg01Stage5Presentation(cg01, stage65);
            PersistCg01Transition(
                _profile.Appearance.PlayerEditorId,
                sex,
                selection,
                package,
                stage65,
                stage80,
                stage85,
                stage90,
                stage100,
                cg01);
            StartCg01TransitionMovie(
                cg01,
                context);
            Callable.From(() => Input.ParseInputEvent(new InputEventKey
            {
                Keycode = Key.Escape,
                PhysicalKeycode = Key.Escape,
                Pressed = true,
            })).CallDeferred();
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(_savePath));
        var root = document.RootElement;
        if (RequiredSaveString(root, "schema") != "opennv-fo3-opening-character/v2" ||
            RequiredSaveString(root, "profileId") != _profile.ProfileId ||
            RequiredSaveString(root, "profileSha256") != _profile.Sha256)
            throw new InvalidOperationException("Fallout 3 CG01 proof save identity differs.");
        _profile.Stage100Transition.ValidateSavedState(
            RequiredSaveObject(root, "stage100Transition"),
            stage100);
        _profile.Cg01Stage0Transition.ValidateSavedState(
            RequiredSaveObject(root, "cg01Stage0Transition"),
            cg01);
        var cg01Stage10 = _profile.Cg01Stage10Transition.Apply(cg01, sex.EngineSex);
        _profile.Cg01Stage10Transition.ValidateSavedState(
            RequiredSaveObject(root, "cg01Stage10Transition"),
            cg01Stage10);
        var cg01Stage12 = _profile.Cg01Stage12Transition.ApplyAuthoredTrigger(
            cg01Stage10,
            _profile.Cg01Stage12Transition.Trigger.ReferenceFormId,
            actionReferenceWasPlayer: true);
        _profile.Cg01Stage12Transition.ValidateSavedState(
            RequiredSaveObject(root, "cg01Stage12Transition"),
            cg01Stage12);
        var toddlerWorld = _profile.Cg01ToddlerWorld.LoadSavedState(
            RequiredSaveObject(root, "cg01ToddlerWorld"));
        var cg01Stage14 = _profile.Cg01Stage12DadResponse.Apply(cg01Stage12);
        _profile.Cg01Stage12DadResponse.ValidateSavedState(
            RequiredSaveObject(root, "cg01Stage12DadResponse"),
            cg01Stage14);
        var restoreContext = new Fo3Cg01RuntimeContext(
            _profile.Appearance.PlayerEditorId,
            sex,
            selection,
            package,
            stage65,
            stage80,
            stage85,
            stage90,
            stage100);
        EnsureCg01VaultScene(restoreContext);
        ValidateBirthRuntimeState(
            RequiredSaveObject(root, "birthRuntime"),
            "cg01-stage14-dad-response-applied-package-evaluated");
        ApplyCg01Stage5Presentation(cg01, stage65);
        BeginCg01ToddlerWorld(
            cg01,
            restoreContext,
            cg01Stage10,
            toddlerWorld,
            acceptanceProof: true,
            restoredStage14: cg01Stage14);
    }

    private void WriteCg01ProofReport(
        Fo3Cg01Stage0State stage5,
        Fo3Cg01Stage10State stage10,
        Fo3Cg01Stage12State stage12,
        Fo3Cg01Stage14State stage14,
        Fo3Cg01ToddlerWorldState toddlerWorld,
        string engineSex,
        string phase,
        bool movieSurfaceRequested,
        bool escapeSkipped,
        bool movieReplayed,
        bool dialoguePlayed)
    {
        var path = _cg01ProofReportPath ?? throw new InvalidOperationException(
            "Fallout 3 CG01 proof report path is absent.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        if (_cg01ProofCapturePath is not null && !_cg01ProofCaptureCompleted)
            throw new InvalidOperationException(
                "Fallout 3 CG01 proof reached its report before coherent capture completed.");
        var cues = _profile.Cg01Stage10Transition.DialogueFor(engineSex);
        var stage5PublishedInfoFormIds = _cg01DadPublishedSpeakerIdleInfoFormIds
            .Where(value => stage10.AppliedInfoFormIds.Contains(
                value,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var stage12PublishedInfoFormIds = _cg01DadPublishedSpeakerIdleInfoFormIds
            .Where(value => stage14.AppliedInfoFormIds.Contains(
                value,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var report = new
        {
            schema = "opennv-fo3-cg01-runtime-proof/v8",
            profileId = _profile.ProfileId,
            profileSha256 = _profile.Sha256,
            phase,
            savePath = _savePath,
            activeQuest = new
            {
                formId = stage10.ActiveQuestFormId,
                editorId = stage10.ActiveQuestEditorId,
                stage = stage14.ActiveStage,
            },
            stage5Commands = new
            {
                accounted = stage5.AccountedCommandCount,
                applied = stage5.AppliedCommandCount,
                trace = stage5.AppliedExecutionTrace,
            },
            dialogue = new
            {
                engineSex,
                infoFormIds = stage10.AppliedInfoFormIds,
                sourceTimerSeconds = cues[0].DadTimerAfterSeconds,
                playedThisProcess = dialoguePlayed,
                replayed = false,
                speakerReferenceFormId = stage5.Dad.Reference.FormId,
                lipClockBoundToSpeaker = true,
                lipCueSamplesThisProcess = _cg01DadLipCueSamples,
                speakerIdleInfoFormIdsPublishedThisProcess =
                    stage5PublishedInfoFormIds,
                speakerIdlesPublishedThisProcess =
                    stage5PublishedInfoFormIds.Length,
                assets = cues.Select(cue => new
                {
                    cue.Sequence,
                    cue.InfoFormId,
                    voiceSha256 = cue.Response.Voice.Sha256,
                    lipSha256 = cue.Response.Lip.Sha256,
                    speakerIdleFormId = cue.SpeakerIdle.FormId,
                    speakerIdlePath = cue.SpeakerIdle.ModelPath,
                    speakerIdleSha256 = cue.SpeakerIdle.SourceSha256,
                }),
            },
            actorPresentation = new
            {
                referenceFormId = _vaultBirthCoverage?.Cg01DadActor.ReferenceFormId,
                baseFormId = _vaultBirthCoverage?.Cg01DadActor.BaseFormId,
                startMarkerReferenceFormId =
                    _vaultBirthCoverage?.Cg01DadAppearance.Actor.StartMarkerReferenceFormId,
                rawMarkerPositionGodotGameUnits = _vaultBirthCoverage is null
                    ? null
                    : new[]
                    {
                        _vaultBirthCoverage.Cg01DadAppearance.Actor
                            .StartMarkerPositionGodotGameUnits.X,
                        _vaultBirthCoverage.Cg01DadAppearance.Actor
                            .StartMarkerPositionGodotGameUnits.Y,
                        _vaultBirthCoverage.Cg01DadAppearance.Actor
                            .StartMarkerPositionGodotGameUnits.Z,
                    },
                presentationPositionGodotGameUnits = _vaultBirthCoverage is null
                    ? null
                    : new[]
                    {
                        _vaultBirthCoverage.Cg01DadGrounding
                            .PresentationPlacementGodotGameUnits.X,
                        _vaultBirthCoverage.Cg01DadGrounding
                            .PresentationPlacementGodotGameUnits.Y,
                        _vaultBirthCoverage.Cg01DadGrounding
                            .PresentationPlacementGodotGameUnits.Z,
                    },
                groundingCorrectionGodotGameUnits = _vaultBirthCoverage?
                    .Cg01DadGrounding.VerticalCorrectionGodotGameUnits,
                stage5Enabled = stage5.Dad.Enabled,
                visible = _vaultBirthCoverage?.Cg01DadActor.Placement.Visible,
                previousDoctorVisible = _vaultBirthCoverage?.DoctorActor.Placement.Visible,
                previousCg00DadVisible = _vaultBirthCoverage?.DadActor.Placement.Visible,
                surfaces = _vaultBirthCoverage?.Cg01DadActorGeometry.Surfaces,
                activeCameraName = _vaultBirthCoverage?.Camera.Name.ToString(),
                activeCameraFramesDad = _cg01DadDialogueGeometry?.FrustumIntersection,
                activeCameraDadSurfaces = _cg01DadDialogueGeometry?.Surfaces,
                appearance = "source-stage65-match-race-50-percent-facegen-applied",
                playerRaceFormId = _vaultBirthCoverage?.Cg01DadAppearance.PlayerRaceFormId,
                playerSex = _vaultBirthCoverage?.Cg01DadAppearance.PlayerSex,
                sceneSha256 = _vaultBirthCoverage?.Cg01DadAppearance.Actor.SceneSha256,
                symmetricGeometrySha256 =
                    _vaultBirthCoverage?.Cg01DadAppearance.SymmetricGeometrySha256,
                asymmetricGeometrySha256 =
                    _vaultBirthCoverage?.Cg01DadAppearance.AsymmetricGeometrySha256,
                symmetricTextureSha256 =
                    _vaultBirthCoverage?.Cg01DadAppearance.SymmetricTextureSha256,
                stage65MatchedRaceApplied = true,
                stage65MatchedFaceGeometryApplied = true,
            },
            stage10Commands = new
            {
                accounted = stage10.AccountedCommandCount,
                applied = stage10.AppliedCommandCount,
                trace = stage10.AppliedExecutionTrace,
                dadTimerSeconds = stage10.DadTimerSeconds,
                displayedObjectiveIndex = stage10.DisplayedObjectiveIndex,
                enabledPlayerControls = stage10.EnabledPlayerControls,
                tutorialQuest = new
                {
                    formId = stage10.TutorialQuestFormId,
                    editorId = stage10.TutorialQuestEditorId,
                    stage = stage10.TutorialQuestStage,
                },
                autosaveRequestCount = stage10.AutosaveRequestCount,
            },
            stage12Trigger = new
            {
                referenceFormId = stage12.TriggerReferenceFormId,
                actionReferenceWasPlayer = stage12.ActionReferenceWasPlayer,
                objectiveText = _profile.Cg01Stage12Transition.ObjectiveText,
                completedObjectiveIndex = stage12.CompletedObjectiveIndex,
                disabledPlayerControls = stage12.DisabledPlayerControls,
                dadDoTalk = stage12.DadDoTalk,
                dadTimerSeconds = stage12.DadTimerSeconds,
                accounted = stage12.AccountedCommandCount,
                applied = stage12.AppliedCommandCount,
                trace = stage12.AppliedExecutionTrace,
            },
            stage12DadResponse = new
            {
                sourceStage = stage14.SourceStage,
                targetStage = stage14.ActiveStage,
                topicFormId = _profile.Cg01Stage12DadResponse.TopicFormId,
                topicEditorId = _profile.Cg01Stage12DadResponse.TopicEditorId,
                infoFormIds = stage14.AppliedInfoFormIds,
                sayOnce = _profile.Cg01Stage12DadResponse.Cues.All(value => value.SayOnce),
                playedThisProcess = phase == "apply",
                replayed = false,
                dadTalking = stage14.DadTalking,
                dadLooksAtPlayer = stage14.DadLooksAtPlayer,
                dadPackageEvaluated = stage14.DadPackageEvaluated,
                accounted = stage14.AccountedCommandCount,
                applied = stage14.AppliedCommandCount,
                speakerReferenceFormId = _profile.Cg01Stage12DadResponse.DadReferenceFormId,
                audioLipIdleClockBoundToSpeaker = true,
                speakerIdleInfoFormIdsPublishedThisProcess =
                    stage12PublishedInfoFormIds,
                speakerIdlesPublishedThisProcess = stage12PublishedInfoFormIds.Length,
                assets = _profile.Cg01Stage12DadResponse.Cues.Select(cue => new
                {
                    cue.Sequence,
                    cue.InfoFormId,
                    cue.TargetStage,
                    voiceSha256 = cue.Response.Voice.Sha256,
                    lipSha256 = cue.Response.Lip.Sha256,
                    speakerIdleFormId = cue.SpeakerIdle.FormId,
                    speakerIdlePath = cue.SpeakerIdle.ModelPath,
                    speakerIdleSha256 = cue.SpeakerIdle.SourceSha256,
                }),
            },
            toddlerWorld = new
            {
                schema = Fo3Cg01ToddlerWorldContract.ExpectedSavedStateSchema,
                physicalBody = true,
                collisionShape = "scaled-open-nv-policy-capsule",
                sourcePlayerScale = _profile.Cg01ToddlerWorld.PlayerScale,
                sourceStartMarkerFormId = toddlerWorld.PlayerStartMarkerFormId,
                sourceTriggerReferenceFormId = toddlerWorld.TriggerReferenceFormId,
                triggerEntered = toddlerWorld.TriggerEntered,
                movementEnabled = toddlerWorld.MovementEnabled,
                authoredCollisionBodies = toddlerWorld.AuthoredCollisionBodies,
                visualBodyPrepared = false,
                playerPositionMeters = new[]
                {
                    toddlerWorld.PlayerPositionMeters.X,
                    toddlerWorld.PlayerPositionMeters.Y,
                    toddlerWorld.PlayerPositionMeters.Z,
                },
            },
            movie = new
            {
                logicalPath = stage5.TransitionMovie.LogicalPath,
                runtimeOutputSha256 = stage5.TransitionMovie.RuntimeOutputSha256,
                requestCount = stage5.TransitionMovieRequestCount,
                surfaceRequested = movieSurfaceRequested,
                nonblankFrameValidatedBeforeVisible = _ownedVideoFrameNonblank,
                everVisible = _ownedVideoEverVisible,
                hiddenAndQueuedAfterCompletion = _ownedVideoCleared,
                escapeSkipped,
                replayed = movieReplayed,
            },
            visualCapture = new
            {
                requested = _cg01ProofCapturePath is not null,
                completed = _cg01ProofCaptureCompleted,
                path = _cg01ProofCapturePath,
                sha256 = _cg01ProofCaptureSha256,
                infoFormId = _cg01ProofCaptureInfoFormId,
                speakerIdleFormId = _cg01ProofCaptureSpeakerIdleFormId,
                width = _cg01ProofCaptureWidth,
                height = _cg01ProofCaptureHeight,
                rgbSpan = _cg01ProofCaptureRgbSpan,
                shellVisible = false,
                movieVisible = false,
                dadCameraFrustum = _cg01DadDialogueGeometry?.FrustumIntersection,
                audioLipIdleSynchronized = _cg01ProofCaptureCompleted,
            },
            nextBoundary = new
            {
                applied = stage14.NextBoundary.Applied,
                blocker = stage14.NextBoundary.Blocker,
            },
        };
        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        File.Move(temporary, path, true);
    }
}
