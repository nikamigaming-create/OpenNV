using System.Globalization;
using System.Text.Json;
using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class GameplayUiController : CanvasLayer
{
    private const float ScreenMarginPixels = 24.0f;
    private const float ScreenTitleOffsetPixels = 16.0f;
    private const float ScreenContentOffsetPixels = 76.0f;
    private const float TabBarOffsetPixels = 52.0f;
    private const float TabBarHeightPixels = 42.0f;
    private const float ContentOffsetPixels = 102.0f;
    private const float ContentFooterOffsetPixels = 150.0f;
    private const float FooterBaselineOffsetPixels = 38.0f;
    private const float TitleFontSizeOffsetPixels = 4.0f;
    private const float BorderLightenAmount = 0.35f;
    private const float PlaneParallelEpsilon = 0.01f;
    private const float PlaneHalfExtent = 0.5f;
    private const float WristCursorSizePixels = 8.0f;
    private const int MaximumVisibleMapMarkers = 12;
    private const int WristFocusFrames = 2;

    private GameplaySession _session = null!;
    private RuntimeConfiguration _configuration = null!;
    private OwnedGameplayUiPresentation? _ownedPresentation;
    private FontFile? _ownedBodyFont;
    private FontFile? _ownedTitleFont;
    private FontFile? _ownedHudFont;
    private bool _useXr;
    private bool _showHud;
    private bool _useClassicDiorama;
    private bool _gameplayEnabled = true;
    private GameplayUiPanel _activePanel = GameplayUiPanel.Status;
    private Panel? _desktopHud;
    private Label? _desktopObjective;
    private Label? _desktopStatus;
    private Label? _desktopInventory;
    private Control? _pipBoyPanel;
    private Control? _ownedPipBoyCanvas;
    private Control? _ownedPipBoyScreen;
    private Control? _ownedPipBoyStatusScreen;
    private Label? _pipBoyContent;
    private Label? _pipBoyFooter;
    private readonly Dictionary<int, Label> _ownedStatusValues = new();
    private readonly Dictionary<int, OwnedPipBoyStringSource> _ownedStatusSources = new();
    private readonly Dictionary<GameplayUiPanel, PipBoyGlowSurface> _ownedButtonGlows = new();
    private Label? _xrContent;
    private ColorRect? _xrCursor;
    private Sprite3D? _xrScreen;
    private SubViewport? _xrViewport;
    private Node3D? _xrAim;
    private WristUiState _wristState = WristUiState.Dormant;
    private int _wristFocusFrames;

    internal bool HasDesktopHud => _desktopHud is not null;
    internal bool HasXrHud => _xrScreen is not null && _xrContent is not null;
    internal bool HasPipBoy => _pipBoyPanel is not null;
    internal float XrHudPixelSize => _xrScreen?.PixelSize ?? 0.0f;
    internal bool IsPipBoyOpen =>
        _gameplayEnabled && !_useXr && _pipBoyPanel?.Visible == true;

    internal void Configure(
        GameplaySession session,
        RuntimeConfiguration configuration,
        bool useXr,
        bool showHud,
        bool useClassicDiorama,
        OwnedGameplayUiPresentation? ownedPresentation)
    {
        _session = session;
        _configuration = configuration;
        _useXr = useXr;
        _showHud = showHud;
        _useClassicDiorama = useClassicDiorama;
        _ownedPresentation = ownedPresentation;
        if (_ownedPresentation is not null)
        {
            var hud = _ownedPresentation.Role("hud");
            var status = _ownedPresentation.Role("status");
            _ownedHudFont = OwnedUiTheme.BuildFont(_ownedPresentation.Font(hud.BodyFontId));
            _ownedBodyFont = OwnedUiTheme.BuildFont(_ownedPresentation.Font(status.BodyFontId));
            _ownedTitleFont = OwnedUiTheme.BuildFont(_ownedPresentation.Font(status.TitleFontId));
        }
        Name = "GameplayUi";
        if (_showHud && !_useXr)
        {
            if (_ownedPresentation is null || _useClassicDiorama)
                BuildDesktopHud();
            else
                BuildOwnedDesktopHud();
        }
        if (_showHud && !_useXr)
        {
            if (_ownedPresentation is null || _useClassicDiorama)
                BuildPipBoy();
            else
                BuildOwnedPipBoy();
        }
        Refresh();
        if (_ownedPresentation is not null)
            GD.Print(
                $"OPENNV_OWNED_GAMEPLAY_UI_READY roles={_ownedPresentation.Roles.Count} " +
                $"fonts={_ownedPresentation.Fonts.Count} canvas={_ownedPresentation.CanvasSize}");
    }

    internal void AttachXrHud(Node3D leftHand, Node3D aimSource)
    {
        if (!_useXr)
            throw new InvalidOperationException("Cannot attach an XR wrist UI to flat mode.");
        if (_xrScreen is not null)
            throw new InvalidOperationException("OpenNV XR wrist UI is already attached.");

        var mount = new Node3D
        {
            Name = "XrWristHud",
            Position = _configuration.Hud.XrMountPositionMeters.Vector3(),
            RotationDegrees = _configuration.Hud.XrMountRotationDegrees.Vector3(),
        };
        leftHand.AddChild(mount);
        _xrAim = aimSource;

        _xrViewport = new SubViewport
        {
            Name = "WristScreenPixels",
            Size = ScreenSizePixels(),
            TransparentBg = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        mount.AddChild(_xrViewport);
        var surface = new Control
        {
            Name = "WristScreenSurface",
            Size = ScreenSizePixels(),
        };
        _xrViewport.AddChild(surface);
        BuildScreenSurface(surface, out _xrContent, out _);
        _xrCursor = new ColorRect
        {
            Name = "WristPointer",
            Color = _configuration.Hud.TextColorRgba.Color(),
            Size = new Vector2(WristCursorSizePixels, WristCursorSizePixels),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        surface.AddChild(_xrCursor);
        _xrScreen = new Sprite3D
        {
            Name = "WristScreenQuad",
            Texture = _xrViewport.GetTexture(),
            PixelSize = _configuration.Hud.XrPixelSizeMeters,
            NoDepthTest = true,
            Shaded = false,
            Position = Vector3.Zero,
            Visible = _gameplayEnabled && Visible,
        };
        mount.AddChild(_xrScreen);
        SetWristState(WristUiState.Active, "attached");
        Refresh();
    }

    internal void TogglePipBoy()
    {
        if (!_gameplayEnabled)
            return;
        if (_useXr)
        {
            if (_xrScreen is null)
                return;
            _xrScreen.Visible = !_xrScreen.Visible;
            if (_xrScreen.Visible)
            {
                _activePanel = GameplayUiPanel.Status;
                Refresh();
            }
            return;
        }
        if (_pipBoyPanel is null)
            return;
        _pipBoyPanel.Visible = !_pipBoyPanel.Visible;
        if (_desktopHud is not null)
            _desktopHud.Visible = !_pipBoyPanel.Visible;
        if (_pipBoyPanel.Visible)
        {
            _activePanel = GameplayUiPanel.Status;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Refresh();
        }
        else if (!_useXr && DisplayServer.GetName() != "headless")
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    internal void ClosePipBoy()
    {
        if (_useXr)
        {
            if (_xrScreen is not null)
                _xrScreen.Visible = false;
            return;
        }
        if (_pipBoyPanel?.Visible != true)
            return;
        _pipBoyPanel.Visible = false;
        if (_desktopHud is not null)
            _desktopHud.Visible = _showHud && _gameplayEnabled;
        if (!_useXr && DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    internal void SetGameplayVisible(bool visible)
    {
        _gameplayEnabled = visible;
        if (!visible)
            ClosePipBoy();
        Visible = visible;
        if (_useXr && _xrScreen is not null)
            _xrScreen.Visible = visible;
    }

    internal void Refresh()
    {
        if (_session is null)
            return;
        var snapshot = _session.BuildUiSnapshot();
        if (_desktopObjective is not null)
        {
            var ownedRetailHud = _ownedPresentation is not null && !_useClassicDiorama;
            _desktopObjective.Text = ownedRetailHud ? string.Empty : snapshot.Objective;
            _desktopStatus!.Text = ownedRetailHud
                ? string.Empty
                : FormatStatusLine(snapshot);
            _desktopInventory!.Text = ownedRetailHud
                ? string.Empty
                : FormatInventorySummary(snapshot);
        }
        if (_pipBoyContent is not null)
            _pipBoyContent.Text = FormatPanel(snapshot, _activePanel);
        RefreshOwnedStatus(snapshot);
        if (_pipBoyFooter is not null)
            _pipBoyFooter.Text = _ownedPresentation is null
                ? _configuration.Hud.PipBoy.CloseHint
                : "TAB CLOSE";
        if (_xrContent is not null)
            _xrContent.Text = FormatWrist(snapshot);
    }

    public override void _Process(double delta)
    {
        if (!_gameplayEnabled || _xrScreen is null || _xrAim is null || !_xrAim.IsInsideTree())
            return;
        var pointer = ResolveWristPointer(_xrScreen, _xrAim);
        if (pointer.HasValue)
        {
            if (_xrCursor is not null)
            {
                _xrCursor.Position = pointer.Value -
                    new Vector2(WristCursorSizePixels * PlaneHalfExtent, WristCursorSizePixels * PlaneHalfExtent);
                _xrCursor.Visible = true;
            }
            _wristFocusFrames++;
            if (_wristState == WristUiState.Active)
                SetWristState(WristUiState.Candidate, "aim-hit");
            if (_wristFocusFrames >= WristFocusFrames)
                SetWristState(WristUiState.Focused, "aim-hit");
            return;
        }
        _wristFocusFrames = 0;
        if (_xrCursor is not null)
            _xrCursor.Visible = false;
        if (_wristState is WristUiState.Candidate or WristUiState.Focused)
            SetWristState(WristUiState.Active, "aim-left-screen");
    }

    private void BuildOwnedDesktopHud()
    {
        var presentation = _ownedPresentation
            ?? throw new InvalidOperationException("Owned gameplay UI presentation is unavailable.");
        var hud = presentation.Role("hud");
        _desktopHud = new Panel
        {
            Name = "OwnedNewVegasHud",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _desktopHud.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(_desktopHud);
        _desktopHud.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var canvas = new Control
        {
            Name = "OwnedNewVegasHudCanvas",
            Size = presentation.CanvasSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _desktopHud.AddChild(canvas);
        _desktopHud.Resized += () => ScaleOwnedCanvas(_desktopHud, canvas, presentation.CanvasSize);
        ScaleOwnedCanvas(_desktopHud, canvas, presentation.CanvasSize);

        _desktopObjective = BuildOwnedLabel(_ownedHudFont!);
        _desktopObjective.Name = "QuestReminder";
        PlaceOwnedCanvasRect(_desktopObjective, hud.Layout["QuestReminder"]);
        canvas.AddChild(_desktopObjective);

        _desktopStatus = BuildOwnedLabel(_ownedHudFont!);
        _desktopStatus.Name = "Messages";
        PlaceOwnedCanvasRect(_desktopStatus, hud.Layout["Messages"]);
        canvas.AddChild(_desktopStatus);

        _desktopInventory = BuildOwnedLabel(_ownedHudFont!);
        _desktopInventory.Name = "Info";
        _desktopInventory.HorizontalAlignment = HorizontalAlignment.Right;
        PlaceOwnedCanvasRect(_desktopInventory, hud.Layout["Info"]);
        canvas.AddChild(_desktopInventory);

        var crosshair = BuildOwnedLabel(_ownedHudFont!);
        crosshair.Name = "ReticleCenter";
        crosshair.Text = "+";
        crosshair.HorizontalAlignment = HorizontalAlignment.Center;
        crosshair.VerticalAlignment = VerticalAlignment.Center;
        PlaceOwnedCanvasRect(crosshair, hud.Layout["ReticleCenter"]);
        canvas.AddChild(crosshair);
    }

    private void BuildOwnedPipBoy()
    {
        var presentation = _ownedPresentation
            ?? throw new InvalidOperationException("Owned gameplay UI presentation is unavailable.");
        _pipBoyPanel = new Panel
        {
            Name = "OwnedNewVegasPipBoy",
            Visible = false,
        };
        _pipBoyPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        AddChild(_pipBoyPanel);
        _pipBoyPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        var deviceContainer = new SubViewportContainer
        {
            Name = "OwnedPipBoyPhysicalDevice",
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _pipBoyPanel.AddChild(deviceContainer);
        deviceContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var visibleSize = GetViewport().GetVisibleRect().Size;
        var deviceViewport = new SubViewport
        {
            Name = "OwnedPipBoyPhysicalPixels",
            Size = new Vector2I(
                Mathf.Max(1, Mathf.RoundToInt(visibleSize.X)),
                Mathf.Max(1, Mathf.RoundToInt(visibleSize.Y))),
            TransparentBg = true,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        deviceContainer.AddChild(deviceViewport);
        deviceContainer.Resized += () =>
        {
            deviceViewport.Size = new Vector2I(
                Mathf.Max(1, Mathf.RoundToInt(deviceContainer.Size.X)),
                Mathf.Max(1, Mathf.RoundToInt(deviceContainer.Size.Y)));
        };

        var crtViewport = new SubViewport
        {
            Name = "OwnedPipBoyCrtPixels",
            Size = new Vector2I(
                Mathf.RoundToInt(presentation.CanvasSize.X),
                Mathf.RoundToInt(presentation.CanvasSize.Y)),
            TransparentBg = false,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        _pipBoyPanel.AddChild(crtViewport);
        _ownedPipBoyCanvas = new Control
        {
            Name = "OwnedNewVegasPipBoyCanvas",
            Size = presentation.CanvasSize,
        };
        crtViewport.AddChild(_ownedPipBoyCanvas);

        var background = new TextureRect
        {
            Name = "OwnedPipBoyBackground",
            Texture = OwnedUiTheme.LoadTexture(presentation.Background.Path),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(
                presentation.SystemColor.R,
                presentation.SystemColor.G,
                presentation.SystemColor.B,
                1.0f),
        };
        background.Position = Vector2.Zero;
        background.Size = presentation.CanvasSize;
        _ownedPipBoyCanvas.AddChild(background);

        var screen = new Panel
        {
            Name = "OwnedPipBoyMainRect",
        };
        screen.AddThemeStyleboxOverride("panel", BuildOwnedFrameStyle());
        _ownedPipBoyScreen = screen;
        _ownedPipBoyCanvas.AddChild(screen);

        var tabs = new HBoxContainer
        {
            Name = "OwnedPipBoyTabs",
            AnchorRight = 1.0f,
            OffsetBottom = TabBarHeightPixels,
        };
        screen.AddChild(tabs);
        AddOwnedTab(tabs, "STAT", GameplayUiPanel.Status);
        AddOwnedTab(tabs, "ITEMS", GameplayUiPanel.Items);
        AddOwnedTab(tabs, "DATA", GameplayUiPanel.Data);

        var content = new VBoxContainer
        {
            Name = "OwnedPipBoyContent",
            AnchorRight = 1.0f,
            AnchorBottom = 1.0f,
            OffsetLeft = ScreenMarginPixels,
            OffsetTop = TabBarHeightPixels + ScreenMarginPixels,
            OffsetRight = -ScreenMarginPixels,
            OffsetBottom = -FooterBaselineOffsetPixels,
        };
        screen.AddChild(content);
        _pipBoyContent = BuildOwnedLabel(_ownedBodyFont!);
        _pipBoyContent.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pipBoyContent.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _pipBoyContent.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        content.AddChild(_pipBoyContent);

        _pipBoyFooter = BuildOwnedLabel(_ownedBodyFont!);
        _pipBoyFooter.Name = "OwnedPipBoyFooter";
        _pipBoyFooter.AnchorTop = 1.0f;
        _pipBoyFooter.AnchorRight = 1.0f;
        _pipBoyFooter.AnchorBottom = 1.0f;
        _pipBoyFooter.OffsetLeft = ScreenMarginPixels;
        _pipBoyFooter.OffsetTop = -FooterBaselineOffsetPixels;
        _pipBoyFooter.OffsetRight = -ScreenMarginPixels;
        _pipBoyFooter.HorizontalAlignment = HorizontalAlignment.Right;
        screen.AddChild(_pipBoyFooter);
        BuildOwnedStatusScreen(presentation);
        ApplyOwnedPipBoyRole();
        BuildOwnedPhysicalPipBoy(
            deviceViewport,
            crtViewport.GetTexture(),
            presentation);
    }

    private void BuildOwnedPhysicalPipBoy(
        SubViewport viewport,
        Texture2D crtTexture,
        OwnedGameplayUiPresentation presentation)
    {
        var contract = presentation.PhysicalDevice;
        VerifiedGltfLoader.VerifyHash(contract.ModelPath, contract.ModelSha256);
        VerifiedGltfLoader.VerifyHash(contract.SidecarPath, contract.SidecarSha256);
        VerifiedGltfLoader.VerifyHash(contract.BufferPath, contract.BufferSha256);
        var loaded = VerifiedGltfLoader.Load(contract.ModelPath, contract.SidecarPath);
        loaded.CollisionScene?.Free();
        if (!loaded.SourceSha256.Equals(contract.SourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Owned physical Pip-Boy source identity changed.");
        var model = loaded.Scene;
        model.Name = "OwnedPipBoy3000";
        viewport.AddChild(model);

        VerifiedGltfLoader.VerifyHash(
            contract.MaterialManifestPath,
            contract.MaterialManifestSha256);
        using var document = JsonDocument.Parse(File.ReadAllText(contract.MaterialManifestPath));
        var materialManifest = document.RootElement;
        if (materialManifest.GetProperty("schema").GetString() !=
                "opennv-static-material-manifest/v1")
            throw new InvalidOperationException(
                "Owned physical Pip-Boy material manifest changed.");
        var textures = RuntimeMaterialLoader.LoadTextures(
            materialManifest,
            _configuration.Renderer);
        var materialBindings = RuntimeMaterialLoader.Apply(
            model,
            materialManifest.GetProperty("asset"),
            textures,
            _configuration.Renderer,
            _configuration.ContentCompiler.RetailGrass);
        var surfaces = Descendants<MeshInstance3D>(model)
            .SelectMany(mesh => Enumerable.Range(0, mesh.Mesh?.GetSurfaceCount() ?? 0)
                .Select(surface => (Mesh: mesh, Surface: surface)))
            .ToArray();
        if (surfaces.Length != contract.Surfaces || materialBindings != contract.Surfaces)
            throw new InvalidOperationException(
                "Owned physical Pip-Boy surface/material coverage changed.");
        var screenMatches = surfaces.Where(surface =>
                RuntimeMaterialLoader.SourceSurfaceIdentity(surface.Mesh, surface.Surface)
                    ?.Equals(contract.ScreenSurface, StringComparison.Ordinal) == true)
            .ToArray();
        if (screenMatches.Length != 1)
            throw new InvalidOperationException(
                "Owned physical Pip-Boy CRT surface does not resolve uniquely.");
        var frame = DerivePipBoyFrame(
            model,
            screenMatches[0].Mesh,
            screenMatches[0].Surface,
            viewport.Size);
        screenMatches[0].Mesh.SetSurfaceOverrideMaterial(
            screenMatches[0].Surface,
            new StandardMaterial3D
            {
                ResourceName = "OpenNV_OwnedPipBoyDynamicCrt",
                AlbedoTexture = crtTexture,
                AlbedoTextureForceSrgb = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            });
        foreach (var (role, surfaceName) in contract.ButtonGlowSurfaces)
        {
            var glowMatches = surfaces.Where(surface =>
                    RuntimeMaterialLoader.SourceSurfaceIdentity(surface.Mesh, surface.Surface)
                        ?.Equals(surfaceName, StringComparison.Ordinal) == true)
                .ToArray();
            if (glowMatches.Length != 1)
                throw new InvalidOperationException(
                    $"Owned physical Pip-Boy button glow does not resolve uniquely: " +
                    $"{role}={surfaceName}");
            var authoredMaterial = glowMatches[0].Mesh.GetSurfaceOverrideMaterial(
                    glowMatches[0].Surface)
                ?? throw new InvalidOperationException(
                    $"Owned physical Pip-Boy button glow has no authored material: {surfaceName}");
            _ownedButtonGlows.Add(
                role.ToLowerInvariant() switch
                {
                    "status" => GameplayUiPanel.Status,
                    "items" => GameplayUiPanel.Items,
                    "data" => GameplayUiPanel.Data,
                    _ => throw new InvalidOperationException(
                        $"Owned physical Pip-Boy button role is unsupported: {role}"),
                },
                new PipBoyGlowSurface(
                    glowMatches[0].Mesh,
                    glowMatches[0].Surface,
                    authoredMaterial,
                    new StandardMaterial3D
                    {
                        ResourceName = $"OpenNV_Inactive_{surfaceName}",
                        AlbedoColor = Colors.Transparent,
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
                    }));
        }
        UpdateOwnedPipBoyButtonGlows();

        var preview = _configuration.DiagnosticPreview;
        var ambient = preview.AmbientColorRgba.Color();
        ambient.R *= preview.AmbientEnergy;
        ambient.G *= preview.AmbientEnergy;
        ambient.B *= preview.AmbientEnergy;
        RuntimeMaterialLoader.ApplyRetailAmbientDirectionalLighting(
            model,
            ambient,
            Colors.Black,
            frame.Far * 2.0f,
            frame.Far * 4.0f,
            1.0f,
            1.0f);
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = Colors.Transparent,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = preview.AmbientColorRgba.Color(),
                AmbientLightEnergy = preview.AmbientEnergy,
                TonemapMode = RuntimeRendering.ParseToneMapper(_configuration.Renderer.ToneMapper),
            },
        });
        var keyLight = new DirectionalLight3D
        {
            Position = frame.CameraPosition,
            LightEnergy = preview.LightEnergy,
            ShadowEnabled = true,
        };
        viewport.AddChild(keyLight);
        keyLight.LookAt(frame.Target, frame.Up);
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = frame.OrthographicHeight,
            Position = frame.CameraPosition,
            Near = frame.Near,
            Far = frame.Far,
            Current = true,
        };
        viewport.AddChild(camera);
        camera.LookAt(frame.Target, frame.Up);
        GD.Print(
            $"OPENNV_OWNED_PIPBOY_PHYSICAL_READY source={contract.LogicalPath} " +
            $"surfaces={contract.Surfaces} vertices={contract.Vertices} " +
            $"textures={contract.Textures} screen={contract.ScreenSurface}");
    }

    private void BuildOwnedStatusScreen(OwnedGameplayUiPresentation presentation)
    {
        if (_ownedPipBoyCanvas is null)
            throw new InvalidOperationException("Owned Pip-Boy canvas is unavailable.");
        var contract = presentation.StatusPresentation;
        var status = new Control
        {
            Name = "OwnedPipBoyStatsMenu",
            Size = presentation.CanvasSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _ownedPipBoyStatusScreen = status;
        _ownedPipBoyCanvas.AddChild(status);

        foreach (var rule in contract.Rules)
            AddOwnedRule(status, rule.Rect.Position, rule.Rect.Size);
        foreach (var source in contract.Headline)
        {
            var label = BuildOwnedLabel(
                OwnedUiTheme.BuildFont(presentation.Font(source.FontId)));
            label.Name = source.Tile;
            label.Position = source.Rect.Position;
            label.Size = source.Rect.Size;
            label.Text = source.Text;
            status.AddChild(label);
            _ownedStatusValues.Add(source.EngineId, label);
            _ownedStatusSources.Add(source.EngineId, source);
        }

        var statusContainer = new Control
        {
            Name = "stats_status_container",
            Position = contract.StatusContainerRect.Position,
            Size = contract.StatusContainerRect.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        status.AddChild(statusContainer);
        foreach (var source in contract.ConditionTabs)
        {
            var label = BuildOwnedLabel(
                OwnedUiTheme.BuildFont(presentation.Font(source.FontId)));
            label.Name = source.Tile;
            label.Text = source.Text;
            label.Position = source.Rect.Position;
            label.Size = source.Rect.Size;
            label.VerticalAlignment = VerticalAlignment.Center;
            if (source.Selected)
            {
                var selection = new Panel
                {
                    Name = $"{source.Tile}_selection",
                    Position = source.Rect.Position,
                    Size = source.Rect.Size,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                selection.AddThemeStyleboxOverride("panel", BuildOwnedFrameStyle());
                statusContainer.AddChild(selection);
            }
            statusContainer.AddChild(label);
        }

        var bodyNodes = new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase)
        {
            ["stats_status_container"] = statusContainer,
        };
        foreach (var source in contract.BodyImages)
        {
            if (!bodyNodes.TryGetValue(source.ParentTile, out var parent))
                throw new InvalidOperationException(
                    $"Owned Pip-Boy body-image parent is unavailable: " +
                    $"{source.Tile} -> {source.ParentTile}");
            var image = new TextureRect
            {
                Name = source.Tile,
                Position = source.Rect.Position,
                Size = source.Rect.Size,
                Texture = OwnedUiTheme.LoadTexture(source.Texture.Path),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Modulate = presentation.SystemColor,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            parent.AddChild(image);
            bodyNodes.Add(source.Tile, image);
        }

        foreach (var source in contract.Navigation)
        {
            var label = BuildOwnedLabel(
                OwnedUiTheme.BuildFont(presentation.Font(source.FontId)));
            label.Name = source.Tile;
            label.Text = source.Text;
            label.Position = source.Rect.Position;
            label.Size = source.Rect.Size;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            if (source.Selected)
            {
                var selection = new Panel
                {
                    Name = $"{source.Tile}_selection",
                    Position = source.Rect.Position,
                    Size = source.Rect.Size,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                selection.AddThemeStyleboxOverride("panel", BuildOwnedFrameStyle());
                status.AddChild(selection);
            }
            status.AddChild(label);
        }
    }

    private void AddOwnedRule(Control parent, Vector2 position, Vector2 size)
    {
        var presentation = _ownedPresentation
            ?? throw new InvalidOperationException("Owned gameplay UI presentation is unavailable.");
        parent.AddChild(new ColorRect
        {
            Position = position,
            Size = size,
            Color = OwnedUiTheme.Brightness(
                presentation.SystemColor,
                presentation.Style.LineBrightness),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }

    private void RefreshOwnedStatus(GameplayUiSnapshot snapshot)
    {
        if (_ownedStatusValues.Count == 0)
            return;
        _ownedStatusValues[39].Text = _ownedStatusSources[39].Text;
        _ownedStatusValues[40].Text = snapshot.Level is { } level
            ? $"{_ownedStatusSources[40].Text}  {level}"
            : _ownedStatusSources[40].Text;
        _ownedStatusValues[41].Text =
            snapshot.HitPoints is { } hp && snapshot.MaximumHitPoints is { } maximumHp
                ? $"{_ownedStatusSources[41].Text}  {hp}/{maximumHp}"
                : _ownedStatusSources[41].Text;
        _ownedStatusValues[42].Text =
            snapshot.ActionPoints is { } ap && snapshot.MaximumActionPoints is { } maximumAp
                ? $"{_ownedStatusSources[42].Text}  {ap}/{maximumAp}"
                : _ownedStatusSources[42].Text;
        _ownedStatusValues[43].Text =
            snapshot.ExperiencePoints is { } xp &&
                snapshot.NextLevelExperiencePoints is { } nextLevelXp
                ? $"{_ownedStatusSources[43].Text}  {xp}/{nextLevelXp}"
                : _ownedStatusSources[43].Text;
    }

    private static PipBoyReferenceFrame DerivePipBoyFrame(
        Node3D model,
        MeshInstance3D screenMesh,
        int screenSurface,
        Vector2I viewportSize)
    {
        var mesh = screenMesh.Mesh
            ?? throw new InvalidOperationException("Owned Pip-Boy CRT has no mesh.");
        var arrays = mesh.SurfaceGetArrays(screenSurface);
        var localVertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var localNormals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        if (localVertices.Length < 3 ||
            localNormals.Length != localVertices.Length ||
            uvs.Length != localVertices.Length)
            throw new InvalidOperationException(
                "Owned Pip-Boy CRT geometry lacks complete position/normal/UV evidence.");
        var vertices = localVertices.Select(screenMesh.ToGlobal).ToArray();
        var center = vertices.Aggregate(Vector3.Zero, (sum, value) => sum + value) /
            vertices.Length;
        var meanUv = uvs.Aggregate(Vector2.Zero, (sum, value) => sum + value) / uvs.Length;
        var normal = localNormals
            .Select(value => (screenMesh.GlobalBasis * value).Normalized())
            .Aggregate(Vector3.Zero, (sum, value) => sum + value)
            .Normalized();
        var uAxis = Vector3.Zero;
        foreach (var index in Enumerable.Range(0, vertices.Length))
        {
            var delta = vertices[index] - center;
            uAxis += delta * (uvs[index].X - meanUv.X);
        }
        uAxis -= normal * uAxis.Dot(normal);
        if (normal.LengthSquared() <= 0.0f || uAxis.LengthSquared() <= 0.0f)
            throw new InvalidOperationException(
                "Owned Pip-Boy CRT frame cannot be derived from its source geometry.");
        var right = uAxis.Normalized();
        var viewDirection = -normal;
        var up = right.Cross(viewDirection).Normalized();

        var points = Descendants<MeshInstance3D>(model)
            .SelectMany(value =>
            {
                var bounds = value.GetAabb();
                return new[]
                {
                    new Vector3(bounds.Position.X, bounds.Position.Y, bounds.Position.Z),
                    new Vector3(bounds.End.X, bounds.Position.Y, bounds.Position.Z),
                    new Vector3(bounds.Position.X, bounds.End.Y, bounds.Position.Z),
                    new Vector3(bounds.End.X, bounds.End.Y, bounds.Position.Z),
                    new Vector3(bounds.Position.X, bounds.Position.Y, bounds.End.Z),
                    new Vector3(bounds.End.X, bounds.Position.Y, bounds.End.Z),
                    new Vector3(bounds.Position.X, bounds.End.Y, bounds.End.Z),
                    bounds.End,
                }.Select(value.ToGlobal);
            })
            .ToArray();
        if (points.Length == 0)
            throw new InvalidOperationException("Owned Pip-Boy model has no bounds.");
        var rightExtents = points.Select(point => (point - center).Dot(right)).ToArray();
        var upExtents = points.Select(point => (point - center).Dot(up)).ToArray();
        var depthExtents = points.Select(point => (point - center).Dot(normal)).ToArray();
        var width = rightExtents.Max() - rightExtents.Min();
        var height = upExtents.Max() - upExtents.Min();
        var depth = depthExtents.Max() - depthExtents.Min();
        var aspect = viewportSize.Y > 0
            ? (float)viewportSize.X / viewportSize.Y
            : 1.0f;
        var orthographicHeight = MathF.Max(height, width / MathF.Max(aspect, 0.01f));
        var target = center +
            right * ((rightExtents.Min() + rightExtents.Max()) * 0.5f) +
            up * ((upExtents.Min() + upExtents.Max()) * 0.5f);
        var cameraDistance = depthExtents.Max() + orthographicHeight;
        return new PipBoyReferenceFrame(
            target + normal * cameraDistance,
            target,
            up,
            orthographicHeight,
            MathF.Max(0.01f, orthographicHeight * 0.01f),
            MathF.Max(1.0f, cameraDistance + depth + orthographicHeight));
    }

    private static IEnumerable<T> Descendants<T>(Node node)
        where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
                yield return match;
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    private readonly record struct PipBoyReferenceFrame(
        Vector3 CameraPosition,
        Vector3 Target,
        Vector3 Up,
        float OrthographicHeight,
        float Near,
        float Far);

    private readonly record struct PipBoyGlowSurface(
        MeshInstance3D Mesh,
        int Surface,
        Material AuthoredMaterial,
        Material InactiveMaterial);

    private Label BuildOwnedLabel(FontFile font)
    {
        var presentation = _ownedPresentation
            ?? throw new InvalidOperationException("Owned gameplay UI presentation is unavailable.");
        var label = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", font.FixedSize);
        label.AddThemeColorOverride(
            "font_color",
            OwnedUiTheme.Brightness(
                presentation.SystemColor,
                presentation.Style.TextBrightness));
        label.AddThemeColorOverride("font_shadow_color", Colors.Black);
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 1);
        return label;
    }

    private void ApplyOwnedButton(Button button)
    {
        var presentation = _ownedPresentation
            ?? throw new InvalidOperationException("Owned gameplay UI presentation is unavailable.");
        button.AddThemeFontOverride("font", _ownedTitleFont!);
        button.AddThemeFontSizeOverride("font_size", _ownedTitleFont!.FixedSize);
        button.AddThemeColorOverride(
            "font_color",
            OwnedUiTheme.Brightness(
                presentation.SystemColor,
                presentation.Style.TextBrightness));
        button.AddThemeColorOverride(
            "font_hover_color",
            OwnedUiTheme.Brightness(
                presentation.SystemColor,
                presentation.Style.TextBrightness));
        button.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        button.AddThemeStyleboxOverride("focus", BuildOwnedFrameStyle());
        button.AddThemeStyleboxOverride("hover", BuildOwnedFrameStyle());
        button.AddThemeStyleboxOverride("pressed", BuildOwnedFrameStyle());
    }

    private StyleBoxFlat BuildOwnedFrameStyle()
    {
        var presentation = _ownedPresentation
            ?? throw new InvalidOperationException("Owned gameplay UI presentation is unavailable.");
        var lineWidth = Mathf.Max(1, Mathf.RoundToInt(presentation.Style.LineThicknessPixels));
        return new StyleBoxFlat
        {
            BgColor = OwnedUiTheme.Brightness(
                presentation.SystemColor,
                presentation.Style.BackgroundFillBrightness,
                presentation.Style.BackgroundFillAlpha),
            BorderColor = OwnedUiTheme.Brightness(
                presentation.SystemColor,
                presentation.Style.LineBrightness),
            BorderWidthLeft = lineWidth,
            BorderWidthTop = lineWidth,
            BorderWidthRight = lineWidth,
            BorderWidthBottom = lineWidth,
        };
    }

    private void AddOwnedTab(HBoxContainer parent, string label, GameplayUiPanel panel)
    {
        var button = new Button
        {
            Text = label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        ApplyOwnedButton(button);
        button.Pressed += () =>
        {
            _activePanel = panel;
            ApplyOwnedPipBoyRole();
            Refresh();
        };
        parent.AddChild(button);
    }

    private static void PlaceOwnedCanvasRect(Control control, Rect2 source)
    {
        control.Position = source.Position;
        control.Size = source.Size;
    }

    private void ApplyOwnedPipBoyRole()
    {
        if (_ownedPresentation is null || _ownedPipBoyScreen is null || _pipBoyContent is null)
            return;
        var (roleId, layoutRoleId, layoutTile) = _activePanel switch
        {
            GameplayUiPanel.Status => ("status", "items", "IM_MainRect"),
            GameplayUiPanel.Items => ("items", "items", "IM_MainRect"),
            GameplayUiPanel.Data or GameplayUiPanel.Map => ("data", "data", "MM_MainRect"),
            _ => ("status", "items", "IM_MainRect"),
        };
        var role = _ownedPresentation.Role(roleId);
        var layoutRole = _ownedPresentation.Role(layoutRoleId);
        PlaceOwnedCanvasRect(_ownedPipBoyScreen, layoutRole.Layout[layoutTile]);
        var showStatus = _activePanel == GameplayUiPanel.Status;
        _ownedPipBoyScreen.Visible = !showStatus;
        if (_ownedPipBoyStatusScreen is not null)
            _ownedPipBoyStatusScreen.Visible = showStatus;
        var font = OwnedUiTheme.BuildFont(_ownedPresentation.Font(role.BodyFontId));
        _pipBoyContent.AddThemeFontOverride("font", font);
        _pipBoyContent.AddThemeFontSizeOverride("font_size", font.FixedSize);
        UpdateOwnedPipBoyButtonGlows();
    }

    private void UpdateOwnedPipBoyButtonGlows()
    {
        foreach (var (panel, glow) in _ownedButtonGlows)
        {
            var selected = panel == _activePanel ||
                panel == GameplayUiPanel.Data && _activePanel == GameplayUiPanel.Map;
            glow.Mesh.SetSurfaceOverrideMaterial(
                glow.Surface,
                selected ? glow.AuthoredMaterial : glow.InactiveMaterial);
        }
    }

    private static void ScaleOwnedCanvas(Control viewport, Control canvas, Vector2 authoredSize)
    {
        if (viewport.Size.X <= 0.0f || viewport.Size.Y <= 0.0f)
            return;
        var scale = Mathf.Min(viewport.Size.X / authoredSize.X, viewport.Size.Y / authoredSize.Y);
        canvas.Scale = Vector2.One * scale;
        canvas.Position = (viewport.Size - authoredSize * scale) * OwnedUiTheme.CenteringFactor;
    }

    private void BuildDesktopHud()
    {
        _desktopHud = new Panel
        {
            Name = "GameplayHud",
            Position = _configuration.Hud.DesktopPanelPositionPixels.Vector2(),
            Size = _configuration.Hud.DesktopPanelSizePixels.Vector2(),
        };
        _desktopHud.AddThemeStyleboxOverride(
            "panel",
            BuildPanelStyle(_configuration.Hud.DesktopPanelColorRgba.Color()));
        AddChild(_desktopHud);
        var labels = new VBoxContainer
        {
            Position = _configuration.Hud.DesktopLabelsPositionPixels.Vector2() -
                _configuration.Hud.DesktopPanelPositionPixels.Vector2(),
            Size = _configuration.Hud.DesktopLabelsSizePixels.Vector2(),
        };
        _desktopHud.AddChild(labels);
        if (_useClassicDiorama)
        {
            var presentation = BuildLabel();
            presentation.Text = "CLASSIC DIORAMA  •  PRESENTATION PROOF";
            labels.AddChild(presentation);
        }
        _desktopObjective = BuildLabel();
        _desktopStatus = BuildLabel();
        _desktopInventory = BuildLabel();
        labels.AddChild(_desktopObjective);
        labels.AddChild(_desktopStatus);
        labels.AddChild(_desktopInventory);
        if (_useClassicDiorama)
            return;
        var crosshair = new Label
        {
            Text = "+",
            Position = _configuration.Hud.CrosshairPositionPixels.Vector2(),
        };
        crosshair.AddThemeColorOverride("font_color", Colors.White);
        crosshair.AddThemeFontSizeOverride("font_size", _configuration.Hud.CrosshairFontSizePixels);
        AddChild(crosshair);
    }

    private void BuildPipBoy()
    {
        _pipBoyPanel = new Panel
        {
            Name = "PipBoy",
            Position = _configuration.Hud.PipBoyPanelPositionPixels.Vector2(),
            Size = _configuration.Hud.PipBoyPanelSizePixels.Vector2(),
            Visible = false,
        };
        _pipBoyPanel.AddThemeStyleboxOverride(
            "panel",
            BuildPanelStyle(_configuration.Hud.DesktopPanelColorRgba.Color()));
        AddChild(_pipBoyPanel);
        BuildScreenSurface(_pipBoyPanel, out _, out var contentContainer);
        var tabBar = new HBoxContainer
        {
            Position = new Vector2(ScreenMarginPixels, TabBarOffsetPixels),
            Size = new Vector2(
                _configuration.Hud.PipBoyPanelSizePixels[0] - ScreenMarginPixels * 2.0f,
                TabBarHeightPixels),
        };
        _pipBoyPanel.AddChild(tabBar);
        AddTab(tabBar, _configuration.Hud.PipBoy.StatusTab, GameplayUiPanel.Status);
        AddTab(tabBar, _configuration.Hud.PipBoy.ItemsTab, GameplayUiPanel.Items);
        AddTab(tabBar, _configuration.Hud.PipBoy.DataTab, GameplayUiPanel.Data);
        AddTab(tabBar, _configuration.Hud.PipBoy.MapTab, GameplayUiPanel.Map);
        AddTab(tabBar, _configuration.Hud.PipBoy.ControlsTab, GameplayUiPanel.Controls);
        contentContainer.Position = new Vector2(ScreenMarginPixels, ContentOffsetPixels);
        contentContainer.Size = new Vector2(
            _configuration.Hud.PipBoyPanelSizePixels[0] - ScreenMarginPixels * 2.0f,
            _configuration.Hud.PipBoyPanelSizePixels[1] - ContentFooterOffsetPixels);
        _pipBoyContent = contentContainer.GetChild<Label>(0);
        _pipBoyFooter = new Label
        {
            Position = new Vector2(
                ScreenMarginPixels,
                _configuration.Hud.PipBoyPanelSizePixels[1] - FooterBaselineOffsetPixels),
        };
        ApplyTheme(_pipBoyFooter);
        _pipBoyPanel.AddChild(_pipBoyFooter);
    }

    private void BuildScreenSurface(
        Control parent,
        out Label? content,
        out VBoxContainer contentContainer)
    {
        var title = new Label
        {
            Name = "PipBoyTitle",
            Text = _ownedPresentation is null
                ? _configuration.Hud.PipBoy.Title
                : "STATS  •  ITEMS  •  DATA",
            Position = new Vector2(ScreenMarginPixels, ScreenTitleOffsetPixels),
        };
        ApplyTheme(title);
        title.AddThemeFontSizeOverride(
            "font_size",
            _configuration.Hud.DesktopFontSizePixels + (int)TitleFontSizeOffsetPixels);
        parent.AddChild(title);
        contentContainer = new VBoxContainer
        {
            Name = "PipBoyContent",
            Position = new Vector2(ScreenMarginPixels, ScreenContentOffsetPixels),
            Size = new Vector2(
                Mathf.Max(1.0f, parent.Size.X - ScreenMarginPixels * 2.0f),
                Mathf.Max(1.0f, parent.Size.Y - ScreenContentOffsetPixels - ScreenMarginPixels)),
        };
        content = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        ApplyTheme(content);
        contentContainer.AddChild(content);
        parent.AddChild(contentContainer);
    }

    private Label BuildLabel()
    {
        var label = new Label();
        ApplyTheme(label);
        return label;
    }

    private void ApplyTheme(Label label)
    {
        if (_ownedPresentation is not null)
        {
            label.AddThemeFontOverride("font", _ownedBodyFont!);
            label.AddThemeFontSizeOverride("font_size", _ownedBodyFont!.FixedSize);
            label.AddThemeColorOverride(
                "font_color",
                OwnedUiTheme.Brightness(
                    _ownedPresentation.SystemColor,
                    _ownedPresentation.Style.TextBrightness));
            return;
        }
        label.AddThemeColorOverride("font_color", _configuration.Hud.TextColorRgba.Color());
        label.AddThemeFontSizeOverride("font_size", _configuration.Hud.DesktopFontSizePixels);
    }

    private void AddTab(HBoxContainer parent, string label, GameplayUiPanel panel)
    {
        var button = new Button
        {
            Text = label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.Pressed += () =>
        {
            _activePanel = panel;
            Refresh();
        };
        parent.AddChild(button);
    }

    private static StyleBoxFlat BuildPanelStyle(Color color) => new()
    {
        BgColor = color,
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        BorderColor = color.Lightened(BorderLightenAmount),
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3,
        CornerRadiusBottomRight = 3,
    };

    private string FormatPanel(GameplayUiSnapshot snapshot, GameplayUiPanel panel)
    {
        if (_ownedPresentation is not null)
            return FormatOwnedPanel(snapshot, panel);
        return panel switch
        {
            GameplayUiPanel.Status => FormatStatus(snapshot),
            GameplayUiPanel.Items => FormatItems(snapshot),
            GameplayUiPanel.Data => FormatData(snapshot),
            GameplayUiPanel.Map => FormatMap(snapshot),
            GameplayUiPanel.Controls => FormatControls(snapshot),
            _ => throw new ArgumentOutOfRangeException(nameof(panel)),
        };
    }

    private string FormatOwnedPanel(GameplayUiSnapshot snapshot, GameplayUiPanel panel) => panel switch
    {
        GameplayUiPanel.Status => string.Join(
            "\n",
            string.IsNullOrWhiteSpace(snapshot.PlayerName) ? "COURIER" : snapshot.PlayerName,
            "",
            "EQUIPPED WEAPON",
            snapshot.EquippedWeaponLabel,
            "",
            "ACTIVE OBJECTIVE",
            DisplayObjective(snapshot.Objective)),
        GameplayUiPanel.Items => snapshot.Inventory.Count == 0
            ? "NO ITEMS"
            : string.Join(
                "\n",
                snapshot.Inventory.Select(item =>
                    $"{(item.Equipped ? ">" : " ")} {DisplayEditorId(item.EditorId)}  ({item.Count})")),
        GameplayUiPanel.Data => FormatOwnedData(snapshot),
        GameplayUiPanel.Map => FormatOwnedData(snapshot),
        GameplayUiPanel.Controls => FormatControls(snapshot),
        _ => throw new ArgumentOutOfRangeException(nameof(panel)),
    };

    private static string FormatOwnedData(GameplayUiSnapshot snapshot)
    {
        var objectives = snapshot.Objectives
            .Where(objective => objective.Enabled &&
                !objective.State.Equals("completed", StringComparison.OrdinalIgnoreCase))
            .Select(objective => objective.Text)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return objectives.Length == 0
            ? "QUESTS\nNO ACTIVE QUESTS"
            : "QUESTS\n" + string.Join("\n", objectives.Select(text => $"> {text}"));
    }

    private static string DisplayEditorId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "ITEM";
        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (index > 0 && char.IsUpper(current) && !char.IsUpper(value[index - 1]))
                result.Append(' ');
            result.Append(index == 0 ? char.ToUpperInvariant(current) : current);
        }
        return result.ToString();
    }

    private static string DisplayObjective(string value)
    {
        const string prefix = "OBJECTIVE ";
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;
    }

    private string FormatStatus(GameplayUiSnapshot snapshot) => string.Join(
        "\n",
        $"{snapshot.CellEditorId}  [{snapshot.CellFormId}]",
        string.IsNullOrWhiteSpace(snapshot.PlayerName)
            ? "Courier"
            : snapshot.PlayerName,
        $"Objective: {snapshot.Objective}",
        $"Weapon: {snapshot.EquippedWeaponLabel}  " +
        $"{snapshot.AmmoInMagazine}/{snapshot.WeaponClipSize}  +{snapshot.ReserveAmmo}",
        $"Status: {snapshot.Status}");

    private string FormatItems(GameplayUiSnapshot snapshot)
    {
        if (snapshot.Inventory.Count == 0)
            return _configuration.Hud.PipBoy.EmptyInventory;
        return string.Join(
            "\n",
            snapshot.Inventory.Select(item =>
                $"{(item.Equipped ? ">" : " ")} {item.EditorId} x{item.Count}  " +
                $"[{item.RecordType}]  {item.FormId}"));
    }

    private string FormatData(GameplayUiSnapshot snapshot)
    {
        var quests = snapshot.Quests.Count == 0
            ? _configuration.Hud.PipBoy.EmptyQuests
            : string.Join(
                "\n",
                snapshot.Quests.Select(quest =>
                    $"{quest.EditorId}  stage {quest.Stage}  " +
                    $"{(quest.Running ? "running" : quest.Stopped ? "stopped" : "inactive")}"));
        var objectives = snapshot.Objectives.Count == 0
            ? "No displayed objectives"
            : string.Join(
                "\n",
                snapshot.Objectives.Where(objective => objective.Enabled).Select(objective =>
                    $"[{objective.State}] {objective.Text}"));
        return $"QUESTS\n{quests}\n\nOBJECTIVES\n{objectives}\n\nMAP\n{FormatMap(snapshot)}";
    }

    private string FormatMap(GameplayUiSnapshot snapshot)
    {
        if (snapshot.MapMarkers.Count == 0)
            return _configuration.Hud.PipBoy.EmptyMap;
        var markers = snapshot.MapMarkers
            .OrderBy(marker => marker.Position.DistanceSquaredTo(snapshot.PlayerPosition))
            .Take(MaximumVisibleMapMarkers)
            .Select(marker =>
                $"{marker.EditorId}  {FormatVector(marker.Position)}  [{marker.FormId}]");
        return $"{snapshot.CellEditorId}\nPLAYER {FormatVector(snapshot.PlayerPosition)}\n" +
            $"AUTHORED REFERENCES {snapshot.MapMarkers.Count}\n\n" +
            string.Join("\n", markers);
    }

    private string FormatControls(GameplayUiSnapshot snapshot) => string.Join(
        "\n",
        snapshot.Controls.Select(control => $"{control.Label,-16} {control.Binding}"));

    private string FormatInventorySummary(GameplayUiSnapshot snapshot) =>
        _configuration.Hud.Copy.InventoryPrefix +
        (snapshot.Inventory.Count == 0
            ? _configuration.Hud.Copy.EmptyInventory
            : string.Join(
                " • ",
                snapshot.Inventory.Select(item => $"{item.EditorId} x{item.Count}")));

    private string FormatStatusLine(GameplayUiSnapshot snapshot)
    {
        var ammunition = snapshot.EquippedWeaponFormId is null
            ? "--/--"
            : $"{snapshot.AmmoInMagazine}/{snapshot.WeaponClipSize}";
        return $"{snapshot.EquippedWeaponLabel} {ammunition} +{snapshot.ReserveAmmo}   {snapshot.Status}";
    }

    private string FormatWrist(GameplayUiSnapshot snapshot) => string.Join(
        "\n",
        _ownedPresentation is null ? _configuration.Hud.PipBoy.Title : "STATS",
        FormatPanel(snapshot, _activePanel));

    private static string FormatVector(Vector3 value) =>
        $"({value.X.ToString("0.0", CultureInfo.InvariantCulture)}, " +
        $"{value.Y.ToString("0.0", CultureInfo.InvariantCulture)}, " +
        $"{value.Z.ToString("0.0", CultureInfo.InvariantCulture)})";

    private Vector2I ScreenSizePixels() => new(
        Mathf.Max(1, Mathf.RoundToInt(_configuration.Hud.PipBoyPanelSizePixels[0])),
        Mathf.Max(1, Mathf.RoundToInt(_configuration.Hud.PipBoyPanelSizePixels[1])));

    private static Vector2? ResolveWristPointer(Sprite3D screen, Node3D aim)
    {
        var normal = screen.GlobalBasis.Z.Normalized();
        var direction = (-aim.GlobalBasis.Z).Normalized();
        var denominator = direction.Dot(normal);
        if (denominator >= -PlaneParallelEpsilon)
            return null;
        var distance = (screen.GlobalPosition - aim.GlobalPosition).Dot(normal) / denominator;
        if (distance <= 0.0f)
            return null;
        var hit = aim.GlobalPosition + direction * distance;
        var local = screen.GlobalTransform.AffineInverse() * hit;
        var width = screen.Texture?.GetWidth() ?? 0;
        var height = screen.Texture?.GetHeight() ?? 0;
        if (width <= 0 || height <= 0 || screen.PixelSize <= 0.0f ||
            Mathf.Abs(local.X) > width * screen.PixelSize * PlaneHalfExtent ||
            Mathf.Abs(local.Y) > height * screen.PixelSize * PlaneHalfExtent)
            return null;
        return new Vector2(
            width * PlaneHalfExtent + local.X / screen.PixelSize,
            height * PlaneHalfExtent - local.Y / screen.PixelSize);
    }

    private void SetWristState(WristUiState state, string reason)
    {
        if (_wristState == state)
            return;
        _wristState = state;
        GD.Print($"OPENNV_WRIST_UI_STATE state={state.ToString().ToLowerInvariant()} reason={reason}");
    }

    private enum WristUiState
    {
        Dormant,
        Candidate,
        Focused,
        Active,
    }
}
