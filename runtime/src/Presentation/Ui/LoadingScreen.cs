using Godot;


namespace OpenNV.Runtime.Presentation.Ui;

internal static class LoadingScreenNumericContracts
{
    // Immutable format, source-art, geometry, and acceptance contracts.
    // Runtime-tunable Fallout 1 behavior remains in the versioned runtime recipe.
    internal const float PresentationFloat0Point008f = 0.008f;
    internal const float PresentationFloat0Point011f = 0.011f;
    internal const float PresentationFloat0Point012f = 0.012f;
    internal const float PresentationFloat0Point018f = 0.018f;
    internal const float PresentationFloat0Point026f = 0.026f;
    internal const float PresentationFloat0Point030f = 0.030f;
    internal const float PresentationFloat0Point20f = 0.20f;
    internal const float PresentationFloat0Point26f = 0.26f;
    internal const float PresentationFloat0Point36f = 0.36f;
    internal const float PresentationFloat0Point38f = 0.38f;
    internal const float PresentationFloat0Point42f = 0.42f;
    internal const float PresentationFloat0Point46f = 0.46f;
    internal const float PresentationFloat0Point50f = 0.50f;
    internal const float PresentationFloat0Point51f = 0.51f;
    internal const float PresentationFloat0Point58f = 0.58f;
    internal const float PresentationFloat0Point66f = 0.66f;
    internal const float PresentationFloat0Point78f = 0.78f;
    internal const float PresentationFloat0Point94f = 0.94f;
    internal const float PresentationFloat0Point95f = 0.95f;
    internal const float PresentationFloat0Point96f = 0.96f;
    internal const int PresentationInt1000 = 1000;
    internal const int PresentationInt14 = 14;
    internal const int PresentationInt15 = 15;
    internal const int PresentationInt17 = 17;
    internal const int PresentationInt20 = 20;
    internal const float PresentationFloat260Point0f = 260.0f;
    internal const int PresentationInt30 = 30;
    internal const float PresentationFloat34Point0f = 34.0f;
    internal const float PresentationFloat42Point0f = 42.0f;
    internal const float PresentationFloat680Point0f = 680.0f;
}

internal partial class LoadingScreen : CanvasLayer
{
    private Label _title = null!;
    private Label _status = null!;
    private Label _pulse = null!;
    private double _elapsed;
    private bool _failed;

    internal void Configure(string status)
    {
        Name = "LiveRetailLoadingScreen";
        Layer = LoadingScreenNumericContracts.PresentationInt1000;

        var background = new ColorRect
        {
            Color = new Color(LoadingScreenNumericContracts.PresentationFloat0Point008f, LoadingScreenNumericContracts.PresentationFloat0Point012f, LoadingScreenNumericContracts.PresentationFloat0Point011f, 1.0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(LoadingScreenNumericContracts.PresentationFloat680Point0f, LoadingScreenNumericContracts.PresentationFloat260Point0f),
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(LoadingScreenNumericContracts.PresentationFloat0Point018f, LoadingScreenNumericContracts.PresentationFloat0Point030f, LoadingScreenNumericContracts.PresentationFloat0Point026f, LoadingScreenNumericContracts.PresentationFloat0Point96f),
            BorderColor = new Color(LoadingScreenNumericContracts.PresentationFloat0Point42f, LoadingScreenNumericContracts.PresentationFloat0Point36f, LoadingScreenNumericContracts.PresentationFloat0Point20f, LoadingScreenNumericContracts.PresentationFloat0Point95f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = LoadingScreenNumericContracts.PresentationFloat42Point0f,
            ContentMarginTop = LoadingScreenNumericContracts.PresentationFloat34Point0f,
            ContentMarginRight = LoadingScreenNumericContracts.PresentationFloat42Point0f,
            ContentMarginBottom = LoadingScreenNumericContracts.PresentationFloat34Point0f,
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        center.AddChild(panel);

        var content = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        content.AddThemeConstantOverride("separation", LoadingScreenNumericContracts.PresentationInt15);
        panel.AddChild(content);

        _title = new Label
        {
            Text = "OPENNV  //  CLASSIC DIORAMA",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _title.AddThemeColorOverride("font_color", new Color(LoadingScreenNumericContracts.PresentationFloat0Point94f, LoadingScreenNumericContracts.PresentationFloat0Point78f, LoadingScreenNumericContracts.PresentationFloat0Point38f));
        _title.AddThemeFontSizeOverride("font_size", LoadingScreenNumericContracts.PresentationInt30);
        content.AddChild(_title);

        _status = new Label
        {
            Text = status,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _status.AddThemeColorOverride("font_color", new Color(LoadingScreenNumericContracts.PresentationFloat0Point66f, LoadingScreenNumericContracts.PresentationFloat0Point95f, LoadingScreenNumericContracts.PresentationFloat0Point50f));
        _status.AddThemeFontSizeOverride("font_size", LoadingScreenNumericContracts.PresentationInt20);
        content.AddChild(_status);

        _pulse = new Label
        {
            Text = "●  ○  ○",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _pulse.AddThemeColorOverride("font_color", new Color(LoadingScreenNumericContracts.PresentationFloat0Point66f, LoadingScreenNumericContracts.PresentationFloat0Point95f, LoadingScreenNumericContracts.PresentationFloat0Point50f));
        _pulse.AddThemeFontSizeOverride("font_size", LoadingScreenNumericContracts.PresentationInt17);
        content.AddChild(_pulse);

        var policy = new Label
        {
            Text = "INDEXING LIVE RETAIL FILES IN MEMORY  •  NO DERIVED CONTENT IS WRITTEN",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        policy.AddThemeColorOverride("font_color", new Color(LoadingScreenNumericContracts.PresentationFloat0Point46f, LoadingScreenNumericContracts.PresentationFloat0Point58f, LoadingScreenNumericContracts.PresentationFloat0Point51f));
        policy.AddThemeFontSizeOverride("font_size", LoadingScreenNumericContracts.PresentationInt14);
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
        _title.AddThemeColorOverride("font_color", new Color(LoadingScreenNumericContracts.PresentationFloat0Point95f, LoadingScreenNumericContracts.PresentationFloat0Point36f, LoadingScreenNumericContracts.PresentationFloat0Point26f));
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
