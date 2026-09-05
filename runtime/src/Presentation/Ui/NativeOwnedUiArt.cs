using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Presentation.Ui;

internal sealed record NativeOwnedUiArt(Texture2D Texture, Rect2 Region)
{
    internal static NativeOwnedUiArt Read(XElement image, string? resolvedFilename = null)
    {
        var filename = resolvedFilename ?? image.Element("filename")?.Value.Trim() ?? throw new InvalidDataException("Source UI image has no filename.");
        var atlas = image.Element("texatlas")?.Value.Trim();
        if (atlas is null)
        {
            var texture = NativeOwnedMediaLoader.LoadTexture("textures/" + filename);
            return new(texture, new Rect2(Vector2.Zero, texture.GetSize()));
        }
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned UI art source is absent.");
        // TAI keys are member basenames even when XML provides a texture path.
        // Resolution remains within the explicitly selected atlas and unique.
        var member = filename.Replace('\\', '/').Split('/')[^1];
        if (!source.TryRead("textures/" + atlas, null, out var bytes, out _)) throw new FileNotFoundException("Owned UI atlas index is missing.", atlas);
        var rows = Encoding.UTF8.GetString(bytes).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.TrimStart().StartsWith('#')).Select(line => Regex.Split(line.Trim(), @"[\s,]+"))
            .Where(row => row[0].Equals(member, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rows.Length != 1 || rows[0].Length != 9 || rows[0][3] != "2D")
            throw new NotSupportedException($"UI atlas {atlas} cannot resolve one 2D region for {filename}.");
        var selected = rows[0];
        var atlasPath = atlas.Replace('\\', '/');
        var atlasTexture = NativeOwnedMediaLoader.LoadTexture("textures/" + atlasPath[..(atlasPath.LastIndexOf('/') + 1)] + selected[1]);
        float Value(int index) => float.Parse(selected[index], CultureInfo.InvariantCulture);
        var region = new Rect2(Value(4) * atlasTexture.GetWidth(), Value(5) * atlasTexture.GetHeight(),
            Value(7) * atlasTexture.GetWidth(), Value(8) * atlasTexture.GetHeight());
        if (Value(6) != 0 || !region.Position.IsFinite() || !region.Size.IsFinite() || region.Size.X <= 0 || region.Size.Y <= 0)
            throw new InvalidDataException("Source UI atlas region is invalid.");
        return new(atlasTexture, region);
    }
}
