using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequireActorModelAssociation(byte[] code, Image image, uint player)
    {
        var method = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x1fc, 4), 0));
        var actor = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x100, 4), 0));
        var helper = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x1c4, 4), 0));
        Need(helper + 0x60f, [0x6a, 0, 0xff, 0x90, 0xfc, 1, 0, 0], "actual final virtual with false argument before the registration-only Extra tail");
        Need(actor, [0xb0, 1, 0xc3], "actual Player Actor-kind predicate");
        Need(method + 0x1d, [0x8b, 0x4f, 0x60], "same current process pointer before controller lookup");
        Need(method + 0x2a, [0x8b, 0x80, 0x88, 2, 0, 0], "actual current controller virtual");
        Need(method + 0x54, [0x83, 0xbe, 0xfc, 0, 0, 0, 2], "actual independent Actor mode-two field");
        Need(method + 0x65, [0x8b, 0x47, 0x3c], "current nullable ParentCELL read");
        Need(method + 0x8d, [0xc7, 0x45, 0xf8, 0, 0, 0, 0], "absent ParentCELL selects actual null target world");
        Need(method + 0x97, [0x8b, 0x48, 8], "same current controller's actual proxy");
        var phantom = Call(method + 0x9e);
        Need(phantom, [0x8b, 0x41, 0x30, 0xc3], "actual proxy's retained phantom getter");
        Need(method + 0xac, [0x8b, 0x52, 8], "actual current phantom world before null test");
        Need(method + 0xbb, [0x33, 0xd2], "detached phantom's actual null world");
        Need(method + 0x18d, [0x8b, 0x88, 0x24, 4, 0, 0], "independent first current body field");
        Need(method + 0x1a3, [0xc7, 0x45, 0xec, 0, 0, 0, 0], "absent first body selects actual null prior model");
        Need(method + 0x1cd, [0x3b, 0xb8, 0x4c, 2, 0, 0], "same current-thread cached reference identity");
        Need(method + 0x1ee, [0x8b, 0x4f, 0x5c], "same actual Reference model fallback");
        Need(method + 0x221, [0x8b, 0x80, 0x2c, 4, 0, 0], "independent second current body setter gate");
        Need(method + 0x235, [0x8b, 0x45, 0xf8, 0x8b, 0x4d, 0xe8, 0x3b, 0xc8], "actual current-versus-target world comparison");
        Need(method + 0x25c, [0x85, 0xc0, 0x0f, 0x84], "null target returns before world admission and later callbacks");
        if (method + 0x264 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(method + 0x260)) != method + 0x397)
            throw new NotSupportedException("Controller model association changed its actual null-world return boundary.");

        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Controller association lost an actual direct source child.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Controller association changed its " + role + "."); }
    }
}
