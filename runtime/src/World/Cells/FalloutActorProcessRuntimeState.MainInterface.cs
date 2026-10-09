using OpenNV.Runtime.Content;
using OpenNV.Runtime.Gameplay.State;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    private FalloutMainInterfaceState? _sourceMainInterface;
    internal FalloutMainInterfaceState SourceMainInterface => _sourceMainInterface ??
        throw new NotSupportedException("Actual selected Main interface constructor is absent.");
    internal string? SourceMainInterfaceSaveBlocker => _sourceMainInterface?.SaveBlocker;
    private void ConstructSourceMainInterface(FalloutMainScriptCallerSource source, FalloutMainInterfaceSnapshot? saved)
    {
        if (_sourceMainInterface is not null || !source.HasNewVegasChildren)
            throw new InvalidOperationException("Main interface cannot replace or select another actual source family.");
        _sourceMainInterface = new(FalloutMainInterfaceSource.Read(source), _stack, _process, saved);
    }
    internal int ReadSourceMainInterface(FalloutMainScriptInvocation invocation, FalloutStandaloneInterfaceQuery query)
    {
        var step = _scriptCallerLast?.Children.LastOrDefault()?.Step ?? throw new InvalidOperationException("Main interface read omitted its actual entered child.");
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
        RequireMainScriptChild(invocation, step);
        if (!allowed) throw new InvalidOperationException("Another Main child borrowed the source interface predicate.");
        return SourceMainInterface.Read(query);
    }
    internal void RetireSourceMainInterface()
    { RequireMainScriptClosureBoundary(retiring: true); _sourceMainInterface?.Retire(); }
    internal void RequireMainInterfaceNativeRetirement() => RequireMainScriptClosureBoundary(retiring: true);
}
