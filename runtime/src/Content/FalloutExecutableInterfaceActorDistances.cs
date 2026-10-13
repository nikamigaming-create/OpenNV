namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequireInterfaceActorDistances(byte[] code, Image image, int caller)
    {
        if (!Match(code, caller + 0x6e3, [0xf3, 0x0f, 0x10, 0x87, 0x3c, 6, 0, 0, 0x0f, 0x2f, 0xd8, 0x72, 0x14]) ||
            !Match(code, caller + 0x6f0, [0xb9]) || !Match(code, caller + 0x6f5, [0xe8]) ||
            !Match(code, caller + 0x6fa, [0xf3, 0x0f, 0x10, 5]) ||
            !Match(code, caller + 0x702, [0xeb, 4, 0xf3, 0x0f, 0x5c, 0xc2, 0xf3, 0x0f, 0x11, 0x87, 0x3c, 6, 0, 0]))
            throw new NotSupportedException("Player distance-sort child lost its actual interface singleton call.");
        var timer = Word(code, caller + 0x6fe);
        if (ReadStaticSettings(image).SingleOrDefault(row => row.Descriptor + 4 == timer) is not
            { Name: "fSortActorDistanceListTimer", Collection: SettingCollection.Game })
            throw new NotSupportedException("Player actor-distance timer lost its independent winning reset setting.");
        var manager = Word(code, caller + 0x6f1); var sort = Child(caller + 0x6f5);
        var constructors = new List<int>();
        for (var at = 0; at <= code.Length - 10; ++at)
            if (code[at] == 0xb9 && Word(code, at + 1) == manager && code[at + 5] == 0xe8)
            {
                var target = Child(at + 5);
                if (Match(code, target + 0x1a8, [0x68, 0xc8, 0, 0, 0, 0x8d, 0x86, 0x88, 0, 0, 0, 0x6a, 0, 0x50, 0xe8]) &&
                    Match(code, target + 0x1be, [0xc7, 0x86, 0x50, 1, 0, 0, 0, 0, 0, 0])) constructors.Add(target);
            }
        if (constructors.Distinct().Count() != 1 || !image.IsWritableExtent(manager + 0x88, 0xcc) ||
            !Match(code, constructors[0] + 0xb6, [0x8d, 0xbe, 0, 3, 1, 0]) ||
            !Match(code, constructors[0] + 0xc6, [0xc7, 7, 0, 0, 0, 0, 0xc7, 0x47, 4, 0, 0, 0, 0]) ||
            !Match(code, sort + 0x10, [0x8d, 0x8e, 0, 3, 1, 0]) ||
            !Match(code, sort + 0x1e, [0x8b, 0xbe, 0x50, 1, 0, 0, 0xb8, 0x39, 0x8e, 0xe3, 0x38, 0x4f, 0xbb, 1, 0, 0, 0, 0xf7, 0xef]) ||
            !Match(code, sort + 0x3b, [0x03, 0xc2, 0x3b, 0xc3, 0x7c, 0x10, 0x8d, 0x1c, 0x5b, 0x43, 0x3b, 0xd8, 0x7e, 0xf8]) ||
            !Match(code, sort + 0x64, [0x8d, 0xbe, 0x88, 0, 0, 0]) ||
            !Match(code, sort + 0x81, [0x8b, 0x4d, 0xf4, 0x8b, 0x37, 0x6a, 0, 0x6a, 0, 0xff, 0x35]) ||
            !Match(code, sort + 0x95, [0x6a, 0, 0x6a, 0, 0xff, 0x35]) ||
            Child(sort + 0x90) != Child(sort + 0xa4) ||
            !Match(code, sort + 0xac, [0xf3, 0x0f, 0x10, 0x45, 0xdc, 0x0f, 0x2f, 0x45, 0xd8, 0x76, 0x23]) ||
            !Match(code, sort + 0xc1, [0x8b, 7, 0x2b, 0xfa, 0x89, 1, 0x2b, 0xca]) ||
            !Match(code, sort + 0xf3, [0x89, 0xb4, 0x87, 0x88, 0, 0, 0]) ||
            !Match(code, sort + 0x112, [0xb8, 0x56, 0x55, 0x55, 0x55, 0xf7, 0xeb]) ||
            !Match(code, sort + 0x128, [0x8b, 0x86, 0x50, 1, 0, 0, 0x3b, 5]) ||
            !Match(code, sort + 0x134, [0x7e, 0x13, 0xc7, 0x84, 0x86, 0x84, 0, 0, 0, 0, 0, 0, 0, 0xff, 0x8e, 0x50, 1, 0, 0, 0xeb, 0xdf]))
            throw new NotSupportedException("Interface actor list lost its actual constructor/lock, Shell gaps, query order, ordered shift or tail trim.");
        var limit = Word(code, sort + 0x130);
        if (ReadStaticSettings(image).SingleOrDefault(row => row.Descriptor + 4 == limit) is not
            { Name: "iAINumberActorsComplexScene", Collection: SettingCollection.Game })
            throw new NotSupportedException("Interface actor-distance list lost its independently winning signed actor-limit setting.");
        var distance = Child(sort + 0x90);
        if (!Match(code, distance + 0xb2, [0xf3, 0x0f, 0x10, 8, 0xf3, 0x0f, 0x10, 0x50, 4]) ||
            !Match(code, distance + 0xca, [0xf3, 0x0f, 0x5c, 0x43, 0x34, 0xf3, 0x0f, 0x59, 0xd2, 0xf3, 0x0f, 0x59, 0xc9,
                0xf3, 0x0f, 0x59, 0xc0, 0xf3, 0x0f, 0x58, 0xd1, 0x0f, 0x57, 0xc9, 0xf3, 0x0f, 0x58, 0xd0, 0x0f, 0x5a, 0xc2]) ||
            !Match(code, distance + 0xef, [0xf2, 0x0f, 0x51, 0xc0]) ||
            !Match(code, distance + 0xfa, [0xf2, 0x0f, 0x5a, 0xc0]))
            throw new NotSupportedException("Actor-distance sort lost its selected Float32 products/order and double square root.");
        var additions = new List<int>(); var removals = new List<int>();
        for (var at = 0; at <= code.Length - 0x148; ++at)
        {
            if (Match(code, at, [0x55, 0x8b, 0xec, 0x51, 0x53, 0x57, 0x8b, 0x7d, 8, 0x8b, 0xd9]) &&
                Match(code, at + 0x3d, [0x8b, 0x93, 0x50, 1, 0, 0]) &&
                Match(code, at + 0x50, [0x39, 0x38, 0x0f, 0x84]) &&
                Match(code, at + 0x8d, [0x83, 0xfe, 0x32, 0x0f, 0x8d]) &&
                Match(code, at + 0x9d, [0x3b, 0x35]) && Word(code, at + 0x9f) == limit &&
                Match(code, at + 0x10a, [0x89, 0xbc, 0xb3, 0x88, 0, 0, 0]) &&
                Match(code, at + 0x124, [0x40, 0x89, 0x83, 0x50, 1, 0, 0])) additions.Add(at);
            if (Match(code, at, [0x55, 0x8b, 0xec, 0x56, 0x8b, 0xf1, 0x57, 0x6a, 0]) &&
                Match(code, at + 0x16, [0x8b, 0x96, 0x50, 1, 0, 0]) &&
                Match(code, at + 0x30, [0x39, 0x19, 0x74, 0x0a]) &&
                Match(code, at + 0x8e, [0x8b, 0x86, 0x50, 1, 0, 0, 0xc7, 0x84, 0x86, 0x84, 0, 0, 0, 0, 0, 0, 0, 0xff, 0x8e, 0x50, 1, 0, 0])) removals.Add(at);
        }
        if (additions.Count != 1 || removals.Count != 1 ||
            !Match(code, additions[0] + 0x22, [0xf3, 0x0f, 0x10, 0x80, 0x8c, 4, 0, 0]) ||
            !Match(code, additions[0] + 0x73, [0xf3, 0x0f, 0x10, 0x80, 0x8c, 4, 0, 0, 0x0f, 0x2f, 0x45, 0xfc, 0x77, 0x0c]) ||
            !Match(code, additions[0] + 0xb0, [0xb9, 0xc4, 0, 0, 0]) ||
            !Match(code, removals[0] + 0x41, [0xba, 0xc4, 0, 0, 0]))
            throw new NotSupportedException("Interface actor list lost its actual nullable-context insertion, membership, full-tail moves or first removal.");
        int Child(int at)
        {
            if (at < 0 || at > code.Length - 5 || code[at] != 0xe8)
                throw new NotSupportedException("Interface actor-distance child has no actual selected source call.");
            return checked(at + 5 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1)));
        }
    }
}
