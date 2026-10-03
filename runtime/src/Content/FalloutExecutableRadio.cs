namespace OpenNV.Runtime.Content;

internal sealed record FalloutRadioHudDeclaration(string Icon, float Seconds, string SoundEditorId);

internal static partial class FalloutExecutableStringTable
{
    internal static FalloutRadioHudDeclaration ReadRadioHudDeclaration(string path)
    {
        var (code, image) = Load(path);
        return ReadRadioHudDeclaration(code, image.Literal, ControlDescriptors(code, image),
            address => BitConverter.ToSingle(image.Read(address, sizeof(float))));
    }

    internal static FalloutRadioHudDeclaration ReadRadioHudDeclaration(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, IReadOnlyDictionary<uint, string> settings, Func<uint, float> scalar)
    {
        var declarations = new List<FalloutRadioHudDeclaration>();
        for (var at = 0; at + 10 <= code.Length; at++)
        {
            if (code[at] != 0xb9 || code[at + 5] != 0xe8 ||
                !settings.TryGetValue(U32(code, at + 1), out var name) || name != "sRadioStationDiscovered") continue;
            var sounds = new List<string>();
            for (var before = Math.Max(0, at - 64); before + 5 <= at; before++)
                if (code[before] == 0x68 && literal(U32(code, before + 1)) is { } sound &&
                    sound.StartsWith("UI", StringComparison.Ordinal)) sounds.Add(sound);
            if (sounds.Count == 0) continue; // Setting construction/getters are not notice declarations.
            var resources = new List<string>(); float? duration = null;
            for (var next = at + 10; next + 6 <= Math.Min(code.Length, at + 80); next++)
            {
                if (code[next] == 0xd9 && code[next + 1] == 0x05) duration = scalar(U32(code, next + 2));
                if (code[next] == 0x68 && literal(U32(code, next + 1)) is { } path &&
                    path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) { resources.Add(path); break; }
            }
            if (resources.Count != 1 || sounds.Count != 1 || duration is not { } seconds || !float.IsFinite(seconds) || seconds <= 0)
                throw new NotSupportedException("Owned radio discovery notice declaration is unbound.");
            declarations.Add(new(resources[0], seconds, sounds[0]));
        }
        var unique = declarations.Distinct().ToArray();
        return unique.Length == 1 ? unique[0] : throw new NotSupportedException("Radio discovery declaration is absent or ambiguous.");
    }
}
