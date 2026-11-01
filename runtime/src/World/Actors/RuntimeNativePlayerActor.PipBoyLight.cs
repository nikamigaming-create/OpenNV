using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private readonly List<MeshInstance3D> _pipBoyLightEffects = [];
    internal void SetWorldPipBoyLight(bool enabled)
    {
        foreach (var mesh in _pipBoyLightEffects) mesh.Visible = enabled;
    }
}
