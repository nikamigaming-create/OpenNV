using Godot;

namespace OpenNV.Runtime;

internal partial class LegalAssetSetupView : CanvasLayer
{
    private readonly Label _status = new();
    private readonly Button _selectButton = new();
    private Action<string>? _selected;

    internal void Configure(string? restoreError, Action<string> selected)
    {
        _selected = selected;
        Name = "LegalAssetSetup";

        var background = new ColorRect { Color = new Color(0.025f, 0.045f, 0.07f) };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);

        var content = new VBoxContainer
        {
            Position = new Vector2(64.0f, 64.0f),
            Size = new Vector2(760.0f, 460.0f),
        };
        AddChild(content);
        var title = new Label { Text = "OPEN NEVADA  /  EXPERIMENTAL GODOT RUNTIME" };
        title.AddThemeFontSizeOverride("font_size", 28);
        content.AddChild(title);
        content.AddChild(new HSeparator());

        var body = new Label
        {
            Text = "Select your legal Fallout: New Vegas Data folder to prepare the first\n" +
                   "data-driven interior cell. Python and external engine runtimes are not required.\n\n" +
                   "No game assets are included, and your installation is never modified.",
        };
        body.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(body);

        _selectButton.Text = "Select Fallout: New Vegas Data folder";
        _selectButton.CustomMinimumSize = new Vector2(0.0f, 48.0f);
        content.AddChild(_selectButton);

        _status.Text = restoreError is null
            ? "Waiting for a legal Data folder."
            : "The previous cache could not be reopened. Select the legal Data folder to rebuild it.";
        _status.AddThemeColorOverride("font_color", new Color(0.70f, 0.80f, 0.90f));
        _status.AddThemeFontSizeOverride("font_size", 16);
        content.AddChild(_status);

        var dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenDir,
            UseNativeDialog = true,
            ModeOverridesTitle = false,
            Title = "Select Fallout: New Vegas Data folder",
        };
        dialog.DirSelected += dataRoot => _selected?.Invoke(dataRoot);
        AddChild(dialog);
        _selectButton.Pressed += () => dialog.PopupCenteredRatio(0.8f);
    }

    internal void SetPreparing()
    {
        _selectButton.Disabled = true;
        _status.Text = "Validating the installation and preparing the private cell cache...";
    }

    internal void ShowError()
    {
        _status.Text = "That folder could not be prepared. Select the Fallout: New Vegas Data folder and try again.";
        _selectButton.Disabled = false;
    }
}
