using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGodotLauncher
{
    private void BuildAdditionalFolders(VBoxContainer stack)
    {
        _additionalFolders = new ItemList
        {
            Name = "AdditionalModFolders",
            CustomMinimumSize = new Vector2(0, 118),
            TooltipText = "Dependencies and patches. Lower folders override matching files above them and in the main mod.",
        };
        _additionalFolders.AddThemeFontSizeOverride("font_size", 14);
        _additionalFolders.AddThemeConstantOverride("v_separation", 6);
        _additionalFolders.ItemSelected += _ => RefreshFolderActions();
        stack.AddChild(_additionalFolders);
        _advancedFolderOrder = new CheckButton { Text = "Advanced: folder priority" };
        _advancedFolderOrder.AddThemeFontSizeOverride("font_size", 13);
        _advancedFolderOrder.Toggled += expanded => _additionalActions.Visible = expanded;
        stack.AddChild(_advancedFolderOrder);
        _additionalActions = new HBoxContainer { Name = "AdditionalFolderActions" };
        _additionalActions.AddThemeConstantOverride("separation", 6);
        foreach (var (title, change) in new[] { ("Move up", -1), ("Move down", 1), ("Remove", 0) })
        {
            var button = ActionButton(title, 32);
            button.Name = "ModFolder_" + change;
            button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            button.Pressed += () => ChangeAdditionalFolder(change);
            _additionalActions.AddChild(button);
        }
        stack.AddChild(_additionalActions);
    }

    private void RefreshAdditionalFolders(string id)
    {
        var visible = FalloutModInstallation.IsMod(id);
        _additionalFolders.Visible = visible;
        _additionalActions.Visible = visible && _advancedFolderOrder.ButtonPressed;
        _additionalFolders.Clear();
        if (visible && _profiles.TryGet(id, out var profile))
            foreach (var path in profile.DependencyRoots ?? [])
            {
                var index = _additionalFolders.AddItem(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)));
                _additionalFolders.SetItemTooltip(index, path);
            }
        RefreshFolderActions();
    }

    private void RefreshFolderActions()
    {
        var selected = _additionalFolders.GetSelectedItems();
        var index = selected.Length == 1 ? selected[0] : -1;
        _additionalActions.GetChild<Button>(0).Disabled = index <= 0;
        _additionalActions.GetChild<Button>(1).Disabled = index < 0 || index >= _additionalFolders.ItemCount - 1;
        _additionalActions.GetChild<Button>(2).Disabled = index < 0;
    }

    private void ChangeAdditionalFolder(int move)
    {
        if (!_profiles.TryGet(_selectedId, out var profile)) return;
        var selected = _additionalFolders.GetSelectedItems();
        if (selected.Length != 1) return;
        var roots = (profile.DependencyRoots ?? []).ToList();
        var index = selected[0];
        if (move == 0) roots.RemoveAt(index);
        else
        {
            var next = index + move;
            if (next < 0 || next >= roots.Count) return;
            (roots[index], roots[next]) = (roots[next], roots[index]);
            index = next;
        }
        try
        {
            _profiles.Save(profile.CampaignId, profile.InstallRoot, profile.BaseInstallRoot, roots);
            Refresh();
            if (roots.Count != 0) _additionalFolders.Select(Math.Min(index, roots.Count - 1));
            RefreshFolderActions();
        }
        catch (Exception error) { ShowToast(error.Message); }
    }
}
