namespace OpenNV.Runtime.Gameplay.State;

internal sealed partial class FalloutPlayerInventory
{
    private bool _scriptIterationActive;
    private bool _scriptTraversalOwned = true;
    internal bool InventoryScriptActive => _scriptIterationActive;

    internal readonly struct ScriptIterationScope(FalloutPlayerInventory owner) : IDisposable
    {
        public void Dispose()
        {
            owner._scriptIterationActive = false;
            owner._scriptTraversalOwned = true;
        }
    }

    internal ScriptIterationScope EnterScriptIteration(bool traversalOwned = true)
    {
        if (_scriptIterationActive) throw new InvalidOperationException("Inventory script iteration already has a live owner.");
        _scriptIterationActive = true;
        _scriptTraversalOwned = traversalOwned;
        return new(this);
    }

    private Guid? _runningInventoryScript;
    internal readonly struct ScriptInvocationScope(FalloutPlayerInventory owner, Guid? previous) : IDisposable
    {
        public void Dispose() => owner._runningInventoryScript = previous;
    }
    internal ScriptInvocationScope EnterScriptInvocation(Guid identity)
    {
        var previous = _runningInventoryScript; _runningInventoryScript = identity;
        return new(this, previous);
    }

    private long _scriptMutationRevision;
    internal long ScriptMutationRevision => _scriptMutationRevision;
    private void MarkScriptTraversalMutation() => _scriptMutationRevision = checked(_scriptMutationRevision + 1);
}
