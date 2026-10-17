using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequireActorAnimationNotes(byte[] code, Image image, uint player, int caller)
    {
        Need(caller + 0x571, [0x8b, 0x80, 0x38, 2, 0, 0], "actual Actor receiver virtual");
        var getter = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x238, 4), 0));
        Need(getter + 0x2c, [0x8d, 0x4e, 0x40, 0x6a, 0x6d], "same reference Extra list and actual receiver subtype");
        Need(getter + 0x53, [0x0f, 0x57, 0xc0, 0x0f, 0x11, 7], "independent receiver zeroed constructor");
        Need(getter + 0x6f, [0xc7, 0x40, 0xc, 0, 0, 1, 0], "empty effective count and grow-by-one callback array");
        Need(getter + 0x91, [0x89, 0x77, 4], "receiver retains its actual Actor owner");
        Need(getter + 0xa6, [0xc6, 0x46, 4, 0x6d], "actual Extra subtype publication");
        Need(getter + 0xba, [0x89, 0x7e, 0xc], "Extra retains the same real receiver");
        Need(getter + 0xf8, [0x8b, 0x40, 0xc], "existing Extra returns its retained receiver");
        if (!image.SourceClassTables(".?AVBSAnimNoteReceiver@@", 1).Contains(Word(code, getter + 0x63)) ||
            !image.SourceClassTables(".?AVExtraAnimNoteReceiver@@", 1).Contains(Word(code, getter + 0xb6)))
            throw new NotSupportedException("Animation-note owner changed its independent receiver or Extra class.");
        Need(caller + 0x579, [0x68], "actual arm-note callback");
        Need(caller + 0x57e, [0x6a, 1], "independent callback type one");
        var registration = Call(caller + 0x588);
        Need(registration + 0x11, [0x0f, 0xb7, 0x73, 0x12], "callback scan consumes its actual array size");
        Need(registration + 0x23, [0x39, 0x39], "first callback key is the actual note type");
        Need(registration + 0x2a, [0x39, 0x79, 4], "independent second key is callback identity");
        Need(registration + 0x30, [0x0f, 0x44, 0xd0], "duplicate selection retains the last matching pair");
        Need(registration + 0x56, [0x89, 0x38, 0x89, 0x48, 4], "new pair retains type and actual callable");
        Need(registration + 0x75, [0x32, 0xc0], "registration returns false after mutation");
        var callback = SourceOffset(code, image.CodeBase, Word(code, caller + 0x57a));
        Need(callback + 7, [0x8b, 0x8e, 0xa4, 0, 0, 0], "callback consumes current independent Actor controller field");
        Need(callback + 0xd, [0x85, 0xc9, 0x74], "absent current controller returns without dispatch");
        var modelLoad = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x1c8, 4), 0));
        var bind = Call(modelLoad + 0x4f8); var manager = Call(bind + 0x148);
        var target = Call(manager + 0x9a); var insert = Call(target + 0x45);
        Need(target + 0x29, [0x89, 0x7e, 0x2c], "manager retains its actual target before palette construction");
        Need(insert + 0x2f, [0x89, 0x7e, 0x30], "new current controller retains the prior embedded head as next");
        Need(insert + 0x53, [0x89, 0x73, 0xc], "actual manager publishes at its target's current controller head");
        var noteList = Call(manager + 0xf4);
        Need(noteList + 2, [0xc7, 0x41, 0xc, 0, 0, 1, 0], "independent source note-list constructor grow-by-one field");
        Need(manager + 0xf9, [0x89, 0x46, 0x64], "manager retains its genuine separately constructed note source");
        Need(caller + 0x646, [0x8b, 0x4e, 0x64, 0x6a, 1], "current manager note source and original receiver type");
        var sourceRegistration = Call(caller + 0x64b);
        Need(sourceRegistration + 0x11, [0x0f, 0xb7, 0x73, 0xa], "source registration consumes its own actual array size");
        Need(sourceRegistration + 0x23, [0x39, 0x39], "source pair note type");
        Need(sourceRegistration + 0x2a, [0x39, 0x79, 4], "independent source pair receiver identity");
        Need(sourceRegistration + 0x30, [0x0f, 0x44, 0xd0], "source pair retains its last actual duplicate");
        Need(sourceRegistration + 0x53, [0x89, 0x38, 0x89, 0x48, 4], "new source pair retains its actual current receiver");
        Need(sourceRegistration + 0x71, [0x32, 0xc0], "source registration returns false after actual mutation");
        var currentFilter = Call(caller + 0x6f6);
        Need(currentFilter + 0x49, [0x8b, 0x45, 8, 0xc7, 0, 0, 0, 0, 0], "independent absent-process filter zero");
        Need(currentFilter + 0x3c, [0x8b, 0x4d, 8, 0x33, 0xc0, 0x89, 1], "independent absent current physical-body filter zero");
        Need(currentFilter + 0x16, [0x8b, 0x88, 0x24, 4, 0, 0], "same actual current controller first-body field");
        Need(caller + 0x707, [0xc1, 0xe8, 0x10, 0x89, 0x41, 0x5c], "source collision group stores the upper current filter word");
        Need(caller + 0x713, [0x83, 0xc1, 0x18], "body-part table's own independent model component");
        Need(caller + 0x735, [0x6a, 0x5c], "literal backslash path selector");
        Need(caller + 0x744, [0x68], "source death filename append");
        if (image.Literal(Word(code, caller + 0x745)) != FalloutRagdollDeathAssetPath.Suffix)
            throw new NotSupportedException("Ragdoll death-path selection changed its actual source filename.");
        var death = Call(caller + 0x797);
        Need(death + 0x40, [0x80, 0x7f, 0x42, 0], "death initializer consumes actual current readiness");
        Need(death + 0x60, [0x89, 0x87, 0x4c, 2, 0, 0], "independent source death-path field publication");
        Need(death + 0x74, [0x8b, 0x87, 0xa4, 2, 0, 0], "actual current animation instance before name lookup");
        foreach (var (at, name) in new[] { (0x95, World.Physics.FalloutRagdollDeathBones.Names[0]),
            (0xbd, World.Physics.FalloutRagdollDeathBones.Names[1]), (0xe5, World.Physics.FalloutRagdollDeathBones.Names[2]) })
            if (image.Literal(Word(code, death + at)) != name)
                throw new NotSupportedException("Ragdoll death initializer changed an actual default bone name.");
        Need(death + 0xf8, [0x8b, 0x4f, 0x48], "actual current physical instance before constraint inspection");
        Need(death + 0x13b, [0x83, 0xf8, 0xd], "malleable current data refuses motor admission");
        Need(death + 0x149, [0x83, 0xf8, 0xc], "breakable current data independently refuses motor admission");
        Need(death + 0x1b6, [0x66, 0x83, 0x7d, 0xd8, 0xff], "failed motor admission retains missing default bone sentinel");
        var constructor = Call(caller + 0x140);
        Need(constructor + 0xa7, [0xa1], "independent death-path constructor sentinel read");
        if (Word(image.Read(Word(code, constructor + 0xa8), 4), 0) != 0)
            throw new NotSupportedException("Death-path constructor source has no reviewed null sentinel.");
        Need(constructor + 0xac, [0x89, 0x86, 0x4c, 2, 0, 0], "actual independent death-path constructor null store");
        Need(constructor + 0x3c8, [0xc6, 0x86, 0x54, 2, 0, 0, 0], "independent final body-part feature availability initially false");
        Need(constructor + 0x38c, [0x66, 0xc7, 0x86, 0x55, 2, 0, 0, 0, 0], "independent final body-part feature enable initially false");
        Need(constructor + 0x485, [0xc7, 0x86, 0x90, 0, 0, 0, 0, 0, 0, 0], "actual final current-body field initially null");
        Need(caller + 0x7d6, [0x80, 0xb9, 0x54, 2, 0, 0, 0], "final independent body-part feature availability read");
        Need(caller + 0x7e0, [0x88, 0x81, 0x55, 2, 0, 0], "final independent feature enable copy");
        Need(caller + 0x808, [0x89, 0x81, 0x90, 0, 0, 0], "final current body pointer publication");
        var helper = SourceOffset(code, image.CodeBase, Word(image.Read(player + 0x1c4, 4), 0));
        Need(helper + 0x5af, [0xc6, 0x80, 0x24, 2, 0, 0, 0], "actual Player-specific arm-control store after Actor configuration returns");
        Need(helper + 0x5bc, [0xc6, 0x80, 0x41, 2, 0, 0, 1], "independent Player flag store after actual Actor return");

        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("Animation-note source lost an actual direct child.");
        void Need(int at, byte[] expected, string role)
        { if (!Match(code, at, expected)) throw new NotSupportedException("Animation-note source changed its " + role + "."); }
    }
}
