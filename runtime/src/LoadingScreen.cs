using Godot;

namespace OpenNV.Runtime;

internal partial class LoadingScreen : CanvasLayer
{
    private Label _title = null!;
    private Label _status = null!;
    private Label _pulse = null!;
    private double _elapsed;
    private bool _failed;

    internal void Configure(string status)
    {
        Name = "OwnedDataLoadingScreen";
        Layer = 1000;

        var background = new ColorRect
        {
            Color = new Color(0.008f, 0.012f, 0.011f, 1.0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(680.0f, 260.0f),
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.018f, 0.030f, 0.026f, 0.96f),
            BorderColor = new Color(0.42f, 0.36f, 0.20f, 0.95f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 42.0f,
            ContentMarginTop = 34.0f,
            ContentMarginRight = 42.0f,
            ContentMarginBottom = 34.0f,
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        center.AddChild(panel);

        var content = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        content.AddThemeConstantOverride("separation", 15);
        panel.AddChild(content);

        _title = new Label
        {
            Text = "OPENNV  //  CLASSIC DIORAMA",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _title.AddThemeColorOverride("font_color", new Color(0.94f, 0.78f, 0.38f));
        _title.AddThemeFontSizeOverride("font_size", 30);
        content.AddChild(_title);

        _status = new Label
        {
            Text = status,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _status.AddThemeColorOverride("font_color", new Color(0.66f, 0.95f, 0.50f));
        _status.AddThemeFontSizeOverride("font_size", 20);
        content.AddChild(_status);

        _pulse = new Label
        {
            Text = "●  ○  ○",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _pulse.AddThemeColorOverride("font_color", new Color(0.66f, 0.95f, 0.50f));
        _pulse.AddThemeFontSizeOverride("font_size", 17);
        content.AddChild(_pulse);

        var policy = new Label
        {
            Text = "VERIFYING PLAYER-OWNED DATA  •  NO RETAIL ASSETS ARE PACKAGED",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        policy.AddThemeColorOverride("font_color", new Color(0.46f, 0.58f, 0.51f));
        policy.AddThemeFontSizeOverride("font_size", 14);
        content.AddChild(policy);
    }

    internal void SetStatus(string status)
    {
        _status.Text = status;
    }

    internal void SetTitle(string title)
    {
        _title.Text = title;
    }

    internal void ShowError(string message)
    {
        _failed = true;
        _title.Text = "OWNED-DATA LOAD FAILED";
        _title.AddThemeColorOverride("font_color", new Color(0.95f, 0.36f, 0.26f));
        _status.Text = message;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _pulse.Text = "PRESS ALT+F4 AFTER RECORDING THIS ERROR";
    }

    public override void _Process(double delta)
    {
        if (_failed)
            return;
        _elapsed += delta;
        var active = (int)(_elapsed * 3.0) % 3;
        _pulse.Text = active switch
        {
            0 => "●  ○  ○",
            1 => "○  ●  ○",
            _ => "○  ○  ●",
        };
    }
}
