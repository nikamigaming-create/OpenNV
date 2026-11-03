namespace OpenNV.Runtime.World.Cells;

internal sealed partial class FalloutReferenceWorld
{
    private FalloutRuntimeReferences? _runtimeReferences;
    internal FalloutRuntimeReferences RuntimeReferences => _runtimeReferences ??= new(records);
}
