using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class LegalAssetSetupView : CanvasLayer
{
    private readonly Label _status = new();
    private readonly Button _selectButton = new();
    private Action<string>? _selected;

    internal void Configure(
        string? restoreError,
        Action<string> selected,
        SetupViewConfiguration configuration)
    {
        _selected = selected;
        Name = "LegalAssetSetup";

        var background = new ColorRect { Color = configuration.BackgroundColorRgba.Color() };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);

        var content = new VBoxContainer
        {
            Position = configuration.ContentPositionPixels.Vector2(),
            Size = configuration.ContentSizePixels.Vector2(),
        };
        AddChild(content);
        var title = new Label { Text = configuration.Copy.Title };
        title.AddThemeFontSizeOverride("font_size", configuration.TitleFontSizePixels);
        content.AddChild(title);
        content.AddChild(new HSeparator());

        var body = new Label
        {
            Text = configuration.Copy.Body,
        };
        body.AddThemeFontSizeOverride("font_size", configuration.BodyFontSizePixels);
        content.AddChild(body);

        _selectButton.Text = configuration.Copy.SelectButton;
        _selectButton.CustomMinimumSize = new Vector2(0.0f, configuration.ButtonMinimumHeightPixels);
        content.AddChild(_selectButton);

        _status.Text = restoreError is null
            ? configuration.Copy.WaitingStatus
            : configuration.Copy.RebuildStatusPrefix + restoreError;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(0.0f, configuration.StatusMinimumHeightPixels);
        _status.AddThemeColorOverride("font_color", configuration.StatusColorRgba.Color());
        _status.AddThemeFontSizeOverride("font_size", configuration.StatusFontSizePixels);
        content.AddChild(_status);

        var dialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenDir,
            UseNativeDialog = true,
            ModeOverridesTitle = false,
            Title = configuration.Copy.DialogTitle,
        };
        dialog.DirSelected += dataRoot => _selected?.Invoke(dataRoot);
        AddChild(dialog);
        _selectButton.Pressed += () => dialog.PopupCenteredRatio(configuration.DialogCenteredRatio);
    }

    internal void SetPreparing()
    {
        _selectButton.Disabled = true;
        _status.Text = "Validating the installation and preparing the private cell cache. This can take several minutes...";
    }

    internal void ShowError(string message)
    {
        _status.Text = "That folder could not be prepared.\n" + message;
        _selectButton.Disabled = false;
    }
}
