namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutActorProcessManager
{
    internal Guid ProcessIdentity => _process;
    private string? ConstructorSourceSaveBlocker =>
        _actors.Values.FirstOrDefault(actor => !actor.Source.EnginePlayer && actor.Construction?.Source is null) is { } missing ?
        "source-actor-constructor-receipt-absent:" + missing.Source.Reference : null;
}
