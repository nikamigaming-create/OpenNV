using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerControllerContext(byte[] code, Image image)
    {
        var player = Table(".?AVPlayerCharacter@@", 191);
        var frame = Function(player, 0x2f8); var helper = Function(player, 0x1c4);
        var constructor = Call(helper + 0x33e);
        Need(constructor + 0x378, [0xc7, 0x86, 0x9c, 4, 0, 0, 0, 0, 0, 0], "independent first body constructor null");
        Need(constructor + 0x382, [0xc7, 0x86, 0xc0, 4, 0, 0, 0, 0, 0, 0], "independent second body constructor null");
        Need(constructor + 0x396, [0xc7, 0x86, 0xc8, 4, 0, 0, 0, 0, 0, 0], "independent object constructor null");
        Need(frame + 0x372, [0x8b, 0xcf, 0xe8], "first actual post-process context getter");
        Need(frame + 0x37c, [0x85, 0xc0, 0x74, 0x65, 0x8b, 0xc8, 0xe8], "nonnull context before first field getter");
        var first = Call(frame + 0x382); var second = Call(frame + 0x38c);
        Need(first, [0x8b, 0x81, 0x9c, 4, 0, 0, 0x85, 0xc0, 0x74, 4, 0x8b, 0x40, 0x0c, 0xc3, 0x33, 0xc0, 0xc3], "pure first body nullable wrapper");
        Need(second, [0x8b, 0x81, 0xc0, 4, 0, 0, 0x85, 0xc0, 0x74, 4, 0x8b, 0x40, 0x0c, 0xc3, 0x33, 0xc0, 0xc3], "pure second body nullable wrapper");
        Need(frame + 0x391, [0x3b, 0xf8, 0x74, 0x4d, 0x8b, 0x4d, 0xd0, 0xe8], "same-body comparison before repeated second getter");
        if (Call(frame + 0x398) != second || Call(frame + 0x374) != Call(frame + 0x3e7))
            throw new NotSupportedException("Controller context changed an independent/repeated selected getter.");
        Need(frame + 0x3ee, [0x74, 0x56, 0x8b, 0xc8, 0xe8], "second nullable context before object getter");
        var other = Call(frame + 0x3f2);
        Need(other, [0x8b, 0x81, 0xc8, 4, 0, 0, 0x85, 0xc0, 0x74, 7, 0x8b, 0x80, 0xc0, 0, 0, 0, 0xc3, 0x33, 0xc0, 0xc3], "pure independent object nullable wrapper");
        Need(frame + 0x3f7, [0x85, 0xc0, 0x74, 0x13], "object-null selected conversion arm");
        Need(frame + 0x40e, [0x33, 0xc0, 0x50, 0xe8], "genuine null before original reference conversion");
        var reference = Call(frame + 0x411);
        Need(reference + 0x13, [0x85, 0xc9, 0x74, 9, 0x6a, 1, 0xe8], "forced first-person read precedes even null input");
        Need(reference + 0x20, [0x85, 0xf6, 0x74, 0x20], "null input returns after current first-person getter");
        RequirePlayerControllerPostActions(code, frame, first, reference);
        // Constructor initialization publishes the distinct base proxy through
        // inherited setter/retention. Its code is not any of the three pure
        // getters' wrapper fields, even though all belong to one controller.
        var initialize = Call(constructor + 0x401);
        var proxyBuilder = Call(initialize + 0x28e);
        Need(constructor + 0x14b, [0xc7, 0], "actual derived controller class publication");
        var controllerTable = Word(code, constructor + 0x14d);
        Need(proxyBuilder + 0xa6, [0xff, 0x90, 0x8c, 0, 0, 0], "distinct inherited base proxy publication");
        Need(proxyBuilder + 0xc9, [0xff, 0x90, 0xbc, 0, 0, 0], "returned proxy initialization cleanup");
        var proxySetter = Function(controllerTable, 0x8c);
        Need(proxySetter, [0x56, 0x8b, 0xf1, 0x85, 0xf6, 0x74, 5, 0x8b, 0x46, 8], "actual separate base-proxy getter");
        var pointerStore = Call(proxySetter + 0x3d);
        Need(pointerStore + 0x1d, [0x89, 0x7e, 8, 0xff, 0x90, 0x90, 0, 0, 0], "base-proxy field store before retained child");
        var retained = Function(controllerTable, 0x90);
        Need(retained + 0x67, [0x89, 0x82, 0xc0, 0, 0, 0], "proxy's owner back-reference remains a distinct object");
        var cleanup = Function(controllerTable, 0xbc);
        Need(cleanup, [0x8b, 1, 0xc7, 0x44, 0x24, 4, 1, 0, 0, 0, 0xff, 0xa0, 0xa4, 0, 0, 0], "selected cleanup virtual forwarding");
        var cleanupBody = Function(controllerTable, 0xa4);
        Need(cleanupBody + 0x2a, [0x0f, 0x84], "false cleanup arm");
        Need(cleanupBody + 0x30, [0x8b, 0x77, 0x0c, 0x85, 0xf6], "temporary creation-info cleanup is not a support body");
        Need(cleanupBody + 0xb5, [0xc7, 0x47, 0x0c, 0, 0, 0, 0], "only creation-info pointer cleared by selected cleanup");

        uint Table(string name, int count) => image.SourceClassTables(name, count).ToArray() is [var table] ? table :
            throw new NotSupportedException("Controller context source class is absent or ambiguous.");
        int Function(uint table, uint slot) => SourceOffset(code, image.CodeBase, Word(image.Read(table + slot, 4), 0));
        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Controller context changed a selected direct child.");
        void Need(int at, byte[] bytes, string role)
        { if (!Match(code, at, bytes)) throw new NotSupportedException("Controller context changed its source " + role + "."); }
    }
}
