using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed partial class NativeGamebryoCredits : Control
{
    private readonly List<CreditDraw> _draws = [];
    private readonly Dictionary<int, NativeBitmapFontAsset> _fonts = [];
    private readonly Action _closed;
    private readonly float _scrollSpeed;
    private readonly float _height;
    private float _scroll;

    internal NativeGamebryoCredits(FalloutInstallationSettings settings, Action closed)
    {
        Name = "CreditsMenu";
        _closed = closed;
        MouseFilter = MouseFilterEnum.Stop;
        TextureFilter = TextureFilterEnum.Linear;
        _scrollSpeed = settings.Number("Menu", "fCreditsScrollSpeed");
        var source = RuntimeLiveContentSource.Current!;
        if (!source.TryRead("Credits.txt", null, out var bytes, out _))
            throw new FileNotFoundException("Owned Credits.txt is missing.");
        var x = 0.0f;
        var y = 0.0f;
        var fontId = 7;
        var justify = 'L';
        var color = Colors.White;
        // The owned credits file documents its command language in its header.
        // Text and images remain source content and are built only in memory.
        foreach (var line in Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text.TrimStart().StartsWith('*')) continue;
            var advancedByImage = false;
            foreach (Match token in Regex.Matches(text, "<[A-Z]:[^>]*>|[^<]+"))
            {
                var value = token.Value;
                if (!value.StartsWith('<'))
                {
                    var font = Font(fontId);
                    var width = font.Font.Measure(value);
                    var offset = justify == 'C' ? width / 2 : justify == 'R' ? width : 0;
                    _draws.Add(new CreditDraw(new Vector2(x - offset, y), font.Font.Height, value, font, null, Vector2.Zero, color));
                    continue;
                }
                var argument = value[3..^1];
                switch (value[1])
                {
                    case 'X': x = Coordinate(x, argument); break;
                    case 'Y': y = Coordinate(y, argument); break;
                    case 'F': fontId = int.Parse(argument, CultureInfo.InvariantCulture); _ = Font(fontId); break;
                    case 'J':
                        if (argument is not ("L" or "C" or "R")) throw new InvalidDataException("Unknown credits justification.");
                        justify = argument[0]; break;
                    case 'C':
                        var channels = argument.Split(',').Select(part => byte.Parse(part, CultureInfo.InvariantCulture)).ToArray();
                        if (channels.Length != 4) throw new InvalidDataException("Credits color requires four bytes.");
                        color = Color.Color8(channels[0], channels[1], channels[2], channels[3]); break;
                    case 'I':
                        var fields = argument.Split(',', 4);
                        if (fields.Length != 4) throw new InvalidDataException("Invalid credits image command.");
                        var size = new Vector2(Number(fields[0]), Number(fields[1]));
                        if (size.X <= 0 || size.Y <= 0) throw new InvalidDataException("Credits image size must be positive.");
                        var alpha = byte.Parse(fields[2], CultureInfo.InvariantCulture);
                        var image = NativeOwnedMediaLoader.LoadTexture("textures\\interface\\credits\\" + fields[3]);
                        var left = x - (justify == 'C' ? size.X / 2 : justify == 'R' ? size.X : 0);
                        _draws.Add(new CreditDraw(new Vector2(left, y), size.Y, null, null, image, size, Color.Color8(255, 255, 255, alpha)));
                        y += size.Y;
                        advancedByImage = true;
                        break;
                    default: throw new NotSupportedException($"Unsupported owned credits command: {value[1]}.");
                }
            }
            if (!advancedByImage) y += Font(fontId).Font.Height;
        }
        _height = y;
        NativeBitmapFontAsset Font(int id)
        {
            if (!_fonts.TryGetValue(id, out var font)) _fonts.Add(id, font = NativeBitmapFontAsset.Read(settings, id));
            return font;
        }
    }

    public override void _Process(double delta)
    {
        Size = GetViewportRect().Size;
        _scroll += (float)delta * _scrollSpeed;
        QueueRedraw();
        if (_scroll > _height + 960) Close();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Color.Color8(8, 8, 20));
        var scale = Size.Y / 960;
        if (scale <= 0) return;
        DrawSetTransform(new Vector2(Size.X / 2, Size.Y - _scroll * scale), 0, Vector2.One * scale);
        foreach (var draw in _draws)
        {
            var top = 960 - _scroll + draw.Position.Y;
            if (top > 960 || top + draw.Height < 0) continue;
            if (draw.Image is not null) DrawTextureRect(draw.Image, new Rect2(draw.Position, draw.Size), false, draw.Color);
            else draw.Font!.Draw(this, draw.Position, draw.Text!, draw.Color);
        }
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    private void Close() { if (!IsQueuedForDeletion()) { _closed(); QueueFree(); } }
    private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);
    private static float Coordinate(float current, string value) => value.StartsWith('+') || value.StartsWith('-') ? current + Number(value) : Number(value);
    private sealed record CreditDraw(Vector2 Position, float Height, string? Text, NativeBitmapFontAsset? Font, Texture2D? Image, Vector2 Size, Color Color);
}
