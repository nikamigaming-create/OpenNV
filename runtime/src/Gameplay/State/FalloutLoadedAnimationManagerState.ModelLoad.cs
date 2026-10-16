using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Gameplay.State;

internal sealed record FalloutPlayerModelNamedRead(string Name, Guid ModelFactory, Guid LogicalLifetime,
    Guid? ManagerFactory, FalloutNifSceneKey? Palette, FalloutNifSceneKey? Fallback,
    FalloutNifSceneKey? Returned);

internal sealed partial class FalloutLoadedAnimationManagerState
{
    // The actual manager constructs one palette from its retained root. Its
    // insertion visits the current root and child slots before later siblings;
    // a repeated interned name replaces the earlier pointer.
    private readonly Dictionary<string, FalloutNifSceneObject> _modelPalette = new(StringComparer.Ordinal);
    private void ConstructModelObjectPalette()
    {
        foreach (var node in _scene.Objects)
            if (!string.IsNullOrEmpty(node.Name)) _modelPalette[node.Name] = node;
    }
    internal FalloutNifSceneObject? ReadModelPalette(string name, FalloutActorLogicalScene expected)
    {
        Require(); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!ReferenceEquals(expected, _scene)) throw new InvalidOperationException("Model lookup borrowed another animation-manager palette.");
        return _modelPalette.GetValueOrDefault(name);
    }
}
