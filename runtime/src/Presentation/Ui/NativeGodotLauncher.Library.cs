using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGodotLauncher
{
    private void RefreshCampaignCards()
    {
        if (_campaignButtons.Count == 0)
        {
            _gamesHeading = Label("GAMES", 11, Muted);
            _campaignList.AddChild(_gamesHeading);
            foreach (var id in CampaignIds)
            {
                var campaign = _campaigns.FirstOrDefault(row => row.Id == id);
                if (campaign is null) continue;
                if (id == FalloutModCatalog.All[0].Id)
                {
                    _modsHeading = Label("MOD COLLECTION", 11, Muted);
                    _modsHeading.CustomMinimumSize = new Vector2(0, 30);
                    _modsHeading.VerticalAlignment = VerticalAlignment.Bottom;
                    _campaignList.AddChild(_modsHeading);
                }
                var button = new Button
                {
                    Name = "Campaign_" + id,
                    Text = LibraryTitle(campaign),
                    CustomMinimumSize = new Vector2(0, 43),
                    Alignment = HorizontalAlignment.Left,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                    ClipText = true,
                    TooltipText = campaign.Title,
                };
                button.Pressed += () =>
                {
                    _selectedId = campaign.Id;
                    if (!FalloutModInstallation.IsMod(campaign.Id))
                    {
                        _selectedGameId = campaign.Id;
                        _selectedPresentation = campaign.DefaultPresentation;
                    }
                    _toast.Text = string.Empty;
                    Refresh();
                };
                _campaignButtons[id] = button;
                if (FalloutModInstallation.IsMod(id))
                {
                    var row = new HBoxContainer();
                    row.AddThemeConstantOverride("separation", 0);
                    var toggle = new CheckBox { Name = "EnableMod_" + id, TooltipText = "Enable " + campaign.Title + " alongside your other mods" };
                    toggle.Toggled += enabled => ToggleMod(id, enabled);
                    _modToggles[id] = toggle;
                    row.AddChild(toggle);
                    button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    row.AddChild(button);
                    _campaignList.AddChild(row);
                    _libraryRows[id] = row;
                }
                else
                {
                    _campaignList.AddChild(button);
                    _libraryRows[id] = button;
                }
            }
            _noResults = Label("No matching games or mods.", 14, Muted);
            _noResults.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _campaignList.AddChild(_noResults);
        }
        foreach (var (id, button) in _campaignButtons)
        {
            var selected = id == _selectedId;
            button.AddThemeStyleboxOverride("normal", Box(selected ? new Color("34312a") : Colors.Transparent,
                selected ? new Color("a28350") : Colors.Transparent));
            button.AddThemeColorOverride("font_color", selected ? Gold : Ink);
        }
        var enabledMods = _profiles.EnabledMods("newvegas");
        foreach (var (id, toggle) in _modToggles)
        {
            toggle.SetPressedNoSignal(enabledMods.Contains(id));
            toggle.Disabled = _selectedGameId != "newvegas" || !_profiles.TryGet("newvegas", out _);
            toggle.TooltipText = toggle.Disabled ? "Select New Vegas and choose its game folder to enable mods." : "Enable " + FalloutModCatalog.Get(id).Title + " alongside your other mods";
        }
        FilterLibrary();
    }

    private void FilterLibrary()
    {
        if (_campaignButtons.Count == 0) return;
        var query = _search.Text.Trim();
        var games = 0;
        var mods = 0;
        foreach (var campaign in _campaigns)
        {
            if (!_campaignButtons.TryGetValue(campaign.Id, out var button)) continue;
            var isMod = FalloutModInstallation.IsMod(campaign.Id);
            var visible = (_libraryFilter == "All" || isMod == (_libraryFilter == "Mods")) &&
                (campaign.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 campaign.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
            _libraryRows[campaign.Id].Visible = visible;
            if (visible) { if (isMod) mods++; else games++; }
        }
        _gamesHeading.Visible = games != 0;
        _modsHeading.Visible = mods != 0;
        _noResults.Visible = games + mods == 0;
        foreach (var (title, button) in _filterButtons)
        {
            var selected = title == _libraryFilter;
            button.AddThemeStyleboxOverride("normal", Box(selected ? new Color("303944") : Colors.Transparent,
                selected ? new Color("53606e") : Line));
        }
    }

    private void RefreshPresentationModes(NativeGodotLauncherCampaign campaign)
    {
        foreach (var child in _presentationList.GetChildren().ToArray())
        {
            _presentationList.RemoveChild(child);
            child.QueueFree();
        }
        var modes = campaign.Id is "fallout1" or "fallout2"
            ? new[] { "hex-tactical", "first-person", "openxr" } : new[] { "first-person", "openxr" };
        foreach (var mode in modes)
        {
            campaign.Presentations.TryGetValue(mode, out var presentation);
            var button = ActionButton(RouteLabel(mode), 38);
            button.Name = "Presentation_" + mode;
            button.CustomMinimumSize = new Vector2(90, 38);
            button.Disabled = presentation is null || !presentation.Launchable || mode == "openxr" && !XrAvailable;
            button.TooltipText = mode == "openxr" && !XrAvailable
                ? "Start OpenNV VR with your headset runtime active to enable this mode."
                : presentation?.Status ?? "Gameplay for this profile is still in development.";
            if (_selectedPresentation == mode)
            {
                button.AddThemeStyleboxOverride("normal", Box(new Color("35342e"), Gold));
                button.AddThemeStyleboxOverride("disabled", Box(new Color("2d2d2a"), new Color("796747")));
                button.AddThemeColorOverride("font_color", Gold);
            }
            button.Pressed += () => { _selectedPresentation = mode; Refresh(); };
            _presentationList.AddChild(button);
        }
    }

    private void RefreshDetails(NativeGodotLauncherCampaign campaign)
    {
        var profileReady = TryGetValidatedProfile(campaign.Id, out var validated, out var profileMessage);
        _profiles.TryGet(campaign.Id, out var stored);
        var profile = validated ?? stored;
        var presentation = campaign.Presentations.GetValueOrDefault(_selectedPresentation);
        var routeReady = campaign.Launchable && presentation?.Launchable == true && profileReady &&
            (_selectedPresentation != "openxr" || XrAvailable);
        var modSelected = FalloutModInstallation.IsMod(campaign.Id);
        _selectionTitle.Text = campaign.Id == "jam" ? "Just Assorted Mods" : campaign.Title;
        _selectionCategory.Text = modSelected ? "MOD COLLECTION  /  FALLOUT: NEW VEGAS" : "YOUR WORLD  /  " + RouteLabel(campaign.DefaultPresentation).ToUpperInvariant();
        _selectionDetail.Text = Description(campaign.Id);
        _selectionStatus.Text = campaign.Launchable
            ? presentation?.PreviewOnly == true ? "Source preview  ·  Campaign incomplete" : "Experimental playtest  ·  Campaign incomplete"
            : "Gameplay in development";
        _folderTitle.Text = modSelected ? "02   Mod folder" : "Game installation";
        _baseFolderRow.Visible = modSelected;
        var baseRoot = profile?.BaseInstallRoot ?? ValidProfileRoot("newvegas");
        _baseStatus.Text = baseRoot ?? "Choose the game this mod builds on";
        _baseStatus.TooltipText = baseRoot ?? "Select the folder containing FalloutNV.esm or its Data folder.";
        _chooseBase.Text = baseRoot is null ? "Choose game folder" : "Change game folder";
        _profileStatus.Text = profile?.InstallRoot ?? (modSelected ? "Select the extracted mod folder" : "Select your installed game folder");
        _profileStatus.TooltipText = profile?.InstallRoot ?? _profileStatus.Text;
        _chooseInstall.Text = profile is null ? modSelected ? "Choose mod folder" : "Choose game folder" : "Change folder";
        _chooseInstall.Disabled = modSelected && baseRoot is null;
        _chooseInstall.TooltipText = _chooseInstall.Disabled ? "Choose your New Vegas game folder first." : string.Empty;
        _dependencyPanel.Visible = modSelected;
        _chooseDependency.Disabled = profile is null;
        _chooseDependency.TooltipText = profile is null ? "Choose your mod folder first." : "Add a dependency or patch. Lower folders take priority.";
        RefreshAdditionalFolders(campaign.Id);
        var count = profile?.DependencyRoots?.Count ?? 0;
        _dependencyCount.Text = count == 0 ? "Add the folders required by this mod" : $"{count} folders  ·  Lower folders take priority";
        _readinessTitle.Text = !profileReady ? "Setup needed" : modSelected ? "Folder connected" : "Ready for an experimental playtest";
        _extensionStatus.Text = !profileReady ? profileMessage : modSelected ? profileMessage : campaign.Status + ".";
        _extensionStatus.TooltipText = profile is null ? string.Empty : "Separate OpenNV save: " + profile.SavePath;
        _launch.Disabled = !routeReady || _launching;
        _launch.Text = _launching ? "Opening…" : !campaign.Launchable ? "In development" : !profileReady ? "Choose a folder to play" :
            presentation?.PreviewOnly == true ? "Open source preview" : "Play  →";
        _launch.TooltipText = routeReady ? "Open this profile · F1" : !profileReady ? profileMessage : campaign.Status;
        _launch.AddThemeColorOverride("font_color", new Color("141b22"));
        _launch.AddThemeStyleboxOverride("normal", Box(Gold, Gold));
        _launch.AddThemeStyleboxOverride("hover", Box(Gold.Lightened(0.15f), Gold));
        _launch.AddThemeStyleboxOverride("pressed", Box(Gold.Darkened(0.12f), Gold));
    }

    private static string RouteLabel(string mode) => mode switch
    {
        "hex-tactical" => "Classic",
        "first-person" => "Desktop",
        "openxr" => "VR",
        _ => mode,
    };

    private static string LibraryTitle(NativeGodotLauncherCampaign campaign) => campaign.Id switch
    {
        "jam" => "JAM · Just Assorted Mods",
        "ttw" => "Tale of Two Wastelands",
        "yup" => "YUP · Bug fixes",
        "nmc" => "NMC's Texture Pack",
        "eve" => "EVE · Visual effects",
        _ => campaign.Title,
    };

    private static string Description(string id) => id switch
    {
        "fallout1" => "Return to the wasteland where it all began.",
        "fallout2" => "A new generation. A world waiting beyond the village.",
        "newvegas" => "Find your own way through the Mojave wasteland.",
        "fallout3" => "Step out of Vault 101 and into the Capital Wasteland.",
        "jam" => "Movement, combat and interface improvements in one collection.",
        "ttw" => "The Capital Wasteland and the Mojave, connected in one adventure.",
        "yup" => "A collection of fixes for New Vegas and its expansions.",
        "jsawyer" => "A different balance of survival, equipment and progression.",
        "uncut-wasteland" => "Restored details, encounters and characters across the wasteland.",
        "living-desert" => "Travelers, patrols and a world that responds to your choices.",
        "nmc" => "Fresh detail for the roads, buildings and objects of the Mojave.",
        "eve" => "Energy weapons and effects with a new visual character.",
        "nevada-skies" => "A new atmosphere for the Mojave, from clear skies to storms.",
        "bounties" => "Follow a trail of contracts through the Mojave wasteland.",
        _ => "Select your files to set up this profile.",
    };
}
