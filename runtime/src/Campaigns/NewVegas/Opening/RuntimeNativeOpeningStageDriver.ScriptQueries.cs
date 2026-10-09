using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;
using OpenNV.Runtime.World.Cells;

namespace OpenNV.Runtime.Campaigns.NewVegas.Opening;

internal partial class RuntimeNativeOpeningStageDriver
{
    private FalloutReferencePlacement ReferenceScriptPlacement(FalloutFormKey reference, bool requireFacing)
    {
        var position = _player.GlobalPosition / _player.UnitsToMeters;
        var rotation = GamebryoCoordinate.ReferenceEuler(_player.GlobalBasis);
        // View pitch is a separate player state component. Composing the
        // camera basis would mix heading into the queried XYZ Euler angles.
        if (requireFacing) rotation.X -= _player.ViewPitchRadians;
        var player = new FalloutReferencePlacement(_activeCell,
            [position.X, -position.Z, position.Y], [rotation.X, rotation.Y, rotation.Z]);
        return _scripts.References!.ScriptPlacement(reference, player, _player.UnitsToMeters, requireFacing);
    }
}
