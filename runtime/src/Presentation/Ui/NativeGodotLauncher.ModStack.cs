using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGodotLauncher
{
    private void ToggleMod(string id, bool enabled)
    {
        try
        {
            var ids = _profiles.EnabledMods("newvegas").ToList();
            if (enabled && !ids.Contains(id)) ids.Add(id);
            if (!enabled) ids.Remove(id);
            _profiles.SetEnabledMods("newvegas", ids);
            if (enabled) _selectedId = id;
            _toast.Text = string.Empty;
        }
        catch (Exception error) { ShowToast(error.Message); }
        Refresh();
        RefreshStackWindow();
    }

    private void RefreshStackLaunch(NativeGodotLauncherCampaign campaign)
    {
        var enabled = _profiles.EnabledMods(campaign.Id);
        if (FindChild("OpenModLoadOrder", true, false) is Button loadOrder)
        {
            loadOrder.Text = _profiles.AutomaticModOrder("newvegas") ? "Load order · Automatic" : "Load order · Custom";
            loadOrder.Visible = campaign.Id == "newvegas";
        }
        _launchGameTitle.Text = campaign.Title + (enabled.Count == 0 ? "  ·  No mods enabled" : $"  ·  {enabled.Count} mods enabled");
        var setupReady = TryGetValidatedProfile(campaign.Id, out var profile, out var message);
        if (setupReady && enabled.Count != 0)
        {
            try { _ = _profiles.ModStack(campaign.Id)!.Resolve(profile!.InstallRoot); }
            catch (Exception error) { setupReady = false; message = error.Message; }
        }
        var unsupported = enabled.Where(id =>
        {
            var mod = _campaigns.Single(row => row.Id == id);
            return !mod.Launchable || mod.Presentations.GetValueOrDefault(_selectedPresentation)?.Launchable != true;
        }).ToArray();
        var route = campaign.Presentations.GetValueOrDefault(_selectedPresentation);
        var launchable = setupReady && campaign.Launchable && route?.Launchable == true && unsupported.Length == 0 &&
            (_selectedPresentation != "openxr" || XrAvailable);
        _stackStatus.Text = !setupReady ? message : unsupported.Length != 0
            ? "Enabled together · Gameplay in development: " + string.Join(", ", unsupported.Select(id => FalloutModCatalog.Get(id).Title))
            : string.Empty;
        _stackStatus.Visible = _stackStatus.Text.Length != 0;
        _launch.Disabled = !launchable || _launching;
        _launch.Text = _launching ? "Opening…" : !setupReady ? "Finish folder setup" : unsupported.Length != 0 || !campaign.Launchable
            ? "In development" : route?.PreviewOnly == true ? "Open source preview" : "Play  →";
        _launch.TooltipText = launchable ? "Play " + campaign.Title + " with the enabled mods · F1" : _stackStatus.Text;
    }

    private void ShowModStack()
    {
        if (_stackWindow is not null) { _stackWindow.GrabFocus(); return; }
        _stackWindow = new Window
        {
            Name = "ModLoadOrder",
            Title = "New Vegas · Mod load order",
            Size = new Vector2I(700, 540),
            MinSize = new Vector2I(600, 440),
            Transient = true,
            Exclusive = true,
            Theme = Theme,
            Borderless = true,
        };
        _stackWindow.CloseRequested += CloseStackWindow;
        _stackWindow.WindowInput += input =>
        {
            if (input is InputEventKey { Pressed: true, Keycode: Key.Escape }) CloseStackWindow();
        };
        AddChild(_stackWindow);
        var background = new ColorRect { Color = Surface, MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _stackWindow.AddChild(background);
        var margin = Margin("StackMargin", 22);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _stackWindow.AddChild(margin);
        var stack = Stack("StackDetails", 12);
        margin.AddChild(stack);
        stack.AddChild(Label("Enabled mods", 24, Ink, heading: true));
        var description = Label("Open Nevada orders enabled mods automatically using their required masters and reviewed rules. Patches follow their masters. Manual changes are optional.", 14, Muted);
        description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(description);
        _stackList = new ItemList { Name = "EnabledModOrder", SizeFlagsVertical = SizeFlags.ExpandFill };
        _stackList.AddThemeConstantOverride("v_separation", 10);
        stack.AddChild(_stackList);
        var custom = new CheckButton { Text = "Advanced: customize mod order", ButtonPressed = !_profiles.AutomaticModOrder("newvegas") };
        custom.Toggled += enabled =>
        {
            try { _profiles.SetAutomaticModOrder("newvegas", !enabled); }
            catch (Exception error) { ShowToast(error.Message); custom.SetPressedNoSignal(!_profiles.AutomaticModOrder("newvegas")); }
            RefreshStackWindow();
            Refresh();
        };
        stack.AddChild(custom);
        var actions = new HBoxContainer();
        _manualStackActions = actions;
        actions.AddThemeConstantOverride("separation", 8);
        foreach (var (title, move) in new[] { ("Move up", -1), ("Move down", 1), ("Disable", 0) })
        {
            var button = ActionButton(title, 38);
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.Pressed += () => ChangeStackOrder(move);
            actions.AddChild(button);
        }
        stack.AddChild(actions);
        var detail = Label("Shared dependency folders are read once at their last position. Each combination has separate saves. Selecting a mod in the library opens its folder settings.", 13, Muted);
        detail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(detail);
        var done = ActionButton("Done", 38);
        done.Pressed += CloseStackWindow;
        stack.AddChild(done);
        RefreshStackWindow();
        _stackWindow.PopupCentered();
    }

    private void RefreshStackWindow()
    {
        if (_stackList is null) return;
        _stackList.Clear();
        var ids = _profiles.EnabledMods("newvegas");
        if (_profiles.AutomaticModOrder("newvegas"))
        {
            try
            {
                var selection = _profiles.ModStack("newvegas");
                ids = selection is null ? [] : FalloutModLoadOrder.OrderMods(selection.Mods).Select(mod => mod.Id).ToArray();
            }
            catch (Exception error) when (error is IOException or ArgumentException or InvalidDataException)
            {
                ids = ids.OrderBy(FalloutModLoadOrder.ModPriority).ThenBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
        _displayedStackIds = ids;
        if (_manualStackActions is not null) _manualStackActions.Visible = !_profiles.AutomaticModOrder("newvegas");
        foreach (var id in ids)
        {
            var profile = _profiles.TryGet(id, out var registered) ? registered : null;
            var item = _stackList.AddItem(FalloutModCatalog.Get(id).Title + (profile is null ? "  ·  Choose folder" :
                $"  ·  {profile.DependencyRoots?.Count ?? 0} dependency / patch folders"));
            _stackList.SetItemTooltip(item, profile is null ? "Select this mod in the library to choose its folder." :
                string.Join("\n", new[] { profile.InstallRoot }.Concat(profile.DependencyRoots ?? [])));
        }
    }

    private void ChangeStackOrder(int move)
    {
        if (_stackList?.GetSelectedItems() is not { Length: 1 } selected) return;
        if (_profiles.AutomaticModOrder("newvegas")) return;
        var ids = _displayedStackIds.ToList();
        var index = selected[0];
        if (move == 0) ids.RemoveAt(index);
        else
        {
            var next = index + move;
            if (next < 0 || next >= ids.Count) return;
            (ids[index], ids[next]) = (ids[next], ids[index]);
            index = next;
        }
        try { _profiles.SetEnabledMods("newvegas", ids); }
        catch (Exception error) { ShowToast(error.Message); }
        Refresh();
        RefreshStackWindow();
        if (_stackList.ItemCount != 0) _stackList.Select(Math.Min(index, _stackList.ItemCount - 1));
    }

    private void CloseStackWindow()
    {
        _stackWindow?.QueueFree();
        _stackWindow = null;
        _stackList = null;
        _manualStackActions = null;
    }
}
