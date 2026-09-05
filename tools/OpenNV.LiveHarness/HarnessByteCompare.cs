using System.Security.Cryptography;
using System.Text.Json;

namespace OpenNV.LiveHarness;

internal static class HarnessByteCompare
{
    internal static object Compare(JsonElement left, JsonElement right)
    {
        var a = Read(left); var b = Read(right);
        var extent = Math.Min(a.Length, b.Length);
        var differences = 0; int? first = null;
        for (var offset = 0; offset < extent; offset++)
            if (a[offset] != b[offset]) { first ??= offset; ++differences; }
        if (a.Length != b.Length) first ??= extent;
        return new { equal = first is null, firstByteDifference = first, differingSharedBytes = differences,
            leftBytes = a.Length, rightBytes = b.Length,
            leftWindow = Window(a, first), rightWindow = Window(b, first),
            interpretation = "exact-bytes-only;semantic-and-frame-alignment-must-be-established-separately" };
    }

    private static byte[] Read(JsonElement descriptor)
    {
        var path = descriptor.GetProperty("file").GetString()!;
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("A private blob path must be absolute.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != descriptor.GetProperty("length").GetInt32() ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(descriptor.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Trace blob length or hash changed.");
        return bytes;
    }

    private static string? Window(byte[] bytes, int? first)
    {
        if (first is not { } offset) return null;
        var start = Math.Max(0, Math.Min(offset, bytes.Length) - 8);
        return Convert.ToHexString(bytes.AsSpan(start, Math.Min(24, bytes.Length - start)));
    }
}
