using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Content;

internal sealed partial class FalloutActorSourceModel
{
    private FalloutActorLogicalScene? _logicalScene;
    internal FalloutActorLogicalScene? LogicalScene => _logicalScene;
    internal FalloutActorLogicalScene ConstructLogicalScene(FalloutActorLogicalSceneSnapshot? saved = null)
    {
        if (_logicalScene is not null) throw new InvalidOperationException("Actual source factory already owns its logical scene.");
        return _logicalScene = new(this, saved);
    }
}
