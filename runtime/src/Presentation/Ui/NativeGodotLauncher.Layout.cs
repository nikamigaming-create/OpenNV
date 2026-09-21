using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGodotLauncher
{
    private static readonly Color Ink = new("edf0f2");
    private static readonly Color Muted = new("9faab5");
    private static readonly Color Gold = new("e9b969");
    private static readonly Color Line = new("303943");
    private static readonly Color Surface = new("171e26");

    private void BuildBackground()
    {
        var background = new ColorRect { Name = "LauncherBackdrop", Color = new Color("10151c"), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        Theme = new Theme { DefaultFont = _interfaceFont, DefaultFontSize = 15 };
        Theme.SetColor("font_color", "Label", Ink);
        Theme.SetColor("font_color", "Button", Ink);
        Theme.SetColor("font_hover_color", "Button", Ink);
        Theme.SetColor("font_focus_color", "Button", Ink);
        Theme.SetColor("font_disabled_color", "Button", Muted);
        Theme.SetStylebox("normal", "Button", Box(Surface, Line));
        Theme.SetStylebox("hover", "Button", Box(new Color("2a333e"), new Color("5d6874")));
        Theme.SetStylebox("pressed", "Button", Box(new Color("39414a"), Gold));
        Theme.SetStylebox("disabled", "Button", Box(new Color("20262e"), Line));
        Theme.SetStylebox("focus", "Button", Box(Colors.Transparent, Gold, border: 2));
        Theme.SetStylebox("normal", "LineEdit", Box(new Color("121820"), Line));
        Theme.SetStylebox("focus", "LineEdit", Box(Colors.Transparent, Gold, border: 2));
        Theme.SetColor("font_color", "LineEdit", Ink);
        Theme.SetColor("font_placeholder_color", "LineEdit", Muted);
        Theme.SetStylebox("panel", "ItemList", Box(new Color("111820"), Line));
        Theme.SetStylebox("selected", "ItemList", Box(new Color("394038"), Gold));
        Theme.SetStylebox("selected_focus", "ItemList", Box(new Color("394038"), Gold));
        Theme.SetStylebox("focus", "ItemList", Box(Colors.Transparent, Gold, border: 2));
        Theme.SetColor("font_color", "ItemList", Ink);
        Theme.SetColor("font_selected_color", "ItemList", Ink);
    }

    private void BuildInterface()
    {
        var margin = Margin("LauncherInterface", 24);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(margin);
        var stack = Stack("LauncherStack", 20);
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
        content.AddChild(scroll);
        var body = Stack("SelectedWorldBody", 18);
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(body);
        body.AddChild(BuildHero());
        body.AddChild(BuildSetup());
        body.AddChild(BuildReadiness());
        content.AddChild(BuildLaunchBar());
    }

    private Control BuildHeader()
    {
        var header = new HBoxContainer { Name = "LauncherHeader", CustomMinimumSize = new Vector2(0, 48) };
        header.AddThemeConstantOverride("separation", 14);
        var emblem = new PanelContainer { CustomMinimumSize = new Vector2(46, 46) };
        emblem.AddThemeStyleboxOverride("panel", Box(Gold, Gold, padding: 0));
        var monogram = Label("ON", 20, new Color("151b22"), heading: true);
        monogram.HorizontalAlignment = HorizontalAlignment.Center;
        monogram.VerticalAlignment = VerticalAlignment.Center;
        emblem.AddChild(monogram);
        header.AddChild(emblem);
        var brand = Stack("Brand", 0);
        brand.AddChild(Label("Open Nevada", 25, Ink, heading: true));
        brand.AddChild(Label("Your worlds. Your way.", 13, Muted));
        header.AddChild(brand);
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var badge = Label("EXPERIMENTAL BUILD", 12, Gold);
        badge.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(badge);
        return header;
    }

    private Control BuildLibrary()
    {
        var library = Stack("WorldLibrary", 12);
        library.CustomMinimumSize = new Vector2(242, 0);
        library.AddChild(Label("Library", 21, Ink, heading: true));
        _search = new LineEdit
        {
            Name = "LibrarySearch",
            PlaceholderText = "Search games & mods",
            ClearButtonEnabled = true,
            CustomMinimumSize = new Vector2(0, 40),
        };
        _search.TextChanged += _ => FilterLibrary();
        library.AddChild(_search);
        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 6);
        foreach (var title in new[] { "All", "Games", "Mods" })
        {
            var button = ActionButton(title, 32);
            button.Name = "LibraryFilter_" + title;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.Pressed += () => { _libraryFilter = title; FilterLibrary(); };
            _filterButtons[title] = button;
            filters.AddChild(button);
        }
        library.AddChild(filters);
        var scroll = new ScrollContainer
        {
            Name = "LibraryScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _campaignList = Stack("CampaignCards", 5);
        _campaignList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_campaignList);
        library.AddChild(scroll);
        library.AddChild(Label("Check mods to combine them", 12, Muted));
        return library;
    }

    private Control BuildHero()
    {
        var hero = new Control { Name = "WorldHero", CustomMinimumSize = new Vector2(0, 230), ClipContents = true };
        var art = new TextureRect
        {
            Name = "MojaveArtwork",
            Texture = GD.Load<Texture2D>("res://assets/launcher/mojave-dusk-v1.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hero.AddChild(art);
        var shade = new ColorRect { Color = new Color(0.025f, 0.045f, 0.065f, 0.24f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hero.AddChild(shade);
        var margin = Margin("HeroCopy", 26);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        hero.AddChild(margin);
        var stack = Stack("HeroText", 10);
        margin.AddChild(stack);
        _selectionCategory = Label(string.Empty, 12, Gold);
        stack.AddChild(_selectionCategory);
        stack.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _selectionTitle = Label(string.Empty, 36, Ink, heading: true);
        _selectionTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_selectionTitle);
        _selectionDetail = Label(string.Empty, 15, new Color("c4cdd5"));
        _selectionDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_selectionDetail);
        _selectionStatus = Label(string.Empty, 13, Gold);
        stack.AddChild(_selectionStatus);
        return hero;
    }

    private Control BuildSetup()
    {
        var panel = Panel("FolderSetup");
        var stack = Stack("FolderSetupStack", 16);
        panel.AddChild(stack);
        var header = Stack("SetupHeader", 3);
        header.AddChild(Label("Set up your files", 20, Ink, heading: true));
        header.AddChild(Label("Choose your existing folders. Open Nevada remembers them.", 14, Muted));
        stack.AddChild(header);
        var columns = new HBoxContainer { Name = "FolderColumns" };
        columns.AddThemeConstantOverride("separation", 24);
        stack.AddChild(columns);
        var folders = Stack("InstallationFolders", 18);
        folders.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(folders);
        _baseFolderRow = Stack("BaseGameFolder", 8);
        _baseFolderRow.AddChild(Label("01   New Vegas installation", 15, Ink, heading: true));
        _baseStatus = PathLabel("BaseGamePath");
        _baseFolderRow.AddChild(_baseStatus);
        _chooseBase = ActionButton("Choose game folder", 38);
        _chooseBase.Name = "ChooseBaseGameFolder";
        _chooseBase.Pressed += () => ChooseFolder(dependency: false, baseGame: true);
        _baseFolderRow.AddChild(_chooseBase);
        folders.AddChild(_baseFolderRow);
        var mod = Stack("SelectedFolder", 8);
        _folderTitle = Label("02   Mod folder", 15, Ink, heading: true);
        mod.AddChild(_folderTitle);
        _profileStatus = PathLabel("SelectedFolderPath");
        mod.AddChild(_profileStatus);
        _chooseInstall = ActionButton("Choose folder", 38);
        _chooseInstall.Name = "ChooseSelectedFolder";
        _chooseInstall.Pressed += ChooseInstallation;
        mod.AddChild(_chooseInstall);
        folders.AddChild(mod);
        _dependencyPanel = Stack("DependencySetup", 8);
        _dependencyPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        columns.AddChild(_dependencyPanel);
        _dependencyPanel.AddChild(Label("03   Dependencies & patches", 15, Ink, heading: true));
        _dependencyCount = Label(string.Empty, 13, Muted);
        _dependencyPanel.AddChild(_dependencyCount);
        BuildAdditionalFolders(_dependencyPanel);
        _chooseDependency = ActionButton("+  Add folder", 38);
        _chooseDependency.Name = "AddDependencyFolder";
        _chooseDependency.Pressed += () => ChooseFolder(dependency: true);
        _dependencyPanel.AddChild(_chooseDependency);
        return panel;
    }

    private Control BuildReadiness()
    {
        var panel = Panel("Readiness");
        var stack = Stack("ReadinessText", 5);
        panel.AddChild(stack);
        _readinessTitle = Label(string.Empty, 15, Gold, heading: true);
        stack.AddChild(_readinessTitle);
        _extensionStatus = Label(string.Empty, 14, Muted);
        _extensionStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_extensionStatus);
        return panel;
    }

    private Control BuildLaunchBar()
    {
        var stack = Stack("LaunchBar", 8);
        var summary = new HBoxContainer();
        summary.AddThemeConstantOverride("separation", 12);
        _launchGameTitle = Label(string.Empty, 14, Ink, heading: true);
        _launchGameTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _launchGameTitle.VerticalAlignment = VerticalAlignment.Center;
        summary.AddChild(_launchGameTitle);
        var loadOrder = ActionButton("Load order · Automatic", 32);
        loadOrder.Name = "OpenModLoadOrder";
        loadOrder.Pressed += ShowModStack;
        summary.AddChild(loadOrder);
        stack.AddChild(summary);
        _stackStatus = Label(string.Empty, 13, Gold);
        _stackStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_stackStatus);
        _toast = Label(string.Empty, 14, Gold);
        _toast.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_toast);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        stack.AddChild(row);
        var modes = Stack("PlayMode", 5);
        modes.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        modes.AddChild(Label("PLAY MODE", 11, Muted));
        _presentationList = new HBoxContainer { Name = "PresentationModes" };
        _presentationList.AddThemeConstantOverride("separation", 6);
        modes.AddChild(_presentationList);
        row.AddChild(modes);
        _launch = ActionButton("Play", 46);
        _launch.Name = "LaunchSelectedWorld";
        _launch.CustomMinimumSize = new Vector2(222, 46);
        _launch.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        _launch.Pressed += LaunchSelected;
        row.AddChild(_launch);
        return stack;
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
        foreach (var edge in new[] { "left", "top", "right", "bottom" })
            margin.AddThemeConstantOverride("margin_" + edge, padding);
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
        panel.AddThemeStyleboxOverride("panel", Box(Surface, Line, padding: 20));
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
        ContentMarginTop = 6,
        ContentMarginBottom = 6,
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
