using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

// This is the actual campaign host, not a fixture's successful callbacks. Each
// missing original child retains its own boundary. Real tile animations,
// source queue windows, published bodies and Menus.GameMode are not aliases.
internal sealed class FalloutCampaignMainScriptConsumers(FalloutMainScriptCallerSource source,
    Func<int, bool> actualOsKeyHighBit) : IFalloutMainScriptCallerConsumers
{
    public FalloutMainScriptCallerSource Source { get; } = source;
    public string Owner => "actual-campaign-Main-script-children/" + Source.Identity;
    public bool AsyncKeyHighBit(FalloutMainScriptInvocation invocation, int virtualKey)
    {
        invocation.Require(virtualKey switch
        {
            9 => FalloutMainScriptCallerStep.TabKey,
            18 => FalloutMainScriptCallerStep.AltKey,
            _ => throw new InvalidDataException("Main source key query requested another OS virtual key.")
        });
        return actualOsKeyHighBit(virtualKey);
    }
    private static NotSupportedException Unowned(string value) => new("source-Main-" + value + "-producer-unbound");
    public void Prologue(FalloutMainScriptInvocation invocation)
    {
        invocation.Require(FalloutMainScriptCallerStep.Prologue);
        if (!Source.HasNewVegasChildren) { invocation.Owner.ExecuteStandaloneMainPrologue(invocation); return; }
        using var scope = invocation.Owner.EnterBoundMainUtilityScope(invocation);
        invocation.Owner.ExecuteMainUtilities(invocation);
    }
    public bool MenuGate(FalloutMainScriptInvocation invocation) => throw Unowned("independent-interface-menu-gate");
    public bool GuiModeTwo(FalloutMainScriptInvocation invocation) => throw Unowned("original-GUI-mode-two-query");
    public bool FirstInterfacePredicate(FalloutMainScriptInvocation invocation) => throw Unowned("first-interface-predicate");
    public bool FinalInterfacePredicate(FalloutMainScriptInvocation invocation) => throw Unowned("final-interface-predicate");
    public bool ForeignActiveMenu(FalloutMainScriptInvocation invocation) => throw Unowned("foreign-active-menu-identity-query");
    public int InterfaceContextKind(FalloutMainScriptInvocation invocation) => throw Unowned("interface-context-signed-kind-query");
    public void KindThreePrelude(FalloutMainScriptInvocation invocation)
    { invocation.Require(FalloutMainScriptCallerStep.KindThreePrelude); throw Unowned("kind-three-before-Player-child"); }
    public Task Player(FalloutMainScriptInvocation invocation)
    { invocation.Require(FalloutMainScriptCallerStep.Player); return invocation.Owner.ExecuteMainPlayerCell(invocation); }
    public void SteamCallbacks(FalloutMainScriptInvocation invocation)
    { invocation.Require(FalloutMainScriptCallerStep.SteamCallbacks); invocation.Owner.ExecuteMainSteamCallbacks(invocation); }
    public bool MainHold(FalloutMainScriptInvocation invocation) => throw Unowned("independent-held-byte-query");
    public void TimedContexts(FalloutMainScriptInvocation invocation, bool advanceTimer)
    { invocation.Require(FalloutMainScriptCallerStep.TimedContexts); throw Unowned("timed-context-prelude-list-update-retirement-and-final-children"); }
}
