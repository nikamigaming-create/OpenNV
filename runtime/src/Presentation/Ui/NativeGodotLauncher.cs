using System.Text.Json;
using Godot;
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
    string? Fallout3WorldRoot,
    FalloutModStackSelection? ModSelection = null);

/// <summary>
/// Godot-native product entry point. The launcher deliberately consumes the
/// same runtime manifest and profile identities as the command-line path, but
/// never claims that a preview, JAM profile, or TTW registration is gameplay.
/// </summary>
internal sealed partial class NativeGodotLauncher : Control
{
    private static readonly IReadOnlyList<string> CampaignIds =
        ["fallout1", "fallout2", "newvegas", "fallout3", .. FalloutModCatalog.All.Select(mod => mod.Id)];
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
    private string _selectedId = "newvegas";
    private string _selectedGameId = "newvegas";
    private string _selectedPresentation = "first-person";
    private readonly Font _interfaceFont = new SystemFont { FontNames = ["Segoe UI", "Inter", "Noto Sans"] };
    private readonly Font _headingFont = new SystemFont { FontNames = ["Segoe UI", "Inter", "Noto Sans"], FontWeight = 600 };
    private readonly Dictionary<string, Button> _campaignButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CheckBox> _modToggles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Control> _libraryRows = new(StringComparer.OrdinalIgnoreCase);
    private VBoxContainer _campaignList = null!;
    private HBoxContainer _presentationList = null!;
    private Label _selectionTitle = null!;
    private Label _selectionStatus = null!;
    private Label _selectionDetail = null!;
    private Label _selectionCategory = null!;
    private Label _profileStatus = null!;
    private Label _baseStatus = null!;
    private Label _folderTitle = null!;
    private Label _extensionStatus = null!;
    private Label _readinessTitle = null!;
    private Label _dependencyCount = null!;
    private Button _chooseInstall = null!;
    private Button _chooseBase = null!;
    private Button _chooseDependency = null!;
    private ItemList _additionalFolders = null!;
    private HBoxContainer _additionalActions = null!;
    private CheckButton _advancedFolderOrder = null!;
    private VBoxContainer _dependencyPanel = null!;
    private VBoxContainer _baseFolderRow = null!;
    private Button _launch = null!;
    private Label _toast = null!;
    private LineEdit _search = null!;
    private Label _noResults = null!;
    private Label _launchGameTitle = null!;
    private Label _stackStatus = null!;
    private Window? _stackWindow;
    private ItemList? _stackList;
    private HBoxContainer? _manualStackActions;
    private IReadOnlyList<string> _displayedStackIds = [];
    private Label _gamesHeading = null!;
    private Label _modsHeading = null!;
    private string _libraryFilter = "All";
    private readonly Dictionary<string, Button> _filterButtons = new();
    private FileDialog? _fileDialog;
    private bool _selectingDependency;
    private bool _selectingBaseGame;
    private bool _configured;
    private bool _launching;
    private static bool XrAvailable => XRServer.FindInterface("OpenXR")?.IsInitialized() == true;

    internal event Action<NativeGodotLauncherLaunchRequest>? LaunchRequested;

    internal void Configure(GodotLauncherProfileStore? profiles = null)
    {
        if (_configured)
            return;
        _profiles = profiles ?? GodotLauncherProfileStore.Load();
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
        BuildInterface();
        Refresh();
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

    private void Refresh()
    {
        var campaign = LaunchCampaign();
        if (!campaign.Presentations.ContainsKey(_selectedPresentation))
            _selectedPresentation = campaign.DefaultPresentation;
        RefreshCampaignCards();
        RefreshPresentationModes(campaign);
        RefreshDetails(CurrentCampaign());
        RefreshStackLaunch(campaign);
    }

    private void ShowToast(string message)
    {
        _toast.Text = message;
        _toast.AddThemeColorOverride("font_color", new Color(0.84f, 0.7f, 0.39f));
    }

    private void ChooseInstallation() => ChooseFolder(dependency: false);

    private void ChooseFolder(bool dependency, bool baseGame = false)
    {
        if (_fileDialog is not null)
            return;
        _selectingDependency = dependency;
        _selectingBaseGame = baseGame;
        _fileDialog = new FileDialog
        {
            Name = "OwnedInstallationPicker",
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = baseGame ? "Choose your Fallout: New Vegas game folder" :
                dependency ? "Choose an extracted dependency or patch folder" :
                $"Choose your {CurrentCampaign().Title} folder",
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
            if (_selectingBaseGame)
            {
                var installation = NativeGameInstallation.Detect(path);
                if (installation.Game != NativeGame.FalloutNewVegas)
                    throw new InvalidDataException("Choose your Fallout: New Vegas installation for this mod.");
                _profiles.Save("newvegas", installation.InstallRoot);
                if (_profiles.TryGet(campaign.Id, out var previous))
                    _profiles.Save(campaign.Id, previous.InstallRoot, installation.InstallRoot, previous.DependencyRoots);
            }
            else if (FalloutModInstallation.IsMod(campaign.Id))
            {
                _profiles.TryGet(campaign.Id, out var previous);
                var baseRoot = previous?.BaseInstallRoot ?? ValidProfileRoot("newvegas");
                var dependencies = previous?.DependencyRoots ?? [];
                var selected = path;
                if (_selectingDependency)
                {
                    if (previous is null) throw new InvalidOperationException("Select the mod folder first.");
                    selected = previous.InstallRoot;
                    dependencies = dependencies.Append(path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                }
                var mod = FalloutModInstallation.Detect(campaign.Id, selected, baseRoot, dependencies);
                _profiles.Save(campaign.Id, mod.SelectedRoot, mod.BaseInstallation.InstallRoot, dependencies);
            }
            else
            {
                var installation = NativeGameInstallation.Detect(path);
                var expected = ExpectedNativeGame(campaign.Id);
                if (installation.Game != expected)
                    throw new InvalidDataException(
                        $"That folder is {installation.Game}, but {campaign.Title} needs {expected}.");
                _profiles.Save(campaign.Id, installation.InstallRoot);
            }
            _toast.Text = "Folder saved. Your original files stay in place.";
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
        var campaign = LaunchCampaign();
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
            var selection = _profiles.ModStack(campaign.Id);
            var mod = selection?.Resolve(profile.InstallRoot);
            var unsupported = _profiles.EnabledMods(campaign.Id).Where(id =>
                !_campaigns.Single(row => row.Id == id).Launchable ||
                _campaigns.Single(row => row.Id == id).Presentations.GetValueOrDefault(_selectedPresentation)?.Launchable != true).ToArray();
            if (unsupported.Length != 0)
                throw new InvalidOperationException("Gameplay support is still in development for: " +
                    string.Join(", ", unsupported.Select(id => FalloutModCatalog.Get(id).Title)));
            _launching = true;
            var request = new NativeGodotLauncherLaunchRequest(
                campaign.Id,
                campaign.EngineCampaign,
                _selectedPresentation,
                mod?.BaseInstallation.InstallRoot ?? profile.InstallRoot,
                _profiles.LaunchSavePath(campaign.Id),
                ValidProfileRoot("newvegas"),
                ValidProfileRoot("fallout3"),
                selection);
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
            message = "Choose a folder to get started.";
            return false;
        }
        try
        {
            if (FalloutModInstallation.IsMod(campaignId))
            {
                var mod = FalloutModInstallation.Detect(campaignId, stored.InstallRoot, stored.BaseInstallRoot,
                    stored.DependencyRoots);
                profile = stored;
                message = mod.SetupStatus;
                return true;
            }
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

    private NativeGodotLauncherCampaign LaunchCampaign() =>
        _campaigns.Single(row => row.Id == _selectedGameId);

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
            var mod = FalloutModInstallation.IsMod(id);
            if (!FallbackCampaigns.TryGetValue(id, out var fallback) && !mod)
                continue;
            var title = row.TryGetProperty("title", out var titleProperty)
                ? titleProperty.GetString() ?? fallback.Title
                : mod ? FalloutModCatalog.Get(id).Title : fallback.Title;
            var engine = mod ? RuntimeLiveContentSource.FalloutNewVegasGame : fallback.Engine;
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

}
