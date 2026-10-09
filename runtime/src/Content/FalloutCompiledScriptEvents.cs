namespace OpenNV.Runtime.Content;

internal static class FalloutCompiledScriptEvents
{
    // Original event declaration identities. This table supplies admission
    // identity only; it does not claim their filter or callback semantics.
    private static readonly string[] Names =
    [
        "GameMode", "MenuMode", "OnActivate", "OnAdd", "OnEquip", "OnUnequip", "OnDrop", "SayToDone",
        "OnHit", "OnHitWith", "OnDeath", "OnMurder", "OnCombatEnd", "Unused", "Unused2", "OnPackageStart",
        "OnPackageDone", "ScriptEffectStart", "ScriptEffectFinish", "ScriptEffectUpdate", "OnPackageChange",
        "OnLoad", "OnMagicEffectHit", "OnSell", "OnTrigger", "OnStartCombat", "OnTriggerEnter", "OnTriggerLeave",
        "OnActorEquip", "OnActorUnequip", "OnReset", "OnOpen", "OnClose", "OnGrab", "OnRelease",
        "OnDestructionStageChange", "OnFire", "OnNPCActivate"
    ];

    internal static string Name(ushort opcode) => opcode < Names.Length && opcode is not (13 or 14)
        ? Names[opcode] : throw new NotSupportedException($"Compiled event opcode {opcode:x4} has no admission identity.");

    internal static void RequireAdmission(FalloutCompiledScriptProgram program, FalloutCompiledEvent block)
    {
        if (block.Event is not (0 or 2 or 21))
            throw new NotSupportedException($"Compiled {Name(block.Event)} event filter/callback semantics are unowned.");
        if (block.Parameters.IsEmpty) return;
        var data = block.Parameters.Span;
        if (data.Length < 2) throw new InvalidDataException("Compiled event argument count is truncated.");
        var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (count == 0 && data.Length == 2) return;
        // The original OnActivate header argument does not filter activation.
        // Its encoded identity must still belong to this source table.
        if (block.Event == 2 && count == 1 && data.Length == 5 && data[2] == (byte)'r')
        {
            _ = program.Reference(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data[3..]));
            return;
        }
        throw new NotSupportedException("Compiled event arguments are outside the owned admission domain.");
    }
}
