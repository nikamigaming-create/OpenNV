using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

// Cell lighting publishes to global environment parameters for standard meshes
// without consuming hardware instance uniform slots, while overridden viewports
// and items preserve explicit instance bindings.
internal partial class RuntimeNativeCellLighting : Node
{
    private FalloutCellLighting _lighting = null!;
    private float _unitsToMeters;

    internal void Configure(FalloutCellLighting lighting, float unitsToMeters)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        if (!float.IsFinite(unitsToMeters) || unitsToMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsToMeters));
        _lighting = lighting;
        _unitsToMeters = unitsToMeters;
    }

    public override void _EnterTree()
    {
        NativeNifMaterialEnvironment.PublishExterior(
            NativeNifMaterialEnvironment.Rgb(_lighting.AmbientRgb),
            NativeNifMaterialEnvironment.Rgb(_lighting.FogRgb),
            new(_lighting.FogNear, _lighting.FogFar, _lighting.FogPower),
            _unitsToMeters);
    }
}
