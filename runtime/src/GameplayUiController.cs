using System.Globalization;
using Godot;

namespace OpenNV.Runtime;

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
    private bool _useXr;
    private bool _showHud;
    private bool _useClassicDiorama;
    private GameplayUiPanel _activePanel = GameplayUiPanel.Status;
    private Panel? _desktopHud;
    private Label? _desktopObjective;
    private Label? _desktopStatus;
    private Label? _desktopInventory;
    private Control? _pipBoyPanel;
    private Label? _pipBoyContent;
    private Label? _pipBoyFooter;
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
    internal bool IsPipBoyOpen => _pipBoyPanel?.Visible == true;

    internal void Configure(
        GameplaySession session,
        RuntimeConfiguration configuration,
        bool useXr,
        bool showHud,
        bool useClassicDiorama)
    {
        _session = session;
        _configuration = configuration;
        _useXr = useXr;
        _showHud = showHud;
        _useClassicDiorama = useClassicDiorama;
        Name = "GameplayUi";
        if (_showHud && !_useXr)
            BuildDesktopHud();
        if (_showHud)
            BuildPipBoy();
        Refresh();
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
        };
        mount.AddChild(_xrScreen);
        SetWristState(WristUiState.Active, "attached");
        Refresh();
    }

    internal void TogglePipBoy()
    {
        if (_pipBoyPanel is null)
            return;
        _pipBoyPanel.Visible = !_pipBoyPanel.Visible;
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
        if (_pipBoyPanel?.Visible != true)
            return;
        _pipBoyPanel.Visible = false;
        if (!_useXr && DisplayServer.GetName() != "headless")
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    internal void Refresh()
    {
        if (_session is null)
            return;
        var snapshot = _session.BuildUiSnapshot();
        if (_desktopObjective is not null)
        {
            _desktopObjective.Text = snapshot.Objective;
            _desktopStatus!.Text = FormatStatusLine(snapshot);
            _desktopInventory!.Text = FormatInventorySummary(snapshot);
        }
        if (_pipBoyContent is not null)
            _pipBoyContent.Text = FormatPanel(snapshot, _activePanel);
        if (_pipBoyFooter is not null)
            _pipBoyFooter.Text = _configuration.Hud.PipBoy.CloseHint;
        if (_xrContent is not null)
            _xrContent.Text = FormatWrist(snapshot);
    }

    public override void _Process(double delta)
    {
        if (_xrScreen is null || _xrAim is null || !_xrAim.IsInsideTree())
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
            Text = _configuration.Hud.PipBoy.Title,
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

    private string FormatPanel(GameplayUiSnapshot snapshot, GameplayUiPanel panel) => panel switch
    {
        GameplayUiPanel.Status => FormatStatus(snapshot),
        GameplayUiPanel.Items => FormatItems(snapshot),
        GameplayUiPanel.Data => FormatData(snapshot),
        GameplayUiPanel.Map => FormatMap(snapshot),
        GameplayUiPanel.Controls => FormatControls(snapshot),
        _ => throw new ArgumentOutOfRangeException(nameof(panel)),
    };

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
        return $"QUESTS\n{quests}\n\nOBJECTIVES\n{objectives}";
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
        _configuration.Hud.PipBoy.Title,
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
