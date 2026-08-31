namespace OpenNV.Runtime.Formats.Gamebryo;

internal static class FalloutFormId
{
    internal const int HexCharacters = 8;

    internal static string Normalize(string value)
    {
        var text = value.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (text.Length is < 1 or > HexCharacters ||
            text.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Invalid Fallout FormID: {value}");
        return text.PadLeft(HexCharacters, '0').ToLowerInvariant();
    }
}
