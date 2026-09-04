using Godot;

namespace OpenNV.Runtime.Diagnostics.Parity;

internal sealed partial class ParityDashboard : Control
{
    private readonly Label _summary = new();
    private readonly TextureRect _retail = FrameView();
    private readonly TextureRect _openNv = FrameView();
    private readonly TextureRect _difference = FrameView();

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var root = new VBoxContainer();
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(root);
        _summary.Text = "PARITY: waiting for matched retail and OpenNV frames";
        root.AddChild(_summary);
        var titles = new HBoxContainer();
        titles.AddChild(Title("RETAIL"));
        titles.AddChild(Title("OPENNV"));
        titles.AddChild(Title("ABSOLUTE DIFFERENCE"));
        root.AddChild(titles);
        var frames = new HBoxContainer();
        frames.SizeFlagsVertical = SizeFlags.ExpandFill;
        frames.AddChild(_retail);
        frames.AddChild(_openNv);
        frames.AddChild(_difference);
        root.AddChild(frames);
    }

    internal void Present(
        ParityComparison comparison,
        Image retail,
        Image openNv)
    {
        if (retail.IsEmpty() || openNv.IsEmpty() ||
            retail.GetWidth() != openNv.GetWidth() ||
            retail.GetHeight() != openNv.GetHeight() ||
            retail.GetFormat() is not (Image.Format.Rgb8 or Image.Format.Rgba8) ||
            openNv.GetFormat() != retail.GetFormat())
            throw new InvalidDataException(
                "Parity visualization requires native-size frames with the same RGB8 or RGBA8 format.");
        _retail.Texture = ImageTexture.CreateFromImage(retail);
        _openNv.Texture = ImageTexture.CreateFromImage(openNv);
        _difference.Texture = ImageTexture.CreateFromImage(Difference(retail, openNv));
        var pixels = retail.GetFormat() == Image.Format.Rgb8
            ? ParityPixelComparator.CompareRgb8(
                retail.GetWidth(), retail.GetHeight(), retail.GetData(), openNv.GetData())
            : ParityPixelComparator.CompareRgba8(
                retail.GetWidth(), retail.GetHeight(), retail.GetData(), openNv.GetData());
        var state = comparison.ComparableState
            ? comparison.ExactStateMatch ? "STATE BYTES EXACT" : "STATE BYTES DIFFER"
            : $"STATE UNALIGNED: {comparison.AlignmentFailure}";
        _summary.Text = $"{state} • fields {comparison.Deltas.Count} • " +
            (pixels.ExactBytes ? "PIXEL BYTES EXACT" :
                $"PIXELS DIFFER {pixels.DifferentPixels} • first byte {pixels.FirstByteOffset}") +
            " • final-frame correspondence unverified";
        _summary.Modulate = new Color(1.0f, 0.3f, 0.25f);
    }

    private static Image Difference(Image left, Image right)
    {
        var leftBytes = left.GetData();
        var rightBytes = right.GetData();
        var stride = left.GetFormat() == Image.Format.Rgb8 ? 3 : 4;
        if (leftBytes.Length != rightBytes.Length || leftBytes.Length % stride != 0)
            throw new InvalidDataException("Parity frame byte layouts differ.");
        var difference = new byte[leftBytes.Length];
        for (var offset = 0; offset < difference.Length; offset += stride)
        {
            var red = Math.Abs(leftBytes[offset] - rightBytes[offset]);
            var green = Math.Abs(leftBytes[offset + 1] - rightBytes[offset + 1]);
            var blue = Math.Abs(leftBytes[offset + 2] - rightBytes[offset + 2]);
            var alpha = stride == 4 ? Math.Abs(leftBytes[offset + 3] - rightBytes[offset + 3]) : 0;
            // Alpha is displayed as gray so alpha-only differences remain visible.
            difference[offset] = (byte)Math.Max(red, alpha);
            difference[offset + 1] = (byte)Math.Max(green, alpha);
            difference[offset + 2] = (byte)Math.Max(blue, alpha);
            if (stride == 4)
                difference[offset + 3] = byte.MaxValue;
        }
        return Image.CreateFromData(
            left.GetWidth(),
            left.GetHeight(),
            false,
            left.GetFormat(),
            difference);
    }

    private static TextureRect FrameView() => new()
    {
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        SizeFlagsVertical = SizeFlags.ExpandFill,
    };

    private static Label Title(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
    };
}
