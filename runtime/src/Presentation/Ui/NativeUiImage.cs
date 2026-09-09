using Godot;

namespace OpenNV.Runtime.Presentation.Ui;

/// <summary>Native image tiles crop a zoomed texture; only zoom -1 stretches it.</summary>
internal static class NativeUiImage
{
    internal readonly record struct SampleRegion(Rect2 Destination, Rect2 Source);

    internal static SampleRegion? Sample(Rect2 tile, Rect2 art, float zoom, Vector2 crop)
    {
        if (!float.IsFinite(zoom) || zoom < -1 || zoom is > -1 and < 0 || !crop.IsFinite())
            throw new InvalidDataException("Owned UI image zoom/crop is invalid.");
        if (zoom == 0 || !tile.HasArea()) return null;
        if (zoom == -1) return new(tile, art);
        var scale = zoom / 100;
        var image = new Rect2(tile.Position - crop, art.Size * scale);
        var visible = image.Intersection(tile);
        if (!visible.HasArea()) return null;
        return new(visible, new(art.Position + (visible.Position - image.Position) / scale, visible.Size / scale));
    }
}
