using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime;

public partial class RuntimeCoordinator
{
    private FalloutCompassFrame NativeCompass(float stripWidth)
    {
        var player = _nativePlayer!;
        var position = NativePipBoyMapPosition(player);
        var rotation = GamebryoCoordinate.ReferenceEuler(player.GlobalBasis);
        return _nativeReferences!.Compass(new(_nativeActiveCell!.Cell.FormKey,
            [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z]),
            player.UnitsToMeters, stripWidth, _nativeQuestState!, _nativeOpeningStageDriver!.EvaluateTargetCondition);
    }
}
