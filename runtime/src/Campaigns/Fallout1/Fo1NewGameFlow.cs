using System.Text.Json;
using Godot;

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
        string startPresentation)
    {
        ValidateHandoff(loaded, contract);
        HideWorld(loaded);
        ShowMainMenu(host, loaded, contract, startPresentation);
    }

    private static void ShowMainMenu(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        string startPresentation)
    {
        var menu = new Fo1MainMenu();
        menu.Configure(startPresentation);
        var selected = false;
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
        GD.Print($"OPENNV_FO1_FRONTEND_READY presentation={startPresentation}");
    }

    private static void ShowCharacterSelection(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        string startPresentation)
    {
        var creator = new Fo1CharacterCreator();
        creator.Configure(contract);
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
        bool skipOpening)
    {
        try
        {
            ValidateHandoff(loaded, contract);
            GD.Print("OPENNV_FO1_NEW_GAME_DEMO_PHASE character-creation");
            HideWorld(loaded);
            var creator = new Fo1CharacterCreator();
            creator.Configure(contract);
            host.AddChild(creator);
            var profile = await creator.RunAutomatedDemo(host);
            profile.Validate();
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
            var landing = await RevealWorld(host, loaded, profile, opening, "first-person");
            await RunCombatShowcase(
                host,
                loaded,
                contract,
                profile,
                reportPath,
                opening,
                landing);
        }
        catch (Exception exception)
        {
            GD.PushError($"OPENNV_FO1_NEW_GAME_DEMO_FAIL {exception.Message}");
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
        var skipped = false;
        for (var frame = 0; frame < Fo1NewGameFlowNumericContracts.PresentationInt10000 && video.IsMoviePlaying; frame++)
        {
            if (skipRequested || Input.IsKeyPressed(Key.Escape))
            {
                skipped = true;
                video.SkipMovie();
                break;
            }
            video.AdvanceMovie(
                Math.Max(1.0 / Fo1NewGameFlowNumericContracts.PresentationDouble240Point0, host.GetProcessDeltaTime()) * playbackScale);
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

    private static async Task RunCombatShowcase(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1CharacterStartContract contract,
        Fo1CharacterProfile profile,
        string reportPath,
        OpeningPlayback opening,
        LandingPlayback landing)
    {
        var showcase = loaded.RuntimeProfile.Showcase;
        var reportFullPath = Path.GetFullPath(reportPath);
        GD.Print("OPENNV_FO1_NEW_GAME_DEMO_PHASE complete-combat-showcase");
        Directory.CreateDirectory(Path.GetDirectoryName(reportFullPath)!);
        var stage = BuildStageBanner(host, showcase.StageBannerVisible);
        stage.Text = "01  CHARACTER PROFILE SURVIVED THE OPENING  •  LIVE HP / AP / AC / SEQUENCE";
        loaded.Session.SetCameraStatus(
            $"{profile.Name}  •  V13ENT  •  exact tile {loaded.EntryTile}  •  " +
            "P opens Pip-Boy 2000");
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

        var combatPresentation = loaded.Session.CombatPresentation;
        if (loaded.Session.Kills - killsBefore < 4 || loaded.Session.FpsKills < 2 ||
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
        loaded.Session.PipBoy is null || loaded.Session.PipBoy.OpenedCount != 1 ||
        loaded.Session.PipBoy.IsOpen)
            throw new InvalidOperationException(
                "Fallout end-to-end new-game/equipment/combat gate failed.");
        var report = new
        {
            schema = "opennv-fo1-new-game-demo/v6",
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
                "owned-original-custom-character-creation",
                opening.Skipped ? "owned-original-overseer-mve-skipped" : "owned-original-overseer-mve",
                "owned-frame-fade-to-exact-live-first-person-v13ent",
                "owned-original-iface-frm-live-gameplay-hud",
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
            character = profile.Report(),
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

    private static async Task MoveTacticalAdjacentToTarget(
        Node host,
        Fo1HexSceneLoader.LoadedFo1HexScene loaded,
        Fo1Mob target)
    {
        var maximumTurns = Fo1HexMath.Width + Fo1HexMath.Height;
        for (var turn = 0; turn < maximumTurns; turn++)
        {
            if (!target.Alive)
                throw new InvalidOperationException(
                    "Fallout tactical melee approach target died before the knife attack.");
            if (Fo1HexMath.Distance(loaded.Session.PlayerTile, target.Tile) <= 1)
                return;
            if (loaded.Session.ActionPoints == 0)
            {
                loaded.Session.EndTurn();
                await WaitFrames(host, loaded.RuntimeProfile.Showcase.TacticalKillHoldFrames);
            }
            var destination = FindWalkablePathToAdjacent(loaded.Session, target)[^1];
            loaded.Session.SelectTile(destination);
            for (var frame = 0;
                 loaded.Session.QueuedMovementSteps > 0 &&
                 frame < Fo1HexMath.Width * Fo1HexMath.Height;
                 frame++)
                await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (loaded.Session.QueuedMovementSteps > 0)
                throw new InvalidOperationException(
                    "Fallout tactical knife approach did not finish queued center-hex movement.");
            if (Fo1HexMath.Distance(loaded.Session.PlayerTile, target.Tile) <= 1)
                return;
            loaded.Session.EndTurn();
            await WaitFrames(host, loaded.RuntimeProfile.Showcase.TacticalKillHoldFrames);
        }
        throw new InvalidOperationException(
            "Fallout tactical knife approach exceeded the finite source-grid turn bound.");
    }

    private static IReadOnlyList<int> FindWalkablePathToAdjacent(
        Fo1TacticalSession session,
        Fo1Mob target)
    {
        var occupied = session.Mobs
            .Where(mob => mob.Alive)
            .Select(mob => mob.Tile)
            .ToHashSet();
        var goals = Fo1HexMath.Neighbors(target.Tile)
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
