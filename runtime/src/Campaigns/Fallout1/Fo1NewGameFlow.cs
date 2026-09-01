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

internal static partial class Fo1NewGameFlow
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
                    !loaded.Session.DestinationGenericDoorOpen)
                    throw new InvalidOperationException("Fallout generic-door Continue proof did not open its authored blocker.");
                loaded.Session.CompleteDestinationDoorPlaybackForHeadlessProof();
                if (!loaded.Session.CanWalk(door.Door.Tile))
                    throw new InvalidOperationException(
                        "Fallout generic-door source playback did not release its authored blocker.");
                if (loaded.Session.ActionPoints == 0)
                    loaded.Session.EndTurn();
                loaded.Session.SelectTile(door.Door.Tile);
                loaded.Session.CompleteQueuedTacticalMovementForHeadlessProof();
                if (loaded.Session.PlayerTile != door.Door.Tile)
                    throw new InvalidOperationException("Fallout generic-door Continue proof did not move through its opened source blocker.");
                var doorState = loaded.Session.DestinationGenericDoorState ??
                    throw new InvalidOperationException(
                        "Fallout generic-door Continue proof has no source presentation state.");
                destinationMove = door.Door.Tile;
                genericDoor = new
                {
                    descriptor = door.Report(open: true),
                    approach = new { sourceWalkMaskOnly = true, pathTiles = approachPath, contactTile },
                    opened = true,
                    movedThroughOpenedBlocker = true,
                    interactionActionPoints = "not-source-backed",
                    sound = doorState.LastSoundLogicalPath,
                    sourceFrame = doorState.Frame,
                    framesPerSecond = door.Presentation.StoredFramesPerSecond,
                    frameCount = door.Presentation.FrameCount,
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


}
