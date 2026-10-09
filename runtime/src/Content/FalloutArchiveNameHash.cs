namespace OpenNV.Runtime.Content;

// The bounded source owner uses original ASCII path components. Non-ASCII
// CRT case folding and drive/path split grammars are independent contracts.
internal static class FalloutArchiveNameHash
{
    private static readonly string[] Categories = ["", ".nif", ".kf", ".dds", ".wav", ".adp"];

    internal static ulong Folder(string folder)
    {
        RequireAscii(folder);
        if (folder.Length == 0 || folder.StartsWith('\\') || folder.EndsWith('\\') ||
            folder.Contains('/') || folder.Contains(':') || folder.Split('\\').Any(part => part is "" or "." or ".."))
            throw new NotSupportedException("Archive folder requires its actual bounded relative source components.");
        return Component(folder, "");
    }

    internal static ulong File(string name)
    {
        RequireAscii(name);
        if (name.Length == 0 || name.IndexOfAny(['\\', '/', ':']) >= 0)
            throw new InvalidDataException("Archive filename is not a direct source component.");
        var dot = name.LastIndexOf('.');
        if (dot == 0) throw new NotSupportedException("Archive dot-only basename requires its original path-split consumer.");
        return dot < 0 ? Component(name, "") : Component(name[..dot], name[dot..]);
    }

    internal static ulong Wildcard(string extension)
    {
        RequireAscii(extension);
        if (!extension.StartsWith('.') || extension.Length < 2 || extension.IndexOfAny(['*', '?', '\\', '/', ':']) >= 0)
            throw new InvalidDataException("Archive wildcard lost its source extension request.");
        return Component("*", extension);
    }

    internal static bool MatchesWildcard(ulong pattern, ulong candidate)
    {
        if ((((uint)pattern >> 24) & 0x7f) != '*')
            throw new InvalidDataException("Source archive comparison requires its leading-star hash.");
        // A leading '*' compares encoded extension classes, not text suffixes.
        return Category(pattern) == Category(candidate);
    }

    internal static int Category(ulong hash) => (int)((hash >> 29) & 4) +
        (int)((hash >> 6) & 2) + (int)((hash >> 15) & 1);

    private static ulong Component(string stem, string extension)
    {
        RequireAscii(stem); RequireAscii(extension);
        if (stem.Length == 0 || stem.Length > byte.MaxValue)
            throw new NotSupportedException("Source archive stem exceeds its retained length byte.");
        stem = stem.ToLowerInvariant(); extension = extension.ToLowerInvariant();
        uint low = ((uint)(byte)stem[0] << 24) | ((uint)stem.Length << 16) | (byte)stem[^1];
        if (stem.Length >= 3) low |= (uint)(byte)stem[^2] << 8;
        uint high = 0;
        for (var at = 1; at < stem.Length - 2; ++at) high = unchecked(high * 65599 + (byte)stem[at]);
        uint tail = 0;
        foreach (var character in extension) tail = unchecked(tail * 65599 + (byte)character);
        high = unchecked(high + tail);
        var category = 0;
        for (var at = 1; at < Categories.Length; ++at)
            if ((extension.Length > 4 ? extension[..4] : extension) == Categories[at]) { category = at; break; }
        var first = unchecked((byte)((byte)(low >> 24) + ((category << 5) & 0x80)));
        var last = unchecked((byte)((byte)low + ((category << 6) & 0x80)));
        var penultimate = unchecked((byte)((byte)(low >> 8) + ((category << 7) & 0x80)));
        low = (low & 0x00ff0000) | ((uint)first << 24) | ((uint)penultimate << 8) | last;
        return ((ulong)high << 32) | low;
    }

    internal static void RequireAscii(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Any(character => character is < ' ' or > '~'))
            throw new NotSupportedException("Original archive hash needs an independently owned non-ASCII/control-byte case-mapping contract.");
    }
}
