using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal partial class LoadingScreen : CanvasLayer
{
    private Label _title = null!;
    private LoadingProgressIndicator _progress = null!;

    internal void Configure(string status)
    {
        Name = "LiveRetailLoadingScreen";
        Layer = 1000;
        ProcessMode = ProcessModeEnum.Always;
        var background = new ColorRect
        {
            Color = new Color(0.008f, 0.012f, 0.011f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(600, 200) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.018f, 0.030f, 0.026f),
            BorderColor = new Color(0.42f, 0.36f, 0.20f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 42,
            ContentMarginRight = 42,
            ContentMarginTop = 34,
            ContentMarginBottom = 34,
        });
        center.AddChild(panel);
        var content = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        content.AddThemeConstantOverride("separation", 24);
        panel.AddChild(content);
        _title = new Label { Text = "OpenNV", HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeColorOverride("font_color", new Color(0.94f, 0.78f, 0.38f));
        _title.AddThemeFontSizeOverride("font_size", 30);
        content.AddChild(_title);
        _progress = new LoadingProgressIndicator(status);
        content.AddChild(_progress);
    }

    internal void SetStatus(string status) => _progress.SetStatus(status);
    internal void SetTitle(string title) => _title.Text = title;
    internal object State => _progress.State;

    internal void ShowError(string message)
    {
        _title.Text = "Unable to load";
        _title.AddThemeColorOverride("font_color", new Color(0.95f, 0.36f, 0.26f));
        _progress.ShowError(message);
    }
}
