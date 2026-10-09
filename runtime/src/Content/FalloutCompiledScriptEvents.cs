namespace OpenNV.Runtime.Content;

internal static partial class FalloutCompiledScriptEvents
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

    internal static void RequireAdmission(FalloutCompiledScriptProgram program, FalloutCompiledEvent block) =>
        _ = ActorCallbackFilter(program, block);
}
