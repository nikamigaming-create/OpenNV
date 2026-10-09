using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGodotLauncher
{
    private static readonly Color Ink = new("eff3f6");
    private static readonly Color Muted = new("a0acb9");
    private static readonly Color Gold = new("eabb76");
    private static readonly Color Line = new("2d3947");
    private static readonly Color Surface = new("141e2b");
    private const string HeroArtworkPath = "res://assets/launcher/mojave-horizon-v2.png";
    private Control _hero = null!;
    private Label _heroContext = null!;
    private Label _saveStatus = null!;
    private VBoxContainer _setupBody = null!;
    private Button _setupToggle = null!;
    private Button _quickSetup = null!;
    private ScrollContainer _setupScroll = null!;
    private string? _setupCampaign;
    private bool _setupExpanded;
    private NativeViewportLayout? _launcherLayout;
    private Texture2D? _heroTexture;
    private LauncherBuildBadge _buildIdentity = new("Development build", "No packaged build metadata was supplied.");

    private void BuildBackground()
    {
        var background = new ColorRect { Name = "LauncherBackdrop", Color = new Color("0a121e"), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        Theme = new Theme { DefaultFont = _interfaceFont, DefaultFontSize = 15 };
        Theme.SetColor("font_color", "Label", Ink);
        Theme.SetColor("font_color", "Button", Ink);
        Theme.SetColor("font_hover_color", "Button", Ink);
        Theme.SetColor("font_focus_color", "Button", Ink);
        Theme.SetColor("font_disabled_color", "Button", new Color("748395"));
        Theme.SetStylebox("normal", "Button", Box(Surface, Line));
        Theme.SetStylebox("hover", "Button", Box(new Color("223044"), new Color("526176")));
        Theme.SetStylebox("pressed", "Button", Box(new Color("2d3a4b"), Gold));
        Theme.SetStylebox("disabled", "Button", Box(new Color("131d2a"), new Color("24303e")));
        Theme.SetStylebox("focus", "Button", Box(Colors.Transparent, Gold, border: 2));
        Theme.SetStylebox("normal", "LineEdit", Box(new Color("0d1724"), Line));
        Theme.SetStylebox("focus", "LineEdit", Box(Colors.Transparent, Gold, border: 2));
        Theme.SetColor("font_color", "LineEdit", Ink);
        Theme.SetColor("font_placeholder_color", "LineEdit", Muted);
        Theme.SetStylebox("panel", "ItemList", Box(new Color("0d1724"), Line));
        Theme.SetStylebox("selected", "ItemList", Box(new Color("303b48"), Gold));
        Theme.SetStylebox("selected_focus", "ItemList", Box(new Color("303b48"), Gold));
        Theme.SetStylebox("focus", "ItemList", Box(Colors.Transparent, Gold, border: 2));
        Theme.SetColor("font_color", "ItemList", Ink);
        Theme.SetColor("font_selected_color", "ItemList", Ink);
        Theme.SetColor("font_color", "CheckBox", Ink);
        Theme.SetColor("font_color", "CheckButton", Muted);
    }

    private void BuildInterface()
    {
        var margin = Margin("LauncherInterface", 24);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(margin);
        var stack = Stack("LauncherStack", 18);
        margin.AddChild(stack);
        stack.AddChild(BuildHeader());
        var main = new HBoxContainer { Name = "LauncherMain", SizeFlagsVertical = SizeFlags.ExpandFill };
        main.AddThemeConstantOverride("separation", 24);
        stack.AddChild(main);
        main.AddChild(BuildLibrary());
        var content = Stack("SelectedWorld", 16);
        content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        main.AddChild(content);
        var scroll = new ScrollContainer
        {
            Name = "SetupScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _setupScroll = scroll;
        content.AddChild(scroll);
        var body = Stack("SelectedWorldBody", 16);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(body);
        body.AddChild(BuildHero());
        body.AddChild(BuildSetup());
        content.AddChild(BuildLaunchBar());
        stack.AddChild(BuildFooter());
        _launcherLayout = new(this, ResizeLauncher);
        ResizeLauncher();
    }

    private void ResizeLauncher()
    {
        if (_hero is not null) _hero.CustomMinimumSize = new Vector2(0, Mathf.Clamp(GetViewportRect().Size.Y * .4f, 292, 390));
    }

    public override void _ExitTree()
    {
        _launcherLayout?.Dispose(); _launcherLayout = null;
        _heroTexture?.Dispose(); _heroTexture = null;
        _headingFont.Dispose();
        _interfaceFont.Dispose();
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer { Name = "LauncherHeader", CustomMinimumSize = new Vector2(0, 54) };
        header.AddThemeConstantOverride("separation", 14);
        var emblem = new PanelContainer { Name = "LauncherMonogram", CustomMinimumSize = new Vector2(48, 48) };
        emblem.AddThemeStyleboxOverride("panel", Box(Gold, Gold, padding: 0));
        var monogram = Label("ON", 20, new Color("17202c"), heading: true);
        monogram.HorizontalAlignment = HorizontalAlignment.Center;
        monogram.VerticalAlignment = VerticalAlignment.Center;
        emblem.AddChild(monogram);
        header.AddChild(emblem);
        var brand = Stack("Brand", 1);
        brand.AddChild(Label("OpenNV", 28, Ink, heading: true));
        brand.AddChild(Label("YOUR WASTELAND LIBRARY", 10, Muted));
        header.AddChild(brand);
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var badge = Label("EXPERIMENTAL PLAYTEST", 11, Gold);
        badge.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(badge);
        var exit = ActionButton("Quit", 34);
        exit.Name = "QuitLauncher";
        exit.TooltipText = "Close OpenNV · Esc";
        exit.Pressed += () => GetTree().Quit();
        header.AddChild(exit);
        return header;
    }

    private Control BuildLibrary()
    {
        var panel = Panel("WorldLibrary");
        panel.CustomMinimumSize = new Vector2(252, 0);
        panel.AddThemeStyleboxOverride("panel", Box(new Color("101a27"), Line, padding: 14));
        var library = Stack("LibraryContents", 14);
        panel.AddChild(library);
        var heading = new HBoxContainer();
        var title = Label("Library", 20, Ink, heading: true);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(title);
        heading.AddChild(Label("GAMES + MODS", 10, Muted));
        library.AddChild(heading);
        _search = new LineEdit
        {
            Name = "LibrarySearch",
            PlaceholderText = "Search your library",
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(0, 40),
        };
        _search.TextChanged += _ => FilterLibrary();
        library.AddChild(_search);
        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 5);
        foreach (var filter in new[] { "All", "Games", "Mods" })
        {
            var button = ActionButton(filter, 32);
            button.Name = "LibraryFilter_" + filter;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.AddThemeFontSizeOverride("font_size", 12);
            button.Pressed += () => { _libraryFilter = filter; FilterLibrary(); };
            _filterButtons[filter] = button;
            filters.AddChild(button);
        }
        library.AddChild(filters);
        var scroll = new ScrollContainer
        {
            Name = "LibraryScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _campaignList = Stack("CampaignCards", 8);
        _campaignList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_campaignList);
        library.AddChild(scroll);
        var hint = Label("Select a game to play.\nCheck mods to combine them.", 12, Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        library.AddChild(hint);
        return panel;
    }

    private Control BuildHero()
    {
        var hero = _hero = new Control { Name = "WorldHero", CustomMinimumSize = new Vector2(0, 300), ClipContents = true };
        var baseColor = new ColorRect { Color = new Color("1a2a40"), MouseFilter = MouseFilterEnum.Ignore };
        baseColor.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hero.AddChild(baseColor);
        if (ResourceLoader.Exists(HeroArtworkPath))
        {
            var texture = _heroTexture = ResourceLoader.Load<Texture2D>(HeroArtworkPath, cacheMode: ResourceLoader.CacheMode.Ignore);
            if (texture is not null)
            {
                var art = new TextureRect
                {
                    Name = "MojaveArtwork",
                    Texture = texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                hero.AddChild(art);
            }
        }
        var shade = new TextureRect
        {
            Name = "HeroReadabilityShade",
            Texture = new GradientTexture2D
            {
                Width = 256,
                Height = 2,
                FillFrom = Vector2.Zero,
                FillTo = Vector2.Right,
                Gradient = new Gradient
                {
                    Offsets = [0, .57f, 1],
                    Colors = [new Color(.025f, .045f, .075f, .48f), new Color(.025f, .045f, .075f, .12f), Godot.Colors.Transparent],
                },
            },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hero.AddChild(shade);
        var margin = Margin("HeroCopy", 28);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hero.AddChild(margin);
        var columns = new HBoxContainer();
        margin.AddChild(columns);
        var text = Stack("HeroText", 10);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.SizeFlagsStretchRatio = 1.8f;
        columns.AddChild(text);
        columns.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1 });
        _selectionCategory = Label(string.Empty, 11, Gold);
        _selectionCategory.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(_selectionCategory);
        text.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _selectionTitle = Label(string.Empty, 38, Ink, heading: true);
        _selectionTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(_selectionTitle);
        _selectionDetail = Label(string.Empty, 15, new Color("d5dce3"));
        _selectionDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        text.AddChild(_selectionDetail);
        _selectionStatus = Label(string.Empty, 11, Gold);
        text.AddChild(_selectionStatus);
        _heroContext = Label(string.Empty, 12, Muted);
        text.AddChild(_heroContext);
        return hero;
    }

    private Control BuildSetup()
    {
        var panel = Panel("FolderSetup");
        var stack = Stack("FolderSetupStack", 14);
        panel.AddChild(stack);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        var title = Label("Installation & mods", 16, Ink, heading: true);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(title);
        _setupToggle = ActionButton("Folder settings  +", 34);
        _setupToggle.Name = "ToggleFolderSetup";
        _setupToggle.Pressed += () => SetSetupExpanded(!_setupExpanded);
        header.AddChild(_setupToggle);
        stack.AddChild(header);
        _setupBody = Stack("FolderSetupBody", 16);
        stack.AddChild(_setupBody);
        var hint = Label("Choose your existing game and mod folders. Your files stay in place.", 13, Muted);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _setupBody.AddChild(hint);
        var columns = new HBoxContainer { Name = "FolderColumns" };
        columns.AddThemeConstantOverride("separation", 24);
        _setupBody.AddChild(columns);
        var folders = Stack("InstallationFolders", 18);
        folders.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(folders);
        _baseFolderRow = Stack("BaseGameFolder", 8);
        _baseFolderRow.AddChild(Label("New Vegas game folder", 14, Ink, heading: true));
        _baseStatus = PathLabel("BaseGamePath");
        _baseFolderRow.AddChild(_baseStatus);
        _chooseBase = ActionButton("Choose game folder", 38);
        _chooseBase.Name = "ChooseBaseGameFolder";
        _chooseBase.Pressed += () => ChooseFolder(dependency: false, baseGame: true);
        _baseFolderRow.AddChild(_chooseBase);
        folders.AddChild(_baseFolderRow);
        var selected = Stack("SelectedFolder", 8);
        _folderTitle = Label("Game folder", 14, Ink, heading: true);
        selected.AddChild(_folderTitle);
        _profileStatus = PathLabel("SelectedFolderPath");
        selected.AddChild(_profileStatus);
        _chooseInstall = ActionButton("Choose folder", 38);
        _chooseInstall.Name = "ChooseSelectedFolder";
        _chooseInstall.Pressed += ChooseInstallation;
        selected.AddChild(_chooseInstall);
        folders.AddChild(selected);
        _dependencyPanel = Stack("DependencySetup", 8);
        _dependencyPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(_dependencyPanel);
        _dependencyPanel.AddChild(Label("Dependencies & patches", 14, Ink, heading: true));
        _dependencyCount = Label(string.Empty, 12, Muted);
        _dependencyCount.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dependencyPanel.AddChild(_dependencyCount);
        BuildAdditionalFolders(_dependencyPanel);
        _chooseDependency = ActionButton("+  Add folder", 38);
        _chooseDependency.Name = "AddDependencyFolder";
        _chooseDependency.Pressed += () => ChooseFolder(dependency: true);
        _dependencyPanel.AddChild(_chooseDependency);
        _setupBody.AddChild(BuildReadiness());
        return panel;
    }

    private void SetSetupExpanded(bool expanded)
    {
        _setupExpanded = expanded;
        _setupBody.Visible = expanded;
        _setupToggle.Text = expanded ? "Hide folder settings  −" : "Folder settings  +";
    }

    private Control BuildReadiness()
    {
        var stack = Stack("Readiness", 5);
        _readinessTitle = Label(string.Empty, 13, Gold, heading: true);
        stack.AddChild(_readinessTitle);
        _extensionStatus = Label(string.Empty, 13, Muted);
        _extensionStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_extensionStatus);
        return stack;
    }

    private Control BuildLaunchBar()
    {
        var panel = Panel("PlayDock");
        panel.AddThemeStyleboxOverride("panel", Box(new Color("182432"), new Color("3d4c5b"), padding: 18));
        var stack = Stack("LaunchBar", 10);
        panel.AddChild(stack);
        var summary = new HBoxContainer();
        summary.AddThemeConstantOverride("separation", 12);
        _launchGameTitle = Label(string.Empty, 16, Ink, heading: true);
        _launchGameTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _launchGameTitle.VerticalAlignment = VerticalAlignment.Center;
        summary.AddChild(_launchGameTitle);
        _quickSetup = ActionButton("Set up files", 32);
        _quickSetup.Name = "ShowSelectedFolderSetup";
        _quickSetup.AddThemeFontSizeOverride("font_size", 12);
        _quickSetup.Pressed += () =>
        {
            SetSetupExpanded(true);
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(this) && IsInsideTree() && GodotObject.IsInstanceValid(_setupScroll))
                    _setupScroll.EnsureControlVisible(_baseFolderRow.Visible && _chooseInstall.Disabled ? _chooseBase : _chooseInstall);
            }).CallDeferred();
        };
        summary.AddChild(_quickSetup);
        var loadOrder = ActionButton("Mod order · Automatic", 32);
        loadOrder.Name = "OpenModLoadOrder";
        loadOrder.AddThemeFontSizeOverride("font_size", 12);
        loadOrder.Pressed += ShowModStack;
        summary.AddChild(loadOrder);
        stack.AddChild(summary);
        _stackStatus = Label(string.Empty, 13, Gold);
        _stackStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_stackStatus);
        _toast = Label(string.Empty, 13, Gold);
        _toast.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_toast);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        stack.AddChild(row);
        var modes = Stack("PlayMode", 5);
        modes.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        modes.AddChild(Label("PLAY MODE", 10, Muted));
        _presentationList = new HBoxContainer { Name = "PresentationModes" };
        _presentationList.AddThemeConstantOverride("separation", 5);
        modes.AddChild(_presentationList);
        row.AddChild(modes);
        _newGame = ActionButton("New Game", 44);
        _newGame.Name = "NewSelectedGame";
        _newGame.CustomMinimumSize = new Vector2(116, 44);
        _newGame.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        _newGame.Pressed += () => LaunchSelected(NativeGodotLauncherEntry.NewGame);
        row.AddChild(_newGame);
        _continue = ActionButton("Continue", 44);
        _continue.Name = "ContinueSelectedGame";
        _continue.CustomMinimumSize = new Vector2(110, 44);
        _continue.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        _continue.Pressed += () => LaunchSelected(NativeGodotLauncherEntry.Continue);
        row.AddChild(_continue);
        _launch = ActionButton("Play  →", 44);
        _launch.Name = "LaunchSelectedWorld";
        _launch.CustomMinimumSize = new Vector2(146, 44);
        _launch.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        _launch.Pressed += () => LaunchSelected();
        _launch.AddThemeColorOverride("font_color", new Color("17202c"));
        _launch.AddThemeColorOverride("font_hover_color", new Color("17202c"));
        _launch.AddThemeColorOverride("font_pressed_color", new Color("17202c"));
        _launch.AddThemeColorOverride("font_focus_color", new Color("17202c"));
        _launch.AddThemeStyleboxOverride("normal", Box(Gold, Gold));
        _launch.AddThemeStyleboxOverride("hover", Box(Gold.Lightened(.12f), Gold));
        _launch.AddThemeStyleboxOverride("pressed", Box(Gold.Darkened(.1f), Gold));
        row.AddChild(_launch);
        _saveStatus = Label(string.Empty, 11, Muted);
        _saveStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_saveStatus);
        return panel;
    }

    private Control BuildFooter()
    {
        var footer = new HBoxContainer { Name = "LauncherFooter", CustomMinimumSize = new Vector2(0, 20) };
        footer.AddThemeConstantOverride("separation", 18);
        var metadata = _buildIdentity = LauncherBuildBadge.ReadBesideExecutable();
        var badge = Label(metadata.Text, 11, Muted);
        badge.Name = "LauncherBuildIdentity";
        badge.TooltipText = metadata.Detail;
        badge.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        badge.ClipText = true;
        badge.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        footer.AddChild(badge);
        footer.AddChild(Label("Your installed files. Separate OpenNV saves.", 11, Muted));
        return footer;
    }

    private Label PathLabel(string name) => new()
    {
        Name = name,
        Text = "No folder selected",
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        ClipText = true,
        Modulate = Muted,
    };

    private static MarginContainer Margin(string name, int padding)
    {
        var margin = new MarginContainer { Name = name };
        foreach (var edge in new[] { "left", "top", "right", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, padding);
        return margin;
    }

    private static VBoxContainer Stack(string name, int separation)
    {
        var stack = new VBoxContainer { Name = name };
        stack.AddThemeConstantOverride("separation", separation);
        return stack;
    }

    private static PanelContainer Panel(string name)
    {
        var panel = new PanelContainer { Name = name };
        panel.AddThemeStyleboxOverride("panel", Box(Surface, Line, padding: 18));
        return panel;
    }

    private static StyleBoxFlat Box(Color background, Color borderColor, int padding = 12, int border = 1) => new()
    {
        BgColor = background,
        BorderColor = borderColor,
        BorderWidthLeft = border,
        BorderWidthTop = border,
        BorderWidthRight = border,
        BorderWidthBottom = border,
        CornerRadiusTopLeft = 8,
        CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8,
        CornerRadiusBottomRight = 8,
        ContentMarginLeft = padding,
        ContentMarginRight = padding,
        ContentMarginTop = 8,
        ContentMarginBottom = 8,
    };

    private Button ActionButton(string text, int height) => new()
    {
        Text = text,
        CustomMinimumSize = new Vector2(0, height),
        FocusMode = FocusModeEnum.All,
    };

    private Label Label(string text, int size, Color color, bool heading = false)
    {
        var label = new Label { Text = text };
        label.AddThemeFontOverride("font", heading ? _headingFont : _interfaceFont);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}
