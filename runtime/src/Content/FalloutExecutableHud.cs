using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal sealed record FalloutHudMessageDeclarations(int SafeZoneScale, int XInset, int YInset,
    IReadOnlyDictionary<int, int> TextTraits, string ItemIcon, float ItemSeconds,
    string SingleItemFormat, string MultipleItemFormat);

internal static partial class FalloutExecutableStringTable
{
    // These are compiler declaration patterns, not addresses or fitted HUD
    // coordinates. Immediate values and resource literals remain owned data.
    internal static FalloutHudMessageDeclarations ReadHudMessageDeclarations(string path)
    {
        var (code, image) = Load(path);
        var settings = ControlDescriptors(code, image);
        return ReadHudMessageDeclarations(code, image.Literal, settings,
            address => BitConverter.ToSingle(image.Read(address, sizeof(float))));
    }

    internal static FalloutHudMessageDeclarations ReadHudMessageDeclarations(ReadOnlySpan<byte> code,
        Func<uint, string?> literal, IReadOnlyDictionary<uint, string> settings, Func<uint, float> scalar)
    {
        var names = new Dictionary<uint, string?>();
        string? Name(uint address)
        {
            if (!names.TryGetValue(address, out var value)) names[address] = value = literal(address);
            return value;
        }
        var pushes = new List<(int At, string Name)>();
        for (var at = 0; at <= code.Length - 5; at++)
            if (code[at] == 0x68 && Name(U32(code, at + 1)) is { } text) pushes.Add((at, text));
        var message = pushes.Single(value => value.Name == "Messages").At;
        var icon = pushes.First(value => value.At > message && value.Name == "template_message_icon").At;
        var textStart = pushes.First(value => value.At > icon && value.Name == "template_justify_left_text").At;
        var bracket = pushes.First(value => value.At > textStart && value.Name == "template_message_bracket").At;
        var placement = code[message..icon];
        // Source integer setters compute X = inset + 2*safeX and
        // Y = inset + 2*safeY. Reject another compiler expression shape.
        var x = placement.IndexOf(new byte[] { 0x8d, 0x0c, 0x50, 0x51, 0x68, 0xa1, 0x0f, 0, 0 });
        var y = placement.IndexOf(new byte[] { 0x8d, 0x4c, 0x00 });
        if (x < 16 || y < 0 || y + 10 > placement.Length || placement[x - 16] != 0xc7 || placement[x - 15] != 0x45 ||
            !placement.Slice(y + 4, 6).SequenceEqual(new byte[] { 0x51, 0x68, 0xa2, 0x0f, 0, 0 }))
            throw new NotSupportedException("Owned HUD safe-zone declaration is unbound.");
        var xInset = BinaryPrimitives.ReadInt32LittleEndian(placement[(x - 13)..]);
        var yInset = unchecked((sbyte)placement[y + 3]);
        var traits = new Dictionary<int, int>();
        var textCode = code[textStart..bracket];
        for (var at = 0; at < textCode.Length; at++)
        {
            if (!TryPushInteger(textCode, at, out var value, out var next) || next + 5 > textCode.Length || textCode[next] != 0x68) continue;
            var trait = checked((int)U32(textCode, next + 1));
            // Engine tile trait IDs are a protocol, unlike their authored values.
            if (trait is not (4001 or 4002 or 4003 or 4009 or 4013 or 4026)) continue;
            if (!traits.TryAdd(trait, value)) throw new InvalidDataException("HUD text trait declaration is ambiguous.");
        }
        if (traits.Count != 6) throw new NotSupportedException("Owned HUD text setters are incomplete.");

        var itemContracts = new List<(string Icon, float Seconds, string Single, string Multiple)>();
        for (var at = 0; at <= code.Length - 10; at++)
        {
            if (code[at] != 0xb9 || code[at + 5] != 0xe8 ||
                !settings.TryGetValue(U32(code, at + 1), out var setting) || setting != "sAddItemtoInventory") continue;
            var begin = at;
            while (begin > 0 && !code.Slice(begin, 3).SequenceEqual(new byte[] { 0x55, 0x8b, 0xec })) begin--;
            var end = at + 10;
            while (end < code.Length - 4 && !(code.Slice(end, 3).SequenceEqual(new byte[] { 0x8b, 0xe5, 0x5d }) && code[end + 3] is 0xc2 or 0xc3)) end++;
            if (begin == 0 || end >= code.Length - 4) throw new NotSupportedException("Owned inventory notice function boundary is unbound.");
            var calls = pushes.Where(value => value.At >= begin && value.At < end).ToArray();
            var formats = calls.Where(value => value.Name.Contains('%')).Select(value => value.Name).ToArray();
            // Other consumers of this game setting (inventory menus, barter)
            // are not the two-branch notification declaration.
            if (formats.Length != 2) continue;
            var singles = formats.Where(value => value.Count(character => character == '%') == 2).ToArray();
            var multiples = formats.Where(value => value.Count(character => character == '%') == 4).ToArray();
            if (singles.Length != 1 || multiples.Length != 1) continue;
            var single = singles[0]; var multiple = multiples[0];
            foreach (var resource in calls.Where(value => value.Name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)))
            {
                float? duration = null;
                for (var position = Math.Max(begin, resource.At - 32); position < resource.At - 5; position++)
                    if (code[position] == 0xd9 && code[position + 1] == 0x05) duration = scalar(U32(code, position + 2));
                if (duration is not { } seconds || !float.IsFinite(seconds) || seconds <= 0)
                    throw new NotSupportedException("Owned inventory notice duration is unbound.");
                itemContracts.Add((resource.Name, seconds, single, multiple));
            }
        }
        var items = itemContracts.Distinct().ToArray();
        if (items.Length != 1) throw new NotSupportedException("Owned inventory notice declaration is missing or ambiguous.");
        return new(2, xInset, yInset, traits, items[0].Icon, items[0].Seconds, items[0].Single, items[0].Multiple);
    }

    private static bool TryPushInteger(ReadOnlySpan<byte> code, int at, out int value, out int next)
    {
        value = 0; next = at;
        if (at + 2 <= code.Length && code[at] == 0x6a) { value = unchecked((sbyte)code[at + 1]); next = at + 2; return true; }
        if (at + 5 <= code.Length && code[at] == 0x68) { value = BinaryPrimitives.ReadInt32LittleEndian(code[(at + 1)..]); next = at + 5; return true; }
        return false;
    }
}
