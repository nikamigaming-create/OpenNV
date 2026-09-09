using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

internal static class NativeUiClip
{
    internal static void Draw(CanvasItem canvas, Texture2D texture, Rect2 destination, Rect2 source, Color color, Rect2? clip)
    {
        if (destination.Size.X <= 0 || destination.Size.Y <= 0) return;
        if (clip is { } bounds)
        {
            var visible = destination.Intersection(bounds);
            if (!visible.HasArea()) return;
            source = new(source.Position + (visible.Position - destination.Position) * source.Size / destination.Size,
                visible.Size * source.Size / destination.Size);
            destination = visible;
        }
        canvas.DrawTextureRectRegion(texture, destination, source, color);
    }
}
