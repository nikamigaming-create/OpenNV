using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutStandaloneInterfaceState? _standaloneInterface;
    internal FalloutStandaloneInterfaceState StandaloneInterface => _standaloneInterface ??
        throw new NotSupportedException("Selected Main has no actual standalone interface constructor.");
    private void ConstructStandaloneInterface(FalloutMainScriptCallerSource source, FalloutStandaloneInterfaceSnapshot? restore)
    {
        if (_standaloneInterface is not null) throw new InvalidOperationException("Standalone interface cannot replace its source lifetime.");
        _standaloneInterface = new(FalloutStandaloneInterfaceSource.Read(source), _stack, _process, restore);
    }
    internal int ReadStandaloneInterface(FalloutMainScriptInvocation invocation, FalloutStandaloneInterfaceQuery query)
    {
        var step = _scriptCallerLast?.Children.LastOrDefault()?.Step ?? throw new InvalidOperationException("Interface query has no real Main child.");
        var allowed = query switch
        {
            FalloutStandaloneInterfaceQuery.MenuGate => step is FalloutMainScriptCallerStep.MenuGateBefore or FalloutMainScriptCallerStep.MenuGateAfter,
            FalloutStandaloneInterfaceQuery.GuiModeTwo => step is FalloutMainScriptCallerStep.GuiModeBefore or FalloutMainScriptCallerStep.GuiModeAfter,
            FalloutStandaloneInterfaceQuery.FirstPredicate => step is FalloutMainScriptCallerStep.FirstPredicateBefore or FalloutMainScriptCallerStep.FirstPredicateAfter,
            FalloutStandaloneInterfaceQuery.FinalPredicate => step is FalloutMainScriptCallerStep.FinalPredicateBefore or FalloutMainScriptCallerStep.FinalPredicateAfter,
            FalloutStandaloneInterfaceQuery.ForeignMenu => step == FalloutMainScriptCallerStep.ForeignMenu,
            FalloutStandaloneInterfaceQuery.ContextKind => step == FalloutMainScriptCallerStep.ContextKind,
            _ => false
        };
        if (!allowed) throw new InvalidOperationException("Standalone interface query changed the actual source child order.");
        RequireMainScriptChild(invocation, step);
        return StandaloneInterface.Read(query);
    }
    internal bool ReadStandalonePlayerHeldInterface(FalloutMainPlayerCellInvocation invocation)
    {
        invocation.Require(FalloutMainPlayerCellStep.HeldInterfaceQuery);
        if (!ReferenceEquals(invocation.Owner, this)) throw new InvalidOperationException("Player interface query changed its actual Main process.");
        return StandaloneInterface.Read(FalloutStandaloneInterfaceQuery.GuiModeTwo) != 0;
    }
    internal void RequireStandaloneInterfaceNativeRetirement() => RequireMainScriptClosureBoundary(retiring: true);
    private void RetireStandaloneInterface() => _standaloneInterface?.Retire();
}
