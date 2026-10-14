using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequirePlayerControllerPostActions(byte[] code, int frame, int firstBody, int reference)
    {
        Need(frame + 0x5ad, [0x8b, 0xcf, 0xe8], "later first current context query");
        Need(frame + 0x5b4, [0x85, 0xc0, 0x74, 0x6a, 0x8b, 0xc8, 0xe8], "nonnull later context before same first body getter");
        if (Call(frame + 0x5af) != Call(frame + 0x374) || Call(frame + 0x62f) != Call(frame + 0x374) ||
            Call(frame + 0x5ba) != firstBody || Call(frame + 0x5da) != reference)
            throw new NotSupportedException("Post-action context changed its repeated controller/body/reference source association.");
        Need(frame + 0x5bf, [0x85, 0xc0, 0x74, 0x14], "first body null enters its real reference conversion arm");
        Need(frame + 0x5d7, [0x33, 0xc0, 0x50, 0xe8], "null body still calls source reference conversion");
        Need(frame + 0x5e2, [0x85, 0xc0, 0x74, 0x3c], "returned null reference selects action removal");
        Need(frame + 0x622, [0x6a, 0, 0x6a, 8, 0x8b, 0xcf, 0xe8], "action eight removal follows actual returned conversion");
        Need(frame + 0x62d, [0x8b, 0xcf, 0xe8], "independently repeated second current context query");
        Need(frame + 0x634, [0x85, 0xc0, 0x74, 0x29], "second current context nullable branch");
        Need(frame + 0x638, [0x8b, 0x80, 0x94, 2, 0, 0, 0xc1, 0xe8, 0x11, 0xa8, 1, 0x74, 0x1c],
            "same real controller flags bit seventeen precedes independent Player context byte");
        Need(frame + 0x645, [0x80, 0xbf, 0xc1, 1, 0, 0, 0, 0x75, 0x1a], "true flag requires current Player notification byte");
        Need(frame + 0x661, [0xc6, 0x87, 0xc1, 1, 0, 0, 0], "false controller flag stores Player context byte zero");

        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Post-action controller lost an original direct child.");
        void Need(int at, byte[] bytes, string role)
        { if (!Match(code, at, bytes)) throw new NotSupportedException("Post-action controller changed its source " + role + "."); }
    }
}
