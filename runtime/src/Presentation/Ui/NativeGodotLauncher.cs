using System.Text.Json;
using Godot;
using OpenNV.Runtime.Campaigns.Classic.Native;
using OpenNV.Runtime.Campaigns.Fallout2.Native;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeGodotLauncherPresentation(
    bool Launchable,
    bool PreviewOnly,
    string Status);

internal sealed record NativeGodotLauncherCampaign(
    string Id,
    string EngineCampaign,
    string Title,
    bool Launchable,
    string Status,
    string DefaultPresentation,
    IReadOnlyDictionary<string, NativeGodotLauncherPresentation> Presentations);

internal sealed record NativeGodotLauncherLaunchRequest(
    string CampaignId,
    string EngineCampaign,
    string Presentation,
    string DataRoot,
    string SavePath,
    string? AppearanceDataRoot,
    string? Fallout3WorldRoot);

/// <summary>
/// Godot-native product entry point. The launcher deliberately consumes the
/// same runtime manifest and profile identities as the command-line path, but
/// never claims that a preview, JAM profile, or TTW registration is gameplay.
/// </summary>
internal sealed partial class NativeGodotLauncher : Control
{
    private static readonly IReadOnlyList<string> StandaloneIds =
        ["fallout1", "fallout2", "newvegas", "fallout3"];
    private static readonly IReadOnlyDictionary<string, (string Title, string Engine)> FallbackCampaigns =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["fallout1"] = ("Fallout 1", "fallout-1"),
            ["fallout2"] = ("Fallout 2", "fallout-2"),
            ["newvegas"] = ("New Vegas", "fallout-new-vegas"),
            ["fallout3"] = ("Fallout 3", "fallout-3"),
        };

    private IReadOnlyList<NativeGodotLauncherCampaign> _campaigns = [];
    private GodotLauncherProfileStore _profiles = null!;
    private string _selectedId = "fallout1";
    private string _selectedPresentation = "hex-tactical";
    private Color _accent = new(0.42f, 0.86f, 0.66f);
    private readonly Font _terminalFont = new SystemFont { FontNames = ["Consolas"] };
    private NativeGodotLauncherSurface _surface = null!;
    private NativeGodotLauncherPreview _preview = null!;
    private VBoxContainer _campaignList = null!;
    private HBoxContainer _presentationList = null!;
    private Label _selectionTitle = null!;
    private Label _selectionStatus = null!;
    private Label _selectionDetail = null!;
    private Label _profileStatus = null!;
    private Label _extensionStatus = null!;
    private Button _chooseInstall = null!;
    private Button _launch = null!;
    private Label _toast = null!;
    private FileDialog? _fileDialog;
    private IFalloutClassicOwnedSource? _classicSource;
    private ClassicArtCache? _classicArt;
    private Texture2D? _classicPreviewFrame;
    private string? _classicSourceKey;
    private bool _configured;
    private bool _launching;
    private static bool XrAvailable => XRServer.FindInterface("OpenXR")?.IsInitialized() == true;

    internal event Action<NativeGodotLauncherLaunchRequest>? LaunchRequested;

    internal void Configure()
    {
        if (_configured)
            return;
        _profiles = GodotLauncherProfileStore.Load();
        _campaigns = LoadManifest();
        _configured = true;
    }

    public override void _Ready()
    {
        Configure();
        Name = "OpenNVGodotLauncher";
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ProcessMode = ProcessModeEnum.Always;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetWindow().MinSize = new Vector2I(1060, 700);

        BuildBackground();
        BuildSurface();
        BuildInterface();
        Refresh();
    }

    public override void _ExitTree()
    {
        ReleaseClassicSource();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.PhysicalKeycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            if (_fileDialog is not null)
                CloseFileDialog();
            else
                GetTree().Quit();
        }
        else if (key.PhysicalKeycode == Key.F1)
        {
            GetViewport().SetInputAsHandled();
            LaunchSelected();
        }
        else if (key.PhysicalKeycode == Key.F2)
        {
            GetViewport().SetInputAsHandled();
            ShowToast("LOAD // save browser is owned by the selected campaign.");
        }
        else if (key.PhysicalKeycode == Key.F3)
        {
            GetViewport().SetInputAsHandled();
            ShowToast("OPTIONS // presentation route is selected above.");
        }
        else if (key.PhysicalKeycode == Key.F4)
        {
            GetViewport().SetInputAsHandled();
            ChooseInstallation();
        }
        else if (key.PhysicalKeycode is Key.Enter or Key.KpEnter && !_launch.Disabled)
        {
            GetViewport().SetInputAsHandled();
            LaunchSelected();
        }
    }

    internal void ReportLaunchFailure(string message)
    {
        _launching = false;
        _toast.Text = message;
        _toast.AddThemeColorOverride("font_color", new Color(0.86f, 0.61f, 0.33f));
        Refresh();
    }

    private void BuildBackground()
    {
        var background = new ColorRect
        {
            Name = "LauncherBackdrop",
            Color = new Color(0.012f, 0.015f, 0.014f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
    }

    private void BuildSurface()
    {
        _surface = new NativeGodotLauncherSurface();
        _surface.Configure(_accent);
        AddChild(_surface);
    }

    private void BuildInterface()
    {
        var margin = new MarginContainer { Name = "LauncherInterface" };
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 82);
        margin.AddThemeConstantOverride("margin_top", 54);
        margin.AddThemeConstantOverride("margin_right", 82);
        margin.AddThemeConstantOverride("margin_bottom", 48);
        AddChild(margin);

        var stack = new VBoxContainer { Name = "LauncherStack" };
        stack.AddThemeConstantOverride("separation", 8);
        margin.AddChild(stack);

        stack.AddChild(BuildHeader());

        var main = new HBoxContainer
        {
            Name = "LauncherMain",
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        main.AddThemeConstantOverride("separation", 10);
        stack.AddChild(main);
        main.AddChild(BuildCampaignPanel());
        main.AddChild(BuildPreviewPanel());
        main.AddChild(BuildDetailPanel());

        stack.AddChild(BuildFunctionRail());
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer
        {
            Name = "LauncherHeader",
            CustomMinimumSize = new Vector2(0, 48),
        };
        header.AddThemeConstantOverride("separation", 10);

        var brand = new VBoxContainer();
        var title = Label("OPEN NEVADA", 24, _accent);
        brand.AddChild(title);
        brand.AddChild(Label("VAULT-TEC PERSONNEL TERMINAL  /  SELECT A WORLD", 10,
            new Color(0.62f, 0.75f, 0.66f)));
        header.AddChild(brand);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(spacer);
        var runtime = new Label
        {
            Name = "GodotRuntimeBadge",
            Text = "RUNTIME // GODOT 4  /  IN-PROCESS",
            VerticalAlignment = VerticalAlignment.Center,
        };
        runtime.AddThemeFontOverride("font", _terminalFont);
        runtime.AddThemeColorOverride("font_color", new Color(0.73f, 0.61f, 0.34f));
        runtime.AddThemeFontSizeOverride("font_size", 10);
        header.AddChild(runtime);
        return header;
    }

    private Control BuildCampaignPanel()
    {
        var panel = Panel("Worlds", new Vector2(236, 0));
        var stack = new VBoxContainer { Name = "CampaignPanelStack" };
        stack.AddThemeConstantOverride("separation", 6);
        panel.AddChild(stack);
        stack.AddChild(Label("WORLD FILES", 15, _accent));
        stack.AddChild(Label("SOURCE ROUTES / OWNED DATA", 9, new Color(0.56f, 0.68f, 0.6f)));
        _campaignList = new VBoxContainer
        {
            Name = "CampaignCards",
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _campaignList.AddThemeConstantOverride("separation", 5);
        stack.AddChild(_campaignList);
        stack.AddChild(Label("SELECT A FILE TO LOAD ITS PROFILE", 9, new Color(0.45f, 0.57f, 0.5f)));
        return panel;
    }

    private Control BuildPreviewPanel()
    {
        var panel = Panel("SourcePreviewPanel", new Vector2(276, 0));
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _preview = new NativeGodotLauncherPreview();
        _preview.Configure(_accent);
        panel.AddChild(_preview);
        return panel;
    }

    private Control BuildDetailPanel()
    {
        var panel = Panel("RouteDetails", Vector2.Zero);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var stack = new VBoxContainer { Name = "RouteDetailsStack" };
        stack.AddThemeConstantOverride("separation", 7);
        panel.AddChild(stack);

        _selectionTitle = Label(string.Empty, 22, _accent);
        stack.AddChild(_selectionTitle);
        _selectionStatus = Label(string.Empty, 10, new Color(0.83f, 0.67f, 0.35f));
        stack.AddChild(_selectionStatus);
        _selectionDetail = Label(string.Empty, 11, new Color(0.74f, 0.83f, 0.75f));
        _selectionDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _selectionDetail.CustomMinimumSize = new Vector2(0, 40);
        stack.AddChild(_selectionDetail);

        stack.AddChild(Label("PRESENTATION ROUTE", 10, new Color(0.47f, 0.67f, 0.58f)));
        _presentationList = new HBoxContainer { Name = "PresentationModes" };
        _presentationList.AddThemeConstantOverride("separation", 5);
        stack.AddChild(_presentationList);

        stack.AddChild(Label("OWNED INSTALLATION", 10, new Color(0.47f, 0.67f, 0.58f)));
        _profileStatus = Label(string.Empty, 10, new Color(0.74f, 0.83f, 0.75f));
        _profileStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _profileStatus.CustomMinimumSize = new Vector2(0, 42);
        stack.AddChild(_profileStatus);
        _chooseInstall = ActionButton("[F4]  CHOOSE OWNED INSTALL", 34);
        _chooseInstall.Pressed += ChooseInstallation;
        stack.AddChild(_chooseInstall);

        var divider = new HSeparator { Name = "LauncherDivider" };
        stack.AddChild(divider);
        stack.AddChild(Label("COMPATIBILITY REALITY", 10, new Color(0.47f, 0.67f, 0.58f)));
        _extensionStatus = Label(string.Empty, 10, new Color(0.82f, 0.69f, 0.39f));
        _extensionStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _extensionStatus.CustomMinimumSize = new Vector2(0, 54);
        stack.AddChild(_extensionStatus);

        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddChild(spacer);
        _toast = Label(string.Empty, 10, new Color(0.93f, 0.76f, 0.4f));
        _toast.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_toast);
        _launch = ActionButton("[F1]  OPEN SELECTED WORLD", 42);
        _launch.Pressed += LaunchSelected;
        stack.AddChild(_launch);
        return panel;
    }

    private Control BuildFunctionRail()
    {
        var rail = new HBoxContainer
        {
            Name = "FunctionRail",
            CustomMinimumSize = new Vector2(0, 30),
        };
        rail.AddThemeConstantOverride("separation", 5);
        rail.AddChild(FunctionButton("[F1]  NEW GAME", LaunchSelected));
        rail.AddChild(FunctionButton("[F2]  LOAD", () => ShowToast("LOAD // save browser is owned by the selected campaign.")));
        rail.AddChild(FunctionButton("[F3]  OPTIONS", () => ShowToast("OPTIONS // presentation route is selected above.")));
        rail.AddChild(FunctionButton("[F4]  INSTALL", ChooseInstallation));
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rail.AddChild(spacer);
        var footer = Label("OWNED FILES STAY LOCAL  //  ESC EXIT", 9, new Color(0.45f, 0.57f, 0.5f));
        footer.VerticalAlignment = VerticalAlignment.Center;
        rail.AddChild(footer);
        return rail;
    }

    private void Refresh()
    {
        var campaign = CurrentCampaign();
        if (!campaign.Presentations.ContainsKey(_selectedPresentation))
            _selectedPresentation = campaign.DefaultPresentation;
        _accent = AccentFor(campaign.Id);
        _surface.SetAccent(_accent);
        _preview.SetAccent(_accent);
        RefreshSourcePreview(campaign);
        RefreshCampaignCards();
        RefreshPresentationModes(campaign);
        RefreshDetails(campaign);
    }

    private void RefreshSourcePreview(NativeGodotLauncherCampaign campaign)
    {
        if (campaign.Id is not ("fallout1" or "fallout2") ||
            !TryGetValidatedProfile(campaign.Id, out var profile, out _) || profile is null)
        {
            ReleaseClassicSource();
            _preview.SetSourceFrame(null,
                "NO OWNED CLASSIC ART LOADED  //  SELECT AN INSTALL TO OPEN THE ORIGINAL SCREEN",
                "SOURCE PROFILE  /  NOT REGISTERED");
            return;
        }

        var sourceKey = campaign.Id + "|" + profile.InstallRoot;
        if (!string.Equals(_classicSourceKey, sourceKey, StringComparison.OrdinalIgnoreCase))
        {
            ReleaseClassicSource();
            try
            {
                _classicSource = campaign.Id == "fallout1"
                    ? Fallout1OwnedContentSource.LoadInstall(profile.InstallRoot)
                    : Fo2NativeOwnedSource.LoadInstall(profile.InstallRoot);
                _classicArt = new ClassicArtCache(path => _classicSource.Read(path, out _));
                _classicSourceKey = sourceKey;
                _classicPreviewFrame = TryReadClassicFrame();
            }
            catch (Exception exception)
            {
                ReleaseClassicSource();
                GD.PushWarning($"Classic launcher art unavailable: {exception.Message}");
            }
        }

        _preview.SetSourceFrame(
            _classicPreviewFrame,
            _classicPreviewFrame is null
                ? "ORIGINAL ART NOT FOUND  //  NATIVE TERMINAL FALLBACK"
                : "ORIGINAL SOURCE ART  //  READ ONLY  //  DECODED FROM OWNED INSTALL",
            _classicPreviewFrame is null
                ? $"{campaign.Title.ToUpperInvariant()}  /  SOURCE PROFILE"
                : $"{campaign.Title.ToUpperInvariant()}  /  ORIGINAL CHARACTER SCREEN");
    }

    private Texture2D? TryReadClassicFrame()
    {
        if (_classicArt is null)
            return null;
        foreach (var path in new[] { "art/intrface/pickchar.frm", "art/intrface/mainmenu.frm" })
        {
            try
            {
                return _classicArt.Frame(path).Texture;
            }
            catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
                // The source reader remains authoritative; a missing optional
                // art member only selects the native terminal fallback.
            }
        }
        return null;
    }

    private void ReleaseClassicSource()
    {
        _classicSource?.Dispose();
        _classicSource = null;
        _classicArt = null;
        _classicPreviewFrame = null;
        _classicSourceKey = null;
    }

    private void RefreshCampaignCards()
    {
        foreach (var child in _campaignList.GetChildren().ToArray())
            child.QueueFree();
        foreach (var id in StandaloneIds)
        {
            var campaign = _campaigns.FirstOrDefault(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (campaign is null)
                continue;
            var profileReady = TryGetValidatedProfile(campaign.Id, out _, out var profileMessage);
            var route = campaign.Presentations.TryGetValue(campaign.DefaultPresentation, out var defaultRoute)
                ? defaultRoute
                : null;
            var button = new Button
            {
                Name = $"Campaign_{campaign.Id}",
                Text = $"{campaign.Title.ToUpperInvariant()}\n{RouteLabel(campaign.DefaultPresentation)}  //  " +
                    (route?.PreviewOnly == true ? "SOURCE" : campaign.Launchable ? "READY" : "LOCKED"),
                CustomMinimumSize = new Vector2(0, 58),
                Alignment = HorizontalAlignment.Left,
                Disabled = false,
            };
            button.AddThemeFontOverride("font", _terminalFont);
            button.AddThemeFontSizeOverride("font_size", 12);
            button.AddThemeColorOverride("font_color", campaign.Id.Equals(_selectedId, StringComparison.OrdinalIgnoreCase)
                ? AccentFor(campaign.Id).Lightened(0.25f)
                : new Color(0.75f, 0.84f, 0.82f));
            button.AddThemeColorOverride("font_hover_color", AccentFor(campaign.Id).Lightened(0.35f));
            button.AddThemeStyleboxOverride("normal", CardStyle(campaign.Id.Equals(_selectedId, StringComparison.OrdinalIgnoreCase)));
            button.AddThemeStyleboxOverride("hover", CardStyle(true));
            button.AddThemeStyleboxOverride("pressed", CardStyle(true));
            button.TooltipText = profileReady ? profileMessage : "Installation not registered: " + profileMessage;
            button.Pressed += () =>
            {
                _selectedId = campaign.Id;
                _selectedPresentation = campaign.DefaultPresentation;
                _toast.Text = string.Empty;
                Refresh();
            };
            _campaignList.AddChild(button);
        }
    }

    private void RefreshPresentationModes(NativeGodotLauncherCampaign campaign)
    {
        foreach (var child in _presentationList.GetChildren().ToArray())
            child.QueueFree();
        foreach (var mode in new[] { "hex-tactical", "first-person", "openxr" })
        {
            campaign.Presentations.TryGetValue(mode, out var presentation);
            var button = new Button
            {
                Name = $"Presentation_{mode}",
                Text = RouteLabel(mode),
                CustomMinimumSize = new Vector2(76, 29),
                Disabled = presentation is null || !presentation.Launchable || mode == "openxr" && !XrAvailable,
                TooltipText = mode == "openxr" && !XrAvailable
                    ? "Start OpenNV VR with your headset runtime active to enable this mode."
                    : presentation?.Status ?? "This presentation is not declared by the runtime.",
            };
            button.AddThemeFontOverride("font", _terminalFont);
            button.AddThemeFontSizeOverride("font_size", 10);
            button.AddThemeColorOverride("font_color", _selectedPresentation == mode ? _accent : new Color(0.68f, 0.78f, 0.77f));
            button.AddThemeStyleboxOverride("normal", SmallStyle(_selectedPresentation == mode));
            button.AddThemeStyleboxOverride("hover", SmallStyle(true));
            button.AddThemeStyleboxOverride("pressed", SmallStyle(true));
            button.Pressed += () =>
            {
                _selectedPresentation = mode;
                Refresh();
            };
            _presentationList.AddChild(button);
        }
    }

    private void RefreshDetails(NativeGodotLauncherCampaign campaign)
    {
        var profileReady = TryGetValidatedProfile(campaign.Id, out var profile, out var profileMessage);
        var presentation = campaign.Presentations.GetValueOrDefault(_selectedPresentation);
        var routeReady = campaign.Launchable && presentation?.Launchable == true && profileReady &&
            (_selectedPresentation != "openxr" || XrAvailable);
        _selectionTitle.Text = campaign.Title.ToUpperInvariant();
        _selectionTitle.AddThemeColorOverride("font_color", _accent);
        _selectionStatus.Text = campaign.Launchable
            ? presentation?.PreviewOnly == true ? "SOURCE ROUTE  //  PREVIEW ONLY" : "SOURCE ROUTE  //  LAUNCHABLE SLICE"
            : "SOURCE ROUTE  //  NOT LAUNCHABLE";
        _selectionDetail.Text = campaign.Status;
        _profileStatus.Text = profileReady
            ? $"READY\n{profile!.InstallRoot}\nSave: {profile.SavePath}"
            : profileMessage;
        _profileStatus.AddThemeColorOverride("font_color", profileReady
            ? new Color(0.46f, 0.83f, 0.61f)
            : new Color(0.83f, 0.67f, 0.35f));

        var ttw = FindExtension("ttw");
        var jam = FindExtension("jam");
        _extensionStatus.Text =
            $"TTW  //  {(ttw?.Launchable == true ? "READY" : "PROFILE/STACK ONLY — DIRECT RUNTIME NOT IMPLEMENTED")}\n" +
            $"JAM  //  {(jam?.Launchable == true ? "READY" : "PROFILE/BOUNDED SEMANTICS ONLY — COMPLETE PLUGIN SEMANTICS NOT IMPLEMENTED")}";
        _extensionStatus.AddThemeColorOverride("font_color", new Color(0.85f, 0.68f, 0.36f));

        _chooseInstall.Text = profileReady ? "[F4]  CHANGE OWNED INSTALL" : "[F4]  CHOOSE OWNED INSTALL";
        _launch.Disabled = !routeReady || _launching;
        _launch.Text = routeReady
            ? presentation?.PreviewOnly == true ? "[F1]  OPEN SOURCE PREVIEW" : $"[F1]  PLAY {campaign.Title.ToUpperInvariant()}"
            : "[F1]  ROUTE NOT READY";
        _launch.AddThemeColorOverride("font_color", routeReady ? new Color(0.02f, 0.06f, 0.05f) : new Color(0.44f, 0.52f, 0.52f));
        _launch.AddThemeStyleboxOverride("normal", ActionStyle(routeReady));
        _launch.AddThemeStyleboxOverride("hover", ActionStyle(routeReady, true));
        _launch.AddThemeStyleboxOverride("pressed", ActionStyle(routeReady, true));
    }

    private Button FunctionButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(108, 29),
            Alignment = HorizontalAlignment.Left,
            FocusMode = Control.FocusModeEnum.All,
        };
        button.AddThemeFontOverride("font", _terminalFont);
        button.AddThemeFontSizeOverride("font_size", 9);
        button.AddThemeColorOverride("font_color", new Color(0.59f, 0.71f, 0.62f));
        button.AddThemeColorOverride("font_hover_color", _accent.Lightened(0.25f));
        button.AddThemeStyleboxOverride("normal", RailStyle());
        button.AddThemeStyleboxOverride("hover", RailStyle(true));
        button.AddThemeStyleboxOverride("pressed", RailStyle(true));
        button.Pressed += action;
        return button;
    }

    private void ShowToast(string message)
    {
        _toast.Text = message;
        _toast.AddThemeColorOverride("font_color", new Color(0.84f, 0.7f, 0.39f));
    }

    private void ChooseInstallation()
    {
        if (_fileDialog is not null)
            return;
        _fileDialog = new FileDialog
        {
            Name = "OwnedInstallationPicker",
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = $"Choose owned {CurrentCampaign().Title} installation",
        };
        _fileDialog.DirSelected += OnDirectorySelected;
        _fileDialog.Canceled += CloseFileDialog;
        AddChild(_fileDialog);
        _fileDialog.PopupCentered(new Vector2I(900, 600));
    }

    private void OnDirectorySelected(string path)
    {
        try
        {
            var campaign = CurrentCampaign();
            var installation = NativeGameInstallation.Detect(path);
            var expected = ExpectedNativeGame(campaign.Id);
            if (installation.Game != expected)
                throw new InvalidDataException(
                    $"That folder is {installation.Game}, but {campaign.Title} needs {expected}.");
            _profiles.Save(campaign.Id, installation.InstallRoot);
            _toast.Text = $"{campaign.Title} registered from live files.";
            _toast.AddThemeColorOverride("font_color", new Color(0.47f, 0.84f, 0.62f));
        }
        catch (Exception exception)
        {
            _toast.Text = exception.Message;
            _toast.AddThemeColorOverride("font_color", new Color(0.86f, 0.61f, 0.33f));
        }
        finally
        {
            CloseFileDialog();
            Refresh();
        }
    }

    private void CloseFileDialog()
    {
        if (_fileDialog is null)
            return;
        _fileDialog.QueueFree();
        _fileDialog = null;
    }

    private void LaunchSelected()
    {
        if (_launching)
            return;
        var campaign = CurrentCampaign();
        if (!TryGetValidatedProfile(campaign.Id, out var profile, out var message) || profile is null)
        {
            _toast.Text = message;
            return;
        }
        var route = campaign.Presentations.GetValueOrDefault(_selectedPresentation);
        if (_selectedPresentation == "openxr" && !XrAvailable)
        {
            _toast.Text = "Start OpenNV VR with your headset runtime active.";
            return;
        }
        if (!campaign.Launchable || route?.Launchable != true)
        {
            _toast.Text = route?.Status ?? campaign.Status;
            return;
        }

        try
        {
            _launching = true;
            var request = new NativeGodotLauncherLaunchRequest(
                campaign.Id,
                campaign.EngineCampaign,
                _selectedPresentation,
                profile.InstallRoot,
                profile.SavePath,
                ValidProfileRoot("newvegas"),
                ValidProfileRoot("fallout3"));
            GD.Print($"OPENNV_GODOT_LAUNCH_REQUEST campaign={campaign.Id} presentation={_selectedPresentation} " +
                $"source={profile.InstallRoot} save={profile.SavePath} mode=in-process");
            LaunchRequested?.Invoke(request);
        }
        catch (Exception exception)
        {
            _toast.Text = exception.Message;
            _launching = false;
        }
    }

    private string? ValidProfileRoot(string campaignId)
    {
        return TryGetValidatedProfile(campaignId, out var profile, out _) && profile is not null
            ? profile.InstallRoot
            : null;
    }

    private bool TryGetValidatedProfile(string campaignId, out GodotLauncherProfile? profile, out string message)
    {
        profile = null;
        if (!_profiles.TryGet(campaignId, out var stored))
        {
            message = "No owned installation is registered for this world.";
            return false;
        }
        try
        {
            var installation = NativeGameInstallation.Detect(stored.InstallRoot);
            if (installation.Game != ExpectedNativeGame(campaignId))
                throw new InvalidDataException($"Registered folder is {installation.Game}, not {campaignId}.");
            profile = stored;
            message = $"{installation.ContentRoot} is present and matches {campaignId}.";
            return true;
        }
        catch (Exception exception)
        {
            message = $"Registered installation is unavailable: {exception.Message}";
            return false;
        }
    }

    private NativeGodotLauncherCampaign CurrentCampaign() =>
        _campaigns.FirstOrDefault(row => row.Id.Equals(_selectedId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Launcher campaign is missing: {_selectedId}.");

    private NativeGodotLauncherCampaign? FindExtension(string id) =>
        _campaigns.FirstOrDefault(row => row.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static NativeGame ExpectedNativeGame(string id) => id switch
    {
        "fallout1" => NativeGame.Fallout1,
        "fallout2" => NativeGame.Fallout2,
        "fallout3" => NativeGame.Fallout3,
        "newvegas" => NativeGame.FalloutNewVegas,
        _ => throw new ArgumentException($"No native game maps to launcher campaign {id}.", nameof(id)),
    };

    private static IReadOnlyList<NativeGodotLauncherCampaign> LoadManifest()
    {
        var json = Godot.FileAccess.GetFileAsString("res://runtime-manifest.json");
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("OpenNV runtime manifest is missing from the Godot product.");
        using var document = JsonDocument.Parse(json);
        var result = new List<NativeGodotLauncherCampaign>();
        foreach (var row in document.RootElement.GetProperty("campaigns").EnumerateArray())
        {
            var rawId = row.GetProperty("id").GetString() ?? throw new InvalidDataException("Campaign id is empty.");
            var id = rawId.ToLowerInvariant();
            if (!FallbackCampaigns.TryGetValue(id, out var fallback) && id is not "ttw" and not "jam")
                continue;
            var title = row.TryGetProperty("title", out var titleProperty)
                ? titleProperty.GetString() ?? fallback.Title
                : id is "ttw" ? "TTW" : id is "jam" ? "JAM" : fallback.Title;
            var engine = id switch
            {
                "ttw" => "TTW",
                "jam" => "JAM",
                _ => fallback.Engine,
            };
            var presentations = new Dictionary<string, NativeGodotLauncherPresentation>(StringComparer.OrdinalIgnoreCase);
            if (row.TryGetProperty("presentations", out var routes) && routes.ValueKind == JsonValueKind.Object)
            {
                foreach (var route in routes.EnumerateObject())
                {
                    var launchable = route.Value.TryGetProperty("launchable", out var launchableProperty) &&
                        launchableProperty.GetBoolean();
                    var preview = route.Value.TryGetProperty("previewOnly", out var previewProperty) &&
                        previewProperty.GetBoolean();
                    var status = route.Value.TryGetProperty("status", out var statusProperty)
                        ? statusProperty.GetString() ?? "No route status supplied."
                        : "No route status supplied.";
                    presentations[route.Name] = new NativeGodotLauncherPresentation(launchable, preview, status);
                }
            }
            var defaultPresentation = id is "fallout1" or "fallout2" ? "hex-tactical" : "first-person";
            result.Add(new NativeGodotLauncherCampaign(
                id,
                engine,
                title,
                row.TryGetProperty("launchable", out var canLaunch) && canLaunch.GetBoolean(),
                row.TryGetProperty("status", out var campaignStatus)
                    ? campaignStatus.GetString() ?? "No campaign status supplied."
                    : "No campaign status supplied.",
                defaultPresentation,
                presentations));
        }
        foreach (var required in FallbackCampaigns.Keys)
            if (result.All(row => !row.Id.Equals(required, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Runtime manifest omitted standalone campaign {required}.");
        return result;
    }

    private static string RouteLabel(string mode) => mode switch
    {
        "hex-tactical" => "HEX",
        "first-person" => "FPS",
        "openxr" => "XR",
        _ => mode.ToUpperInvariant(),
    };

    private static Color AccentFor(string id) => id switch
    {
        "fallout1" => new Color(0.42f, 0.86f, 0.66f),
        "fallout2" => new Color(0.88f, 0.57f, 0.29f),
        "newvegas" => new Color(0.91f, 0.69f, 0.31f),
        "fallout3" => new Color(0.67f, 0.82f, 0.83f),
        _ => new Color(0.42f, 0.86f, 0.66f),
    };

    private static PanelContainer Panel(string name, Vector2 minimum)
    {
        var panel = new PanelContainer
        {
            Name = name,
            CustomMinimumSize = minimum,
        };
        panel.AddThemeStyleboxOverride("panel", PanelStyle());
        return panel;
    }

    private static StyleBoxFlat PanelStyle() => new()
    {
        BgColor = new Color(0.018f, 0.044f, 0.034f, 0.96f),
        BorderColor = new Color(0.26f, 0.42f, 0.31f, 0.95f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        ContentMarginLeft = 12,
        ContentMarginTop = 11,
        ContentMarginRight = 12,
        ContentMarginBottom = 11,
    };

    private static StyleBoxFlat CardStyle(bool selected) => new()
    {
        BgColor = selected ? new Color(0.045f, 0.13f, 0.085f, 0.98f) : new Color(0.022f, 0.062f, 0.047f, 0.92f),
        BorderColor = selected ? new Color(0.42f, 0.86f, 0.66f, 0.95f) : new Color(0.21f, 0.36f, 0.27f, 0.95f),
        BorderWidthLeft = selected ? 3 : 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        ContentMarginLeft = 11,
        ContentMarginRight = 9,
    };

    private static StyleBoxFlat SmallStyle(bool selected) => new()
    {
        BgColor = selected ? new Color(0.055f, 0.15f, 0.092f) : new Color(0.018f, 0.055f, 0.04f),
        BorderColor = selected ? new Color(0.42f, 0.86f, 0.66f) : new Color(0.21f, 0.36f, 0.27f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
    };

    private static StyleBoxFlat ActionStyle(bool enabled, bool hover = false) => new()
    {
        BgColor = enabled ? hover ? new Color(0.58f, 0.86f, 0.64f) : new Color(0.34f, 0.69f, 0.5f) : new Color(0.045f, 0.085f, 0.065f),
        BorderColor = enabled ? new Color(0.7f, 1.0f, 0.82f) : new Color(0.18f, 0.29f, 0.3f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
    };

    private static StyleBoxFlat RailStyle(bool hover = false) => new()
    {
        BgColor = hover ? new Color(0.055f, 0.13f, 0.08f) : new Color(0.025f, 0.06f, 0.045f),
        BorderColor = hover ? new Color(0.42f, 0.86f, 0.66f) : new Color(0.2f, 0.34f, 0.25f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
    };

    private Button ActionButton(string text, int height)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, height),
            Alignment = HorizontalAlignment.Center,
            FocusMode = Control.FocusModeEnum.All,
        };
        button.AddThemeFontOverride("font", _terminalFont);
        button.AddThemeFontSizeOverride("font_size", 10);
        return button;
    }

    private Label Label(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontOverride("font", _terminalFont);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}
