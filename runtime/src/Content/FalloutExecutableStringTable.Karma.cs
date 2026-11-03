namespace OpenNV.Runtime.Content;

internal sealed record FalloutKarmaNotice(string ImageSetting, float Seconds);
internal static partial class FalloutExecutableStringTable
{
    internal static IReadOnlyDictionary<string, FalloutKarmaNotice> ReadKarmaNotices(string path)
    {
        var (code, image) = Load(path);
        return ReadKarmaNotices(code, ControlDescriptors(code, image), address => BitConverter.ToSingle(image.Read(address, sizeof(float))));
    }
    internal static IReadOnlyDictionary<string, FalloutKarmaNotice> ReadKarmaNotices(ReadOnlySpan<byte> code,
        IReadOnlyDictionary<uint, string> settings, Func<uint, float> scalar)
    {
        var found = new Dictionary<string, FalloutKarmaNotice>(StringComparer.Ordinal);
        var expected = new[] { "sKarmaMajorLost", "sKarmaMinorLost", "sKarmaMajorGained", "sKarmaMinorGained" };
        for (var at = 0; at <= code.Length - 46; ++at)
        {
            var row = code[at..];
            if (!row[..5].SequenceEqual(new byte[] { 0x6a, 0, 0x51, 0xd9, 5 }) ||
                !row.Slice(9, 6).SequenceEqual(new byte[] { 0xd9, 0x1c, 0x24, 0x6a, 0, 0xb9 }) ||
                row[19] != 0xe8 || !row.Slice(24, 4).SequenceEqual(new byte[] { 0x50, 0x6a, 0, 0xb9 }) ||
                row[32] != 0xe8 || row[37] != 0x50 || row[38] != 0xe8 ||
                !row.Slice(43, 3).SequenceEqual(new byte[] { 0x83, 0xc4, 0x18 }) ||
                !settings.TryGetValue(U32(row, 28), out var text) || !expected.Contains(text, StringComparer.Ordinal)) continue;
            if (!settings.TryGetValue(U32(row, 15), out var image) || image != text + "Image")
                throw new NotSupportedException("Karma notice lost its source image-setting pair.");
            var seconds = scalar(U32(row, 5));
            if (!float.IsFinite(seconds) || seconds <= 0) throw new NotSupportedException("Karma notice has no finite positive source lifetime.");
            var notice = new FalloutKarmaNotice(image, seconds);
            if (found.TryGetValue(text, out var previous) && previous != notice)
                throw new NotSupportedException("Karma notice has ambiguous source declarations.");
            found[text] = notice;
        }
        if (expected.Any(name => !found.ContainsKey(name))) throw new NotSupportedException("Karma notice source declarations are incomplete.");
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, FalloutKarmaNotice>(found);
    }
}
