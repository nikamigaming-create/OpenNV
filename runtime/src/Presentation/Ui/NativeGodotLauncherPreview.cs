using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>
/// The center glass of the launcher terminal. When a classic install is
/// registered this displays a decoded, read-only source FRM. Without an
/// install it still provides a deliberate terminal silhouette instead of a
/// fake web-card illustration.
/// </summary>
internal sealed partial class NativeGodotLauncherPreview : PanelContainer
{
    private readonly Font _font = new SystemFont { FontNames = ["Consolas"] };
    private NativeGodotLauncherPreviewCanvas _canvas = null!;
    private Label _caption = null!;
    private Label _source = null!;
    private Color _accent = new(0.42f, 0.86f, 0.66f);
    private bool _configured;

    internal void Configure(Color accent)
    {
        if (_configured)
            return;
        _configured = true;
        _accent = accent;
        Name = "SourceCharacterPreview";
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        TextureFilter = TextureFilterEnum.Nearest;
        AddThemeStyleboxOverride("panel", FrameStyle(_accent));

        var stack = new VBoxContainer { Name = "SourcePreviewStack" };
        stack.AddThemeConstantOverride("separation", 6);
        AddChild(stack);

        _source = Label("SOURCE PROFILE", 11, new Color(0.55f, 0.68f, 0.6f));
        stack.AddChild(_source);

        _canvas = new NativeGodotLauncherPreviewCanvas();
        _canvas.Name = "SourcePreviewGlass";
        _canvas.CustomMinimumSize = new Vector2(0, 212);
        _canvas.SizeFlagsVertical = SizeFlags.ExpandFill;
        _canvas.Configure(_accent);
        stack.AddChild(_canvas);

        _caption = Label("NO OWNED CLASSIC ART LOADED", 10, new Color(0.53f, 0.67f, 0.59f));
        _caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        stack.AddChild(_caption);

        stack.AddChild(Label("S.P.E.C.I.A.L.  //  PREVIEW STATE", 11, _accent));
        var stats = new GridContainer
        {
            Name = "SpecialPreview",
            Columns = 2,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        stats.AddThemeConstantOverride("h_separation", 9);
        stats.AddThemeConstantOverride("v_separation", 3);
        foreach (var (name, value) in new[]
        {
            ("STR", 4), ("PER", 5), ("END", 4), ("CHA", 6),
            ("INT", 7), ("AGI", 6), ("LCK", 5),
        })
        {
            stats.AddChild(Label(name, 10, new Color(0.64f, 0.75f, 0.66f)));
            var bar = new ProgressBar
            {
                Name = $"Special_{name}",
                Value = value * 10.0,
                MaxValue = 100.0,
                ShowPercentage = false,
                CustomMinimumSize = new Vector2(62, 8),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            bar.AddThemeStyleboxOverride("background", BarStyle(new Color(0.05f, 0.09f, 0.075f)));
            bar.AddThemeStyleboxOverride("fill", BarStyle(_accent));
            stats.AddChild(bar);
        }
        stack.AddChild(stats);
    }

    internal void SetAccent(Color accent)
    {
        _accent = accent;
        if (!_configured)
            return;
        AddThemeStyleboxOverride("panel", FrameStyle(_accent));
        _canvas.Configure(_accent);
        _canvas.SetAccent(_accent);
    }

    internal void SetSourceFrame(Texture2D? frame, string caption, string sourceName)
    {
        if (!_configured)
            return;
        _canvas.SetSourceFrame(frame);
        _caption.Text = caption;
        _source.Text = sourceName;
        _caption.AddThemeColorOverride("font_color", frame is null
            ? new Color(0.55f, 0.67f, 0.59f)
            : new Color(0.5f, 0.83f, 0.61f));
    }

    private Label Label(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontOverride("font", _font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static StyleBoxFlat FrameStyle(Color accent) => new()
    {
        BgColor = new Color(0.025f, 0.045f, 0.037f, 0.96f),
        BorderColor = accent.Darkened(0.5f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        ContentMarginLeft = 12,
        ContentMarginTop = 11,
        ContentMarginRight = 12,
        ContentMarginBottom = 11,
    };

    private static StyleBoxFlat BarStyle(Color color) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = 0,
        CornerRadiusTopRight = 0,
        CornerRadiusBottomLeft = 0,
        CornerRadiusBottomRight = 0,
    };
}

internal sealed partial class NativeGodotLauncherPreviewCanvas : Control
{
    private Texture2D? _sourceFrame;
    private Color _accent = new(0.42f, 0.86f, 0.66f);

    internal void Configure(Color accent)
    {
        _accent = accent;
        MouseFilter = MouseFilterEnum.Ignore;
        SetMeta("opennv_launcher_preview", "owned-classic-frm-or-terminal-fallback-v1");
        QueueRedraw();
    }

    internal void SetAccent(Color accent)
    {
        _accent = accent;
        QueueRedraw();
    }

    internal void SetSourceFrame(Texture2D? frame)
    {
        _sourceFrame = frame;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0.0f || size.Y <= 0.0f)
            return;

        var glass = new Rect2(Vector2.Zero, size);
        DrawRect(glass, new Color(0.012f, 0.025f, 0.019f));
        DrawRect(new Rect2(Vector2.One * 5.0f, size - Vector2.One * 10.0f),
            new Color(_accent.R, _accent.G, _accent.B, 0.13f), false, 1.0f);

        if (_sourceFrame is not null)
            DrawSourceFrame(size);
        else
            DrawFallbackPortrait(size);

        var scanline = new Color(_accent.R, _accent.G, _accent.B, 0.045f);
        for (var y = 3.0f; y < size.Y; y += 4.0f)
            DrawLine(new Vector2(0.0f, y), new Vector2(size.X, y), scanline, 1.0f);
    }

    private void DrawSourceFrame(Vector2 size)
    {
        var sourceSize = _sourceFrame!.GetSize();
        var available = size - new Vector2(16.0f, 16.0f);
        var scale = Mathf.Min(available.X / sourceSize.X, available.Y / sourceSize.Y);
        var drawSize = sourceSize * scale;
        var position = (size - drawSize) * 0.5f;
        DrawTextureRect(_sourceFrame, new Rect2(position, drawSize), false, Colors.White);
    }

    private void DrawFallbackPortrait(Vector2 size)
    {
        var center = new Vector2(size.X * 0.5f, size.Y * 0.43f);
        var scale = Mathf.Min(size.X / 190.0f, size.Y / 245.0f);
        var glow = new Color(_accent.R, _accent.G, _accent.B, 0.09f);
        var ink = new Color(_accent.R, _accent.G, _accent.B, 0.72f);
        var faint = new Color(_accent.R, _accent.G, _accent.B, 0.22f);

        for (var radius = 28.0f; radius < 120.0f; radius += 23.0f)
            DrawArc(center, radius * scale, 0.1f, Mathf.Pi - 0.1f, 36, faint, 1.0f);
        DrawLine(new Vector2(size.X * 0.1f, center.Y), new Vector2(size.X * 0.9f, center.Y), faint, 1.0f);
        DrawLine(new Vector2(center.X, 14.0f), new Vector2(center.X, size.Y - 14.0f), faint, 1.0f);

        DrawCircle(center + new Vector2(0.0f, -60.0f * scale), 28.0f * scale, glow);
        DrawCircle(center + new Vector2(0.0f, -60.0f * scale), 23.0f * scale, new Color(0.02f, 0.08f, 0.058f));
        DrawArc(center + new Vector2(0.0f, -60.0f * scale), 23.0f * scale, 0.0f, Mathf.Tau, 32, ink, 2.0f);

        var shoulders = new[]
        {
            center + new Vector2(-69.0f, 78.0f) * scale,
            center + new Vector2(69.0f, 78.0f) * scale,
            center + new Vector2(92.0f, 132.0f) * scale,
            center + new Vector2(-92.0f, 132.0f) * scale,
        };
        DrawPolygon(shoulders, new[] { new Color(_accent.R, _accent.G, _accent.B, 0.12f) });
        DrawPolyline(shoulders, ink, 2.0f, true);

        DrawLine(center + new Vector2(-16.0f, -60.0f) * scale,
            center + new Vector2(-5.0f, -60.0f) * scale, ink, 2.0f);
        DrawLine(center + new Vector2(5.0f, -60.0f) * scale,
            center + new Vector2(16.0f, -60.0f) * scale, ink, 2.0f);
        DrawLine(center + new Vector2(-15.0f, -42.0f) * scale,
            center + new Vector2(15.0f, -42.0f) * scale, ink, 2.0f);

        var baseline = size.Y - 18.0f;
        for (var x = 18.0f; x < size.X - 18.0f; x += 18.0f)
            DrawLine(new Vector2(x, baseline), new Vector2(x + 9.0f, baseline), faint, 2.0f);
    }
}
