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
            retail.GetFormat() != Image.Format.Rgba8 ||
            openNv.GetFormat() != Image.Format.Rgba8)
            throw new InvalidDataException(
                "Parity visualization requires matched nonempty RGBA8 frames.");
        _retail.Texture = ImageTexture.CreateFromImage(retail);
        _openNv.Texture = ImageTexture.CreateFromImage(openNv);
        _difference.Texture = ImageTexture.CreateFromImage(Difference(retail, openNv));
        _summary.Text = comparison.ExactStateMatch
            ? $"PARITY EXACT • tick Δ {comparison.SimulationTickDelta} • time Δ {comparison.MonotonicNanosecondsDelta} ns"
            : $"PARITY DIVERGED • state byte {comparison.FirstStateByteOffset?.ToString() ?? "identity"} • " +
              $"fields {comparison.Deltas.Count} • tick Δ {comparison.SimulationTickDelta} • " +
              $"time Δ {comparison.MonotonicNanosecondsDelta} ns";
        _summary.Modulate = comparison.ExactStateMatch
            ? new Color(0.35f, 1.0f, 0.45f)
            : new Color(1.0f, 0.3f, 0.25f);
    }

    private static Image Difference(Image left, Image right)
    {
        var leftBytes = left.GetData();
        var rightBytes = right.GetData();
        if (leftBytes.Length != rightBytes.Length || leftBytes.Length % 4 != 0)
            throw new InvalidDataException("Parity frame byte layouts differ.");
        var difference = new byte[leftBytes.Length];
        for (var offset = 0; offset < difference.Length; offset += 4)
        {
            var red = Math.Abs(leftBytes[offset] - rightBytes[offset]);
            var green = Math.Abs(leftBytes[offset + 1] - rightBytes[offset + 1]);
            var blue = Math.Abs(leftBytes[offset + 2] - rightBytes[offset + 2]);
            difference[offset] = (byte)Math.Max(red, Math.Max(green, blue));
            difference[offset + 1] = (byte)(green / 4);
            difference[offset + 2] = (byte)(blue / 4);
            difference[offset + 3] = byte.MaxValue;
        }
        return Image.CreateFromData(
            left.GetWidth(),
            left.GetHeight(),
            false,
            Image.Format.Rgba8,
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
