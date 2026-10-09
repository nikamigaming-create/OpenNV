using Godot;

namespace OpenNV.Runtime.World.Actors;

internal sealed partial class RuntimeNativePlayerActor
{
    private readonly Dictionary<GeometryInstance3D, uint> _physicalHeadLayers = [];
    internal void SetPhysicalView(Camera3D camera, bool excludeOwnHead)
    {
        if (_first) throw new InvalidOperationException("Physical world presentation requires the actual whole player body.");
        SetViewPolicy(true, true);
        if (_physicalHeadLayers.Count == 0)
        {
            for (var index = 0; index < Actor.Parts.Count; index++)
            {
                var part = Actor.Appearance.Models[index];
                const uint headSlots = 0x00017e03;
                if ((part.BipedSlots & headSlots) == 0 && part.Role is not ("head" or "hair" or "ears" or "mouth" or
                    "teeth-lower" or "teeth-upper" or "tongue" or "eye-left" or "eye-right" or "head-addon")) continue;
                if ((part.BipedSlots & 4) != 0)
                    throw new NotSupportedException("Combined head/body equipment needs its source partition visibility owner.");
                foreach (var mesh in Actor.Parts[index].Root.FindChildren("*", "", true, false).OfType<GeometryInstance3D>())
                    _physicalHeadLayers.Add(mesh, mesh.Layers);
            }
        }
        foreach (var (mesh, layers) in _physicalHeadLayers)
            mesh.Layers = excludeOwnHead ? SelfHeadLayer : layers;
        if (excludeOwnHead) camera.CullMask &= ~SelfHeadLayer;
    }
    internal void RetirePhysicalView()
    {
        foreach (var (mesh, layers) in _physicalHeadLayers)
            if (GodotObject.IsInstanceValid(mesh)) mesh.Layers = layers;
        _physicalHeadLayers.Clear(); _eyeVisible = null;
    }
}
