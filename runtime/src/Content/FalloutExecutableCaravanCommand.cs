using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutExecutableCommandParameter(string Name, uint Type, bool Optional);
internal sealed record FalloutCaravanMinimumNotice(string Setting, string Icon, float Seconds, string SoundEditorId);

internal static partial class FalloutExecutableStringTable
{
    // This reads declarations from the selected executable; it does not execute
    // the command or establish what the AI type and percentage actually do.
    internal static IReadOnlyList<FalloutExecutableCommandParameter> ReadCaravanCommandParameters(string path)
    {
        var (code, image) = Load(path);
        _ = image.ScriptCommandStart("ShowCaravanMenu", code, 3, referenceFlag: 1);
        var parameters = image.CommandParameters("ShowCaravanMenu");
        if (parameters.Count != 3 ||
            parameters[0] != new FalloutExecutableCommandParameter("Caravan Deck", 0x44, false) ||
            parameters[1] != new FalloutExecutableCommandParameter("AI TYPE", 1, false) ||
            parameters[2] != new FalloutExecutableCommandParameter("BET PERCENTAGE", 2, false))
            throw new NotSupportedException("Owned Caravan command parameters differ from the evidenced declaration.");
        return parameters;
    }

    internal static string ReadCaravanMenuPath(string path)
    {
        var (code, image) = Load(path);
        return ReadCaravanMenuPath(code, image.ScriptCommandStart("ShowCaravanMenu", code, 3, referenceFlag: 1), image.Literal);
    }

    // Follow the command's five-argument opening call and identify the owned
    // menu resource passed by its target. Unrecognized compiler layouts are not
    // substituted with a manually chosen menu.
    internal static string ReadCaravanMenuPath(ReadOnlySpan<byte> code, int command, Func<uint, string?> literal)
    {
        if (command < 0 || command >= code.Length) throw new InvalidDataException("Caravan command lies outside executable code.");
        var targets = new List<int>();
        for (var at = command; at + 26 <= Math.Min(code.Length, command + 512); ++at)
        {
            var row = code[at..];
            if (row[0] != 0x6a || row[1] != 0 || row[2] != 0x51 || row[3] != 0xd9 || row[4] != 0x45 ||
                row[6] != 0xd9 || row[7] != 0x1c || row[8] != 0x24 || row[9] != 0x8b || row[10] != 0x4d ||
                row[12] != 0x51 || row[13] != 0x8b || row[14] != 0x55 || row[16] != 0x52 ||
                row[17] != 0x8b || row[18] != 0x45 || row[20] != 0x50 || row[21] != 0xe8) continue;
            var target = (long)at + 26 + BinaryPrimitives.ReadInt32LittleEndian(row[22..]);
            if (target < 0 || target >= code.Length) throw new InvalidDataException("Caravan menu opening target is outside executable code.");
            targets.Add((int)target);
        }
        if (targets.Count != 1) throw new NotSupportedException("Caravan menu opening call is absent or ambiguous.");
        var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var at = targets[0]; at + 5 <= Math.Min(code.Length, targets[0] + 1280); ++at)
        {
            if (code[at] != 0x68 || literal(U32(code, at + 1)) is not { } candidate) continue;
            var normalized = candidate.Replace('\\', '/');
            if (normalized.StartsWith("Data/Menus/", StringComparison.OrdinalIgnoreCase) &&
                normalized.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) resources.Add(normalized[5..]);
        }
        return resources.Count == 1 ? resources.Single() :
            throw new NotSupportedException("Caravan opening menu source is absent or ambiguous.");
    }

    // Bind the notice to its dynamically initialized setting descriptor and
    // the nearby authored icon, duration and UI sound. Ambiguous executable
    // layouts remain unsupported instead of supplying a replacement notice.
    internal static FalloutCaravanMinimumNotice ReadCaravanMinimumNotice(string path)
    {
        var (code, image) = Load(path);
        return ReadCaravanMinimumNotice(code, image.Literal, ControlDescriptors(code, image),
            address => BitConverter.ToSingle(image.Read(address, sizeof(float))));
    }

    internal static FalloutCaravanMinimumNotice ReadCaravanMinimumNotice(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, IReadOnlyDictionary<uint, string> settings, Func<uint, float> scalar)
    {
        var declarations = new List<FalloutCaravanMinimumNotice>();
        for (var at = 0; at + 10 <= code.Length; ++at)
        {
            if (code[at] != 0xb9 || code[at + 5] != 0xe8 ||
                !settings.TryGetValue(U32(code, at + 1), out var setting) || setting != "sCardCountText") continue;
            var icons = new List<string>(); var sounds = new List<string>(); var durations = new List<float>();
            for (var next = at + 10; next + 6 <= Math.Min(code.Length, at + 144); ++next)
            {
                if (code[next] == 0xd9 && code[next + 1] == 0x05)
                    durations.Add(scalar(U32(code, next + 2)));
                if (code[next] != 0x68 || literal(U32(code, next + 1)) is not { } value) continue;
                if (value.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) icons.Add(value);
                if (value.StartsWith("UIPopUp", StringComparison.Ordinal)) sounds.Add(value);
            }
            if (icons.Count == 0 && sounds.Count == 0) continue;
            if (icons.Count != 1 || sounds.Count != 1 || durations.Count != 1 ||
                !float.IsFinite(durations[0]) || durations[0] <= 0)
                throw new NotSupportedException("Caravan minimum notice declaration is unbound or ambiguous.");
            declarations.Add(new(setting, icons[0], durations[0], sounds[0]));
        }
        var unique = declarations.Distinct().ToArray();
        return unique.Length == 1 ? unique[0] :
            throw new NotSupportedException("Caravan minimum notice declaration is absent or ambiguous.");
    }

    private sealed partial class Image
    {
        internal IReadOnlyList<FalloutExecutableCommandParameter> CommandParameters(string name)
        {
            IReadOnlyList<FalloutExecutableCommandParameter>? found = null;
            foreach (var section in headers.SectionHeaders.Where(section =>
                         (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0))
            {
                var data = bytes.AsSpan(section.PointerToRawData, section.SizeOfRawData);
                for (var at = 0; at <= data.Length - 40; at += sizeof(uint))
                {
                    var row = data[at..];
                    if (Literal(U32(row, 0)) != name) continue;
                    var count = BinaryPrimitives.ReadUInt16LittleEndian(row[18..]);
                    var payload = Read(U32(row, 20), checked(count * 12));
                    var parameters = new FalloutExecutableCommandParameter[count];
                    for (var i = 0; i < count; ++i)
                    {
                        var offset = i * 12;
                        var label = Literal(U32(payload, offset)) ??
                            throw new InvalidDataException("Script command parameter has no source label.");
                        var optional = U32(payload, offset + 8);
                        if (optional > 1) throw new InvalidDataException("Script command optional flag is invalid.");
                        parameters[i] = new(label, U32(payload, offset + 4), optional != 0);
                    }
                    if (found is not null) throw new InvalidDataException("Owned script command declaration is ambiguous.");
                    found = Array.AsReadOnly(parameters);
                }
            }
            return found ?? throw new NotSupportedException($"Owned script command has no declaration: {name}.");
        }
    }
}
