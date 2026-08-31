using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;
using OpenNV.Runtime.Presentation.CharacterCreation;
using OpenNV.Runtime.Presentation.Ui;


using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.Fallout3;

internal partial class Fo3OpeningFlow
{
    private void BuildShell()
    {
        _background = new ColorRect { Color = Colors.Black };
        _background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_background);

        _panel = new PanelContainer();
        _panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _panel.AnchorLeft -= Fo3OpeningFlowNumericContracts.PanelWidthFraction * Fo3OpeningFlowNumericContracts.Center;
        _panel.AnchorRight += Fo3OpeningFlowNumericContracts.PanelWidthFraction * Fo3OpeningFlowNumericContracts.Center;
        _panel.AnchorTop -= Fo3OpeningFlowNumericContracts.PanelHeightFraction * Fo3OpeningFlowNumericContracts.Center;
        _panel.AnchorBottom += Fo3OpeningFlowNumericContracts.PanelHeightFraction * Fo3OpeningFlowNumericContracts.Center;
        _panel.GrowHorizontal = Control.GrowDirection.Both;
        _panel.GrowVertical = Control.GrowDirection.Both;
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(
                _profile.InterfaceColor.R * Fo3OpeningFlowNumericContracts.DimmedColorScale,
                _profile.InterfaceColor.G * Fo3OpeningFlowNumericContracts.DimmedColorScale,
                _profile.InterfaceColor.B * Fo3OpeningFlowNumericContracts.DimmedColorScale,
                Fo3OpeningFlowNumericContracts.PanelAlpha),
            BorderColor = _profile.InterfaceColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
        });
        AddChild(_panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            margin.AddThemeConstantOverride(side, Fo3OpeningFlowNumericContracts.MarginPixels);
        _panel.AddChild(margin);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        margin.AddChild(scroll);
        _content = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _content.AddThemeConstantOverride("separation", Fo3OpeningFlowNumericContracts.SeparationPixels);
        scroll.AddChild(_content);
    }

    private void StartMenuMusic()
    {
        var stream = AudioStreamMP3.LoadFromFile(_profile.MainMenuMusicPath);
        if (stream is null)
            throw new InvalidOperationException("Fallout 3 owned main-menu music could not be loaded.");
        stream.Loop = true;
        _music = new AudioStreamPlayer
        {
            Name = "Fallout3OwnedMainMenuMusic",
            Stream = stream,
        };
        AddChild(_music);
        _music.Play();
    }

    private void ShowMainMenu()
    {
        ClearContent();
        _content.AddChild(Label("FALLOUT 3", Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(
            "OWNED GOTY PROFILE  •  OPENNV",
            Fo3OpeningFlowNumericContracts.BodyFontPixels));
        var newGame = Button("NEW GAME");
        newGame.Pressed += PlayIntro;
        _content.AddChild(newGame);
        var continueGame = Button("CONTINUE CG00");
        continueGame.Disabled = !File.Exists(_savePath);
        continueGame.Pressed += ContinueCharacter;
        _content.AddChild(continueGame);
        var quit = Button("QUIT");
        quit.Pressed += () => GetTree().Quit();
        _content.AddChild(quit);
        Callable.From(newGame.GrabFocus).CallDeferred();
    }

    private void PlayIntro()
    {
        if (_video is not null)
            return;
        _introCompleted = false;
        _ownedVideoMode = Fo3OwnedVideoMode.Intro;
        _music.Stop();
        _introLayer = new Control { Name = "Fallout3OwnedIntro" };
        _introLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_introLayer);
        var black = new ColorRect { Color = Colors.Black };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _introLayer.AddChild(black);
        _video = new VideoStreamPlayer
        {
            Name = "Fallout3OwnedIntroVideo",
            Stream = new VideoStreamTheora { File = _profile.IntroVideoPath },
            Expand = true,
            Loop = false,
            Visible = false,
        };
        _video.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _video.Finished += () => CompleteOwnedVideo(false);
        _introLayer.AddChild(_video);
        var skip = Button("SKIP  •  ESC");
        skip.Name = "SkipFallout3OwnedIntro";
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
            $"OPENNV_FO3_INTRO_STARTED profile={_profile.ProfileId} " +
            "source=owned-transcode escapeSkip=1");
    }

    private void CompleteIntro(bool skipped)
    {
        if (_introCompleted || _ownedVideoMode != Fo3OwnedVideoMode.Intro)
            return;
        _introCompleted = true;
        ClearOwnedVideo();
        StartCg00EarlyBirthSequence();
        GD.Print(
            $"OPENNV_FO3_INTRO_COMPLETE profile={_profile.ProfileId} " +
            $"mode={(skipped ? "skipped" : "watched")} next={_profile.QuestEditorId}");
    }

    private void CompleteOwnedVideo(bool skipped)
    {
        switch (_ownedVideoMode)
        {
            case Fo3OwnedVideoMode.Intro:
                CompleteIntro(skipped);
                break;
            case Fo3OwnedVideoMode.Cg01Transition:
                CompleteCg01TransitionMovie(skipped);
                break;
        }
    }

    private void ClearOwnedVideo()
    {
        if (_video is not null)
        {
            if (_video.Visible && !_ownedVideoFrameNonblank)
                throw new InvalidOperationException(
                    "Fallout 3 owned movie exposed an unvalidated frame.");
            _video.Stop();
            _video.Visible = false;
        }
        if (_introLayer is not null)
        {
            _introLayer.Visible = false;
            _introLayer.QueueFree();
            if (_introLayer.Visible || !_introLayer.IsQueuedForDeletion())
                throw new InvalidOperationException(
                    "Fallout 3 owned movie surface was not hidden and queued for release.");
        }
        _video = null;
        _introLayer = null;
        _ownedVideoMode = Fo3OwnedVideoMode.None;
        _ownedVideoCleared = true;
    }

    private void BeginOwnedVideoSurfaceGate()
    {
        if (_video is null || _introLayer is null)
            throw new InvalidOperationException(
                "Fallout 3 owned movie surface is absent before playback.");
        _video.Visible = false;
        _ownedVideoFrameNonblank = false;
        _ownedVideoEverVisible = false;
        _ownedVideoCleared = false;
        EnforceOwnedPresentationShell();
    }

    private void UpdateOwnedVideoSurface()
    {
        if (_video is null)
            return;
        if (_video.Visible && !_ownedVideoFrameNonblank)
            throw new InvalidOperationException(
                "Fallout 3 owned movie surface became visible before frame validation.");
        if (_ownedVideoFrameNonblank)
            return;
        var image = _video.GetVideoTexture()?.GetImage();
        if (image is null || image.IsEmpty())
            return;
        image.Convert(Image.Format.Rgba8);
        var pixels = image.GetData();
        if (pixels.Length < 4)
            return;
        var red = pixels[0];
        var green = pixels[1];
        var blue = pixels[2];
        for (var index = 4; index + 2 < pixels.Length; index += 4)
        {
            if (Math.Abs(pixels[index] - red) <= 4 &&
                Math.Abs(pixels[index + 1] - green) <= 4 &&
                Math.Abs(pixels[index + 2] - blue) <= 4)
                continue;
            _ownedVideoFrameNonblank = true;
            _ownedVideoEverVisible = true;
            _video.Visible = true;
            GD.Print(
                $"OPENNV_FO3_OWNED_VIDEO_NONBLANK_FRAME_READY " +
                $"mode={_ownedVideoMode} width={image.GetWidth()} height={image.GetHeight()}");
            return;
        }
    }

    private void EnforceOwnedPresentationShell()
    {
        var ownedPresentationActive = _cg01ProofMode is not null ||
            _vaultPreviewHost is not null ||
            _ownedVideoMode != Fo3OwnedVideoMode.None;
        if (ownedPresentationActive)
        {
            _background.Visible = false;
            _panel.Visible = _cg00EarlySexMenuActive;
        }
        if (ownedPresentationActive &&
            (_background.Visible || (_panel.Visible && !_cg00EarlySexMenuActive)))
            throw new InvalidOperationException(
                "Fallout 3 owned presentation exposed the menu shell.");
        if (_panel.Visible && _content.GetChildCount() == 0)
            throw new InvalidOperationException(
                "Fallout 3 menu panel became visible with empty content.");
    }

    private void ShowSexSelection()
    {
        ClearContent();
        if (_cg00EarlySequence is null)
            _content.AddChild(Label(
                "FALLOUT 3  •  CG00",
                Fo3OpeningFlowNumericContracts.TitleFontPixels));
        _content.AddChild(Label(_profile.SexTitle, Fo3OpeningFlowNumericContracts.BodyFontPixels));
        foreach (var choice in _profile.SexChoices)
        {
            var captured = choice;
            var button = Button(choice.Label);
            button.Pressed += () =>
            {
                if (_cg00EarlySequence is null)
                    ShowNameSelection(captured);
                else
                    SelectCg00EarlySex(captured);
            };
            _content.AddChild(button);
        }
        GD.Print(
            $"OPENNV_FO3_CG00_READY profile={_profile.ProfileId} " +
            $"quest={_profile.QuestEditorId} form={_profile.QuestFormId} " +
            $"sexChoices={_profile.SexChoices.Count} nameStage={_profile.NameStage}");
    }

    private void ShowNameSelection(Fo3SexChoice sex)
    {
        _selectedSex = sex;
        EnsureCreatorVaultBackdrop(sex);
        ClearContent();
        var nameUi = _profile.Appearance.Ui.Name;
        var source = nameUi.TextEditMenu;
        var panel = CreatorSurface(
            source.Panel,
            nameUi.BackgroundTexture,
            source.CanvasSize);
        var prompt = Label("", Fo3OpeningFlowNumericContracts.BodyFontPixels);
        OwnedGamebryoTileRuntime.BindText(prompt, source.Prompt.Text);
        var promptSize = prompt.GetCombinedMinimumSize();
        OwnedGamebryoTileRuntime.ApplyTraitPosition(
            prompt,
            source.Prompt.Placement,
            source.Panel.Rect.Size,
            promptSize);
        panel.AddChild(prompt);
        var name = new LineEdit
        {
            PlaceholderText = source.Prompt.Text.Text,
            Alignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(
                source.InputWrapWidth,
                Fo3OpeningFlowNumericContracts.ButtonMinimumHeightPixels),
        };
        name.AddThemeFontSizeOverride("font_size", Fo3OpeningFlowNumericContracts.BodyFontPixels);
        OwnedGamebryoTileRuntime.ApplyTraitPosition(
            name,
            source.Input,
            source.Panel.Rect.Size,
            name.CustomMinimumSize);
        name.TextSubmitted += _ => AcceptName(name);
        panel.AddChild(name);
        var accept = Button("");
        OwnedGamebryoTileRuntime.BindText(accept, source.Accept.Text);
        var acceptSize = accept.GetCombinedMinimumSize();
        OwnedGamebryoTileRuntime.ApplyTraitPosition(
            accept,
            source.Accept.Placement,
            source.Panel.Rect.Size,
            acceptSize);
        accept.Pressed += () => AcceptName(name);
        panel.AddChild(accept);
        _activeNameInput = name;
        Callable.From(name.GrabFocus).CallDeferred();
        GD.Print(
            $"OPENNV_FO3_CG00_NAME_READY stage={_profile.NameStage} " +
            $"sourcePanel={nameUi.PanelWidth}x{nameUi.PanelHeight} " +
            $"background={nameUi.BackgroundTexture.SourceSha256}");
    }

    private void EnsureCreatorVaultBackdrop(Fo3SexChoice sex)
    {
        if (_birthPresentation is null || _vaultPreviewHost is not null)
            return;
        var selection = _profile.Appearance.DefaultSelection(sex.EngineSex);
        var stage65 = _profile.Stage65Appearance.Apply(
            sex.EngineSex,
            selection.Race.FormId,
            selection.Sex.FaceGen);
        var futureDad = _birthPresentation.Cg01DadActorFor(
            selection.Race.FormId,
            sex.EngineSex,
            stage65);
        var host = new Node3D { Name = "FO3_VAULT101_CREATOR_BACKDROP" };
        _worldHost.AddChild(host);
        try
        {
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
        _background.Visible = false;
        _panel.Visible = false;
        GD.Print(
            $"OPENNV_FO3_CREATOR_VAULT_BACKDROP_READY cell=" +
            $"{_birthPresentation.CellFormId} sex={sex.EngineSex} " +
            $"doctorVisible={_vaultBirthCoverage.DoctorActor.Placement.Visible} " +
            $"dadVisible={_vaultBirthCoverage.DadActor.Placement.Visible}");
    }

    private void AcceptName(LineEdit input)
    {
        var playerName = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(playerName))
        {
            input.GrabFocus();
            return;
        }
        PersistNamedCharacter(playerName, _selectedSex!);
        if (_cg00EarlySequence is null)
            ShowAppearanceSelection(playerName, _selectedSex!);
        else
        {
            ClearContent();
            ResumeCg00AfterName(playerName);
        }
        GD.Print(
            $"OPENNV_FO3_CG00_CHARACTER_SAVED profile={_profile.ProfileId} " +
            $"stage={_profile.AppearanceStage} save={_savePath}");
    }

    private void ContinueCharacter()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(_savePath));
            var root = document.RootElement;
            if (RequiredSaveString(root, "schema") != "opennv-fo3-opening-character/v2" ||
                RequiredSaveString(root, "profileId") != _profile.ProfileId ||
                RequiredSaveString(root, "profileSha256") != _profile.Sha256 ||
                RequiredSaveString(root, "questEditorId") != _profile.QuestEditorId ||
                RequiredSaveString(root, "questFormId") != _profile.QuestFormId)
                throw new InvalidOperationException("Saved Fallout 3 CG00 character does not match this profile.");
            var savedSex = RequiredSaveObject(root, "sex");
            _selectedSex = _profile.SexChoices.Single(value =>
                value.Label == RequiredSaveString(savedSex, "label") &&
                value.EngineSex == RequiredSaveString(savedSex, "engineSex"));
            var playerName = RequiredSaveString(root, "playerName");
            var stage = RequiredSaveInteger(root, "stage");
            if (stage == _profile.Appearance.Stage)
            {
                ShowAppearanceSelection(playerName, _selectedSex);
                return;
            }
            if (stage != _profile.Appearance.AcceptedStage &&
                stage != _profile.Stage65Appearance.Stage &&
                stage != _profile.Stage80Transition.Stage &&
                stage != _profile.Stage85Transition.Stage &&
                stage != _profile.Stage90Transition.Stage &&
                stage != _profile.Stage100Transition.Stage &&
                stage != _profile.Cg01Stage0Transition.ResultingStage &&
                stage != _profile.Cg01Stage10Transition.TargetStage &&
                stage != _profile.Cg01Stage12Transition.TargetStage &&
                stage != _profile.Cg01Stage12DadResponse.TargetStage)
                throw new InvalidOperationException("Saved Fallout 3 CG00 stage is unsupported.");
            var savedAppearance = RequiredSaveObject(root, "appearance");
            if (RequiredSaveString(savedAppearance, "sourceContract") !=
                Fo3AppearanceContract.ExpectedSchema)
                throw new InvalidOperationException("Saved Fallout 3 appearance contract is unsupported.");
            var selection = _profile.Appearance.ResolveSelection(
                _selectedSex.EngineSex,
                RequiredSaveString(savedAppearance, "adultRaceFormId"),
                RequiredSaveString(savedAppearance, "childRaceFormId"),
                RequiredSaveString(savedAppearance, "hairFormId"),
                RequiredSaveString(savedAppearance, "eyesFormId"));
            var faceGen = RequiredSaveObject(savedAppearance, "faceGen");
            selection = LoadSavedFaceControls(faceGen, selection);
            if (RequiredSaveString(faceGen, "symmetricGeometrySha256") !=
                    selection.Sex.FaceGen.SymmetricGeometrySha256 ||
                RequiredSaveString(faceGen, "asymmetricGeometrySha256") !=
                    selection.Sex.FaceGen.AsymmetricGeometrySha256 ||
                RequiredSaveString(faceGen, "symmetricTextureSha256") !=
                    selection.Sex.FaceGen.SymmetricTextureSha256)
                throw new InvalidOperationException("Saved Fallout 3 FaceGen defaults differ from the profile.");
            if (stage == _profile.Appearance.AcceptedStage)
            {
                if (root.TryGetProperty("playerPackage", out var savedStage62Package))
                {
                    _profile.Section4Transition.ValidateSavedState(savedStage62Package);
                    ShowVault101BirthRoomBeforeStage65(
                        playerName,
                        _selectedSex,
                        selection,
                        persistPackage: false);
                    return;
                }
                ShowVault101BirthRoom(playerName, _selectedSex, selection);
                return;
            }
            var savedPackage = RequiredSaveObject(root, "playerPackage");
            if (stage == _profile.Stage100Transition.Stage)
                ValidateRemovedPlayerPackageState(savedPackage);
            else
                _profile.Section4Transition.ValidateSavedState(savedPackage);
            var stage65 = _profile.Stage65Appearance.Apply(
                _selectedSex.EngineSex,
                selection.Race.FormId,
                selection.Sex.FaceGen);
            _profile.Stage65Appearance.ValidateSavedState(
                RequiredSaveObject(root, "stage65Appearance"),
                stage65);
            if (stage == stage65.Stage)
            {
                ShowVault101BirthRoom(playerName, _selectedSex, selection, stage65);
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage65-source-bound-ready");
                return;
            }
            var stage80 = _profile.Stage80Transition.Apply(_selectedSex.EngineSex, stage65);
            _profile.Stage80Transition.ValidateSavedState(
                RequiredSaveObject(root, "stage80Transition"),
                stage80);
            if (stage == stage80.Stage)
            {
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage65-cue-finished-stage80-applied");
                ShowVault101BirthRoom(
                    playerName,
                    _selectedSex,
                    selection,
                    stage65,
                    stage80);
                return;
            }
            var stage85 = _profile.Stage85Transition.Apply(stage80);
            _profile.Stage85Transition.ValidateSavedState(
                RequiredSaveObject(root, "stage85Transition"),
                stage85);
            if (stage == stage85.Stage)
            {
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage80-info-trigger-stage85-applied");
                ShowVault101BirthRoom(
                    playerName,
                    _selectedSex,
                    selection,
                    stage65,
                    stage80,
                    stage85);
                return;
            }
            var stage90 = _profile.Stage90Transition.Apply(stage85);
            _profile.Stage90Transition.ValidateSavedState(
                RequiredSaveObject(root, "stage90Transition"),
                stage90);
            if (stage == stage90.Stage)
            {
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage85-info-finished-stage90-applied");
                ShowVault101BirthRoom(
                    playerName,
                    _selectedSex,
                    selection,
                    stage65,
                    stage80,
                    stage85,
                    stage90);
                return;
            }
            var stage100 = _profile.Stage100Transition.Apply(stage90, 0.0);
            _profile.Stage100Transition.ValidateSavedState(
                RequiredSaveObject(root, "stage100Transition"),
                stage100);
            Fo3Cg01Stage0State? cg01 = null;
            Fo3Cg01Stage10State? cg01Stage10 = null;
            Fo3Cg01Stage12State? cg01Stage12 = null;
            Fo3Cg01ToddlerWorldState? cg01ToddlerWorld = null;
            Fo3Cg01Stage14State? cg01Stage14 = null;
            if (root.TryGetProperty("cg01Stage0Transition", out var savedCg01) &&
                savedCg01.ValueKind == JsonValueKind.Object)
            {
                cg01 = _profile.Cg01Stage0Transition.Apply(stage100);
                _profile.Cg01Stage0Transition.ValidateSavedState(savedCg01, cg01);
                if (root.TryGetProperty("cg01Stage10Transition", out var savedCg01Stage10) &&
                    savedCg01Stage10.ValueKind == JsonValueKind.Object)
                {
                    cg01Stage10 = _profile.Cg01Stage10Transition.Apply(
                        cg01,
                        _selectedSex.EngineSex);
                    _profile.Cg01Stage10Transition.ValidateSavedState(
                        savedCg01Stage10,
                        cg01Stage10);
                    if (root.TryGetProperty("cg01Stage12Transition", out var savedCg01Stage12) &&
                        savedCg01Stage12.ValueKind == JsonValueKind.Object)
                    {
                        cg01Stage12 = _profile.Cg01Stage12Transition.ApplyAuthoredTrigger(
                            cg01Stage10,
                            _profile.Cg01Stage12Transition.Trigger.ReferenceFormId,
                            actionReferenceWasPlayer: true);
                        _profile.Cg01Stage12Transition.ValidateSavedState(
                            savedCg01Stage12,
                            cg01Stage12);
                        cg01ToddlerWorld = _profile.Cg01ToddlerWorld.LoadSavedState(
                            RequiredSaveObject(root, "cg01ToddlerWorld"));
                        if (root.TryGetProperty(
                                "cg01Stage12DadResponse",
                                out var savedCg01Stage14) &&
                            savedCg01Stage14.ValueKind == JsonValueKind.Object)
                        {
                            cg01Stage14 = _profile.Cg01Stage12DadResponse.Apply(cg01Stage12);
                            _profile.Cg01Stage12DadResponse.ValidateSavedState(
                                savedCg01Stage14,
                                cg01Stage14);
                        }
                    }
                    ValidateBirthRuntimeState(
                        RequiredSaveObject(root, "birthRuntime"),
                        cg01Stage14 is not null
                            ? "cg01-stage14-dad-response-applied-package-evaluated"
                        : cg01Stage12 is null
                            ? "cg01-stage10-toddler-world-active"
                            : "cg01-stage12-physical-trigger-applied-post-stage12-blocked");
                }
                else
                {
                    ValidateBirthRuntimeState(
                        RequiredSaveObject(root, "birthRuntime"),
                        "cg01-stage0-stage5-applied-dad-dialogue-pending");
                }
            }
            else
            {
                ValidateBirthRuntimeState(
                    RequiredSaveObject(root, "birthRuntime"),
                    "stage90-timer-finished-stage100-applied");
            }
            ShowVault101BirthRoom(
                playerName,
                _selectedSex,
                selection,
                stage65,
                stage80,
                stage85,
                stage90,
                stage100,
                cg01,
                cg01Stage10,
                cg01Stage12,
                cg01ToddlerWorld,
                cg01Stage14);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            GD.PushError($"OPENNV_FO3_CONTINUE_FAIL {exception.Message}");
            ShowMainMenu();
        }
    }

    private void ShowAppearanceSelection(string playerName, Fo3SexChoice sex)
    {
        ClearContent();
        var ui = _profile.Appearance.Ui;
        var characterReflectron = _characterReflectron ??
            throw new InvalidOperationException(
                "Fallout 3 character creation requires the shared owned Reflectron manifest.");
        _creatorLayer = new Control { Name = "FO3_SHARED_REFLECTRON_HOST" };
        _creatorLayer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_creatorLayer);
        _panel.Visible = false;
        _background.Visible = false;
        var referenceCanvas = characterReflectron.NewGameFlow.ReferenceCanvasSize;
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var deviceScale = MathF.Min(
            viewportSize.X / referenceCanvas.X,
            viewportSize.Y / referenceCanvas.Y);
        var deviceCanvas = new Control
        {
            Name = "FO3_SHARED_REFLECTRON_1600X1200",
            Size = referenceCanvas,
            Scale = Vector2.One * deviceScale,
            Position = (viewportSize - referenceCanvas * deviceScale) * 0.5f,
        };
        _creatorLayer.AddChild(deviceCanvas);
        var renderedDevice = characterReflectron.NewGameFlow.Menus.Values
            .Select(menu => menu.RenderedDevice)
            .SingleOrDefault(device => device is not null)
            ?? throw new InvalidOperationException(
                "The shared owned opening manifest has no Reflectron device.");
        var creatorLighting = new CellContentLoader.LightingContract(
            "fo3-character-reflectron-2.0",
            _birthPresentation!.ProofAmbientColor,
            _birthPresentation.ProofAmbientColor,
            _birthPresentation.ProofBackgroundColor,
            _birthPresentation.ProofFogNearGameUnits,
            _birthPresentation.ProofFogFarGameUnits,
            _birthPresentation.ProofFogPower,
            Vector2.Zero,
            0.0f,
            []);
        _reflectron = new OpeningRaceSexRenderedDeviceHost(
            renderedDevice,
            deviceCanvas,
            referenceCanvas,
            _runtimeConfiguration,
            creatorLighting,
            _birthPresentation.UnitsToMeters);
        var panel = _reflectron.CreateMenuPresentationHost(
            new Rect2(0.0f, 0.0f, 340.0f, 500.0f));
        var content = CreatorColumn(
            panel,
            Fo3OpeningFlowNumericContracts.CreatorPanelMarginPixels);
        content.AddThemeConstantOverride(
            "separation",
            Fo3OpeningFlowNumericContracts.CreatorAppearancePanelSeparationPixels);
        content.AddChild(Label(
            $"{playerName}{System.Environment.NewLine}{sex.Label.ToUpperInvariant()}",
            Fo3OpeningFlowNumericContracts.CreatorStatusFontPixels));

        var scaledListItemHeight = ui.ListItemHeight;
        var categorySelect = new OptionButton
        {
            CustomMinimumSize = new Vector2(0.0f, scaledListItemHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        foreach (var category in new[] { "RACE", "HAIR", "EYES", "FACE" })
            categorySelect.AddItem(category);
        categorySelect.AddThemeFontSizeOverride(
            "font_size",
            Fo3OpeningFlowNumericContracts.CreatorStatusFontPixels);
        content.AddChild(categorySelect);
        _activeAppearanceCategory = categorySelect;
        var selectors = new GridContainer { Columns = 1 };
        selectors.AddThemeConstantOverride(
            "h_separation",
            Fo3OpeningFlowNumericContracts.CreatorPanelSeparationPixels);
        selectors.AddThemeConstantOverride(
            "v_separation",
            Fo3OpeningFlowNumericContracts.CreatorPanelSeparationPixels);
        var raceSelect = new OptionButton();
        var hairSelect = new OptionButton();
        var eyesSelect = new OptionButton();
        AddSelector(selectors, "RACE", raceSelect);
        AddSelector(selectors, "HAIR", hairSelect);
        AddSelector(selectors, "EYES", eyesSelect);
        content.AddChild(selectors);

        var defaultSelection = _profile.Appearance.DefaultSelection(sex.EngineSex);
        FillOptions(raceSelect, _profile.Appearance.Races, defaultSelection.Race.FormId, "RACE");
        var faceFrame = _reflectron.CreateFacePresentationHost();
        var previewSource = _profile.Appearance.PreviewFor(
            defaultSelection,
            sex.EngineSex);
        var control = _profile.Appearance.FaceControl;
        var activeControl = control;
        _activeFacePreview = OpeningPlayerFaceGenPreviewHost.Load(
            previewSource,
            _profile.Appearance.FaceControls.Select(value =>
                new OpeningNativeFaceGenGeometryControl(
                    value.ControlIndex,
                    value.SettingEntity,
                    value.SourceLabel,
                    value.AxisSha256)).ToArray(),
            new OpeningFaceGenPreviewControl(
                control.ControlIndex,
                control.SettingEntity,
                control.SourceLabel,
                control.AxisSha256,
                control.Minimum,
                control.Maximum,
                control.Step,
                control.Jump,
                control.MorphWeightScale,
                control.ResetValue,
                control.AcceptanceValue,
                new OpeningFaceGenSliderSemanticsEvidence(
                    Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceClassification,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceEngineBuild,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceExecutableSha256Prefix +
                    Fo3OpeningFlowNumericContracts.FaceGenSliderEvidenceExecutableSha256Suffix,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderSourceMinimum,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderSourceMaximum,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderUiScale,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderUiMinimum,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderUiMaximum,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderOrdinaryIncrement,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderJump,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderMorphWeightScale,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderLowGlobalAddress,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderHighGlobalAddress,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderIncrementTrait,
                    Fo3OpeningFlowNumericContracts.FaceGenSliderIncrementDefaultThreshold),
                new OpeningFaceGenPreviewPresentation(
                    control.Presentation.ViewportWidthFraction,
                    control.Presentation.ViewportHeightFraction,
                    control.Presentation.VerticalFovHalfAngleFactor,
                    control.Presentation.DepthExtentFraction,
                    control.Presentation.FullInVerticalOffsetGameUnits,
                    control.Presentation.FullInDistanceGameUnits,
                    control.Presentation.FullInYawRadians,
                    control.Presentation.FullOutVerticalOffsetGameUnits,
                    control.Presentation.FullOutDistanceGameUnits,
                    control.Presentation.FullOutYawRadians,
                    control.Presentation.StartingZoomFraction),
                control.Semantics),
            faceFrame,
            _runtimeConfiguration,
            creatorLighting,
            _birthPresentation.UnitsToMeters,
            faceFrame.Size,
            renderedDevice);
        var previewProportions =
            CharacterBodyProportions.Neutral("fo3-custom-live-v1");
        var faceFraming = true;
        var greenProjection = false;
        void RefreshProjection()
        {
            _activeFacePreview.SetPreviewState(
                previewProportions,
                faceFraming,
                greenProjection);
            _reflectron.SetCreatorModeState(
                faceFraming ? "FACE" : "BODY",
                bodyEnabled: !faceFraming,
                projectionEnabled: greenProjection,
                faceEnabled: faceFraming);
        }
        RefreshProjection();
        var liveStatus = Label(
            "SCULPT FACE",
            Fo3OpeningFlowNumericContracts.CreatorStatusFontPixels);
        content.AddChild(liveStatus);
        var faceControlSelect = new OptionButton();
        foreach (var faceControl in _profile.Appearance.FaceControls)
            faceControlSelect.AddItem(faceControl.SourceLabel);
        faceControlSelect.Select(Array.IndexOf(
            _profile.Appearance.FaceControls.ToArray(),
            control));
        content.AddChild(faceControlSelect);
        var slider = new HSlider
        {
            Name = "FO3_RaceSexMenu_RSM_slider_option",
            MinValue = control.Minimum,
            MaxValue = control.Maximum,
            Step = control.Step,
            Value = control.ResetValue,
            CustomMinimumSize = new Vector2(
                0.0f,
                ui.SliderHeight * GetViewport().GetVisibleRect().Size.Y /
                    Fo3OpeningFlowNumericContracts.SourceUiCanvasHeightPixels),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        content.AddChild(slider);
        _activeFaceControlSlider = slider;

        void SelectRaceDefaults(Fo3AppearanceRace race)
        {
            var raceSex = race.Sex[sex.EngineSex];
            FillOptions(hairSelect, raceSex.HairOptions, raceSex.DefaultHairFormId, "HAIR");
            FillOptions(eyesSelect, raceSex.EyeOptions, raceSex.DefaultEyesFormId, "EYES");
            SelectCurrent();
        }

        void SelectCurrent()
        {
            var race = _profile.Appearance.Races[raceSelect.Selected];
            var raceSex = race.Sex[sex.EngineSex];
            var selection = new Fo3AppearanceSelection(
                race,
                raceSex,
                raceSex.HairOptions[hairSelect.Selected],
                raceSex.EyeOptions[eyesSelect.Selected],
                _profile.Appearance.FaceControls.ToDictionary(
                    value => value.SettingEntity,
                    value => value.ResetValue,
                    StringComparer.Ordinal));
            var previewSupported = sex.EngineSex == previewSource.Sex &&
                selection.Race.FormId == previewSource.RaceFormId &&
                selection.Hair.FormId == previewSource.HairFormId &&
                selection.Eyes.FormId == previewSource.EyesFormId;
            slider.Editable = previewSupported;
            foreach (var faceControl in _profile.Appearance.FaceControls)
                _activeFacePreview.Apply(faceControl.SettingEntity, faceControl.ResetValue);
            activeControl = control;
            faceControlSelect.Select(Array.IndexOf(
                _profile.Appearance.FaceControls.ToArray(),
                control));
            slider.Value = activeControl.ResetValue;
            _activeFacePreview.Control.Visible = previewSupported;
            liveStatus.Text = previewSupported
                ? "SCULPT FACE"
                : "3D PREVIEW NOT AVAILABLE FOR THIS SELECTION";
            _activeAppearanceSelection = selection;
        }

        raceSelect.ItemSelected += index => SelectRaceDefaults(_profile.Appearance.Races[(int)index]);
        hairSelect.ItemSelected += _ => SelectCurrent();
        eyesSelect.ItemSelected += _ => SelectCurrent();
        slider.ValueChanged += value =>
        {
            if (!slider.Editable || _activeAppearanceSelection is null)
                return;
            _activeFacePreview.Apply(
                activeControl.SettingEntity,
                (float)value * activeControl.MorphWeightScale);
            _activeAppearanceSelection = _profile.Appearance.ApplyFaceControl(
                _activeAppearanceSelection,
                activeControl,
                (float)value);
            liveStatus.Text =
                $"{activeControl.SourceLabel}{System.Environment.NewLine}" +
                $"{(float)value:+0.00;-0.00;0.00}";
        };
        faceControlSelect.ItemSelected += index =>
        {
            activeControl = _profile.Appearance.FaceControls[(int)index];
            slider.MinValue = activeControl.Minimum;
            slider.MaxValue = activeControl.Maximum;
            slider.Step = activeControl.Step;
            slider.Value = _activeAppearanceSelection?.FaceControlValue(
                activeControl.SettingEntity) ?? activeControl.ResetValue;
            liveStatus.Text = activeControl.SourceLabel;
        };
        SelectRaceDefaults(defaultSelection.Race);
        void ShowCategory(long index)
        {
            raceSelect.Visible = index == 0;
            hairSelect.Visible = index == 1;
            eyesSelect.Visible = index == 2;
            slider.Visible = index == 3;
            faceControlSelect.Visible = index == 3;
            liveStatus.Visible = index == 3;
            _reflectron.SetActiveList(index switch
            {
                0 => "race",
                1 => "hair",
                2 => "eyes",
                _ => "face",
            });
        }
        categorySelect.ItemSelected += ShowCategory;
        ShowCategory(0);
        void SelectCategory(int index)
        {
            categorySelect.Select(index);
            ShowCategory(index);
        }
        _reflectron.ConfigureCharacterControls(
            characterReflectron.Font,
            () => { },
            () => SelectCategory(0),
            () => SelectCategory(3),
            () => SelectCategory(1),
            () =>
            {
                faceFraming = true;
                SelectCategory(3);
                RefreshProjection();
            },
            () =>
            {
                faceFraming = false;
                RefreshProjection();
            },
            () =>
            {
                greenProjection = !greenProjection;
                RefreshProjection();
            });
        RefreshProjection();

        var accept = Button("ACCEPT APPEARANCE");
        accept.CustomMinimumSize = new Vector2(0.0f, scaledListItemHeight);
        accept.Pressed += () => AcceptAppearance(playerName, sex);
        content.AddChild(accept);
        Callable.From(raceSelect.GrabFocus).CallDeferred();
        GD.Print(
            $"OPENNV_FO3_CG00_APPEARANCE_READY profile={_profile.ProfileId} " +
            $"stage={_profile.Appearance.Stage} entered={_profile.Appearance.MenuEnteredStage} " +
            $"races={_profile.Appearance.Races.Count} sex={sex.EngineSex} " +
            $"preview=owned-live-default-full-body controls={_profile.Appearance.FaceControls.Count} " +
            $"boundSurfaces={_activeFacePreview.BoundSurfaceCount} " +
            $"bodySurfaces={_activeFacePreview.BodySurfaceCount}");
    }

    private void AcceptAppearance(string playerName, Fo3SexChoice sex)
    {
        var selection = _activeAppearanceSelection ?? throw new InvalidOperationException(
            "Fallout 3 appearance selection is absent.");
        PersistAppearance(playerName, sex, selection);
        if (_cg00EarlySequence is not null)
        {
            _cg00EarlySequence = null;
            _cg00EarlyStage = _profile.Appearance.AcceptedStage;
            _cg00EarlyTimerTargetStage = null;
            ClearCg00ImageSpace();
            _cg00EarlySubtitle?.QueueFree();
            _cg00EarlySubtitle = null;
        }
        if (_birthPresentation is null)
            ShowAppearanceAccepted(playerName, sex, selection);
        else if (_profile.Appearance.FaceControls.Any(control =>
                     selection.FaceControlValue(control.SettingEntity) != control.ResetValue))
            ShowVault101BirthRoomBeforeStage65(
                playerName,
                sex,
                selection,
                persistPackage: true);
        else
            ShowVault101BirthRoom(playerName, sex, selection);
    }
}
