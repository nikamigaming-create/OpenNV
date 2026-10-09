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
            foreach (var mesh in NativePlayerSelfVisibility.HeadGeometry(Actor))
                _physicalHeadLayers.Add(mesh, mesh.Layers);
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
