using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>
/// Asset-free terminal housing for the launcher. The product is a 2D Godot
/// interface, but its presentation still has a physical screen: hard square
/// bezel lines, recessed glass, screws, vents, and a restrained CRT pass.
/// This replaces the old decorative 3D slab, which competed with the actual
/// Fallout interface instead of framing it.
/// </summary>
internal sealed partial class NativeGodotLauncherSurface : Control
{
    private Color _accent = new(0.42f, 0.86f, 0.66f);

    internal void Configure(Color accent)
    {
        _accent = accent;
        Name = "OpenNVLauncherTerminal";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        SetMeta("opennv_launcher_surface", "godot-crt-terminal-frame-v2");
        SetMeta("opennv_launcher_assets", "none");
        SetMeta("opennv_launcher_button_count", 0);
        QueueRedraw();
    }

    internal void SetAccent(Color accent)
    {
        _accent = accent;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X <= 0.0f || size.Y <= 0.0f)
            return;

        var bounds = new Rect2(Vector2.Zero, size);
        DrawRect(bounds, new Color(0.018f, 0.021f, 0.019f));

        var frame = new Rect2(24.0f, 22.0f, Mathf.Max(0.0f, size.X - 48.0f), Mathf.Max(0.0f, size.Y - 44.0f));
        DrawRect(frame, new Color(0.16f, 0.17f, 0.15f));
        DrawRect(new Rect2(frame.Position + new Vector2(3.0f, 3.0f), frame.Size - new Vector2(6.0f, 6.0f)),
            new Color(0.055f, 0.063f, 0.058f));

        DrawLine(frame.Position, new Vector2(frame.End.X, frame.Position.Y), new Color(0.34f, 0.36f, 0.31f), 2.0f);
        DrawLine(frame.Position, new Vector2(frame.Position.X, frame.End.Y), new Color(0.3f, 0.32f, 0.28f), 2.0f);
        DrawLine(new Vector2(frame.Position.X, frame.End.Y), frame.End, new Color(0.035f, 0.04f, 0.035f), 3.0f);
        DrawLine(new Vector2(frame.End.X, frame.Position.Y), frame.End, new Color(0.035f, 0.04f, 0.035f), 3.0f);

        var screen = new Rect2(52.0f, 50.0f, Mathf.Max(0.0f, size.X - 104.0f), Mathf.Max(0.0f, size.Y - 100.0f));
        DrawRect(screen, new Color(0.015f, 0.025f, 0.021f));
        DrawRect(new Rect2(screen.Position - new Vector2(4.0f, 4.0f), screen.Size + new Vector2(8.0f, 8.0f)),
            new Color(0.025f, 0.029f, 0.026f), false, 2.0f);
        DrawLine(new Vector2(screen.Position.X, screen.End.Y), screen.End, new Color(0.0f, 0.0f, 0.0f, 0.8f), 4.0f);
        DrawLine(new Vector2(screen.End.X, screen.Position.Y), screen.End, new Color(0.0f, 0.0f, 0.0f, 0.8f), 4.0f);

        var scanline = new Color(_accent.R, _accent.G, _accent.B, 0.025f);
        for (var y = screen.Position.Y + 3.0f; y < screen.End.Y; y += 4.0f)
            DrawLine(new Vector2(screen.Position.X, y), new Vector2(screen.End.X, y), scanline, 1.0f);

        var gridLine = new Color(_accent.R, _accent.G, _accent.B, 0.016f);
        for (var x = screen.Position.X + 24.0f; x < screen.End.X; x += 64.0f)
            DrawLine(new Vector2(x, screen.Position.Y), new Vector2(x, screen.End.Y), gridLine, 1.0f);

        DrawScrew(new Vector2(frame.Position.X + 13.0f, frame.Position.Y + 13.0f));
        DrawScrew(new Vector2(frame.End.X - 13.0f, frame.Position.Y + 13.0f));
        DrawScrew(new Vector2(frame.Position.X + 13.0f, frame.End.Y - 13.0f));
        DrawScrew(new Vector2(frame.End.X - 13.0f, frame.End.Y - 13.0f));
        DrawVents(new Vector2(frame.End.X - 42.0f, frame.Position.Y + 28.0f), frame.Size.Y - 56.0f);

        SetMeta("opennv_launcher_surface_depth_meters", 0.0f);
    }

    private void DrawScrew(Vector2 center)
    {
        DrawCircle(center, 4.0f, new Color(0.045f, 0.05f, 0.045f));
        DrawCircle(center, 2.5f, new Color(0.22f, 0.23f, 0.2f));
        DrawLine(center - new Vector2(1.8f, 1.8f), center + new Vector2(1.8f, 1.8f),
            new Color(0.04f, 0.045f, 0.04f), 1.0f);
        DrawLine(center + new Vector2(1.8f, -1.8f), center - new Vector2(1.8f, -1.8f),
            new Color(0.04f, 0.045f, 0.04f), 1.0f);
    }

    private void DrawVents(Vector2 start, float height)
    {
        var ventColor = new Color(0.025f, 0.029f, 0.026f);
        var count = Mathf.Max(1, Mathf.FloorToInt(height / 13.0f));
        for (var index = 0; index < count; index++)
        {
            var y = start.Y + index * 13.0f;
            DrawLine(new Vector2(start.X, y), new Vector2(start.X + 20.0f, y), ventColor, 3.0f);
            DrawLine(new Vector2(start.X, y - 1.0f), new Vector2(start.X + 20.0f, y - 1.0f),
                new Color(0.25f, 0.26f, 0.23f, 0.28f), 1.0f);
        }
    }
}
