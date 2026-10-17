using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerPostActionSpatial(byte[] code, Image image, int update, uint high, int middleConstructor)
    {
        var first = Child(update + 0x93);
        Need(first, [0x8b, 0x54, 0x24, 4, 0x8b, 0x41, 0x60, 0x89, 2, 0x85, 0xc0, 0x75, 3, 0xc2, 4, 0,
            0x8b, 8, 0x89, 0x0a, 0x8b, 0x40, 8, 0xc2, 4, 0], "current shader light-list iterator");
        if (Child(update + 0xd9) != first) throw new NotSupportedException("Post-action shader requery changed its actual getter.");
        Need(update + 0xc8, [0x6a, 1, 0xe8], "independent first-model getter argument");
        var model = Child(update + 0xca);
        Need(model, [0x55, 0x8b, 0xec, 0x80, 0x7d, 8, 0, 0x57, 0x8b, 0xf9, 0x74, 0x0b,
            0x8b, 0x87, 0xf0, 5, 0, 0, 0x5f, 0x5d, 0xc2, 4, 0], "same current first-model field");
        Need(middleConstructor + 0x2c7, [0xc7, 0x86, 0x24, 2, 0, 0, 0, 0, 0, 0], "independent constructor-null BBX cache");
        var bound = Word(image.Read(high + 0x5e8, 4), 0);
        if (!Match(image.Read(bound, 7), 0, [0x8b, 0x81, 0x24, 2, 0, 0, 0xc3]))
            throw new NotSupportedException("Post-action BBX getter changed its independent current process field.");
        Need(update + 0x13f, [0xff, 0x90, 0xdc, 1, 0, 0], "maximum bound virtual");
        Need(update + 0x151, [0xff, 0x90, 0xd8, 1, 0, 0], "minimum bound virtual");
        if (Word(image.Read(Word(code, update + 0x16c), 4), 0) != 0x3f800000 ||
            Word(image.Read(Word(code, update + 0x174), 4), 0) != 0x3f000000)
            throw new NotSupportedException("Light sample changed its source BBX height offset.");
        var scene = Child(update + 0x1af);
        Need(scene + 0x11, [0x8b, 0xb5, 0xb4, 0, 0, 0, 0x85, 0xf6], "independent scene light-list head");
        Need(scene + 0x67, [0x8b, 0x85, 0xe0, 0, 0, 0], "independent scene directional wrapper");
        var contribution = Child(scene + 0x46);
        Need(contribution + 0xd, [0x8b, 0xbe, 0xf8, 0, 0, 0], "same real wrapped light");
        Need(contribution + 0x1c, [0x80, 0xbe, 0xec, 0, 0, 0, 1], "wrapper early-return field");
        Need(contribution + 0x29, [0x80, 0xbe, 0xf4, 0, 0, 0, 0], "radial attenuation field");
        Need(contribution + 0x152, [0xf3, 0x0f, 0x11, 0x46, 0x0c], "actual wrapper contribution cache store");

        // Ni's stream registration selects the genuine derived constructor;
        // its linked LightingProperty parent initializes this same list.
        var factories = new HashSet<uint>();
        for (var at = 0; at <= code.Length - 11; at++)
            if (code[at] == 0x68 && code[at + 5] == 0x68 && code[at + 10] == 0xe8 &&
                image.Literal(Word(code, at + 6)) == "BSShaderPPLightingProperty") factories.Add(Word(code, at + 1));
        if (factories.Count != 1) throw new NotSupportedException("Current shader light list lost its actual stream factory.");
        var factory = SourceOffset(code, image.CodeBase, factories.Single());
        var derived = Child(factory + 0x40); var lighting = Child(derived + 0x2d);
        Need(lighting + 0x15, [0xc7, 0x46, 0x60, 0, 0, 0, 0], "actual LightingProperty constructor head clear");
        Need(lighting + 0x1c, [0xc7, 0x46, 0x68, 0, 0, 0, 0], "actual LightingProperty constructor count clear");
        int Child(int at)
        {
            if (!Match(code, at, [0xe8])) throw new NotSupportedException("Post-action spatial contract lost its linked source child.");
            return checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        }
        void Need(int at, byte[] bytes, string role)
        { if (!Match(code, at, bytes)) throw new NotSupportedException("Post-action spatial source changed its " + role + "."); }
    }
}
