using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Campaigns.Classic.Native;

internal sealed class ClassicArtCache(Func<string, byte[]> read)
{
    private readonly byte[] _palette = read("color.pal");
    private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (ImageTexture Texture, Fallout1NativeFrmFrame Frame)> _frames = [];
    private readonly Dictionary<string, ImageTexture> _floors = [];
    private readonly Dictionary<string, ImageTexture> _floorNormals = [];
    internal int DecodedFrames => _frames.Count;

    internal byte[] Read(string path)
    {
        if (!_bytes.TryGetValue(path, out var bytes)) _bytes.Add(path, bytes = read(path));
        return bytes;
    }

    internal (ImageTexture Texture, Fallout1NativeFrmFrame Frame) Frame(string path, int rotation = 0, int index = 0)
    {
        var extension = Path.GetExtension(path);
        if (extension.Length == 4 && extension.StartsWith(".fr", StringComparison.OrdinalIgnoreCase) && extension[3] is >= '0' and <= '5')
            path = path[..^1] + rotation.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var key = $"{path.ToLowerInvariant()}:{rotation}:{index}";
        if (_frames.TryGetValue(key, out var result)) return result;
        var frame = Fallout1NativeFrmReader.ReadFrame(Read(path), rotation, index);
        if (_palette.Length < 768) throw new InvalidDataException("Classic COLOR.PAL is truncated.");
        var rgba = new byte[frame.PaletteIndexes.Length * 4];
        for (var pixel = 0; pixel < frame.PaletteIndexes.Length; pixel++)
        {
            var color = frame.PaletteIndexes[pixel];
            for (var channel = 0; channel < 3; channel++)
                rgba[pixel * 4 + channel] = (byte)Math.Min(255, _palette[color * 3 + channel] * 4);
            rgba[pixel * 4 + 3] = color == 0 ? (byte)0 : (byte)255;
        }
        using var image = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, rgba);
        result = (ImageTexture.CreateFromImage(image), frame);
        _frames.Add(key, result);
        return result;
    }

    internal ImageTexture Floor(string path, int size, bool extendPadding = true)
    {
        var key = $"{path.ToLowerInvariant()}:{size}:{extendPadding}";
        if (_floors.TryGetValue(key, out var cached)) return cached;
        if (size < 4) throw new InvalidDataException("Classic floor sampling extent is invalid.");
        using var source = Frame(path).Texture.GetImage();
        if (source.GetWidth() != 80 || source.GetHeight() != 36)
            throw new InvalidDataException($"Source floor has an unsupported projection extent: {path}");
        using var result = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var filled = new bool[size * size]; var queue = new Queue<int>();
        // The source diamond's four tips map to the world quad's four corners.
        // Drawing its bounding rectangle would leave holes between every tile.
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var u = x / (float)(size - 1); var v = y / (float)(size - 1);
                var sample = ClassicMapProjection.FloorSample(u, v);
                var color = source.GetPixel(Math.Clamp((int)Math.Round(sample.X), 0, 79), Math.Clamp((int)Math.Round(sample.Y), 0, 35));
                result.SetPixel(x, y, color);
                if (color.A > 0.99f) { filled[y * size + x] = true; queue.Enqueue(y * size + x); }
            }
        // An all-transparent source tile is valid (for example fom1000 in
        // HALLDED's roof layer). It contributes no pixels and needs no padding.
        // Extend edge texels through rasterization padding. MAP floor IDs own
        // occupancy; this in-memory sampling repair never changes walkability.
        while (extendPadding && queue.TryDequeue(out var pixel))
        {
            var x = pixel % size; var y = pixel / size;
            foreach (var neighbor in new[] { new Vector2I(x - 1, y), new Vector2I(x + 1, y), new Vector2I(x, y - 1), new Vector2I(x, y + 1) })
            {
                if (neighbor.X < 0 || neighbor.X >= size || neighbor.Y < 0 || neighbor.Y >= size) continue;
                var at = neighbor.Y * size + neighbor.X;
                if (filled[at]) continue;
                result.SetPixel(neighbor.X, neighbor.Y, result.GetPixel(x, y)); filled[at] = true; queue.Enqueue(at);
            }
        }
        result.GenerateMipmaps();
        cached = ImageTexture.CreateFromImage(result); _floors.Add(key, cached); return cached;
    }

    internal ImageTexture FloorNormal(string path, int size)
    {
        var key = $"{path.ToLowerInvariant()}:{size}";
        if (_floorNormals.TryGetValue(key, out var cached)) return cached;
        using var albedo = Floor(path, size).GetImage();
        using var normals = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float Height(int x, int y)
        {
            var color = albedo.GetPixel(Math.Clamp(x, 0, size - 1), Math.Clamp(y, 0, size - 1));
            return color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;
        }
        // A restrained relief interpretation of the original floor paint,
        // never a replacement tile or a navigation height field. Clamp at the
        // edge: neighboring MAP tiles need not use this same art.
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var normal = new Vector3((Height(x - 2, y) - Height(x + 2, y)) * 3,
                    (Height(x, y - 2) - Height(x, y + 2)) * 3, 1).Normalized();
                normals.SetPixel(x, y, new Color(normal.X * 0.5f + 0.5f, normal.Y * 0.5f + 0.5f, normal.Z * 0.5f + 0.5f));
            }
        normals.GenerateMipmaps();
        cached = ImageTexture.CreateFromImage(normals); _floorNormals.Add(key, cached); return cached;
    }

    internal (string Path, int Frame) Critter(uint fid, int storedFrame)
    {
        const string weapons = "adefghijklm";
        var index = (int)(fid & 0xfff);
        var animation = (int)((fid >> 16) & 0xff);
        var weapon = (int)((fid >> 12) & 0xf);
        var splitDirection = (int)(fid >> 28);
        if (splitDirection > 6 || weapon >= weapons.Length)
            throw new NotSupportedException($"Classic critter FID {fid:x8} has unsupported packed art fields.");
        var entries = System.Text.Encoding.ASCII.GetString(Read("art/critters/critters.lst"))
            .Replace("\r", "", StringComparison.Ordinal).Split('\n');
        if (index >= entries.Length) throw new InvalidDataException($"Critter FID {fid:x8} exceeds critters.lst.");
        var terminal = animation is >= 48 and <= 62;
        if (animation is >= 48 and <= 60) animation = animation - 48 + 20;
        else if (animation is 61 or 62) animation = animation - 61 + 34;
        string Suffix() => animation switch
        {
            < 20 => $"{weapons[weapon]}{(char)('a' + animation)}",
            >= 20 and <= 35 => $"r{(char)('a' + animation - 20)}",
            63 => "na",
            _ => throw new NotSupportedException($"Classic critter animation {animation} is unsupported."),
        };
        var fields = entries[index].Split(',');
        var extension = splitDirection == 0 ? ".frm" : $".fr{splitDirection - 1}";
        string PathFor(string entry) => $"art/critters/{entry.Split(',')[0].Trim()}{Suffix()}{extension}";
        var path = PathFor(entries[index]);
        try { Read(path); }
        catch (FileNotFoundException) when (fields.Length > 1 && int.TryParse(fields[1], out var alias) &&
            alias >= 0 && alias < entries.Length && alias != index)
        {
            path = PathFor(entries[int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture)]);
            Read(path);
        }
        return (path, terminal ? -1 : storedFrame);
    }
}
