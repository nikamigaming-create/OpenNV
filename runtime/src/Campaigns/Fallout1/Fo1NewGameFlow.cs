using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.NewVegas.Opening;

namespace OpenNV.Runtime.Campaigns.Fallout1;

internal static class Fo1NewGameFlowNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloatNEgativE14Point0f = -14.0f;
    internal const float PresentationFloatNEgativE26Point0f = -26.0f;
    internal const float PresentationFloatNEgativE34Point0f = -34.0f;
    internal const float PresentationFloatNEgativE36Point0f = -36.0f;
    internal const float PresentationFloatNEgativE38Point0f = -38.0f;
    internal const float PresentationFloatNEgativE42Point0f = -42.0f;
    internal const float PresentationFloatNEgativE45Point0f = -45.0f;
    internal const float PresentationFloatNEgativE46Point0f = -46.0f;
    internal const float PresentationFloatNEgativE62Point0f = -62.0f;
    internal const float PresentationFloat0Point0001f = 0.0001f;
    internal const float PresentationFloat0Point001f = 0.001f;
    internal const float PresentationFloat0Point012f = 0.012f;
    internal const float PresentationFloat0Point015f = 0.015f;
    internal const float PresentationFloat0Point018f = 0.018f;
    internal const float PresentationFloat0Point01f = 0.01f;
    internal const float PresentationFloat0Point20f = 0.20f;
    internal const float PresentationFloat0Point27f = 0.27f;
    internal const float PresentationFloat0Point46f = 0.46f;
    internal const float PresentationFloat0Point48f = 0.48f;
    internal const float PresentationFloat0Point5f = 0.5f;
    internal const float PresentationFloat0Point68f = 0.68f;
    internal const float PresentationFloat0Point70f = 0.70f;
    internal const float PresentationFloat0Point72f = 0.72f;
    internal const float PresentationFloat0Point78f = 0.78f;
    internal const float PresentationFloat0Point79f = 0.79f;
    internal const float PresentationFloat0Point93f = 0.93f;
    internal const float PresentationFloat0Point96f = 0.96f;
    internal const float PresentationFloat0Point97f = 0.97f;
    internal const float PresentationFloat0Point9999f = 0.9999f;
    internal const float PresentationFloat1Point15f = 1.15f;
    internal const float PresentationFloat1Point35f = 1.35f;
    internal const int PresentationInt10000 = 10_000;
    internal const int PresentationInt114 = 114;
    internal const int PresentationInt115 = 115;
    internal const int PresentationInt120 = 120;
    internal const int PresentationInt13 = 13;
    internal const float PresentationFloat13Point0f = 13.0f;
    internal const float PresentationFloat130Point0f = 130.0f;
    internal const float PresentationFloat150Point0f = 150.0f;
    internal const int PresentationInt16 = 16;
    internal const float PresentationFloat16Point0f = 16.0f;
    internal const float PresentationFloat178Point0f = 178.0f;
    internal const int PresentationInt18 = 18;
    internal const float PresentationFloat18Point0f = 18.0f;
    internal const float PresentationFloat180Point0f = 180.0f;
    internal const float PresentationFloat19Point0f = 19.0f;
    internal const float PresentationFloat22Point0f = 22.0f;
    internal const double PresentationDouble240Point0 = 240.0;
    internal const float PresentationFloat25Point0f = 25.0f;
    internal const float PresentationFloat26Point0f = 26.0f;
    internal const float PresentationFloat28Point0f = 28.0f;
    internal const float PresentationFloat30Point0f = 30.0f;
    internal const float PresentationFloat31Point0f = 31.0f;
    internal const float PresentationFloat320Point0f = 320.0f;
    internal const float PresentationFloat34Point0f = 34.0f;
    internal const float PresentationFloat4Point2f = 4.2f;
    internal const float PresentationFloat4Point4f = 4.4f;
    internal const float PresentationFloat42Point0f = 42.0f;
    internal const float PresentationFloat432Point0f = 432.0f;
    internal const float PresentationFloat46Point0f = 46.0f;
    internal const int PresentationInt5 = 5;
    internal const float PresentationFloat5Point0f = 5.0f;
    internal const float PresentationFloat640Point0f = 640.0f;
    internal const int PresentationInt70 = 70;
    internal const float PresentationFloat70Point0f = 70.0f;
    internal const float PresentationFloat8Point0f = 8.0f;
    internal const float PresentationFloat915Point0f = 915.0f;
    internal const float PresentationFloat940Point0f = 940.0f;
}

internal static class Fo1NewGameFlow
{
    internal static void StartInteractive(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        string startPresentation,
        bool continueMenuProof = false,
        string? continueProofReportPath = null,
        bool continueFlareUseProof = false,
        bool continueGenericDoorProof = false)
    {
        ValidateHandoff(loaded, contract);
        HideWorld(loaded);
        ShowMainMenu(host, loaded, contract, startPresentation, continueMenuProof, continueProofReportPath, continueFlareUseProof, continueGenericDoorProof);
    }

    private static void ShowMainMenu(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        string startPresentation,
        bool continueMenuProof = false,
        string? continueProofReportPath = null,
        bool continueFlareUseProof = false,
        bool continueGenericDoorProof = false)
    {
        var menu = new Fo1MainMenu();
        menu.Configure(startPresentation, loaded.Session.CanContinue);
        var selected = false;
        menu.ContinueRequested += () =>
        {
            if (selected)
                return;
            selected = true;
            menu.QueueFree();
            _ = ResumeInteractive(host, loaded, contract, continueProofReportPath, continueFlareUseProof, continueGenericDoorProof);
        };
        menu.NewGameRequested += () =>
        {
            if (selected)
                return;
            selected = true;
            menu.QueueFree();
            ShowCharacterSelection(host, loaded, contract, startPresentation);
        };
        menu.ExitRequested += () => host.GetTree().Quit(0);
        host.AddChild(menu);
        if (continueMenuProof)
            _ = RunContinueMenuProof(host, menu);
        GD.Print($"OPENNV_FO1_FRONTEND_READY presentation={startPresentation}");
    }

    private static async Task ResumeInteractive(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        string? continueProofReportPath,
        bool continueFlareUseProof,
        bool continueGenericDoorProof)
    {
        try
        {
            var profile = loaded.Session.RequireRestoredCharacterForContinue();
            var camera = loaded.Session.RequireRestoredCameraForContinue();
            loaded.Session.AttachPipBoy(contract, profile);
            loaded.Session.AttachClassicInterface(contract);
            if (loaded.Session.LoadedDestinationPresentation is { } destination)
                await RevealRestoredDestination(
                    host,
                    loaded,
                    profile,
                    camera,
                    destination,
                    continueProofReportPath,
                    continueFlareUseProof,
                    continueGenericDoorProof);
            else
                await RevealRestoredWorld(host, loaded, profile, camera);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_CONTINUE_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task RunContinueMenuProof(Node host, Fo1MainMenu menu)
    {
        await WaitFrames(host, 1);
        menu.RequestContinueForHeadlessProof();
    }

    private static void ShowCharacterSelection(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        string startPresentation)
    {
        var creator = new Fo1CharacterCreator();
        creator.Configure(
            contract,
            enableHexPortraitToggle: true,
            loaded.PlayerDonors);
        var resolved = false;
        creator.CharacterReady += profile =>
        {
            if (resolved)
                return;
            resolved = true;
            _ = CompleteInteractive(host, loaded, contract, creator, profile, startPresentation);
        };
        creator.BackRequested += () =>
        {
            if (resolved)
                return;
            resolved = true;
            creator.QueueFree();
            ShowMainMenu(host, loaded, contract, startPresentation);
        };
        host.AddChild(creator);
    }

    internal static async Task RunDemo(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        string reportPath,
        bool accelerateOpening,
        bool skipOpening,
        string? captureRoot,
        bool nativeFirstBeatHeadlessProof)
    {
        try
        {
            ValidateHandoff(loaded, contract);
            GD.Print("OPENNV_FO1_NEW_GAME_DEMO_PHASE character-creation");
            HideWorld(loaded);
            var creator = new Fo1CharacterCreator();
            creator.Configure(
                contract,
                enableHexPortraitToggle: true,
                loaded.PlayerDonors);
            host.AddChild(creator);
            var profile = captureRoot is null && !nativeFirstBeatHeadlessProof
                ? await creator.RunAutomatedDemo(host)
                : await creator.RunAutomatedOwnedDonorDemo(host);
            profile.Validate();
            var premadePlayerPreview = creator.PremadePlayerPreviewReport();
            GD.Print("OPENNV_FO1_NEW_GAME_DEMO_PHASE overseer-opening");
            var opening = await PlayOpening(
                host,
                contract,
                creator,
                accelerateOpening,
                skipOpening,
                loaded.RuntimeProfile.Showcase);
            GD.Print("OPENNV_FO1_NEW_GAME_DEMO_PHASE v13ent-handoff");
            loaded.Session.ApplyCharacter(profile);
            loaded.Session.AttachPipBoy(contract, profile);
            loaded.Session.AttachClassicInterface(contract);
            if (nativeFirstBeatHeadlessProof)
            {
                await RunCombatShowcase(
                    host,
                    loaded,
                    contract,
                    profile,
                    reportPath,
                    opening,
                    default,
                    premadePlayerPreview,
                    captureRoot,
                    nativeFirstBeatHeadlessProof);
                return;
            }
            var landing = await RevealWorld(host, loaded, profile, opening, "first-person");
            await RunCombatShowcase(
                host,
                loaded,
                contract,
                profile,
                reportPath,
                opening,
                landing,
                premadePlayerPreview,
                captureRoot,
                nativeFirstBeatHeadlessProof);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_NEW_GAME_DEMO_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    internal static async Task RunCharacterVideo(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        string character,
        OpeningManifest characterReflectron)
    {
        try
        {
            ValidateHandoff(loaded, contract);
            HideWorld(loaded);
            var creator = new Fo1CharacterCreator();
            creator.Configure(
                contract,
                enableHexPortraitToggle: true,
                loaded.PlayerDonors,
                characterReflectron);
            host.AddChild(creator);
            var profile = await creator.RunCharacterVideo(host, character);
            profile.Validate();
            var opening = await PlayOpening(
                host,
                contract,
                creator,
                accelerate: true,
                forceSkip: true,
                loaded.RuntimeProfile.Showcase);
            loaded.Session.ApplyCharacter(profile);
            loaded.Session.AttachPipBoy(contract, profile);
            loaded.Session.AttachClassicInterface(contract);
            await RevealWorld(host, loaded, profile, opening, "hex-tactical");
            await WaitFrames(host, Fo1NewGameFlowNumericContracts.PresentationInt120);
            GD.Print($"OPENNV_FO1_CHARACTER_VIDEO_COMPLETE character={character}");
            host.GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_CHARACTER_VIDEO_FAIL {exception}");
            host.GetTree().Quit(1);
        }
    }

    private static async Task CompleteInteractive(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        Fo1CharacterCreator creator,
        Fo1CharacterProfile profile,
        string startPresentation)
    {
        try
        {
            profile.Validate();
            var opening = await PlayOpening(
                host,
                contract,
                creator,
                false,
                false,
                loaded.RuntimeProfile.Showcase);
            loaded.Session.ApplyCharacter(profile);
            loaded.Session.AttachPipBoy(contract, profile);
            loaded.Session.AttachClassicInterface(contract);
            await RevealWorld(host, loaded, profile, opening, startPresentation);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_NEW_GAME_FAIL {exception.Message}");
            host.GetTree().Quit(1);
        }
    }

    private static void ValidateHandoff(
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract)
    {
        if (contract.EntryTile != loaded.EntryTile || contract.EntryElevation != 0 ||
            contract.EntryRotation != 2)
            throw new InvalidOperationException(
                "Fallout creator/opening contract does not hand off to exact V13ENT tile 17690 rotation 2.");
    }

    private static void HideWorld(Fo1HexSceneLoader.LoadedFo1HexScene loaded)
    {
        loaded.Root.Visible = false;
        loaded.Session.Hud.Visible = false;
        loaded.Session.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.Camera.ProcessMode = Node.ProcessModeEnum.Disabled;
    }

    private static async Task<OpeningPlayback> PlayOpening(
        Node host,
        Fo1CharacterStartContract contract,
        Fo1CharacterCreator creator,
        bool accelerate,
        bool forceSkip,
        Fo1ShowcaseProfile showcase)
    {
        var layer = new CanvasLayer { Name = "OriginalFalloutOverseerOpening", Layer = Fo1NewGameFlowNumericContracts.PresentationInt120 };
        host.AddChild(layer);
        var black = new ColorRect { Color = Colors.Black };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(black);
        var viewport = host.GetViewport().GetVisibleRect().Size;
        var height = MathF.Min(viewport.Y - Fo1NewGameFlowNumericContracts.PresentationFloat70Point0f, Fo1NewGameFlowNumericContracts.PresentationFloat640Point0f);
        var width = height * Fo1NewGameFlowNumericContracts.PresentationFloat432Point0f / Fo1NewGameFlowNumericContracts.PresentationFloat320Point0f;
        var video = new Fo1MoviePackPlayer
        {
            Name = "OwnedOriginalOverseerMvePlayback",
            Position = new Vector2((viewport.X - width) * Fo1NewGameFlowNumericContracts.PresentationFloat0Point5f, (viewport.Y - height) * Fo1NewGameFlowNumericContracts.PresentationFloat0Point5f - Fo1NewGameFlowNumericContracts.PresentationFloat8Point0f),
            Size = new Vector2(width, height),
        };
        video.Configure(contract);
        layer.AddChild(video);
        var provenance = new Label
        {
            Position = new Vector2(0.0f, viewport.Y - Fo1NewGameFlowNumericContracts.PresentationFloat34Point0f),
            Size = new Vector2(viewport.X, Fo1NewGameFlowNumericContracts.PresentationFloat26Point0f),
            Text = "OWNED ORIGINAL FALLOUT 1  •  OVERSEER BRIEFING  •  VIDEO + AUDIO",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        provenance.AddThemeColorOverride("font_color", new Color(Fo1NewGameFlowNumericContracts.PresentationFloat0Point72f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point68f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point48f));
        provenance.AddThemeFontSizeOverride("font_size", Fo1NewGameFlowNumericContracts.PresentationInt13);
        layer.AddChild(provenance);
        var skipRequested = forceSkip;
        var skip = new Button
        {
            Name = "SkipOwnedFalloutOpening",
            Position = new Vector2(viewport.X - Fo1NewGameFlowNumericContracts.PresentationFloat178Point0f, Fo1NewGameFlowNumericContracts.PresentationFloat22Point0f),
            Size = new Vector2(Fo1NewGameFlowNumericContracts.PresentationFloat150Point0f, Fo1NewGameFlowNumericContracts.PresentationFloat42Point0f),
            Text = "SKIP  •  ESC",
            FocusMode = Control.FocusModeEnum.None,
        };
        skip.AddThemeColorOverride("font_color", new Color(Fo1NewGameFlowNumericContracts.PresentationFloat0Point96f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point79f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point27f));
        skip.AddThemeFontSizeOverride("font_size", Fo1NewGameFlowNumericContracts.PresentationInt16);
        skip.Pressed += () => skipRequested = true;
        layer.AddChild(skip);
        var handoffFade = new ColorRect
        {
            Name = "OwnedOpeningToLiveWorldFade",
            Color = new Color(0.0f, 0.0f, 0.0f, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        handoffFade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(handoffFade);
        creator.QueueFree();
        await WaitFrames(host, 2);
        video.PlayMovie();
        await WaitFrames(host, 2);
        if (!video.IsMoviePlaying)
            throw new InvalidOperationException("Owned Fallout Overseer video did not start.");
        var playbackScale = accelerate || DisplayServer.GetName() == "headless"
            ? showcase.AcceleratedOpeningScale
            : 1.0;
        var cinematicTailFirstFrame = Math.Max(
            0,
            contract.OpeningFrameCount - contract.OpeningFramesPerSecond * 4);
        var skipped = false;
        for (var frame = 0; frame < Fo1NewGameFlowNumericContracts.PresentationInt10000 && video.IsMoviePlaying; frame++)
        {
            if (skipRequested || Input.IsKeyPressed(Key.Escape))
            {
                skipped = true;
                video.SkipMovie();
                break;
            }
            var frameScale = playbackScale > 1.0 &&
                video.CurrentFrameIndex >= cinematicTailFirstFrame
                    ? 1.0
                    : playbackScale;
            video.AdvanceMovie(
                Math.Max(1.0 / Fo1NewGameFlowNumericContracts.PresentationDouble240Point0, host.GetProcessDeltaTime()) * frameScale);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        if (video.IsMoviePlaying || !skipped &&
            playbackScale == 1.0 && video.RenderedFrames < contract.OpeningFrameCount)
            throw new InvalidOperationException("Owned Fallout Overseer video did not finish within the gate.");
        var handoffFrameIndex = video.CurrentFrameIndex;
        var handoffFrameSha256 = video.CurrentFrameSha256;
        for (var frame = 0; frame < showcase.OpeningFadeOutFrames; frame++)
        {
            handoffFade.Color = new Color(
                0.0f,
                0.0f,
                0.0f,
                (frame + 1.0f) / showcase.OpeningFadeOutFrames);
            await WaitFrames(host, 1);
        }
        layer.QueueFree();
        return new OpeningPlayback(
            skipped,
            video.RenderedFrames,
            skipped ? 0.0 : playbackScale,
            handoffFrameIndex,
            handoffFrameSha256);
    }

    private static async Task<LandingPlayback> RevealWorld(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterProfile profile,
        OpeningPlayback opening,
        string startPresentation)
    {
        var layer = new CanvasLayer { Name = "V13ENTFirstPersonHandoff", Layer = Fo1NewGameFlowNumericContracts.PresentationInt115 };
        host.AddChild(layer);
        var black = new ColorRect
        {
            Name = "MovieFinalFrameToLiveFirstPersonFade",
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(black);

        loaded.Root.Visible = true;
        loaded.Session.Hud.Visible = true;
        loaded.Session.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.Camera.Visible = true;
        loaded.Camera.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.CaveCutaway.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.CaveCutaway.SetMeltEnabled(false);
        loaded.Door.Controller.SetOpenAmount(1.0f);
        loaded.Session.SetWorldGuidesVisible(false);
        foreach (var mob in loaded.Session.Mobs)
            mob.SetReadabilityMarkersVisible(false);
        loaded.Session.SetCinematicPlayerAnimation(false, moving: false);

        var door = Fo1HexMath.Center(loaded.DoorTile);
        var exactEntry = Fo1HexMath.Center(loaded.EntryTile) +
            Vector3.Up * loaded.RuntimeProfile.Scene.SourceSprites.GroundAnchorMeters;
        var towardCave = exactEntry - door;
        towardCave.Y = 0.0f;
        towardCave = towardCave.Normalized();
        loaded.Session.PlayerToken.Position = exactEntry;
        loaded.Session.PlayerToken.LookAt(exactEntry + towardCave * Fo1NewGameFlowNumericContracts.PresentationFloat5Point0f, Vector3.Up);

        loaded.Camera.SetFirstPersonMode(true);
        loaded.Camera.SetOrbitDegrees(
            Mathf.RadToDeg(loaded.Camera.TargetYawRadians),
            loaded.RuntimeProfile.Camera.FirstPerson.InitialPitchDegrees);
        loaded.Camera.Camera.Current = true;
        var preparedCameraPosition = loaded.Camera.FirstPersonEyePosition;
        var preparedCameraForward = loaded.Camera.FirstPersonForward;
        preparedCameraForward.Y = 0.0f;
        var spawnErrorMeters = loaded.Session.PlayerToken.Position.DistanceTo(exactEntry);
        var eyeErrorMeters = preparedCameraPosition.DistanceTo(
            loaded.Session.PlayerToken.GlobalPosition +
            Vector3.Up * loaded.Camera.FirstPersonEyeHeightMeters);
        var caveForwardAlignment = preparedCameraForward.Normalized().Dot(towardCave);
        if (!loaded.Camera.FirstPersonMode || loaded.Session.PlayerToken.Visible ||
            !loaded.Door.Controller.IsOpen || spawnErrorMeters > Fo1NewGameFlowNumericContracts.PresentationFloat0Point001f ||
            eyeErrorMeters > Fo1NewGameFlowNumericContracts.PresentationFloat0Point001f || caveForwardAlignment < Fo1NewGameFlowNumericContracts.PresentationFloat0Point9999f)
            throw new InvalidOperationException(
                $"Fallout Vault 13 first-person handoff preparation failed: " +
                $"spawn={spawnErrorMeters:F6} eye={eyeErrorMeters:F6} " +
                $"forward={caveForwardAlignment:F6} doorOpen={loaded.Door.Controller.IsOpen}.");

        loaded.Camera.ProcessMode = Node.ProcessModeEnum.Inherit;
        loaded.Session.SetCameraStatus(
            $"{profile.Name} • {(opening.Skipped ? "opening skipped" : "opening watched")} • " +
            $"LIVE FIRST-PERSON V13ENT • exact tile {loaded.EntryTile} rotation 2 • C tactical");
        for (var frame = 0;
             frame < loaded.RuntimeProfile.Showcase.LandingFadeInFrames;
             frame++)
        {
            var amount = (frame + 1.0f) /
                loaded.RuntimeProfile.Showcase.LandingFadeInFrames;
            var eased = amount * amount * (3.0f - 2.0f * amount);
            black.Color = new Color(0.0f, 0.0f, 0.0f, 1.0f - eased);
            await WaitFrames(host, 1);
        }

        var liveCameraPosition = loaded.Camera.FirstPersonEyePosition;
        var liveCameraForward = loaded.Camera.FirstPersonForward;
        var cameraPositionSeamMeters = preparedCameraPosition.DistanceTo(liveCameraPosition);
        var cameraForwardSeamAlignment = preparedCameraForward.Normalized().Dot(
            new Vector3(liveCameraForward.X, 0.0f, liveCameraForward.Z).Normalized());
        loaded.Session.ProcessMode = Node.ProcessModeEnum.Inherit;
        layer.QueueFree();
        if (cameraPositionSeamMeters > Fo1NewGameFlowNumericContracts.PresentationFloat0Point001f || cameraForwardSeamAlignment < Fo1NewGameFlowNumericContracts.PresentationFloat0Point9999f ||
            loaded.Session.PlayerTile != loaded.EntryTile ||
            loaded.Session.PlayerToken.Position.DistanceTo(exactEntry) > Fo1NewGameFlowNumericContracts.PresentationFloat0Point001f)
            throw new InvalidOperationException(
                $"Fallout Vault 13 movie-to-control seam drifted: " +
                $"position={cameraPositionSeamMeters:F6} forward={cameraForwardSeamAlignment:F6}.");
        if (startPresentation == "hex-tactical")
            loaded.Camera.SetExplorationMode(false);
        loaded.Session.PersistCameraState();
        return new LandingPlayback(
            "owned-opening-frame-fade-to-exact-live-first-person-v13ent",
            loaded.Door.Controller.IsOpen,
            loaded.EntryTile,
            opening.Skipped,
            loaded.Camera.FirstPersonEyeHeightMeters,
            loaded.Camera.FirstPersonFovDegrees,
            spawnErrorMeters,
            caveForwardAlignment,
            cameraPositionSeamMeters,
            cameraForwardSeamAlignment,
            "presentation-adaptation-open-for-corridor-lookback; not claimed as retail door-state parity");
    }

    private static async Task RevealRestoredWorld(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterProfile profile,
        Fo1CameraSaveState cameraState)
    {
        var layer = new CanvasLayer
        {
            Name = "Fo1ContinueResumeHandoff",
            Layer = Fo1NewGameFlowNumericContracts.PresentationInt115,
        };
        host.AddChild(layer);
        var black = new ColorRect
        {
            Name = "Fo1ContinuePreparedWorldCover",
            Color = Colors.Black,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(black);

        var savedTile = loaded.Session.PlayerTile;
        loaded.Root.Visible = true;
        loaded.Session.Hud.Visible = true;
        loaded.Session.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.Camera.Visible = true;
        loaded.Camera.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.CaveCutaway.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.Door.Controller.SetOpenAmount(1.0f);
        loaded.Session.SnapPlayerToHexCenter();
        loaded.Session.SetCinematicPlayerAnimation(false, moving: false);

        loaded.Camera.ApplySaveState(cameraState);
        if (cameraState.Mode == "first-person")
        {
            loaded.Session.SetWorldGuidesVisible(false);
            foreach (var mob in loaded.Session.Mobs)
                mob.SetReadabilityMarkersVisible(false);
            loaded.CaveCutaway.SetMeltEnabled(false);
        }
        else
        {
            loaded.Session.SetWorldGuidesVisible(true);
            foreach (var mob in loaded.Session.Mobs)
                mob.SetReadabilityMarkersVisible(true);
            loaded.CaveCutaway.SetMeltEnabled(cameraState.Mode != "first-person");
        }
        loaded.Camera.Camera.Current = true;
        loaded.Session.SetCameraStatus(
            $"{profile.Name} • continued saved hex {savedTile} • " +
            cameraState.Mode.ToUpperInvariant());

        loaded.Camera.ProcessMode = Node.ProcessModeEnum.Inherit;
        loaded.CaveCutaway.ProcessMode = Node.ProcessModeEnum.Inherit;
        await WaitFrames(host, 1);
        _ = loaded.Session.RequireRestoredCharacterForContinue();
        if (loaded.Session.PlayerTile != savedTile ||
            loaded.Session.PlayerHexCenterErrorMeters >
                Fo1NewGameFlowNumericContracts.PresentationFloat0Point001f ||
            loaded.Session.PipBoy is null || !loaded.Session.ClassicInterfaceAttached ||
            loaded.Camera.CaptureSaveState() != cameraState)
            throw new InvalidOperationException(
                "Fallout 1 Continue failed to preserve the saved player, UI, or selected camera mode.");
        loaded.Session.ProcessMode = Node.ProcessModeEnum.Inherit;
        layer.QueueFree();
        GD.Print(
            $"OPENNV_FO1_CONTINUE_READY tile={savedTile} camera={cameraState.Mode}");
    }

    private static async Task RevealRestoredDestination(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterProfile profile,
        Fo1CameraSaveState cameraState,
        Fo1DestinationPresentationContract destination,
        string? continueProofReportPath,
        bool continueFlareUseProof,
        bool continueGenericDoorProof)
    {
        var transition = loaded.Session.ExitGridTransition ?? throw new InvalidOperationException(
            "Fallout destination Continue has no explicit exit-grid descriptor.");
        if (loaded.Root.Visible || loaded.Session.ActivatedExitGridTile is not { } activatedTile ||
            !transition.IsTrigger(activatedTile))
            throw new InvalidOperationException(
                "Fallout destination Continue did not restore from an owned V13ENT exit trigger.");
        var savedTile = loaded.Session.PlayerTile;
        if (!loaded.Session.CanWalk(savedTile))
            throw new InvalidOperationException(
                "Fallout destination Continue restored a tile outside the source walk mask.");
        loaded.Session.Hud.Visible = true;
        loaded.Session.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.Camera.Visible = false;
        loaded.Camera.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.CaveCutaway.ProcessMode = Node.ProcessModeEnum.Disabled;
        loaded.Session.SnapPlayerToHexCenter();
        loaded.Session.SetWorldGuidesVisible(true);
        loaded.Session.SetCinematicPlayerAnimation(false, moving: false);
        loaded.Session.SetCameraStatus(
            $"{profile.Name} • continued VAULT13 saved hex {savedTile} • " +
            $"source MAP {destination.Map.SourceFile}");
        await WaitFrames(host, 1);
        _ = loaded.Session.RequireRestoredCharacterForContinue();
        if (loaded.Root.Visible || loaded.Session.PlayerTile != savedTile ||
            loaded.Session.PlayerHexCenterErrorMeters >
                Fo1NewGameFlowNumericContracts.PresentationFloat0Point001f ||
            loaded.Session.PipBoy is null || !loaded.Session.ClassicInterfaceAttached ||
            cameraState.Mode.Length == 0)
            throw new InvalidOperationException(
                "Fallout destination Continue failed to preserve the saved player or UI state.");
        loaded.Session.ProcessMode = Node.ProcessModeEnum.Inherit;
        if (!string.IsNullOrWhiteSpace(continueProofReportPath))
        {
            object? flareUse = null;
            if (continueFlareUseProof)
            {
                var flare = loaded.Session.DestinationFlareUse ?? throw new InvalidOperationException(
                    "Fallout flare Continue proof requires an explicit flare-use descriptor.");
                var inventory = loaded.Session.ClassicInventory ?? throw new InvalidOperationException(
                    "Fallout flare Continue proof requires the owned inventory screen.");
                loaded.Camera._UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = loaded.Session.InventoryKey });
                inventory.SelectSourceInventorySymbolForProof(flare.Symbol);
                inventory.UseSelectedSourceInventoryForProof();
                loaded.Camera._UnhandledInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Escape });
                if (inventory.IsOpen || !loaded.Session.DestinationFlareLit)
                    throw new InvalidOperationException("Fallout flare Continue proof did not persist source-script lit state.");
                flareUse = new
                {
                    flare = flare.Report(),
                    selectedSymbol = inventory.SelectedSymbol,
                    lit = true,
                    activeHand = "not-proven-by-script",
                    expiry = "unimplemented-fail-closed"
                };
            }
            object? genericDoor = null;
            int destinationMove;
            if (continueGenericDoorProof)
            {
                var door = loaded.Session.DestinationGenericDoor ?? throw new InvalidOperationException(
                    "Fallout generic-door Continue proof requires an explicit door descriptor.");
                var approachPath = await MoveTacticalAdjacentToSourceTile(host, loaded, door.Door.Tile);
                var contactTile = loaded.Session.PlayerTile;
                if (!Fo1HexMath.AreNeighbors(contactTile, door.Door.Tile) ||
                    !loaded.Session.TryActivateAdjacentDestinationGenericDoor() ||
                    !loaded.Session.DestinationGenericDoorOpen || !loaded.Session.CanWalk(door.Door.Tile))
                    throw new InvalidOperationException("Fallout generic-door Continue proof did not open its authored blocker.");
                if (loaded.Session.ActionPoints == 0)
                    loaded.Session.EndTurn();
                loaded.Session.SelectTile(door.Door.Tile);
                loaded.Session.CompleteQueuedTacticalMovementForHeadlessProof();
                if (loaded.Session.PlayerTile != door.Door.Tile)
                    throw new InvalidOperationException("Fallout generic-door Continue proof did not move through its opened source blocker.");
                destinationMove = door.Door.Tile;
                genericDoor = new
                {
                    descriptor = door.Report(open: true),
                    approach = new { sourceWalkMaskOnly = true, pathTiles = approachPath, contactTile },
                    opened = true,
                    movedThroughOpenedBlocker = true,
                    interactionActionPoints = "not-source-backed",
                    sound = "unsupported-fail-closed",
                    animationTiming = "unsupported-fail-closed",
                };
            }
            else
                destinationMove = MoveOneLegalDestinationHex(loaded.Session);
            if (loaded.Session.PlayerTile != destinationMove)
                throw new InvalidOperationException(
                    "Fallout destination Continue did not admit its first source-mask move.");
            var report = new
            {
                schema = "opennv-fo1-launcher-continue-destination-proof/v1",
                status = "pass-source-bound-launcher-menu-continue-vault13-headless-not-rendered",
                launcher = new { route = "fo1-new-game", menuAction = "continue", eventContract = "Fo1MainMenu.ContinueRequested" },
                transition = transition.Report(activatedTile, destinationSceneLoaded: true),
                destinationPresentation = destination.Report(transition),
                restored = new { playerTile = savedTile, sourceWalkMaskOnly = true, sourceRootVisible = loaded.Root.Visible },
                flareUse,
                genericDoor,
                firstControllableDestinationMove = new { sourceWalkMaskOnly = true, destinationMove },
                gameplay = loaded.Session.Report(),
                rendered = false,
                interactive = false,
                files = Array.Empty<object>(),
            };
            File.WriteAllText(
                continueProofReportPath,
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                    System.Environment.NewLine);
            GD.Print(
                $"OPENNV_FO1_LAUNCHER_CONTINUE_VAULT13_PASS restored={savedTile} move={destinationMove}");
            host.GetTree().Quit(0);
            return;
        }
        GD.Print($"OPENNV_FO1_CONTINUE_READY tile={savedTile} destination={destination.Map.Id}");
    }

    private static async Task RunCombatShowcase(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        Fo1CharacterProfile profile,
        string reportPath,
        OpeningPlayback opening,
        LandingPlayback landing,
        object premadePlayerPreview,
        string? captureRoot,
        bool nativeFirstBeatHeadlessProof)
    {
        var showcase = loaded.RuntimeProfile.Showcase;
        var reportFullPath = Path.GetFullPath(reportPath);
        string? captureFullPath = null;
        if (!string.IsNullOrWhiteSpace(captureRoot))
        {
            captureFullPath = Path.GetFullPath(captureRoot);
            if (Directory.Exists(captureFullPath) || File.Exists(captureFullPath))
                throw new InvalidOperationException(
                    $"Refusing to overwrite Fallout 1 native first-beat proof: {captureFullPath}");
            Directory.CreateDirectory(captureFullPath);
        }
        GD.Print("OPENNV_FO1_NEW_GAME_DEMO_PHASE complete-combat-showcase");
        Directory.CreateDirectory(Path.GetDirectoryName(reportFullPath)!);
        var stage = BuildStageBanner(host, showcase.StageBannerVisible);
        stage.Text = "01  CHARACTER PROFILE SURVIVED THE OPENING  •  LIVE HP / AP / AC / SEQUENCE";
        loaded.Session.SetCameraStatus(
            $"{profile.Name}  •  V13ENT  •  exact tile {loaded.EntryTile}  •  " +
            "I opens Inventory • P opens Pip-Boy 2000");
        await WaitFrames(host, showcase.LandingHoldFrames);
        var inventoryBefore = loaded.Session.InventorySnapshot();
        var inventorySaveSha256Before = FileSha256(loaded.Session.SavePath);
        loaded.Camera._UnhandledInput(new InputEventKey
        {
            Pressed = true,
            PhysicalKeycode = loaded.Session.InventoryKey,
        });
        if (!nativeFirstBeatHeadlessProof)
            await WaitFrames(host, 1);
        var inventory = loaded.Session.ClassicInventory;
        if (inventory is null || !inventory.IsOpen || inventory.OpenedCount != 1 ||
            inventory.VisibleStackCount != inventoryBefore.Count ||
            string.IsNullOrWhiteSpace(inventory.SelectedSymbol))
            throw new InvalidOperationException(
                "Fallout classic inventory failed to open from its configured input.");
        var rangedSymbolBeforeInventory = loaded.Session.EquippedWeaponSymbol;
        object? inventoryCapture = null;
        var requiresMeleeInventorySelection =
            captureFullPath is not null || nativeFirstBeatHeadlessProof;
        if (requiresMeleeInventorySelection)
        {
            var meleeButton = inventory.FindChild(
                "OwnedInventoryMeleeHandButton",
                true,
                false) as Button ?? throw new InvalidOperationException(
                    "Fallout classic inventory has no source-bound melee active-hand control.");
            meleeButton.EmitSignal(Button.SignalName.Pressed);
            if (!nativeFirstBeatHeadlessProof)
                await WaitFrames(host, 2);
            if (loaded.Session.EquippedWeaponSymbol == rangedSymbolBeforeInventory ||
                inventory.EquipmentChangedCount != 1)
                throw new InvalidOperationException(
                    "Fallout classic inventory melee active-hand control did not mutate equipment state.");
            if (captureFullPath is not null)
                inventoryCapture = SaveNativeCapture(
                    host,
                    captureFullPath,
                    "v13ent-inventory-equipped-melee.png");
        }
        loaded.Camera._UnhandledInput(new InputEventKey
        {
            Pressed = true,
            PhysicalKeycode = Key.Escape,
        });
        var inventoryAfter = loaded.Session.InventorySnapshot();
        var inventorySaveSha256After = FileSha256(loaded.Session.SavePath);
        var inventoryUnchanged = inventoryBefore.SequenceEqual(inventoryAfter);
        var saveByteStable = inventorySaveSha256Before == inventorySaveSha256After;
        var savedEquippedSymbol = ReadSavedEquippedWeapon(loaded.Session.SavePath);
        if (inventory.IsOpen || inventory.ClosedCount != 1 ||
            !inventoryUnchanged ||
            !requiresMeleeInventorySelection && !saveByteStable ||
            requiresMeleeInventorySelection &&
                (inventory.EquipmentChangedCount != 1 ||
                 savedEquippedSymbol == rangedSymbolBeforeInventory ||
                 savedEquippedSymbol != loaded.Session.EquippedWeaponSymbol))
            throw new InvalidOperationException(
                "Fallout classic inventory changed gameplay or save truth.");
        var inventoryProof = new
        {
            input = loaded.Session.InventoryKey.ToString(),
            openedCount = inventory.OpenedCount,
            closedCount = inventory.ClosedCount,
            stackCount = inventory.VisibleStackCount,
            selectedSymbol = inventory.SelectedSymbol,
            inventoryBefore,
            inventoryAfter,
            saveSha256Before = inventorySaveSha256Before,
            saveSha256After = inventorySaveSha256After,
            unchanged = inventoryUnchanged,
            saveByteStable,
            equipmentMutation = !requiresMeleeInventorySelection
                ? null
                : new
                {
                    equipmentChangedCount = inventory.EquipmentChangedCount,
                    activeHandSymbol = loaded.Session.EquippedWeaponSymbol,
                    savedEquippedSymbol,
                    nativeCapture = inventoryCapture,
                },
        };
        if (captureFullPath is not null)
        {
            await CompleteNativeFirstBeatProof(
                host,
                loaded,
                contract,
                profile,
                reportFullPath,
                captureFullPath,
                inventoryProof,
                inventoryCapture,
                premadePlayerPreview,
                stage);
            return;
        }
        if (nativeFirstBeatHeadlessProof)
        {
            await CompleteNativeFirstBeatHeadlessProof(
                host,
                loaded,
                contract,
                profile,
                reportFullPath,
                inventoryProof,
                premadePlayerPreview);
            return;
        }
        loaded.Session.TogglePipBoy();
        await WaitFrames(host, showcase.LandingHoldFrames);
        loaded.Session.TogglePipBoy();

        var caveFacingYaw = loaded.Camera.TargetYawRadians;
        stage.Text = "02  LIVE FIRST-PERSON  •  TURN AROUND  •  OPEN VAULT 13 DOOR + CORRIDOR";
        GD.Print("OPENNV_FO1_NEW_GAME_DEMO_PHASE first-person-vault-lookback");
        await SmoothFirstPersonYaw(
            host,
            loaded.Camera,
            caveFacingYaw,
            caveFacingYaw + MathF.PI,
            loaded.RuntimeProfile.Camera.FirstPerson.InitialPitchDegrees,
            showcase.VaultLookBackFrames);
        await WaitFrames(host, showcase.VaultLookBackHoldFrames);
        stage.Text = "02  SAME LIVE CAMERA  •  TURN BACK INTO THE SOURCE V13ENT CAVE";
        await SmoothFirstPersonYaw(
            host,
            loaded.Camera,
            caveFacingYaw + MathF.PI,
            caveFacingYaw + MathF.PI * 2.0f,
            loaded.RuntimeProfile.Camera.FirstPerson.InitialPitchDegrees,
            showcase.CaveLookFrames);
        await WaitFrames(host, showcase.CaveLookHoldFrames);

        var first = NearestLiving(loaded.Session);
        var startTile = loaded.Session.PlayerTile;
        var movementTarget = ChooseMovementTarget(
            loaded.Session,
            first.Tile,
            showcase.FpsMoveMaximumHexes);
        stage.Text = "03  FPS CAVE WALK  •  CONTINUOUS WASD-STYLE LOCOMOTION  •  NO TACTICAL AP";
        GD.Print("OPENNV_FO1_NEW_GAME_DEMO_PHASE first-person-exploration");
        await WaitUntilTile(host, loaded, movementTarget, showcase.FpsMoveMaximumFrames);
        await WaitFrames(host, showcase.FpsMoveHoldFrames);

        var killsBefore = loaded.Session.Kills;
        var killed = new List<object>();
        await KillRatFirstPerson(host, loaded, stage, first, killed);

        var fpsMeleeTarget = NearestLiving(loaded.Session);
        await KillRatFirstPersonMelee(host, loaded, stage, fpsMeleeTarget, killed);

        stage.Text = "06  SHOULDER MODE  •  SAME DWELLER  •  ORBIT WHILE THE WORLD STAYS LIVE";
        loaded.Camera.SetFirstPersonMode(false);
        var shoulderYaw = loaded.Camera.TargetYawRadians;
        await SmoothShoulderOrbit(
            host,
            loaded.Camera,
            shoulderYaw,
            shoulderYaw + MathF.PI * Fo1NewGameFlowNumericContracts.PresentationFloat0Point72f,
            Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE14Point0f,
            Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE26Point0f,
            showcase.ShoulderOrbitFrames);
        var shoulderTargetRat = NearestLiving(loaded.Session);
        var shoulderStartTile = loaded.Session.PlayerTile;
        var shoulderMovementTarget = ChooseMovementTarget(
            loaded.Session,
            shoulderTargetRat.Tile,
            showcase.ShoulderMoveMaximumHexes);
        stage.Text = "07  SHOULDER HEX COMMAND  •  WATCH THE DWELLER MOVE CENTER TO CENTER";
        loaded.Session.SelectTile(shoulderMovementTarget);
        await WaitUntilTile(
            host,
            loaded,
            shoulderMovementTarget,
            showcase.ShoulderMoveMaximumFrames);
        await WaitFrames(host, showcase.ShoulderMoveHoldFrames);

        stage.Text = "08  SAFE MODE CHANGE  •  SHOULDER VIEW FADES INTO THE SAME TACTICAL STATE";
        await FadeToTactical(host, loaded);
        await WaitFrames(host, showcase.ModeTransitionHoldFrames);

        if (!loaded.Session.GridVisible)
            loaded.Session.ToggleGrid();
        stage.Text = "09  EXACT FALLOUT HEXES VISIBLE  •  ONE AP PER HEX";
        loaded.Camera.SetOrbitDegrees(Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE38Point0f, Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE38Point0f);
        loaded.Camera.FocusTileAtHeight(loaded.Session.PlayerTile, Fo1NewGameFlowNumericContracts.PresentationFloat5Point0f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point70f);
        await WaitFrames(host, showcase.GridHoldFrames);

        var tacticalRangedTarget = NearestLiving(loaded.Session);
        await KillRatTacticalRanged(host, loaded, stage, tacticalRangedTarget, killed);

        stage.Text = "11  TACTICAL RELOAD  •  SOURCE 12-ROUND MAGAZINE  •  OWNED ANIMATION + AUDIO";
        if (loaded.Session.ActionPoints < loaded.RuntimeProfile.Gameplay.ReloadActionPointCost)
            loaded.Session.EndTurn();
        var reloadsBefore = loaded.Session.Reloads;
        if (!loaded.Session.Reload() || loaded.Session.Reloads != reloadsBefore + 1)
            throw new InvalidOperationException("Fallout tactical showcase reload failed.");
        await WaitFrames(host, showcase.ReloadHoldFrames);

        var tacticalMeleeTarget = NearestLiving(loaded.Session);
        await KillRatTacticalMelee(host, loaded, stage, tacticalMeleeTarget, killed);

        stage.Text = "13  WIDE CAVE TOUR  •  PAN + ORBIT + ZOOM OVER THE SAME SOURCE MAP";
        if (loaded.Session.GridVisible)
            loaded.Session.ToggleGrid();
        await SmoothTacticalMapTour(
            host,
            loaded.Camera,
            loaded.Session.PlayerTile,
            loaded.DoorTile,
            loaded.EntryTile,
            showcase.TacticalTourFrames);

        stage.Text = "FULL SHOWCASE  •  FPS + TACTICAL  •  PISTOL + KNIFE + RELOAD + EFFECTS";
        loaded.Session.SetCameraStatus(
            "C tactical → third-person → first-person • mouse orbit/look • G exact hex grid");
        loaded.Camera.SetOrbitDegrees(Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE45Point0f, Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE42Point0f);
        loaded.Camera.FrameEntryPair(loaded.Session.PlayerTile, loaded.DoorTile);
        await WaitFrames(host, showcase.FinalHoldFrames);
        var gameplayCapture = captureFullPath is null
            ? null
            : SaveNativeCapture(
                host,
                captureFullPath,
                "v13ent-hex-hud-equipped-knife.png");

        var combatPresentation = loaded.Session.CombatPresentation;
        var playerPresentationBinding = loaded.Session.PlayerPresentationBinding;
        if (loaded.Session.Kills - killsBefore < 4 || loaded.Session.FpsKills < 2 ||
        playerPresentationBinding is null ||
        playerPresentationBinding.CharacterName != profile.Name ||
        playerPresentationBinding.Sex != profile.Sex ||
        !playerPresentationBinding.ActorRootBound ||
        !playerPresentationBinding.AnimationBound ||
        (playerPresentationBinding.UsesOwnedDonor &&
            (!playerPresentationBinding.WeaponAttachmentsBound ||
             playerPresentationBinding.WeaponVisualsSuppressed)) ||
        (!playerPresentationBinding.UsesOwnedDonor &&
            (playerPresentationBinding.WeaponAttachmentsBound ||
             !playerPresentationBinding.WeaponVisualsSuppressed)) ||
        loaded.Session.CharacterProfile is null ||
        loaded.Session.PlayerHitPoints <= 0 || loaded.Session.Attacks < 2 ||
        loaded.Session.RangedHits < 2 || loaded.Session.MeleeHits < 2 ||
        loaded.Session.Reloads < 1 || killed.Count < 4 ||
        loaded.Session.HudWeaponArtSwitches < 3 ||
        loaded.Session.EquippedWeaponSymbol != "PID_KNIFE" ||
        loaded.Session.OwnedPlayerWeapon?.Root.Visible != false ||
        loaded.Session.OwnedPlayerMeleeWeapon?.Root.Visible != true ||
        loaded.RuntimeProfile.CombatPresentation.ImpactRadiusMeters > Fo1NewGameFlowNumericContracts.PresentationFloat0Point015f ||
        combatPresentation is null ||
        combatPresentation.Tracers != loaded.Session.RangedAttacks ||
        combatPresentation.Casings != loaded.Session.RangedAttacks ||
        combatPresentation.GroundedCasings != combatPresentation.Casings ||
        combatPresentation.Impacts != loaded.Session.RangedAttacks ||
        combatPresentation.MeleeSweeps != loaded.Session.MeleeAttacks ||
        profile.ActionPoints != loaded.Session.CharacterProfile.ActionPoints ||
        loaded.Session.WeaponActionPointCost != 4 || loaded.Camera.ExplorationMode ||
        loaded.Camera.FirstPersonMode || !loaded.Session.PlayerToken.Visible ||
        !loaded.Door.Controller.IsOpen || !loaded.Session.ClassicInterfaceAttached ||
        loaded.Session.ClassicInventory is null || loaded.Session.ClassicInventory.IsOpen ||
        loaded.Session.ClassicInventory.OpenedCount != 1 ||
        loaded.Session.ClassicInventory.ClosedCount != 1 ||
        loaded.Session.PipBoy is null || loaded.Session.PipBoy.OpenedCount != 1 ||
        loaded.Session.PipBoy.IsOpen)
            throw new InvalidOperationException(
                "Fallout end-to-end new-game/equipment/combat gate failed.");
        var report = new
        {
            schema = "opennv-fo1-new-game-demo/v7",
            status = "pass",
            fixedFpsExpected = showcase.FixedFramesPerSecond,
            openingPlaybackScale = opening.PlaybackScale,
            opening = new
            {
                mode = opening.Skipped ? "skipped" : "watched",
                opening.RenderedFrames,
                opening.HandoffFrameIndex,
                opening.HandoffFrameSha256,
                transition = "visible owned frame fades to black; black lifts from prepared live camera",
                skipStillRunsLanding = landing.OpeningWasSkipped,
            },
            landing,
            scene = loaded.ScenePath,
            sceneSha256 = loaded.SceneSha256,
            sequence = new[]
            {
                "owned-original-character-picker-max-stone-natalia-albert-custom",
                "true-3d-premade-player-preview-owned-fnv-donor-plus-first-party-adaptation",
                "owned-original-custom-character-creation",
                opening.Skipped ? "owned-original-overseer-mve-skipped" : "owned-original-overseer-mve",
                "owned-frame-fade-to-exact-live-first-person-v13ent",
                "owned-original-iface-frm-live-gameplay-hud",
                "owned-original-invbox-inventory-open-and-escape-close-with-unchanged-state",
                "pip-boy-2000-attached-opened-status-and-closed",
                "first-person-open-vault-door-corridor-lookback",
                "first-person-continuous-source-walk-mask-walk",
                "first-person-pistol-ranged-rat-kill",
                "first-person-knife-melee-rat-kill",
                "third-person-shoulder-orbit-and-center-hex-movement",
                "fade-to-shared-tactical-state",
                "turn-based-tactical-pistol-ranged-rat-kill",
                "turn-based-tactical-source-capacity-reload",
                "turn-based-tactical-knife-melee-rat-kill",
                "wide-tactical-map-pan-orbit-zoom",
            },
            characterStart = contract.Report(),
            premadePlayerPreview,
            character = profile.Report(),
            inventory = inventoryProof,
            handoff = new
            {
                map = "V13ENT",
                tile = loaded.EntryTile,
                rotation = contract.EntryRotation,
                doorTile = loaded.DoorTile,
            },
            movement = new
            {
                firstPerson = new
                {
                    fromTile = startTile,
                    toTile = movementTarget,
                    distanceHexes = Fo1HexMath.Distance(startTile, movementTarget),
                    presentation = "first-person-perspective",
                    tacticalActionPointsConsumed = false,
                },
                shoulder = new
                {
                    fromTile = shoulderStartTile,
                    toTile = shoulderMovementTarget,
                    distanceHexes = Fo1HexMath.Distance(shoulderStartTile, shoulderMovementTarget),
                    presentation = "third-person-shoulder",
                    centerHexCommand = true,
                },
                authoritativeState = "same Fo1TacticalSession hex path and AP",
            },
            combat = new
            {
                killsBefore,
                killsAfter = loaded.Session.Kills,
                killsInDemo = loaded.Session.Kills - killsBefore,
                killed,
                attacks = loaded.Session.Attacks,
                fpsShots = loaded.Session.FpsShots,
                fpsHits = loaded.Session.FpsHits,
                fpsKills = loaded.Session.FpsKills,
                rangedAttempts = loaded.Session.RangedAttacks,
                rangedHits = loaded.Session.RangedHits,
                meleeAttempts = loaded.Session.MeleeAttacks,
                meleeHits = loaded.Session.MeleeHits,
                reloads = loaded.Session.Reloads,
                magazineRounds = loaded.Session.MagazineRounds,
                reserveRounds = loaded.Session.ReserveRounds,
                equippedWeaponSymbol = loaded.Session.EquippedWeaponSymbol,
                hudWeaponArtSwitches = loaded.Session.HudWeaponArtSwitches,
                impactRadiusMeters = loaded.RuntimeProfile.CombatPresentation.ImpactRadiusMeters,
                presentation = combatPresentation.Report(),
                playerAlive = loaded.Session.PlayerHitPoints > 0,
                weaponActionPointCost = loaded.Session.WeaponActionPointCost,
            },
            finalSession = loaded.Session.Report(),
            nativeCaptures = captureFullPath is null
                ? null
                : new
                {
                    root = captureFullPath,
                    inventory = inventoryCapture,
                    gameplay = gameplayCapture,
                },
            windowsAppControlUsed = false,
            foregroundActivationUsed = false,
            foregroundInputInjected = false,
        };
        File.WriteAllText(
            reportFullPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        GD.Print(
            $"OPENNV_FO1_NEW_GAME_DEMO_PASS character={profile.Name} entry={loaded.EntryTile} " +
            $"kills={loaded.Session.Kills - killsBefore} ranged={loaded.Session.RangedAttacks} " +
            $"melee={loaded.Session.MeleeAttacks} reloads={loaded.Session.Reloads}");
        host.GetTree().Quit(0);
    }

    private static async Task CompleteNativeFirstBeatProof(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        Fo1CharacterProfile profile,
        string reportPath,
        string captureRoot,
        object inventoryProof,
        object? inventoryCapture,
        object premadePlayerPreview,
        Label stage)
    {
        var binding = loaded.Session.PlayerPresentationBinding;
        if (binding is null || !binding.UsesOwnedDonor ||
            !binding.ActorRootBound || !binding.AnimationBound ||
            !binding.WeaponAttachmentsBound || binding.WeaponVisualsSuppressed ||
            loaded.Session.OwnedPlayer?.Root.Visible != true ||
            loaded.Session.OwnedPlayerWeapon?.Root.Visible != false ||
            loaded.Session.OwnedPlayerMeleeWeapon?.Root.Visible != true)
            throw new InvalidOperationException(
                "Fallout 1 native first-beat proof requires owned animated donor geometry " +
                "with the selected held weapon attached.");
        loaded.Camera.SetFirstPersonMode(false);
        loaded.Camera.SetExplorationMode(false);
        if (!loaded.Session.GridVisible)
            loaded.Session.ToggleGrid();
        loaded.Session.SetWorldGuidesVisible(true);
        foreach (var mob in loaded.Session.Mobs)
            mob.SetReadabilityMarkersVisible(true);
        var adjacentRatEngagement = await RunNativeFirstBeatAdjacentRatEngagement(
            host,
            loaded);
        loaded.Camera.SetOrbitDegrees(
            Fo1HexCaptureNumericContracts.AcceptanceFloat135Point0f,
            Fo1HexCaptureNumericContracts.AcceptanceFloatNEgativE26Point0f);
        loaded.Camera.FocusTileAtHeight(
            loaded.Session.PlayerTile,
            3.0f,
            Fo1HexCaptureNumericContracts.AcceptanceFloat0Point86f);
        stage.Text =
            "FIRST PLAYABLE BEAT  •  OWNED 3D DWELLER  •  KNIFE EQUIPPED FROM INVENTORY";
        await WaitFrames(host, Fo1NewGameFlowNumericContracts.PresentationInt5);
        var gameplayCapture = SaveNativeCapture(
            host,
            captureRoot,
            "v13ent-hex-hud-equipped-knife.png");
        var report = new
        {
            schema = "opennv-fo1-native-first-beat/v1",
            status = "pass",
            scene = loaded.ScenePath,
            sceneSha256 = loaded.SceneSha256,
            characterStart = contract.Report(),
            character = profile.Report(),
            premadePlayerPreview,
            entryTile = loaded.EntryTile,
            doorTile = loaded.DoorTile,
            inventory = inventoryProof,
            playerPresentation = binding.Report(),
            adjacentRatEngagement,
            gameplay = loaded.Session.Report(),
            files = new[] { inventoryCapture, gameplayCapture },
            windowsAppControlUsed = false,
            foregroundActivationUsed = false,
            foregroundInputInjected = false,
        };
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        GD.Print(
            $"OPENNV_FO1_NATIVE_FIRST_BEAT_PASS character={profile.Name} " +
            $"entry={loaded.EntryTile} equipped={loaded.Session.EquippedWeaponSymbol}");
        host.GetTree().Quit(0);
    }

    private static async Task CompleteNativeFirstBeatHeadlessProof(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        Fo1CharacterProfile profile,
        string reportPath,
        object inventoryProof,
        object premadePlayerPreview)
    {
        var binding = loaded.Session.PlayerPresentationBinding;
        if (binding is null || !binding.UsesOwnedDonor ||
            !binding.ActorRootBound || !binding.AnimationBound ||
            !binding.WeaponAttachmentsBound || binding.WeaponVisualsSuppressed ||
            loaded.Session.OwnedPlayer?.Root.Visible != true ||
            loaded.Session.OwnedPlayerWeapon?.Root.Visible != false ||
            loaded.Session.OwnedPlayerMeleeWeapon?.Root.Visible != true)
            throw new InvalidOperationException(
                "Fallout 1 native first-beat proof requires owned animated donor geometry " +
                "with the selected held weapon attached.");
        var mapInventoryPickup = await RunNativeFirstBeatMapInventoryPickup(
            host,
            loaded);
        var classicInventoryHud = RunNativeFirstBeatClassicInventoryHudProof(
            loaded,
            mapInventoryPickup);
        var adjacentRatEngagement = await RunNativeFirstBeatAdjacentRatEngagement(
            host,
            loaded);
        var caveExitGridTransition = loaded.Session.ExitGridTransition is null
            ? null
            : await RunNativeFirstBeatCaveExitGridTransition(host, loaded);
        var report = new
        {
            schema = "opennv-fo1-native-first-beat-headless-proof/v1",
            status = "pass-source-bound-pickup-equip-use-combat-save-restore-headless-not-rendered",
            scene = loaded.ScenePath,
            sceneSha256 = loaded.SceneSha256,
            characterStart = contract.Report(),
            character = profile.Report(),
            premadePlayerPreview,
            entryTile = loaded.EntryTile,
            doorTile = loaded.DoorTile,
            inventory = inventoryProof,
            playerPresentation = binding.Report(),
            mapInventoryPickup = new
            {
                mapInventoryPickup.HostSerial,
                mapInventoryPickup.HostPid,
                mapInventoryPickup.WeaponSymbol,
                mapInventoryPickup.WeaponPid,
                pickup = mapInventoryPickup.Report,
                use = adjacentRatEngagement,
            },
            classicInventoryHud,
            adjacentRatEngagement,
            caveExitGridTransition,
            gameplay = loaded.Session.Report(),
            files = Array.Empty<object>(),
            rendered = false,
            interactive = false,
            windowsAppControlUsed = false,
            foregroundActivationUsed = false,
            foregroundInputInjected = false,
        };
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        GD.Print(
            $"OPENNV_FO1_NATIVE_FIRST_BEAT_HEADLESS_PASS character={profile.Name} " +
            $"entry={loaded.EntryTile} equipped={loaded.Session.EquippedWeaponSymbol}");
        host.GetTree().Quit(0);
    }

    internal static async Task RunDestinationColdRestoreProof(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        string reportPath)
    {
        await WaitFrames(host, 1);
        var session = loaded.Session;
        var transition = session.ExitGridTransition ?? throw new InvalidOperationException(
            "Fallout destination cold restore has no explicit exit-grid descriptor.");
        var destination = session.LoadedDestinationPresentation ?? throw new InvalidOperationException(
            "Fallout destination cold restore did not load the saved presentation identity.");
        if (loaded.Root.Visible || session.ActivatedExitGridTile is not { } activatedTile ||
            !transition.IsTrigger(activatedTile))
            throw new InvalidOperationException(
                "Fallout destination cold restore did not leave V13ENT at an owned exit trigger.");
        var restoredTile = session.PlayerTile;
        if (!session.CanWalk(restoredTile))
            throw new InvalidOperationException(
                "Fallout destination cold restore player tile is not admitted by the source walk mask.");
        var genericDoor = session.DestinationGenericDoor;
        if (genericDoor is not null &&
            (!session.DestinationGenericDoorOpen || !session.CanWalk(genericDoor.Door.Tile)))
            throw new InvalidOperationException(
                "Fallout destination cold restore lost the opened generic MAP door passability state.");
        var destinationMove = MoveOneLegalDestinationHex(session);
        if (session.PlayerTile != destinationMove)
            throw new InvalidOperationException(
                "Fallout destination cold restore did not admit its first source-mask move.");
        var report = new
        {
            schema = "opennv-fo1-destination-cold-restore-proof/v1",
            status = "pass-source-bound-vault13-cold-restore-headless-not-rendered",
            coldProcess = true,
            sourceScene = new { path = loaded.ScenePath, sha256 = loaded.SceneSha256, visible = loaded.Root.Visible },
            transition = transition.Report(activatedTile, destinationSceneLoaded: true),
            destinationPresentation = destination.Report(transition),
            restored = new { playerTile = restoredTile, sourceWalkMaskOnly = true },
            genericDoor = genericDoor is null ? null : genericDoor.Report(open: session.DestinationGenericDoorOpen),
            firstControllableDestinationMove = new { sourceWalkMaskOnly = true, destinationMove },
            gameplay = session.Report(),
            rendered = false,
            interactive = false,
            files = Array.Empty<object>(),
        };
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
                System.Environment.NewLine);
        GD.Print(
            $"OPENNV_FO1_VAULT13_COLD_RESTORE_PASS restored={restoredTile} move={destinationMove}");
        host.GetTree().Quit(0);
    }

    internal static async Task RunDestinationInventoryInteractionProof(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        string reportPath)
    {
        await WaitFrames(host, 1);
        var session = loaded.Session;
        var destination = session.LoadedDestinationPresentation ?? throw new InvalidOperationException(
            "Fallout destination interaction requires a committed destination presentation.");
        var interaction = session.DestinationInventoryInteraction ?? throw new InvalidOperationException(
            "Fallout destination interaction requires an explicit hash-bound inventory descriptor.");
        var inventoryHost = interaction.Host;
        var approachPath = await MoveTacticalAdjacentToMapInventoryHost(host, loaded, inventoryHost);
        if (!Fo1HexMath.AreNeighbors(session.PlayerTile, inventoryHost.Tile))
            throw new InvalidOperationException("Fallout destination interaction route is not source-adjacent to its MAP container.");
        var contactTile = session.PlayerTile;
        var before = session.InventorySnapshot();
        var pickup = session.PickupAdjacentMapInventoryHost(inventoryHost.Serial);
        if (!session.IsMapInventoryHostLooted(inventoryHost.Serial) ||
            inventoryHost.Items.Any(item => pickup.Inventory.GetValueOrDefault(item.Symbol) !=
                before.GetValueOrDefault(item.Symbol) + item.Objects))
            throw new InvalidOperationException("Fallout destination source container pickup did not preserve exact MAP item stacks.");
        var nextMove = MoveOneLegalDestinationHex(session);
        var report = new
        {
            schema = "opennv-fo1-destination-inventory-interaction-proof/v1",
            status = "pass-source-bound-vault13-container-pickup-headless-not-rendered",
            destinationPresentation = destination.Report(session.ExitGridTransition!),
            interaction = interaction.Report(),
            approach = new { sourceWalkMaskOnly = true, pathTiles = approachPath, contactTile, hostTile = inventoryHost.Tile, contactIsAdjacent = true },
            pickup = new { hostSerial = inventoryHost.Serial, items = inventoryHost.Items, looted = session.IsMapInventoryHostLooted(inventoryHost.Serial) },
            nextLegalGameplayBeat = new { sourceWalkMaskOnly = true, move = nextMove },
            gameplay = session.Report(),
            rendered = false,
            interactive = false,
            files = Array.Empty<object>(),
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + System.Environment.NewLine);
        GD.Print($"OPENNV_FO1_VAULT13_INTERACTION_PASS host={inventoryHost.Serial} move={nextMove}");
        host.GetTree().Quit(0);
    }

    internal static async Task RunDestinationInventoryInteractionColdRestoreProof(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        string reportPath)
    {
        await WaitFrames(host, 1);
        var session = loaded.Session;
        var interaction = session.DestinationInventoryInteraction ?? throw new InvalidOperationException(
            "Fallout destination interaction cold restore requires its explicit descriptor.");
        if (!session.IsMapInventoryHostLooted(interaction.Host.Serial))
            throw new InvalidOperationException("Fallout destination interaction cold restore lost its MAP container state.");
        var flareUse = session.DestinationFlareUse;
        if (flareUse is not null && !session.DestinationFlareLit)
            throw new InvalidOperationException("Fallout destination interaction cold restore lost its source-script flare state.");
        var nextMove = MoveOneLegalDestinationHex(session);
        var report = new
        {
            schema = "opennv-fo1-destination-inventory-interaction-cold-restore-proof/v1",
            status = "pass-source-bound-vault13-container-cold-restore-headless-not-rendered",
            coldProcess = true,
            interaction = interaction.Report(),
            flareUse = flareUse is null ? null : new { descriptor = flareUse.Report(), lit = session.DestinationFlareLit },
            restored = new { hostSerial = interaction.Host.Serial, looted = true, sourceWalkMaskOnly = true },
            nextLegalGameplayBeat = new { sourceWalkMaskOnly = true, move = nextMove },
            gameplay = session.Report(),
            rendered = false,
            interactive = false,
            files = Array.Empty<object>(),
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + System.Environment.NewLine);
        GD.Print($"OPENNV_FO1_VAULT13_INTERACTION_COLD_PASS host={interaction.Host.Serial} move={nextMove}");
        host.GetTree().Quit(0);
    }

    internal static async Task RunDestinationMedicLookProof(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        string reportPath)
    {
        await WaitFrames(host, 1);
        var session = loaded.Session;
        var destination = session.LoadedDestinationPresentation ?? throw new InvalidOperationException(
            "Fallout Medic look requires a committed destination presentation.");
        var door = session.DestinationGenericDoor ?? throw new InvalidOperationException(
            "Fallout Medic look requires the opened generic-door prerequisite.");
        var medic = session.DestinationMedicLook ?? throw new InvalidOperationException(
            "Fallout Medic look requires an explicit source script/message descriptor.");
        if (!session.DestinationGenericDoorOpen || !session.CanWalk(door.Door.Tile))
            throw new InvalidOperationException("Fallout Medic look requires a persisted opened generic-door tile.");
        var approachPath = await MoveTacticalAdjacentToSourceTile(host, loaded, medic.Tile);
        var contactTile = session.PlayerTile;
        if (!Fo1HexMath.AreNeighbors(contactTile, medic.Tile) || !session.TryLookAtAdjacentDestinationMedic() ||
            !session.DestinationMedicLookViewed || session.Status != medic.MessageText)
            throw new InvalidOperationException("Fallout Medic look did not emit its exact source message from an adjacent hex.");
        var sourceMessage = session.Status;
        var nextMove = MoveOneLegalDestinationHex(session);
        var report = new
        {
            schema = "opennv-fo1-destination-medic-look-proof/v1",
            status = "pass-source-bound-vault13-medic-look-headless-not-rendered",
            destinationPresentation = destination.Report(session.ExitGridTransition!),
            genericDoor = door.Report(open: session.DestinationGenericDoorOpen),
            medicLook = medic.Report(viewed: session.DestinationMedicLookViewed),
            approach = new
            {
                sourceWalkMaskOnly = true,
                pathTiles = approachPath,
                contactTile,
                actorTile = medic.Tile,
                contactIsAdjacent = true
            },
            interaction = new
            {
                result = "display-message-only",
                message = sourceMessage,
                dialogue = "unimplemented-fail-closed",
                combat = "not-proven-by-look-at-only",
                actionPoints = "not-source-backed",
                saved = true
            },
            nextLegalGameplayBeat = new { sourceWalkMaskOnly = true, move = nextMove },
            gameplay = session.Report(),
            rendered = false,
            interactive = false,
            files = Array.Empty<object>(),
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + System.Environment.NewLine);
        GD.Print($"OPENNV_FO1_VAULT13_MEDIC_LOOK_PASS actor={medic.Serial} move={nextMove}");
        host.GetTree().Quit(0);
    }

    internal static async Task RunDestinationMedicLookColdRestoreProof(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        string reportPath)
    {
        await WaitFrames(host, 1);
        var session = loaded.Session;
        var medic = session.DestinationMedicLook ?? throw new InvalidOperationException(
            "Fallout Medic look cold restore requires its explicit descriptor.");
        var door = session.DestinationGenericDoor ?? throw new InvalidOperationException(
            "Fallout Medic look cold restore requires its generic-door prerequisite.");
        if (!session.DestinationMedicLookViewed || !session.DestinationGenericDoorOpen ||
            !session.CanWalk(door.Door.Tile))
            throw new InvalidOperationException("Fallout Medic look cold restore lost its source interaction or opened door state.");
        var restoredTile = session.PlayerTile;
        if (!session.CanWalk(restoredTile))
            throw new InvalidOperationException("Fallout Medic look cold restore tile is outside the source walk mask.");
        var nextMove = MoveOneLegalDestinationHex(session);
        var report = new
        {
            schema = "opennv-fo1-destination-medic-look-cold-restore-proof/v1",
            status = "pass-source-bound-vault13-medic-look-cold-restore-headless-not-rendered",
            coldProcess = true,
            restored = new { playerTile = restoredTile, sourceWalkMaskOnly = true },
            genericDoor = door.Report(open: true),
            medicLook = medic.Report(viewed: true),
            nextLegalGameplayBeat = new { sourceWalkMaskOnly = true, move = nextMove },
            gameplay = session.Report(),
            rendered = false,
            interactive = false,
            files = Array.Empty<object>(),
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + System.Environment.NewLine);
        GD.Print($"OPENNV_FO1_VAULT13_MEDIC_LOOK_COLD_PASS actor={medic.Serial} move={nextMove}");
        host.GetTree().Quit(0);
    }

    internal static async Task RunDestinationReturnExitProof(Node host, Fo1HexSceneLoader.LoadedFo1HexScene loaded, string reportPath)
    {
        var session = loaded.Session;
        var medic = session.DestinationMedicLook ?? throw new InvalidOperationException("Fallout return exit requires the saved Medic interaction.");
        var transition = session.DestinationReturnExitGrid ?? throw new InvalidOperationException("Fallout return exit requires an explicit exit-grid descriptor.");
        if (!session.DestinationMedicLookViewed)
            throw new InvalidOperationException("Fallout return exit requires the persisted Medic interaction state.");
        var path = await MoveTacticalToTiles(host, loaded, transition.Triggers.Select(trigger => trigger.Tile));
        if (!transition.IsTrigger(session.PlayerTile) || !session.TryActivateDestinationReturnExitGrid() ||
            session.ActivatedDestinationReturnExitGridTile != session.PlayerTile)
            throw new InvalidOperationException("Fallout return exit proof did not commit an exact VAULT13 MAP trigger.");
        var activatedTile = session.PlayerTile;
        session.EnterCommittedSourceReturn();
        loaded.Root.Visible = true;
        if (!session.ReturnedToSource || session.PlayerTile != transition.DestinationTile ||
            !loaded.Root.Visible)
            throw new InvalidOperationException(
                "Fallout return exit proof did not restore the exact V13ENT MAP destination.");
        var nextMove = MoveOneLegalDestinationHex(session);
        var report = new
        {
            schema = "opennv-fo1-v13ent-reciprocal-return-proof/v1",
            status = "pass-source-bound-v13ent-reciprocal-return-headless-not-rendered",
            medicLook = medic.Report(viewed: true),
            approach = new { sourceWalkMaskOnly = true, pathTiles = path, triggerTile = activatedTile },
            transition = transition.Report(activatedTile, destinationSceneLoaded: true),
            v13ent = new
            {
                mapIndex = transition.DestinationMapIndex,
                mapName = transition.DestinationMapName,
                sourceMapSha256 = transition.DestinationMapSha256,
                elevation = transition.DestinationElevation,
                rotation = transition.DestinationRotation,
                arrivalTile = transition.DestinationTile,
                sourceWalkMaskOnly = true,
                loaded = true,
            },
            nextLegalGameplayBeat = new { sourceWalkMaskOnly = true, move = nextMove },
            gameplay = session.Report(),
            rendered = false,
            interactive = false,
            files = Array.Empty<object>(),
        };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + System.Environment.NewLine);
        GD.Print($"OPENNV_FO1_V13ENT_RETURN_PASS trigger={activatedTile} arrival={transition.DestinationTile} move={nextMove}");
        host.GetTree().Quit(0);
    }

    internal static Task RunDestinationReturnExitColdRestoreProof(Node host, Fo1HexSceneLoader.LoadedFo1HexScene loaded, string reportPath)
    {
        var session = loaded.Session;
        var transition = session.DestinationReturnExitGrid ?? throw new InvalidOperationException("Fallout return exit cold restore requires its descriptor.");
        if (!session.DestinationMedicLookViewed || session.ActivatedDestinationReturnExitGridTile is not { } tile ||
            !transition.IsTrigger(tile) || !session.ReturnedToSource)
            throw new InvalidOperationException("Fallout return exit cold restore lost its source-authored committed trigger.");
        var nextMove = MoveOneLegalDestinationHex(session);
        var report = new { schema = "opennv-fo1-v13ent-reciprocal-return-cold-restore-proof/v1", status = "pass-source-bound-v13ent-reciprocal-return-cold-restore-headless-not-rendered", coldProcess = true, restored = new { playerTile = session.PlayerTile, sourceWalkMaskOnly = true }, transition = transition.Report(tile, destinationSceneLoaded: true), v13ent = new { mapIndex = transition.DestinationMapIndex, mapName = transition.DestinationMapName, sourceMapSha256 = transition.DestinationMapSha256, elevation = transition.DestinationElevation, rotation = transition.DestinationRotation, arrivalTile = transition.DestinationTile, loaded = true }, nextLegalGameplayBeat = new { sourceWalkMaskOnly = true, move = nextMove }, gameplay = session.Report(), rendered = false, interactive = false, files = Array.Empty<object>() };
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + System.Environment.NewLine);
        GD.Print($"OPENNV_FO1_V13ENT_RETURN_COLD_PASS trigger={tile} move={nextMove}");
        host.GetTree().Quit(0);
        return Task.CompletedTask;
    }

    private static async Task<IReadOnlyList<int>> MoveTacticalToTiles(Node host, Fo1HexSceneLoader.LoadedFo1HexScene loaded, IEnumerable<int> targetTiles)
    {
        var goals = targetTiles.ToHashSet(); var path = new List<int> { loaded.Session.PlayerTile };
        for (var turn = 0; turn < Fo1HexMath.Width + Fo1HexMath.Height; turn++)
        {
            if (goals.Contains(loaded.Session.PlayerTile)) return path;
            if (loaded.Session.ActionPoints == 0) loaded.Session.EndTurn();
            var sourcePath = FindWalkablePathToAny(loaded.Session, goals).ToList();
            loaded.Session.SelectTile(sourcePath[^1]);
            if (DisplayServer.GetName() == "headless") loaded.Session.CompleteQueuedTacticalMovementForHeadlessProof();
            else while (loaded.Session.QueuedMovementSteps > 0) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var index = sourcePath.IndexOf(loaded.Session.PlayerTile);
            if (index < 0) throw new InvalidOperationException("Fallout return exit approach left its source walk mask.");
            path.AddRange(sourcePath.Skip(1).Take(index));
        }
        throw new InvalidOperationException("Fallout return exit has no finite source-walkable route.");
    }

    private static async Task<object> RunNativeFirstBeatCaveExitGridTransition(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded)
    {
        var session = loaded.Session;
        var contract = session.ExitGridTransition ?? throw new InvalidOperationException(
            "Fallout cave exit proof requires an explicit exit-grid descriptor.");
        var doorApproach = await MoveTacticalAdjacentToSourceTile(host, loaded, session.DoorTile);
        if (!session.TryActivateAdjacentSourceDoor())
            throw new InvalidOperationException("Fallout cave exit proof could not activate its adjacent MAP door.");
        loaded.Door.Controller.SetOpenAmount(1.0f);
        if (!loaded.Door.Controller.IsOpen || !session.SourceDoorOpen)
            throw new InvalidOperationException("Fallout MAP door visual and tactical states did not open together.");
        var path = await MoveTacticalToExitGrid(host, loaded, contract.Triggers.Select(trigger => trigger.Tile));
        if (session.ActivatedExitGridTile is not { } activatedTile || !contract.IsTrigger(activatedTile))
            throw new InvalidOperationException("Fallout cave exit proof did not activate an owned MAP exit grid.");
        session.RestoreSaveForProof();
        if (session.ActivatedExitGridTile != activatedTile)
            throw new InvalidOperationException("Fallout cave exit-grid activation did not persist through save restore.");
        var destination = session.LoadCommittedDestinationPresentation();
        loaded.Root.Visible = false;
        var destinationViewer = new Fo1CampaignPresentationViewer();
        host.AddChild(destinationViewer);
        var destinationCoverage = destinationViewer.Configure(
            destination.Catalog,
            destination.Map.Id,
            contract.DestinationElevation,
            includeSourcePlayer: false);
        destinationViewer.SetStatusVisible(false);
        session.EnterCommittedDestination(destination);
        var destinationMove = MoveOneLegalDestinationHex(session);
        if (loaded.Root.Visible || session.PlayerTile != destinationMove)
            throw new InvalidOperationException("Fallout destination first controllable move did not leave the source cave.");
        return new
        {
            sourceWalkMaskOnly = true,
            doorActivation = new { doorApproach, sourceDoor = true, doorTile = session.DoorTile },
            pathTiles = path,
            contract = contract.Report(activatedTile, destinationSceneLoaded: true),
            destinationPresentation = destination.Report(contract),
            destinationCoverage,
            firstControllableDestinationMove = new { sourceWalkMaskOnly = true, destinationMove },
            persistence = new { matched = true, activatedTile },
        };
    }

    private static int MoveOneLegalDestinationHex(Fo1TacticalSession session)
    {
        var target = Fo1HexMath.Neighbors(session.PlayerTile)
            .Where(session.CanWalk)
            .OrderBy(tile => tile)
            .FirstOrDefault(-1);
        if (target < 0)
            throw new InvalidOperationException("Fallout destination entry has no source-walkable first move.");
        if (session.ActionPoints == 0)
            session.EndTurn();
        session.SelectTile(target);
        session.CompleteQueuedTacticalMovementForHeadlessProof();
        if (session.PlayerTile != target)
            throw new InvalidOperationException("Fallout destination first move was not admitted by its source walk mask.");
        return target;
    }

    private static object RunNativeFirstBeatClassicInventoryHudProof(
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        NativeFirstBeatMapInventoryPickup pickup)
    {
        var session = loaded.Session;
        var inventory = session.ClassicInventory ?? throw new InvalidOperationException(
            "Fallout 1 first-beat requires the owned classic inventory screen.");
        var hud = session.ClassicHud ?? throw new InvalidOperationException(
            "Fallout 1 first-beat requires the owned classic gameplay HUD.");
        if (inventory.IsOpen)
            throw new InvalidOperationException(
                "Fallout 1 first-beat classic inventory must be closed before the loot UI proof.");
        var rangedSymbol = session.RangedWeaponSymbol;
        var meleeSymbol = session.MeleeWeaponSymbol;
        var inventoryBefore = session.InventorySnapshot();
        var equipmentChangesBefore = inventory.EquipmentChangedCount;
        var hudArtSwitchesBefore = hud.WeaponArtSwitches;
        loaded.Camera._UnhandledInput(new InputEventKey
        {
            Pressed = true,
            PhysicalKeycode = session.InventoryKey,
        });
        if (!inventory.IsOpen)
            throw new InvalidOperationException(
                "Fallout 1 first-beat did not open the owned inventory from its configured input.");
        inventory.SelectSourceInventorySymbolForProof(rangedSymbol);
        inventory.EquipSourceActiveHandForProof(rangedSymbol);
        if (session.EquippedWeaponSymbol != rangedSymbol ||
            hud.EquippedWeaponSymbol != rangedSymbol)
            throw new InvalidOperationException(
                "Fallout 1 classic ranged inventory selection did not update the owned HUD hand art.");
        inventory.SelectSourceInventorySymbolForProof(meleeSymbol);
        inventory.EquipSourceActiveHandForProof(meleeSymbol);
        if (session.EquippedWeaponSymbol != meleeSymbol ||
            hud.EquippedWeaponSymbol != meleeSymbol)
            throw new InvalidOperationException(
                "Fallout 1 classic melee inventory selection did not update the owned HUD hand art.");
        loaded.Camera._UnhandledInput(new InputEventKey
        {
            Pressed = true,
            PhysicalKeycode = Key.Escape,
        });
        if (inventory.IsOpen)
            throw new InvalidOperationException(
                "Fallout 1 first-beat Escape did not close the owned inventory screen.");
        var saved = ReadNativeFirstBeatClassicInventoryUiSave(
            session.SavePath,
            pickup.HostSerial,
            rangedSymbol,
            meleeSymbol);
        if (!saved.Looted || saved.EquippedWeaponSymbol != meleeSymbol ||
            saved.InventoryObjects.GetValueOrDefault(meleeSymbol) !=
                inventoryBefore.GetValueOrDefault(meleeSymbol) ||
            !saved.InventoryObjects.Keys.All(inventoryBefore.ContainsKey))
            throw new InvalidOperationException(
                "Fallout 1 classic inventory UI did not persist its source-backed cave inventory state.");
        session.RestoreSaveForProof();
        if (session.EquippedWeaponSymbol != meleeSymbol ||
            hud.EquippedWeaponSymbol != meleeSymbol ||
            !session.InventorySnapshot().SequenceEqual(inventoryBefore))
            throw new InvalidOperationException(
                "Fallout 1 classic inventory UI did not restore its selected source weapon state.");
        if (inventory.EquipmentChangedCount != equipmentChangesBefore +
                new[] { rangedSymbol, meleeSymbol }.Distinct().Count() ||
            hud.WeaponArtSwitches < hudArtSwitchesBefore +
                new[] { rangedSymbol, meleeSymbol }.Distinct().Count())
            throw new InvalidOperationException(
                "Fallout 1 classic inventory UI did not drive both owned active-hand HUD updates.");
        return new
        {
            input = session.InventoryKey.ToString(),
            close = Key.Escape.ToString(),
            sourceInventory = inventory.Report(),
            sourceHud = hud.Report(),
            sequence = new[]
            {
                new { action = "open", symbol = rangedSymbol },
                new { action = "select", symbol = rangedSymbol },
                new { action = "equip", symbol = rangedSymbol },
                new { action = "select", symbol = meleeSymbol },
                new { action = "equip", symbol = meleeSymbol },
                new { action = "close", symbol = meleeSymbol },
            },
            inventoryBefore,
            saved,
            restored = new
            {
                equippedWeaponSymbol = session.EquippedWeaponSymbol,
                inventoryObjects = session.InventorySnapshot(),
                hudEquippedWeaponSymbol = hud.EquippedWeaponSymbol,
            },
            matched = true,
        };
    }

    private static async Task<NativeFirstBeatMapInventoryPickup> RunNativeFirstBeatMapInventoryPickup(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded)
    {
        var session = loaded.Session;
        var weaponSymbol = session.MeleeWeaponSymbol;
        var mapHost = session.MapInventoryHosts
            .Where(host => host.Items.Any(item =>
                item.Symbol == weaponSymbol && item.SubtypeName == "weapon"))
            .SingleOrDefault();
        if (mapHost is null)
            throw new InvalidOperationException(
                "Fallout 1 first-beat source scene has no MAP loot host for the admitted melee weapon.");
        var weapon = mapHost.Items.Single(item =>
            item.Symbol == weaponSymbol && item.SubtypeName == "weapon");
        if (weapon.Pid != session.MeleeWeapon.Pid ||
            weapon.PrototypeSha256 != session.MeleeWeapon.PrototypeSha256)
            throw new InvalidOperationException(
                "Fallout 1 MAP loot weapon differs from the admitted source melee weapon.");
        if (session.EquippedWeaponSymbol == weapon.Symbol)
            session.SwapEquippedWeapon();
        if (session.EquippedWeaponSymbol == weapon.Symbol)
            throw new InvalidOperationException(
                "Fallout 1 MAP loot proof cannot establish a distinct source inventory equip transition.");
        var equippedBeforePickup = session.EquippedWeaponSymbol;
        var inventoryBefore = session.InventorySnapshot();
        var approachPath = await MoveTacticalAdjacentToMapInventoryHost(host, loaded, mapHost);
        if (!Fo1HexMath.AreNeighbors(session.PlayerTile, mapHost.Tile))
            throw new InvalidOperationException(
                "Fallout 1 MAP loot route did not finish source-adjacent to its inventory host.");
        var pickup = session.PickupAdjacentMapInventoryHost(mapHost.Serial);
        foreach (var item in mapHost.Items)
        {
            var before = inventoryBefore.GetValueOrDefault(item.Symbol);
            if (pickup.Inventory.GetValueOrDefault(item.Symbol) != before + item.Objects)
                throw new InvalidOperationException(
                    "Fallout 1 MAP loot pickup inventory count differs from the source item stack.");
        }
        if (!session.EquipLootedMapInventoryWeaponForHeadlessProof(mapHost.Serial, weapon.Symbol) ||
            session.EquippedWeaponSymbol != weapon.Symbol)
            throw new InvalidOperationException(
                "Fallout 1 MAP loot weapon did not equip from the collected source inventory.");
        var persisted = ReadNativeFirstBeatMapInventorySave(
            session.SavePath,
            mapHost.Serial,
            weapon.Symbol);
        if (!persisted.Looted || persisted.EquippedWeaponSymbol != weapon.Symbol ||
            persisted.InventoryObjects != pickup.Inventory.GetValueOrDefault(weapon.Symbol))
            throw new InvalidOperationException(
                "Fallout 1 MAP loot pickup/equip result did not persist to the save.");
        session.RestoreSaveForProof();
        if (session.EquippedWeaponSymbol != weapon.Symbol ||
            session.InventorySnapshot().GetValueOrDefault(weapon.Symbol) !=
                persisted.InventoryObjects)
            throw new InvalidOperationException(
                "Fallout 1 MAP loot pickup/equip result did not restore from the save.");
        return new NativeFirstBeatMapInventoryPickup(
            mapHost.Serial,
            mapHost.Pid,
            weapon.Symbol,
            weapon.Pid,
            new
            {
                sourceWalkMaskOnly = true,
                host = new
                {
                    serial = mapHost.Serial,
                    pid = mapHost.Pid,
                    prototypeSha256 = mapHost.PrototypeSha256,
                    tile = mapHost.Tile,
                },
                approach = new
                {
                    pathTiles = approachPath,
                    startTile = approachPath[0],
                    contactTile = session.PlayerTile,
                    hostTile = mapHost.Tile,
                    contactIsAdjacent = Fo1HexMath.AreNeighbors(session.PlayerTile, mapHost.Tile),
                },
                equippedBeforePickup,
                collectedItems = mapHost.Items.Select(item => new
                {
                    item.Index,
                    item.Serial,
                    item.Symbol,
                    item.DisplayName,
                    item.Pid,
                    objects = item.Objects,
                    item.PrototypeSha256,
                    item.SubtypeName,
                    inventoryBefore = inventoryBefore.GetValueOrDefault(item.Symbol),
                    inventoryAfter = pickup.Inventory.GetValueOrDefault(item.Symbol),
                }),
                equippedWeaponSymbol = session.EquippedWeaponSymbol,
                persistence = new
                {
                    saved = persisted,
                    restored = new
                    {
                        equippedWeaponSymbol = session.EquippedWeaponSymbol,
                        inventoryObjects = session.InventorySnapshot().GetValueOrDefault(weapon.Symbol),
                    },
                    matched = true,
                },
            });
    }

    private static async Task<object> RunNativeFirstBeatAdjacentRatEngagement(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded)
    {
        var session = loaded.Session;
        var target = NearestLiving(session);
        var approachPath = await MoveTacticalAdjacentToTarget(host, loaded, target);
        var weapon = session.MeleeWeapon;
        if (session.ActionPoints < weapon.ActionPointCost)
        {
            session.EndTurn();
            if (DisplayServer.GetName() != "headless")
                await WaitFrames(host, loaded.RuntimeProfile.Showcase.TacticalKillHoldFrames);
        }
        if (!target.Alive || !Fo1HexMath.AreNeighbors(session.PlayerTile, target.Tile) ||
            session.ActionPoints < weapon.ActionPointCost)
            throw new InvalidOperationException(
                "Fallout 1 first-beat source rat cannot prove one adjacent melee engagement.");

        var attacksBefore = session.Attacks;
        var meleeAttacksBefore = session.MeleeAttacks;
        var meleeHitsBefore = session.MeleeHits;
        var sourceAttemptLimit =
            loaded.RuntimeProfile.Gameplay.TacticalMaximumHitChancePercent;
        var attempts = new List<object>();
        Fo1TacticalSession.CombatResult? successfulResult = null;
        var successfulActionPointsBefore = 0;
        var successfulTargetHitPointsBefore = 0;
        for (var attempt = 0; attempt < sourceAttemptLimit; attempt++)
        {
            if (!target.Alive || !Fo1HexMath.AreNeighbors(session.PlayerTile, target.Tile))
                throw new InvalidOperationException(
                    "Fallout 1 first-beat rat lost its source-adjacent melee contact.");
            if (session.ActionPoints < weapon.ActionPointCost)
                session.EndTurn();
            if (session.PlayerHitPoints <= 0 || session.ActionPoints < weapon.ActionPointCost)
                throw new InvalidOperationException(
                    "Fallout 1 first-beat player cannot take the next source melee attempt.");
            var actionPointsBefore = session.ActionPoints;
            var targetHitPointsBefore = target.HitPoints;
            session.ActivateTile(target.Tile, attackRequested: false);
            var attemptResult = session.AttackSelectedMelee();
            attempts.Add(new
            {
                actionPointsBefore,
                actionPointsAfter = session.ActionPoints,
                hitPointsBefore = targetHitPointsBefore,
                hitPointsAfter = target.HitPoints,
                attempted = attemptResult.Attempted,
                hit = attemptResult.Hit,
                appliedDamage = attemptResult.Damage,
                chancePercent = attemptResult.ChancePercent,
                rollPercent = attemptResult.RollPercent,
            });
            if (!attemptResult.Hit)
                continue;
            successfulResult = attemptResult;
            successfulActionPointsBefore = actionPointsBefore;
            successfulTargetHitPointsBefore = targetHitPointsBefore;
            break;
        }
        if (successfulResult is not { } result || !result.Attempted ||
            result.Kind != "melee" || result.Mode != "tactical" || result.Damage <= 0 ||
            target.HitPoints != successfulTargetHitPointsBefore - result.Damage ||
            session.ActionPoints != successfulActionPointsBefore - weapon.ActionPointCost ||
            session.Attacks != attacksBefore + attempts.Count ||
            session.MeleeAttacks != meleeAttacksBefore + attempts.Count ||
            session.MeleeHits != meleeHitsBefore + 1 ||
            session.EquippedWeaponSymbol != session.MeleeWeaponSymbol)
            throw new InvalidOperationException(
                "Fallout 1 first-beat melee result differs from its source gameplay contract.");

        var persisted = ReadNativeFirstBeatCombatSave(session.SavePath, target.Serial);
        if (persisted.ActionPoints != session.ActionPoints ||
            persisted.Attacks != session.Attacks ||
            persisted.MeleeAttacks != session.MeleeAttacks ||
            persisted.MeleeHits != session.MeleeHits ||
            persisted.EquippedWeaponSymbol != session.EquippedWeaponSymbol ||
            persisted.TargetHitPoints != target.HitPoints)
            throw new InvalidOperationException(
                "Fallout 1 first-beat melee result did not persist to its source-bound save.");
        session.RestoreSaveForProof();
        var restoredTarget = session.Mobs.SingleOrDefault(mob => mob.Serial == target.Serial);
        if (restoredTarget is null ||
            session.ActionPoints != persisted.ActionPoints ||
            session.Attacks != persisted.Attacks ||
            session.MeleeAttacks != persisted.MeleeAttacks ||
            session.MeleeHits != persisted.MeleeHits ||
            session.EquippedWeaponSymbol != persisted.EquippedWeaponSymbol ||
            restoredTarget.HitPoints != persisted.TargetHitPoints)
            throw new InvalidOperationException(
                "Fallout 1 first-beat save did not restore its source-bound combat result.");

        return new
        {
            mode = result.Mode,
            adjacent = true,
            playerTile = session.PlayerTile,
            approach = new
            {
                sourceWalkMaskOnly = true,
                pathTiles = approachPath,
                startTile = approachPath[0],
                contactTile = session.PlayerTile,
                targetTile = target.Tile,
                contactIsAdjacent = Fo1HexMath.AreNeighbors(session.PlayerTile, target.Tile),
            },
            target = new
            {
                serial = target.Serial,
                name = target.DisplayName,
                pid = target.Pid,
                prototypeSha256 = target.PrototypeSha256,
                tile = target.Tile,
                hitPointsBefore = successfulTargetHitPointsBefore,
                hitPointsAfter = target.HitPoints,
            },
            weapon = new
            {
                name = weapon.Name,
                pid = weapon.Pid,
                prototypeSha256 = weapon.PrototypeSha256,
                minimumDamage = weapon.MinimumDamage,
                maximumDamage = weapon.MaximumDamage,
                rangeHexes = weapon.RangeHexes,
                actionPointCost = weapon.ActionPointCost,
                characterMeleeDamage = session.MeleeDamage,
            },
            sourceAttemptLimit,
            attempts,
            successfulAttemptIndex = attempts.Count - 1,
            actionPointsBefore = successfulActionPointsBefore,
            actionPointsAfter = session.ActionPoints,
            result = new
            {
                attempted = result.Attempted,
                hit = result.Hit,
                appliedDamage = result.Damage,
                killed = result.Killed,
                chancePercent = result.ChancePercent,
                rollPercent = result.RollPercent,
            },
            persistence = new
            {
                saved = persisted,
                restored = new
                {
                    actionPoints = session.ActionPoints,
                    attacks = session.Attacks,
                    meleeAttacks = session.MeleeAttacks,
                    meleeHits = session.MeleeHits,
                    equippedWeaponSymbol = session.EquippedWeaponSymbol,
                    targetHitPoints = restoredTarget.HitPoints,
                },
                matched = true,
            },
        };
    }

    private static NativeFirstBeatSavedCombat ReadNativeFirstBeatCombatSave(
        string path,
        int targetSerial)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "opennv-fo1-hex-save/v1")
            throw new InvalidOperationException(
                "Fallout 1 first-beat save has an unknown schema.");
        var target = root.GetProperty("mobs").EnumerateArray()
            .SingleOrDefault(row => row.GetProperty("serial").GetInt32() == targetSerial);
        if (target.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                "Fallout 1 first-beat save has no selected source-rat state.");
        return new NativeFirstBeatSavedCombat(
            root.GetProperty("actionPoints").GetInt32(),
            root.GetProperty("attacks").GetInt32(),
            root.GetProperty("meleeAttacks").GetInt32(),
            root.GetProperty("meleeHits").GetInt32(),
            root.GetProperty("equippedWeaponSymbol").GetString() ?? "",
            target.GetProperty("hitPoints").GetInt32());
    }

    private static NativeFirstBeatSavedMapInventory ReadNativeFirstBeatMapInventorySave(
        string path,
        int hostSerial,
        string weaponSymbol)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "opennv-fo1-hex-save/v1")
            throw new InvalidOperationException(
                "Fallout 1 MAP loot save has an unknown schema.");
        var looted = root.GetProperty("lootedMapInventoryHostSerials").EnumerateArray()
            .Select(value => value.GetInt32())
            .ToArray();
        if (looted.Distinct().Count() != looted.Length)
            throw new InvalidOperationException(
                "Fallout 1 MAP loot save has duplicate collected source hosts.");
        var inventory = root.GetProperty("inventoryObjects");
        if (!inventory.TryGetProperty(weaponSymbol, out var weaponObjects))
            throw new InvalidOperationException(
                "Fallout 1 MAP loot save has no collected source weapon stack.");
        return new NativeFirstBeatSavedMapInventory(
            looted.Contains(hostSerial),
            root.GetProperty("equippedWeaponSymbol").GetString() ?? "",
            weaponObjects.GetInt32());
    }

    private static NativeFirstBeatSavedClassicInventoryUi ReadNativeFirstBeatClassicInventoryUiSave(
        string path,
        int hostSerial,
        string rangedSymbol,
        string meleeSymbol)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.GetProperty("schema").GetString() != "opennv-fo1-hex-save/v1")
            throw new InvalidOperationException(
                "Fallout 1 classic inventory UI save has an unknown schema.");
        var inventoryObjects = root.GetProperty("inventoryObjects").EnumerateObject()
            .ToDictionary(row => row.Name, row => row.Value.GetInt32(), StringComparer.Ordinal);
        if (!inventoryObjects.ContainsKey(rangedSymbol) || !inventoryObjects.ContainsKey(meleeSymbol))
            throw new InvalidOperationException(
                "Fallout 1 classic inventory UI save is missing an active-hand source stack.");
        return new NativeFirstBeatSavedClassicInventoryUi(
            root.GetProperty("lootedMapInventoryHostSerials").EnumerateArray()
                .Select(value => value.GetInt32())
                .Contains(hostSerial),
            root.GetProperty("equippedWeaponSymbol").GetString() ?? "",
            inventoryObjects);
    }

    private readonly record struct NativeFirstBeatSavedCombat(
        int ActionPoints,
        int Attacks,
        int MeleeAttacks,
        int MeleeHits,
        string EquippedWeaponSymbol,
        int TargetHitPoints);

    private readonly record struct NativeFirstBeatSavedMapInventory(
        bool Looted,
        string EquippedWeaponSymbol,
        int InventoryObjects);

    private readonly record struct NativeFirstBeatSavedClassicInventoryUi(
        bool Looted,
        string EquippedWeaponSymbol,
        IReadOnlyDictionary<string, int> InventoryObjects);

    private readonly record struct NativeFirstBeatMapInventoryPickup(
        int HostSerial,
        string HostPid,
        string WeaponSymbol,
        string WeaponPid,
        object Report);

    private static object SaveNativeCapture(
        Node host,
        string output,
        string filename)
    {
        var path = Path.Combine(output, filename);
        var image = host.GetViewport().GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        var error = image.SavePng(path);
        if (error != Error.Ok)
            throw new InvalidOperationException(
                $"Could not save Fallout 1 native first-beat capture: {error}");
        using var stream = File.OpenRead(path);
        return new
        {
            path,
            bytes = stream.Length,
            width = image.GetWidth(),
            height = image.GetHeight(),
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
        };
    }

    private static string FileSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string ReadSavedEquippedWeapon(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var symbol = document.RootElement.GetProperty("equippedWeaponSymbol").GetString();
        return string.IsNullOrWhiteSpace(symbol)
            ? throw new InvalidOperationException(
                "Fallout 1 save has no persisted equipped weapon symbol.")
            : symbol;
    }

    private static async Task KillRatFirstPerson(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Label stage,
        Fo1Mob rat,
        List<object> killed)
    {
        var showcase = loaded.RuntimeProfile.Showcase;
        if (!loaded.Camera.FirstPersonMode || !rat.Alive)
            throw new InvalidOperationException("Fallout FPS showcase lost its live target camera.");
        var hitPointsBefore = rat.HitPoints;
        var shotsBefore = loaded.Session.FpsShots;
        var hitsBefore = loaded.Session.FpsHits;
        var missDirection = loaded.Session.FindClearFirstPersonDirection(
            loaded.Camera.FirstPersonEyePosition);
        var missYaw = MathF.Atan2(-missDirection.X, -missDirection.Z);
        stage.Text = "04  FPS RANGED MISS  •  WALL IMPACT + CASING  •  NO TACTICAL AP";
        loaded.Session.SetCameraStatus(
            "Traditional FPS • intentional miss proves walk-mask impact and casing physics");
        await SmoothFirstPersonAim(
            host,
            loaded.Camera,
            loaded.Camera.TargetYawRadians,
            missYaw,
            loaded.Camera.TargetPitchRadians,
            0.0f,
            showcase.FpsMissAimFrames);
        if (loaded.Session.FireFirstPerson(
                loaded.Camera.FirstPersonEyePosition,
                missDirection) ||
            loaded.Session.FpsShots != shotsBefore + 1 ||
            loaded.Session.FpsHits != hitsBefore)
            throw new InvalidOperationException(
                "Fallout FPS showcase intentional miss hit a rat or was rejected.");
        await WaitFrames(host, showcase.FpsMissHoldFrames);
        await host.ToSignal(
            host.GetTree().CreateTimer(
                loaded.RuntimeProfile.Gameplay.FirstPersonShotCooldownSeconds),
            SceneTreeTimer.SignalName.Timeout);
        await WaitFrames(host, 1);

        var targetPoint = rat.GlobalPosition +
            Vector3.Up * showcase.FpsAimTargetHeightMeters;
        var offset = targetPoint - loaded.Camera.FirstPersonEyePosition;
        var horizontal = MathF.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
        var targetYaw = MathF.Atan2(-offset.X, -offset.Z);
        var targetPitch = MathF.Atan2(offset.Y, MathF.Max(Fo1NewGameFlowNumericContracts.PresentationFloat0Point001f, horizontal));
        stage.Text = "04  FPS RANGED HIT  •  AIM DOWN THE CAVE  •  10MM RAT KILL";
        loaded.Session.SetCameraStatus(
            "Traditional FPS • continuous movement • mouse-look direction • no tactical AP");
        await SmoothFirstPersonAim(
            host,
            loaded.Camera,
            loaded.Camera.TargetYawRadians,
            targetYaw,
            loaded.Camera.TargetPitchRadians,
            targetPitch,
            showcase.FpsAimFrames);
        await WaitFrames(host, showcase.FpsAimHoldFrames);
        for (var attempt = 0; attempt < showcase.MaximumFpsShots && rat.Alive; attempt++)
        {
            if (attempt > 0)
                await host.ToSignal(
                    host.GetTree().CreateTimer(showcase.ShotCooldownWaitSeconds),
                    SceneTreeTimer.SignalName.Timeout);
            targetPoint = rat.GlobalPosition +
                Vector3.Up * showcase.FpsAimTargetHeightMeters;
            if (!loaded.Session.FireFirstPerson(
                    loaded.Camera.FirstPersonEyePosition,
                    (targetPoint - loaded.Camera.FirstPersonEyePosition).Normalized()))
                throw new InvalidOperationException(
                    $"Fallout FPS showcase shot {attempt + 1} did not hit its source rat.");
            await WaitFrames(host, showcase.FpsShotHoldFrames);
        }
        await WaitFrames(host, showcase.FpsKillHoldFrames);
        if (rat.Alive || loaded.Session.FpsKills < 1 ||
            !rat.CorpseVisible ||
            rat.CorpseGroundErrorMeters > showcase.RatCorpseGroundToleranceMeters)
            throw new InvalidOperationException(
                $"Fallout FPS showcase rat did not enter a grounded death state: " +
                $"hp={rat.HitPoints} corpse={rat.CorpseVisible} " +
                $"error={rat.CorpseGroundErrorMeters:F6}.");
        killed.Add(new
        {
            mode = "first-person-shooter",
            serial = rat.Serial,
            pid = rat.Pid,
            hitPointsBefore,
            hitPointsAfter = rat.HitPoints,
            shots = loaded.Session.FpsShots - shotsBefore,
            misses = 1,
            corpseVisible = rat.CorpseVisible,
            corpseGroundErrorMeters = rat.CorpseGroundErrorMeters,
        });
        stage.Text = $"FPS RAT DOWN  •  SOURCE ENTITY {rat.Serial}  •  GROUNDED LIVE DEATH STATE";
        await WaitFrames(host, showcase.FpsPostKillHoldFrames);
    }

    private static async Task KillRatFirstPersonMelee(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Label stage,
        Fo1Mob rat,
        List<object> killed)
    {
        var showcase = loaded.RuntimeProfile.Showcase;
        if (!loaded.Camera.FirstPersonMode || !rat.Alive)
            throw new InvalidOperationException(
                "Fallout FPS knife showcase lost its live target camera.");
        stage.Text = "05  FPS MELEE  •  WALK INTO KNIFE RANGE  •  OWNED SWING + FLESH AUDIO";
        loaded.Session.SetCameraStatus(
            "Traditional FPS • RMB knife • source 1–6 damage + character melee bonus");
        await MoveFirstPersonAdjacentToTarget(host, loaded, rat);

        var hitPointsBefore = rat.HitPoints;
        var attacksBefore = loaded.Session.MeleeAttacks;
        var targetPoint = rat.GlobalPosition + Vector3.Up * showcase.FpsAimTargetHeightMeters;
        var offset = targetPoint - loaded.Camera.FirstPersonEyePosition;
        var horizontal = MathF.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
        var targetYaw = MathF.Atan2(-offset.X, -offset.Z);
        var targetPitch = MathF.Atan2(offset.Y, MathF.Max(Fo1NewGameFlowNumericContracts.PresentationFloat0Point001f, horizontal));
        await SmoothFirstPersonAim(
            host,
            loaded.Camera,
            loaded.Camera.TargetYawRadians,
            targetYaw,
            loaded.Camera.TargetPitchRadians,
            targetPitch,
            showcase.FpsMeleeAimFrames);
        await WaitFrames(host, showcase.FpsMeleeAimHoldFrames);

        for (var attempt = 0;
             attempt < showcase.MaximumTacticalAttacks && rat.Alive;
             attempt++)
        {
            if (attempt > 0)
                await host.ToSignal(
                    host.GetTree().CreateTimer(
                        loaded.RuntimeProfile.Gameplay.FirstPersonMeleeCooldownSeconds),
                    SceneTreeTimer.SignalName.Timeout);
            targetPoint = rat.GlobalPosition + Vector3.Up * showcase.FpsAimTargetHeightMeters;
            if (!loaded.Session.MeleeFirstPerson(
                    loaded.Camera.FirstPersonEyePosition,
                    (targetPoint - loaded.Camera.FirstPersonEyePosition).Normalized()))
                throw new InvalidOperationException(
                    $"Fallout FPS knife showcase swing {attempt + 1} did not hit its source rat.");
            await WaitFrames(host, showcase.FpsMeleeSwingHoldFrames);
        }
        await WaitFrames(host, showcase.FpsMeleeKillHoldFrames);
        if (rat.Alive || !rat.CorpseVisible ||
            rat.CorpseGroundErrorMeters > showcase.RatCorpseGroundToleranceMeters)
            throw new InvalidOperationException(
                $"Fallout FPS knife showcase did not leave a grounded rat corpse: " +
                $"hp={rat.HitPoints} corpse={rat.CorpseVisible} " +
                $"error={rat.CorpseGroundErrorMeters:F6}.");
        killed.Add(new
        {
            mode = "first-person-shooter-melee",
            weapon = "Knife",
            serial = rat.Serial,
            pid = rat.Pid,
            hitPointsBefore,
            hitPointsAfter = rat.HitPoints,
            attacks = loaded.Session.MeleeAttacks - attacksBefore,
            corpseVisible = rat.CorpseVisible,
            corpseGroundErrorMeters = rat.CorpseGroundErrorMeters,
        });
        stage.Text = $"FPS KNIFE RAT DOWN  •  SOURCE ENTITY {rat.Serial}  •  SAME LIVE HP STATE";
        await WaitFrames(host, showcase.FpsPostKillHoldFrames);
    }

    private static async Task KillRatTacticalRanged(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Label stage,
        Fo1Mob rat,
        List<object> killed)
    {
        var showcase = loaded.RuntimeProfile.Showcase;
        var hpBefore = rat.HitPoints;
        var attacksBefore = loaded.Session.Attacks;
        for (var attempt = 0;
             attempt < showcase.MaximumTacticalAttacks && rat.Alive;
             attempt++)
        {
            if (loaded.Session.ActionPoints < loaded.Session.WeaponActionPointCost)
            {
                stage.Text = "10  TACTICAL RANGED  •  END TURN  •  LOCAL RAT AI  •  AP RESTORED";
                loaded.Session.EndTurn();
                await WaitFrames(host, showcase.TacticalKillHoldFrames);
            }
            stage.Text = "10  TACTICAL RANGED  •  10MM ATTACK  •  CHANCE + AP + AMMO + HP";
            loaded.Session.ActivateTile(rat.Tile, false);
            loaded.Camera.SetOrbitDegrees(Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE45Point0f, Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE36Point0f);
            loaded.Camera.FrameCombatPair(loaded.Session.PlayerTile, rat.Tile);
            await WaitFrames(host, showcase.TacticalTargetHoldFrames);
            loaded.Camera.FocusTileAtHeight(rat.Tile, Fo1NewGameFlowNumericContracts.PresentationFloat4Point2f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point46f);
            await WaitFrames(host, showcase.TacticalFrameHoldFrames);
            loaded.Session.AttackSelectedRanged();
            await WaitFrames(host, showcase.TacticalAttackHoldFrames);
        }
        if (rat.Alive)
            throw new InvalidOperationException("Fallout tactical ranged showcase did not kill its rat.");
        await host.ToSignal(
            host.GetTree().CreateTimer(showcase.TacticalAttackSettleSeconds),
            SceneTreeTimer.SignalName.Timeout);
        if (!rat.CorpseVisible ||
            rat.CorpseGroundErrorMeters > showcase.RatCorpseGroundToleranceMeters)
            throw new InvalidOperationException(
                "Fallout tactical ranged showcase corpse grounding failed: " +
                $"visible={rat.CorpseVisible} error={rat.CorpseGroundErrorMeters:F6}");
        killed.Add(new
        {
            mode = "turn-based-tactical-ranged",
            weapon = "10mm Pistol",
            serial = rat.Serial,
            pid = rat.Pid,
            hitPointsBefore = hpBefore,
            hitPointsAfter = rat.HitPoints,
            attacks = loaded.Session.Attacks - attacksBefore,
            corpseVisible = rat.CorpseVisible,
            corpseGroundErrorMeters = rat.CorpseGroundErrorMeters,
        });
        stage.Text = $"TACTICAL PISTOL RAT DOWN  •  SOURCE ENTITY {rat.Serial}  •  LIVE DEATH STATE";
        await WaitFrames(host, showcase.TacticalKillHoldFrames);
    }

    private static async Task KillRatTacticalMelee(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Label stage,
        Fo1Mob rat,
        List<object> killed)
    {
        var showcase = loaded.RuntimeProfile.Showcase;
        stage.Text = "12  TACTICAL MELEE  •  CENTER-HEX APPROACH  •  KNIFE + AP + HIT CHANCE";
        await MoveTacticalAdjacentToTarget(host, loaded, rat);
        var hitPointsBefore = rat.HitPoints;
        var attacksBefore = loaded.Session.MeleeAttacks;
        for (var attempt = 0;
             attempt < showcase.MaximumTacticalAttacks && rat.Alive;
             attempt++)
        {
            if (Fo1HexMath.Distance(loaded.Session.PlayerTile, rat.Tile) > 1)
                await MoveTacticalAdjacentToTarget(host, loaded, rat);
            if (loaded.Session.ActionPoints < loaded.Session.MeleeActionPointCost)
            {
                loaded.Session.EndTurn();
                await WaitFrames(host, showcase.TacticalKillHoldFrames);
            }
            loaded.Session.ActivateTile(rat.Tile, false);
            loaded.Camera.SetOrbitDegrees(Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE45Point0f, Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE34Point0f);
            loaded.Camera.FrameCombatPair(loaded.Session.PlayerTile, rat.Tile);
            await WaitFrames(host, showcase.TacticalTargetHoldFrames);
            var result = loaded.Session.AttackSelectedMelee();
            if (!result.Attempted)
                throw new InvalidOperationException(
                    "Fallout tactical knife showcase attack was rejected.");
            await WaitFrames(host, showcase.TacticalAttackHoldFrames);
        }
        if (rat.Alive)
            throw new InvalidOperationException("Fallout tactical knife showcase did not kill its rat.");
        await host.ToSignal(
            host.GetTree().CreateTimer(showcase.TacticalAttackSettleSeconds),
            SceneTreeTimer.SignalName.Timeout);
        if (!rat.CorpseVisible ||
            rat.CorpseGroundErrorMeters > showcase.RatCorpseGroundToleranceMeters)
            throw new InvalidOperationException(
                "Fallout tactical knife showcase corpse grounding failed: " +
                $"visible={rat.CorpseVisible} error={rat.CorpseGroundErrorMeters:F6}");
        killed.Add(new
        {
            mode = "turn-based-tactical-melee",
            weapon = "Knife",
            serial = rat.Serial,
            pid = rat.Pid,
            hitPointsBefore,
            hitPointsAfter = rat.HitPoints,
            attacks = loaded.Session.MeleeAttacks - attacksBefore,
            corpseVisible = rat.CorpseVisible,
            corpseGroundErrorMeters = rat.CorpseGroundErrorMeters,
        });
        stage.Text = $"TACTICAL KNIFE RAT DOWN  •  SOURCE ENTITY {rat.Serial}  •  GROUNDED CORPSE";
        await WaitFrames(host, showcase.TacticalKillHoldFrames);
    }

    private static async Task SmoothFirstPersonYaw(
        Node host,
        Fo1TacticalCamera camera,
        float fromYawRadians,
        float toYawRadians,
        float pitchDegrees,
        int frames)
    {
        if (!camera.FirstPersonMode)
            throw new InvalidOperationException("Fallout first-person look proof lost its live camera.");
        for (var frame = 0; frame < frames; frame++)
        {
            var amount = (frame + 1.0f) / frames;
            var eased = amount * amount * (3.0f - 2.0f * amount);
            camera.SetOrbitDegrees(
                Mathf.RadToDeg(Mathf.Lerp(fromYawRadians, toYawRadians, eased)),
                pitchDegrees);
            await WaitFrames(host, 1);
        }
    }

    private static async Task SmoothFirstPersonAim(
        Node host,
        Fo1TacticalCamera camera,
        float fromYawRadians,
        float toYawRadians,
        float fromPitchRadians,
        float toPitchRadians,
        int frames)
    {
        if (!camera.FirstPersonMode)
            throw new InvalidOperationException("Fallout FPS aim showcase lost its live camera.");
        for (var frame = 0; frame < frames; frame++)
        {
            var amount = (frame + 1.0f) / frames;
            var eased = amount * amount * (3.0f - 2.0f * amount);
            camera.SetOrbitDegrees(
                Mathf.RadToDeg(Mathf.LerpAngle(fromYawRadians, toYawRadians, eased)),
                Mathf.RadToDeg(Mathf.Lerp(fromPitchRadians, toPitchRadians, eased)));
            await WaitFrames(host, 1);
        }
    }

    private static async Task SmoothShoulderOrbit(
        Node host,
        Fo1TacticalCamera camera,
        float fromYawRadians,
        float toYawRadians,
        float fromPitchDegrees,
        float toPitchDegrees,
        int frames)
    {
        if (!camera.ExplorationMode || camera.FirstPersonMode)
            throw new InvalidOperationException("Fallout shoulder showcase lost third-person mode.");
        for (var frame = 0; frame < frames; frame++)
        {
            var amount = (frame + 1.0f) / frames;
            var eased = amount * amount * (3.0f - 2.0f * amount);
            camera.SetOrbitDegrees(
                Mathf.RadToDeg(Mathf.LerpAngle(fromYawRadians, toYawRadians, eased)),
                Mathf.Lerp(fromPitchDegrees, toPitchDegrees, eased));
            await WaitFrames(host, 1);
        }
    }

    private static async Task SmoothTacticalMapTour(
        Node host,
        Fo1TacticalCamera camera,
        int playerTile,
        int doorTile,
        int entryTile,
        int frames)
    {
        if (camera.ExplorationMode)
            throw new InvalidOperationException("Fallout map tour requires tactical projection.");
        var from = (Fo1HexMath.Center(playerTile) + Fo1HexMath.Center(entryTile)) * Fo1NewGameFlowNumericContracts.PresentationFloat0Point5f +
            Vector3.Up * Fo1NewGameFlowNumericContracts.PresentationFloat1Point15f;
        var to = (Fo1HexMath.Center(entryTile) + Fo1HexMath.Center(doorTile)) * Fo1NewGameFlowNumericContracts.PresentationFloat0Point5f +
            Vector3.Up * Fo1NewGameFlowNumericContracts.PresentationFloat1Point35f;
        for (var frame = 0; frame < frames; frame++)
        {
            var amount = (frame + 1.0f) / frames;
            var eased = amount * amount * (3.0f - 2.0f * amount);
            camera.SetOrbitDegrees(
                Mathf.Lerp(Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE62Point0f, Fo1NewGameFlowNumericContracts.PresentationFloat28Point0f, eased),
                Mathf.Lerp(Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE46Point0f, Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE34Point0f, eased));
            camera.FocusWorldPoint(
                from.Lerp(to, eased),
                Mathf.Lerp(Fo1NewGameFlowNumericContracts.PresentationFloat13Point0f, Fo1NewGameFlowNumericContracts.PresentationFloat19Point0f, eased),
                Fo1NewGameFlowNumericContracts.PresentationFloat130Point0f);
            await WaitFrames(host, 1);
        }
    }

    private static async Task FadeToTactical(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded)
    {
        if (!loaded.Camera.ExplorationMode)
            throw new InvalidOperationException("Fallout tactical transition did not start in perspective mode.");
        var layer = new CanvasLayer { Name = "FirstPersonToTacticalFade", Layer = Fo1NewGameFlowNumericContracts.PresentationInt114 };
        host.AddChild(layer);
        var black = new ColorRect
        {
            Color = new Color(0.0f, 0.0f, 0.0f, 0.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        black.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(black);
        var showcase = loaded.RuntimeProfile.Showcase;
        for (var frame = 0; frame < showcase.FadeToTacticalOutFrames; frame++)
        {
            black.Color = new Color(
                0.0f,
                0.0f,
                0.0f,
                (frame + 1.0f) / showcase.FadeToTacticalOutFrames);
            await WaitFrames(host, 1);
        }

        loaded.Camera.SetExplorationMode(false);
        loaded.CaveCutaway.SetMeltEnabled(true);
        loaded.CaveCutaway.ProcessMode = Node.ProcessModeEnum.Inherit;
        loaded.Session.SetWorldGuidesVisible(true);
        foreach (var mob in loaded.Session.Mobs)
            mob.SetReadabilityMarkersVisible(true);
        loaded.Camera.SetOrbitDegrees(Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE38Point0f, Fo1NewGameFlowNumericContracts.PresentationFloatNEgativE38Point0f);
        loaded.Camera.FrameEntryPair(loaded.Session.PlayerTile, loaded.DoorTile);
        loaded.Session.SetCameraStatus(
            "TACTICAL • same player, cave, rats, hex path, HP and AP • C cycles perspective");
        await WaitFrames(host, 2);

        for (var frame = 0; frame < showcase.FadeToTacticalInFrames; frame++)
        {
            var amount = (frame + 1.0f) / showcase.FadeToTacticalInFrames;
            var eased = amount * amount * (3.0f - 2.0f * amount);
            black.Color = new Color(0.0f, 0.0f, 0.0f, 1.0f - eased);
            await WaitFrames(host, 1);
        }
        layer.QueueFree();
        if (loaded.Camera.ExplorationMode || loaded.Camera.FirstPersonMode ||
            !loaded.Session.PlayerToken.Visible ||
            loaded.Camera.Camera.Projection != Camera3D.ProjectionType.Orthogonal)
            throw new InvalidOperationException("Fallout first-person-to-tactical fade lost shared state.");
    }

    private static async Task MoveFirstPersonAdjacentToTarget(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1Mob target)
    {
        if (!loaded.Camera.FirstPersonMode)
            throw new InvalidOperationException(
                "Fallout FPS melee approach requires the live first-person camera.");
        var path = FindWalkablePathToAdjacent(loaded.Session, target);
        foreach (var tile in path.Skip(1))
        {
            var destination = Fo1HexMath.Center(tile) +
                Vector3.Up * loaded.RuntimeProfile.Scene.SourceSprites.GroundAnchorMeters;
            var offset = destination - loaded.Session.PlayerToken.Position;
            var yaw = MathF.Atan2(-offset.X, -offset.Z);
            await SmoothFirstPersonAim(
                host,
                loaded.Camera,
                loaded.Camera.TargetYawRadians,
                yaw,
                loaded.Camera.TargetPitchRadians,
                Mathf.DegToRad(loaded.RuntimeProfile.Camera.FirstPerson.InitialPitchDegrees),
                loaded.RuntimeProfile.Showcase.FpsMeleeApproachTurnFrames);
            await WaitUntilTile(
                host,
                loaded,
                tile,
                loaded.RuntimeProfile.Showcase.FpsMoveMaximumFrames);
        }
        if (Fo1HexMath.Distance(loaded.Session.PlayerTile, target.Tile) > 1)
            throw new InvalidOperationException(
                "Fallout FPS melee approach did not finish adjacent to its source rat.");
    }

    private static async Task<IReadOnlyList<int>> MoveTacticalAdjacentToTarget(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1Mob target)
    {
        var approachPath = new List<int> { loaded.Session.PlayerTile };
        var maximumTurns = Fo1HexMath.Width + Fo1HexMath.Height;
        for (var turn = 0; turn < maximumTurns; turn++)
        {
            if (!target.Alive)
                throw new InvalidOperationException(
                    "Fallout tactical melee approach target died before the knife attack.");
            if (Fo1HexMath.Distance(loaded.Session.PlayerTile, target.Tile) <= 1)
                return approachPath;
            if (loaded.Session.ActionPoints == 0)
            {
                loaded.Session.EndTurn();
                if (DisplayServer.GetName() != "headless")
                    await WaitFrames(host, loaded.RuntimeProfile.Showcase.TacticalKillHoldFrames);
            }
            var sourcePath = FindWalkablePathToAdjacent(loaded.Session, target).ToList();
            var destination = sourcePath[^1];
            loaded.Session.SelectTile(destination);
            if (DisplayServer.GetName() == "headless")
                loaded.Session.CompleteQueuedTacticalMovementForHeadlessProof();
            else
                for (var frame = 0;
                     loaded.Session.QueuedMovementSteps > 0 &&
                     frame < Fo1HexMath.Width * Fo1HexMath.Height;
                     frame++)
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (loaded.Session.QueuedMovementSteps > 0)
                throw new InvalidOperationException(
                    "Fallout tactical knife approach did not finish queued center-hex movement.");
            var arrivalIndex = sourcePath.IndexOf(loaded.Session.PlayerTile);
            if (arrivalIndex < 0)
                throw new InvalidOperationException(
                    "Fallout tactical knife approach left its source-walkable path.");
            approachPath.AddRange(sourcePath.Skip(1).Take(arrivalIndex));
            if (Fo1HexMath.Distance(loaded.Session.PlayerTile, target.Tile) <= 1)
                return approachPath;
            loaded.Session.EndTurn();
            if (DisplayServer.GetName() != "headless")
                await WaitFrames(host, loaded.RuntimeProfile.Showcase.TacticalKillHoldFrames);
        }
        throw new InvalidOperationException(
            "Fallout tactical knife approach exceeded the finite source-grid turn bound.");
    }

    private static async Task<IReadOnlyList<int>> MoveTacticalAdjacentToMapInventoryHost(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1TacticalSession.MapInventoryHost mapHost)
    {
        var approachPath = new List<int> { loaded.Session.PlayerTile };
        var maximumTurns = Fo1HexMath.Width + Fo1HexMath.Height;
        for (var turn = 0; turn < maximumTurns; turn++)
        {
            if (Fo1HexMath.AreNeighbors(loaded.Session.PlayerTile, mapHost.Tile))
                return approachPath;
            if (loaded.Session.ActionPoints == 0)
            {
                loaded.Session.EndTurn();
                if (DisplayServer.GetName() != "headless")
                    await WaitFrames(host, loaded.RuntimeProfile.Showcase.TacticalKillHoldFrames);
            }
            var sourcePath = FindWalkablePathToAdjacent(loaded.Session, mapHost.Tile).ToList();
            var destination = sourcePath[^1];
            loaded.Session.SelectTile(destination);
            if (DisplayServer.GetName() == "headless")
                loaded.Session.CompleteQueuedTacticalMovementForHeadlessProof();
            else
                for (var frame = 0;
                     loaded.Session.QueuedMovementSteps > 0 &&
                     frame < Fo1HexMath.Width * Fo1HexMath.Height;
                     frame++)
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (loaded.Session.QueuedMovementSteps > 0)
                throw new InvalidOperationException(
                    "Fallout MAP loot approach did not finish queued source-grid movement.");
            var arrivalIndex = sourcePath.IndexOf(loaded.Session.PlayerTile);
            if (arrivalIndex < 0)
                throw new InvalidOperationException(
                    "Fallout MAP loot approach left its source-walkable path.");
            approachPath.AddRange(sourcePath.Skip(1).Take(arrivalIndex));
            if (Fo1HexMath.AreNeighbors(loaded.Session.PlayerTile, mapHost.Tile))
                return approachPath;
            loaded.Session.EndTurn();
            if (DisplayServer.GetName() != "headless")
                await WaitFrames(host, loaded.RuntimeProfile.Showcase.TacticalKillHoldFrames);
        }
        throw new InvalidOperationException(
            "Fallout MAP loot approach exceeded the finite source-grid turn bound.");
    }

    private static async Task<IReadOnlyList<int>> MoveTacticalToExitGrid(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        IEnumerable<int> triggerTiles)
    {
        var pathTiles = new List<int> { loaded.Session.PlayerTile };
        var goals = triggerTiles.ToHashSet();
        var maximumTurns = Fo1HexMath.Width + Fo1HexMath.Height;
        for (var turn = 0; turn < maximumTurns; turn++)
        {
            if (loaded.Session.ActivatedExitGridTile is not null)
                return pathTiles;
            if (loaded.Session.ActionPoints == 0)
                loaded.Session.EndTurn();
            var sourcePath = FindWalkablePathToAny(loaded.Session, goals).ToList();
            loaded.Session.SelectTile(sourcePath[^1]);
            if (DisplayServer.GetName() == "headless")
                loaded.Session.CompleteQueuedTacticalMovementForHeadlessProof();
            else
                for (var frame = 0; loaded.Session.QueuedMovementSteps > 0 &&
                     frame < Fo1HexMath.Width * Fo1HexMath.Height; frame++)
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var arrivalIndex = sourcePath.IndexOf(loaded.Session.PlayerTile);
            if (arrivalIndex < 0)
                throw new InvalidOperationException("Fallout cave exit approach left its source-walkable path.");
            pathTiles.AddRange(sourcePath.Skip(1).Take(arrivalIndex));
            if (loaded.Session.ActivatedExitGridTile is not null)
                return pathTiles;
            loaded.Session.EndTurn();
        }
        throw new InvalidOperationException("Fallout cave exit grid has no finite source-walkable approach path.");
    }

    private static async Task<IReadOnlyList<int>> MoveTacticalAdjacentToSourceTile(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        int targetTile)
    {
        var pathTiles = new List<int> { loaded.Session.PlayerTile };
        var maximumTurns = Fo1HexMath.Width + Fo1HexMath.Height;
        for (var turn = 0; turn < maximumTurns; turn++)
        {
            if (Fo1HexMath.AreNeighbors(loaded.Session.PlayerTile, targetTile))
                return pathTiles;
            if (loaded.Session.ActionPoints == 0)
                loaded.Session.EndTurn();
            var sourcePath = FindWalkablePathToAdjacent(loaded.Session, targetTile).ToList();
            loaded.Session.SelectTile(sourcePath[^1]);
            if (DisplayServer.GetName() == "headless")
                loaded.Session.CompleteQueuedTacticalMovementForHeadlessProof();
            else
                for (var frame = 0; loaded.Session.QueuedMovementSteps > 0 &&
                     frame < Fo1HexMath.Width * Fo1HexMath.Height; frame++)
                    await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            var arrivalIndex = sourcePath.IndexOf(loaded.Session.PlayerTile);
            if (arrivalIndex < 0)
                throw new InvalidOperationException("Fallout door approach left its source-walkable path.");
            pathTiles.AddRange(sourcePath.Skip(1).Take(arrivalIndex));
            if (Fo1HexMath.AreNeighbors(loaded.Session.PlayerTile, targetTile))
                return pathTiles;
            loaded.Session.EndTurn();
        }
        throw new InvalidOperationException("Fallout MAP door has no source-walkable adjacent approach path.");
    }

    private static IReadOnlyList<int> FindWalkablePathToAdjacent(
        Fo1TacticalSession session,
        Fo1Mob target)
        => FindWalkablePathToAdjacent(session, target.Tile);

    private static IReadOnlyList<int> FindWalkablePathToAdjacent(
        Fo1TacticalSession session,
        int targetTile)
    {
        var occupied = session.Mobs
            .Where(mob => mob.Alive)
            .Select(mob => mob.Tile)
            .ToHashSet();
        var goals = Fo1HexMath.Neighbors(targetTile)
            .Where(tile => session.CanWalk(tile) && !occupied.Contains(tile))
            .ToHashSet();
        if (goals.Count == 0)
            throw new InvalidOperationException(
                "Fallout melee target has no source-walkable adjacent hex.");
        if (goals.Contains(session.PlayerTile))
            return new[] { session.PlayerTile };

        var queue = new Queue<int>();
        var previous = new Dictionary<int, int>();
        var visited = new HashSet<int> { session.PlayerTile };
        queue.Enqueue(session.PlayerTile);
        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            foreach (var neighbor in Fo1HexMath.Neighbors(tile))
            {
                if (!session.CanWalk(neighbor) || occupied.Contains(neighbor) ||
                    !visited.Add(neighbor))
                    continue;
                previous[neighbor] = tile;
                if (goals.Contains(neighbor))
                {
                    var reversed = new List<int> { neighbor };
                    var cursor = neighbor;
                    while (cursor != session.PlayerTile)
                    {
                        cursor = previous[cursor];
                        reversed.Add(cursor);
                    }
                    reversed.Reverse();
                    return reversed;
                }
                queue.Enqueue(neighbor);
            }
        }
        throw new InvalidOperationException(
            "Fallout melee target has no source-walkable approach path.");
    }

    private static IReadOnlyList<int> FindWalkablePathToAny(
        Fo1TacticalSession session,
        IReadOnlySet<int> goals)
    {
        if (goals.Contains(session.PlayerTile))
            return new[] { session.PlayerTile };
        var queue = new Queue<int>();
        var previous = new Dictionary<int, int>();
        var visited = new HashSet<int> { session.PlayerTile };
        queue.Enqueue(session.PlayerTile);
        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            foreach (var neighbor in Fo1HexMath.Neighbors(tile))
            {
                if (!session.CanWalk(neighbor) || !visited.Add(neighbor))
                    continue;
                previous[neighbor] = tile;
                if (!goals.Contains(neighbor))
                {
                    queue.Enqueue(neighbor);
                    continue;
                }
                var path = new List<int> { neighbor };
                for (var cursor = neighbor; cursor != session.PlayerTile;)
                {
                    cursor = previous[cursor];
                    path.Add(cursor);
                }
                path.Reverse();
                return path;
            }
        }
        throw new InvalidOperationException("Fallout cave exit has no source-walkable MAP path.");
    }

    private static Fo1Mob NearestLiving(Fo1TacticalSession session) =>
        session.Mobs.Where(mob => mob.Alive)
            .OrderBy(mob => Fo1HexMath.Distance(session.PlayerTile, mob.Tile))
            .ThenBy(mob => mob.Serial)
            .FirstOrDefault()
        ?? throw new InvalidOperationException("Fallout V13ENT has no living source rats.");

    private static int ChooseMovementTarget(
        Fo1TacticalSession session,
        int towardTile,
        int maximumSteps)
    {
        var current = session.PlayerTile;
        var visited = new HashSet<int> { current };
        for (var step = 0; step < maximumSteps; step++)
        {
            var next = Fo1HexMath.Neighbors(current)
                .Where(tile => session.CanWalk(tile) && !visited.Contains(tile))
                .OrderBy(tile => Fo1HexMath.Distance(tile, towardTile))
                .ThenBy(tile => tile)
                .FirstOrDefault(-1);
            if (next < 0)
                break;
            current = next;
            visited.Add(current);
        }
        if (current == session.PlayerTile)
            throw new InvalidOperationException("Fallout new-game demo could not find a movement path.");
        return current;
    }

    private static async Task WaitUntilTile(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        int tile,
        int maximumFrames)
    {
        var targetCenter = Fo1HexMath.Center(tile) +
            Vector3.Up * loaded.RuntimeProfile.Scene.SourceSprites.GroundAnchorMeters;
        for (var frame = 0; frame < maximumFrames &&
             (loaded.Session.PlayerTile != tile ||
              loaded.Session.PlayerToken.Position.DistanceTo(targetCenter) >
                  loaded.RuntimeProfile.Gameplay.TacticalArrivalToleranceMeters); frame++)
        {
            if (loaded.Camera.FirstPersonMode)
            {
                var direction = targetCenter - loaded.Session.PlayerToken.Position;
                direction.Y = 0.0f;
                if (direction.LengthSquared() > Fo1NewGameFlowNumericContracts.PresentationFloat0Point0001f)
                {
                    loaded.Session.TryMoveFirstPerson(
                        direction,
                        MathF.Min(
                            direction.Length(),
                            loaded.Camera.FirstPersonMoveSpeedMetersPerSecond /
                            loaded.RuntimeProfile.Showcase.FixedFramesPerSecond));
                }
            }
            else if (!loaded.Camera.ExplorationMode)
                loaded.Camera.FocusWorldPoint(
                    loaded.Session.PlayerToken.GlobalPosition + Vector3.Up * Fo1NewGameFlowNumericContracts.PresentationFloat0Point68f,
                    Fo1NewGameFlowNumericContracts.PresentationFloat4Point4f,
                    Fo1NewGameFlowNumericContracts.PresentationFloat180Point0f);
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        if (loaded.Session.PlayerTile != tile)
            throw new InvalidOperationException("Fallout new-game demo movement timed out.");
        if (loaded.Camera.FirstPersonMode &&
            loaded.Session.PlayerToken.Position.DistanceTo(targetCenter) >
                loaded.RuntimeProfile.Gameplay.TacticalArrivalToleranceMeters)
            throw new InvalidOperationException(
                "Fallout FPS demo did not reach the requested source-hex center.");
    }

    private static Label BuildStageBanner(Node host, bool visible)
    {
        var layer = new CanvasLayer
        {
            Name = "Fo1NewGameDemoBanner",
            Layer = Fo1NewGameFlowNumericContracts.PresentationInt70,
            Visible = visible,
        };
        host.AddChild(layer);
        layer.AddChild(new ColorRect
        {
            Position = new Vector2(Fo1NewGameFlowNumericContracts.PresentationFloat18Point0f, Fo1NewGameFlowNumericContracts.PresentationFloat16Point0f),
            Size = new Vector2(Fo1NewGameFlowNumericContracts.PresentationFloat940Point0f, Fo1NewGameFlowNumericContracts.PresentationFloat46Point0f),
            Color = new Color(Fo1NewGameFlowNumericContracts.PresentationFloat0Point012f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point018f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point01f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point93f),
        });
        var label = new Label
        {
            Position = new Vector2(Fo1NewGameFlowNumericContracts.PresentationFloat31Point0f, Fo1NewGameFlowNumericContracts.PresentationFloat25Point0f),
            Size = new Vector2(Fo1NewGameFlowNumericContracts.PresentationFloat915Point0f, Fo1NewGameFlowNumericContracts.PresentationFloat30Point0f),
            Text = "FALLOUT 1 NEW GAME  •  END-TO-END PROOF",
        };
        label.AddThemeColorOverride("font_color", new Color(Fo1NewGameFlowNumericContracts.PresentationFloat0Point97f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point78f, Fo1NewGameFlowNumericContracts.PresentationFloat0Point20f));
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", Fo1NewGameFlowNumericContracts.PresentationInt5);
        label.AddThemeFontSizeOverride("font_size", Fo1NewGameFlowNumericContracts.PresentationInt18);
        layer.AddChild(label);
        return label;
    }

    private static async Task WaitFrames(Node host, int count)
    {
        for (var frame = 0; frame < count; frame++)
        {
            if (DisplayServer.GetName() == "headless")
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            else
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
    }

    private readonly record struct OpeningPlayback(
        bool Skipped,
        int RenderedFrames,
        double PlaybackScale,
        int HandoffFrameIndex,
        string HandoffFrameSha256);

    private readonly record struct LandingPlayback(
        string Sequence,
        bool DoorOpenAtControl,
        int FinalEntryTile,
        bool OpeningWasSkipped,
        float EyeHeightMeters,
        float FovDegrees,
        float SpawnErrorMeters,
        float CaveForwardAlignment,
        float CameraPositionSeamMeters,
        float CameraForwardSeamAlignment,
        string DoorStateAuthority);
}
