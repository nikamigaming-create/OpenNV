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
    public bool MenuGate(FalloutMainScriptInvocation invocation) => Source.HasNewVegasChildren
        ? throw Unowned("independent-interface-menu-gate") : invocation.Owner.ReadStandaloneInterface(invocation,
            OpenNV.Runtime.Gameplay.State.FalloutStandaloneInterfaceQuery.MenuGate) != 0;
    public bool GuiModeTwo(FalloutMainScriptInvocation invocation) => Source.HasNewVegasChildren
        ? throw Unowned("original-GUI-mode-two-query") : invocation.Owner.ReadStandaloneInterface(invocation,
            OpenNV.Runtime.Gameplay.State.FalloutStandaloneInterfaceQuery.GuiModeTwo) != 0;
    public bool FirstInterfacePredicate(FalloutMainScriptInvocation invocation) => Source.HasNewVegasChildren
        ? throw Unowned("first-interface-predicate") : invocation.Owner.ReadStandaloneInterface(invocation,
            OpenNV.Runtime.Gameplay.State.FalloutStandaloneInterfaceQuery.FirstPredicate) != 0;
    public bool FinalInterfacePredicate(FalloutMainScriptInvocation invocation) => Source.HasNewVegasChildren
        ? throw Unowned("final-interface-predicate") : invocation.Owner.ReadStandaloneInterface(invocation,
            OpenNV.Runtime.Gameplay.State.FalloutStandaloneInterfaceQuery.FinalPredicate) != 0;
    public bool ForeignActiveMenu(FalloutMainScriptInvocation invocation) => Source.HasNewVegasChildren
        ? throw Unowned("foreign-active-menu-identity-query") : invocation.Owner.ReadStandaloneInterface(invocation,
            OpenNV.Runtime.Gameplay.State.FalloutStandaloneInterfaceQuery.ForeignMenu) != 0;
    public int InterfaceContextKind(FalloutMainScriptInvocation invocation) => Source.HasNewVegasChildren
        ? throw Unowned("interface-context-signed-kind-query") : invocation.Owner.ReadStandaloneInterface(invocation,
            OpenNV.Runtime.Gameplay.State.FalloutStandaloneInterfaceQuery.ContextKind);
    public void KindThreePrelude(FalloutMainScriptInvocation invocation)
    { invocation.Require(FalloutMainScriptCallerStep.KindThreePrelude); throw Unowned("kind-three-before-Player-child"); }
    public Task Player(FalloutMainScriptInvocation invocation)
    { invocation.Require(FalloutMainScriptCallerStep.Player); return invocation.Owner.ExecuteMainPlayerCell(invocation); }
    public void SteamCallbacks(FalloutMainScriptInvocation invocation)
    { invocation.Require(FalloutMainScriptCallerStep.SteamCallbacks); invocation.Owner.ExecuteMainSteamCallbacks(invocation); }
    public bool MainHold(FalloutMainScriptInvocation invocation)
    {
        invocation.Require(FalloutMainScriptCallerStep.MainHold);
        if (!Source.HasNewVegasChildren) return invocation.Owner.ReadStandaloneMainHold(invocation);
        throw Unowned("independent-held-byte-query");
    }
    public void TimedContexts(FalloutMainScriptInvocation invocation, bool advanceTimer)
    { invocation.Require(FalloutMainScriptCallerStep.TimedContexts); throw Unowned("timed-context-prelude-list-update-retirement-and-final-children"); }
}
