using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed record FalloutMainSteamServiceSource(FalloutMainScriptCallerSource Main, string ContractSha256)
{
    private const string Contract = "actual-source-Main-Steam-child/v1;lazy-service-getter-before-unconditional-exported-pump;" +
        "real-selected-provider-constructor-callback-member-lifetime-and-source-handler;" +
        "source-singleton-constructor-failed-prefix-never-replayed;no-Player-or-prologue-phase-alias";
    internal static FalloutMainSteamServiceSource Read(FalloutMainScriptCallerSource main)
    { main.Validate(); return new(main, FalloutAdvancementRuntimeReceipt.Hash(Contract)); }
    internal void Validate()
    { if (this != Read(Main)) throw new InvalidDataException("Actual Main Steam service changed its source constructor/caller."); }
}
internal interface IFalloutMainSteamService
{
    FalloutMainSteamServiceSource Source { get; }
    string Owner { get; }
    void MainCallbacks(FalloutMainScriptInvocation invocation);
}
internal interface IFalloutMainUtilityInvocationScope
{
    IDisposable EnterMainUtility(FalloutMainScriptInvocation invocation);
}
