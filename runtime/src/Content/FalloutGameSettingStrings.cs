using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenNV.Runtime.Content;

internal static class FalloutGameSettingStrings
{
    private static readonly ConditionalWeakTable<RuntimeLiveContentSource, IReadOnlyDictionary<string, string>> Defaults = new();

    internal static string Read(FalloutPluginStack records, string name)
    {
        var overrides = records.EffectiveRecords("GMST").Where(record => record.ReadSubrecords().Any(field =>
            field.Signature == "EDID" && FalloutDialogueTopic.Text(field.Data.Span).Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (overrides.Length > 1) throw new InvalidDataException($"Multiple winning GMST identities have EDID {name}.");
        if (overrides.Length == 1)
            return FalloutDialogueTopic.Text(overrides[0].ReadSubrecords().Single(field => field.Signature == "DATA").Data.Span);
        var source = RuntimeLiveContentSource.Current ?? throw new InvalidOperationException("Owned game settings are unavailable.");
        var defaults = Defaults.GetValue(source, ReadDefaults);
        return defaults.TryGetValue(name, out var value) ? value :
            throw new NotSupportedException($"Owned executable default setting is unbound: {name}.");
    }

    private static IReadOnlyDictionary<string, string> ReadDefaults(RuntimeLiveContentSource source)
    {
        if (source.Game != RuntimeLiveContentSource.FalloutNewVegasGame)
            throw new NotSupportedException("This engine's executable default-string layout has not been admitted.");
        var path = Path.Combine(Path.GetDirectoryName(source.ContentRoot)!, "FalloutNV.exe");
        return FalloutExecutableStringTable.Read(path);
    }

    // The admitted unpooled compiler layout places a setting's terminated name,
    // alignment padding and its terminated literal together. A private native
    // setting-object audit checked 1,073 printable pairs against actual pointers.
    // Pooled literals do not have this layout and MUST NOT become another name
    // or a guessed humanized label. They need a separate source association owner.
    internal static string ReadUnpooledDefault(ReadOnlySpan<byte> pool, string name)
    {
        if (!Regex.IsMatch(name, @"^s[A-Z][A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("String setting identity is invalid.");
        var needle = Encoding.ASCII.GetBytes(name + "\0");
        var found = -1;
        for (var start = 0; start <= pool.Length - needle.Length;)
        {
            var relative = pool[start..].IndexOf(needle);
            if (relative < 0) break;
            var offset = start + relative;
            if (offset == 0 || pool[offset - 1] == 0)
            {
                if (found >= 0) throw new InvalidDataException($"Owned executable string identity is ambiguous: {name}.");
                found = offset;
            }
            start = offset + needle.Length;
        }
        if (found < 0) throw new KeyNotFoundException($"Owned executable has no string setting {name}.");
        var valueStart = found + needle.Length;
        while (valueStart < pool.Length && pool[valueStart] == 0) valueStart++;
        var end = pool[valueStart..].IndexOf((byte)0);
        if (end <= 0 || pool.Slice(valueStart, end).ContainsAnyExceptInRange((byte)32, (byte)126))
            throw new NotSupportedException($"Setting {name} does not have an admitted printable default literal.");
        var value = Encoding.ASCII.GetString(pool.Slice(valueStart, end));
        if (Regex.IsMatch(value, @"^[sifb][A-Z][A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
            throw new NotSupportedException($"Setting {name} uses a pooled default literal; its source association is unbound.");
        return value;
    }
}
