using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessRuntimeState
{
    internal void StoreMainPlayerWorldBracket(FalloutMainPlayerCellInvocation invocation, bool value)
    {
        invocation.Require(value ? FalloutMainPlayerCellStep.WorldBracketSet : FalloutMainPlayerCellStep.WorldBracketClear);
        _mainPlayerWorldBracket = value; _ = Next();
    }
    internal void StoreMainPlayerMovementBracket(FalloutMainPlayerCellInvocation invocation, bool value)
    {
        invocation.Require(value ? FalloutMainPlayerCellStep.PlayerBracketSet : FalloutMainPlayerCellStep.PlayerBracketClear);
        _mainPlayerMovementBracket = value; _ = Next();
    }
    internal void StoreMainPlayerRootBinding(FalloutMainPlayerCellInvocation invocation, FalloutFormKey cell, ulong nativeRoot)
    {
        invocation.Require(FalloutMainPlayerCellStep.RootStore);
        if (nativeRoot == 0 || _mainPlayerCellLast?.AfterCell?.Cell != cell ||
            _mainPlayerWorldBracket != true || _mainPlayerMovementBracket != true)
            throw new InvalidOperationException("Player source root store has no original ordered bracket/current target/native owner.");
        _mainPlayerRootBinding = new(cell, _process, nativeRoot, Next());
    }
    internal void RebindColdMainPlayerRoot(FalloutFormKey cell, ulong nativeRoot)
    {
        RequireNotBusy();
        if (_mainPlayerCellActiveMain is not null || _mainPlayerCellLease == Guid.Empty || _mainPlayerRootBinding is not null ||
            nativeRoot == 0 || _mainPlayerPreviousRoot is not { } previous || previous.Cell != cell || previous.Process == _process)
            throw new InvalidOperationException("Cold Player root rebinding must observe its actual newly published matching native source CELL.");
        _mainPlayerRootBinding = new(cell, _process, nativeRoot, Next());
    }
    internal FalloutFormKey? MainPlayerColdRootToRebind => _mainPlayerRootBinding is null ? _mainPlayerPreviousRoot?.Cell : null;
}
