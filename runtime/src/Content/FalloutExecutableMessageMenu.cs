using System.Runtime.CompilerServices;

namespace OpenNV.Runtime.Content;

internal static class FalloutMessageMenuDefaults
{
    private sealed record Labels(string Button);
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, Labels> Defaults = new();

    internal static string Button()
    {
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned message declarations are unavailable.");
        return Defaults.GetValue(source, content =>
        {
            if (content.Game != RuntimeLiveContentSource.FalloutNewVegasGame)
                throw new NotSupportedException("This engine's default message declaration is unbound.");
            return new(FalloutExecutableStringTable.ReadShowMessageDefaultButton(
                Path.Combine(Path.GetDirectoryName(content.ContentRoot)!, "FalloutNV.exe")));
        }).Button;
    }
}

internal static partial class FalloutExecutableStringTable
{
    internal static string ReadShowMessageDefaultButton(string path)
    {
        var (code, image) = Load(path);
        return ReadShowMessageDefaultButton(image.ScriptCommandBody("ShowMessage", code), image.Literal);
    }

    // A MESG with no enabled authored buttons takes the literal assigned by
    // ShowMessage's empty-button branch. The separate sOk setting is not this
    // declaration. Read the owned literal without imposing text or casing.
    internal static string ReadShowMessageDefaultButton(ReadOnlySpan<byte> body, Func<uint, string?> literal)
    {
        string? result = null;
        for (var at = 0; at <= body.Length - 15; at++)
        {
            var row = body[at..];
            // Widen the local empty flag, test it, and skip one literal store
            // when an authored button exists. Register/local offsets may vary.
            if (row[0] != 0x0f || row[1] != 0xb6 || (row[2] & 0xc7) != 0x45 || row[3] < 0x80 ||
                row[4] != 0x85 || row[5] != 0xc0 + ((row[2] >> 3) & 7) * 9 ||
                row[6] != 0x74 || row[7] != 7 || row[8] != 0xc7 || row[9] != 0x45 || row[10] < 0x80) continue;
            var value = literal(U32(row, 11));
            if (string.IsNullOrEmpty(value)) continue;
            if (result is not null) throw new InvalidDataException("Owned default message button is ambiguous.");
            result = value;
        }
        return result ?? throw new NotSupportedException("Owned default message button declaration is unbound.");
    }
}
