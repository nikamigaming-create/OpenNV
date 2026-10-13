namespace OpenNV.Runtime.Content;

internal static partial class FalloutCompiledScriptEvents
{
    internal const ushort PackageStart = 15;
    internal const ushort PackageDone = 16;
    internal const ushort PackageChange = 20;
    internal static bool IsPackage(ushort opcode) => opcode is PackageStart or PackageDone or PackageChange;

    internal const ushort Trigger = 24;
    internal const ushort TriggerEnter = 26;
    internal const ushort TriggerLeave = 27;
    internal static bool IsTrigger(ushort opcode) => opcode is Trigger or TriggerEnter or TriggerLeave;

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
