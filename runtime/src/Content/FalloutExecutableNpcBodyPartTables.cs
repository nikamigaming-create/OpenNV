using System.Buffers.Binary;

namespace OpenNV.Runtime.Content;

internal static partial class FalloutExecutableStringTable
{
    private static void RequireNpcBodyPartTables(byte[] code, Image image, int ragdollCaller)
    {
        var npc = image.SourceClassTables(".?AVTESNPC@@", 78).ToArray();
        if (npc is not [var npcTable]) throw new NotSupportedException("Ragdoll body-part selection lost the actual NPC virtual.");
        var getter = SourceOffset(code, image.CodeBase, Word(image.Read(npcTable + 0x180, 4), 0));
        Need(getter, [0xa1], "independent loaded Player body-part table");
        Need(getter + 0xf, [0x3b, 0x4a, 0x1c, 0x74, 5], "actual current Base identity against the Player Base");
        Need(getter + 0x14, [0xa1], "independent default body-part table fallback");
        var playerGlobal = Word(code, getter + 1); var defaultGlobal = Word(code, getter + 0x15);
        var writers = new List<int>();
        for (var at = 0; at <= code.Length - 0x140; ++at)
            if (Match(code, at, [0x6a, 0x1d, 0xe8]) && Match(code, at + 7, [0x83, 0xc4, 4, 0x50, 0xe8]) &&
                Match(code, at + 0x10, [0x83, 0xc4, 0x14, 0xa3]) && Word(code, at + 0x14) == defaultGlobal &&
                Match(code, at + 0x9e, [0x6a, 0x1c, 0xe8]) && Match(code, at + 0xae, [0x83, 0xc4, 0x14, 0xa3]) &&
                Word(code, at + 0xb2) == playerGlobal) writers.Add(at);
        if (writers is not [var writer]) throw new NotSupportedException("NPC body-part globals lost their actual two independent loaded-record writers.");
        if (Call(writer + 2) != Call(writer + 0xa0) || Call(writer + 0xb) != Call(writer + 0xa9))
            throw new NotSupportedException("NPC default and Player body-part tables no longer share actual lookup and cast services.");
        Need(writer + 0x12e, [0xc7, 5], "absent Player record clears its separate override after temporary fallback construction");
        if (Word(code, writer + 0x130) != playerGlobal || Word(code, writer + 0x134) != 0)
            throw new NotSupportedException("NPC missing Player table invented a persistent override.");

        var table = image.SourceClassTables(".?AVBGSBodyPartData@@", 42).ToArray();
        if (table is not [var partTable]) throw new NotSupportedException("Ragdoll body-part source lost the actual BPTD class.");
        var load = SourceOffset(code, image.CodeBase, Word(image.Read(partTable + 0x20, 4), 0));
        var partLoad = Call(load + 0x18c);
        Need(partLoad + 0x114, [0x3d, 0x42, 0x50, 0x4e, 0x49], "actual BPNI subrecord discriminator");
        Need(partLoad + 0x135, [0x8d, 0x4b, 0x14], "independent current IK start-node string writer");
        var enumerate = Call(ragdollCaller + 0x24b);
        Need(enumerate + 0xd, [0xb8, 0x0f, 0, 0, 0], "all fifteen actual BPTD slots");
        Need(enumerate + 0x26, [0xf6, 0x47, 0x60, 2], "actual slot admission flag before list-head publication");
        Need(enumerate + 0x5f, [0x89, 0x3e], "eligible body parts publish at the current list head");
        Need(ragdollCaller + 0x2f6, [0xf6, 0x46, 0x60, 4], "actual IK admission flag");
        Need(ragdollCaller + 0x300, [0x8b, 0x4e, 0x14], "IK uses BPNI independently of target and damage-node names");
        Need(ragdollCaller + 0x3b0, [0xf6, 0x46, 0x60, 0x20], "independent first tracking-part flag");
        Need(ragdollCaller + 0x4a2, [0x68], "actual body-part editor-name selector");
        if (image.Literal(Word(code, ragdollCaller + 0x4a3)) != World.Physics.FalloutRagdollBodyPartPreparation.PrimeEditorSelector)
            throw new NotSupportedException("Ragdoll body-part model classification changed its original selector.");
        var contains = Call(ragdollCaller + 0x4b2);
        Need(contains + 0x1e, [0x0f, 0xb6, 0x11], "source byte equality without case folding in model classification");
        RequireFootIkInitialization(code, image, ragdollCaller);

        int Call(int at) => code[at] == 0xe8 ? checked(at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(at + 1))) :
            throw new NotSupportedException("NPC body-part source lost an actual direct child.");
        void Need(int at, byte[] bytes, string role)
        { if (!Match(code, at, bytes)) throw new NotSupportedException("NPC body-part source changed its " + role + "."); }
    }
}
