using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal static class FalloutConsoleSourceEdit
{
    internal static byte[] Normalize(FalloutMainUtilityCommandSource source, ReadOnlySpan<byte> edit)
    {
        source.Validate();
        if (edit.Length > source.TextCapacity || edit.IndexOf((byte)0) >= 0)
            throw new NotSupportedException("Console edit has no complete bounded source byte-string extent.");
        // Cursor placement is consumed by the original submit event. A cursor
        // is not a command character; preserve every other byte and whitespace.
        var result = new List<byte>(edit.Length);
        foreach (var character in edit) if (character != 0x7c) result.Add(character);
        for (var index = 0; index < result.Count; index++)
        {
            if (result[index] != 0x0a) continue;
            if (index == 0)
                throw new NotSupportedException("Original console newline normalization reads an unowned preceding source stack byte.");
            if (result[index - 1] == 0x2d)
            {
                result.RemoveRange(index - 1, 2);
                // The source outer index increments after shifting. Recheck
                // only the current shifted byte for its second LF-to-space arm.
                if (index < result.Count && result[index] == 0x0a) result[index] = 0x20;
            }
            else result[index] = 0x20;
        }
        return result.ToArray();
    }
}
