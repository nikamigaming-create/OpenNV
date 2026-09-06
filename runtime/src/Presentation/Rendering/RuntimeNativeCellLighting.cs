using Godot;
using OpenNV.Runtime.Content;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

// Instance uniforms belong to the active cell, including objects attached
// after cell construction. Separate viewport scenes own their environment.
internal partial class RuntimeNativeCellLighting : Node
{
    private FalloutCellLighting _lighting = null!;
    private float _unitsToMeters;
    private Node _cell = null!;
    private SceneTree? _tree;

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
        _cell = GetParent();
        _tree = GetTree();
        BindExisting(_cell);
        _tree.NodeAdded += BindAdded;
    }

    public override void _ExitTree()
    {
        if (_tree is not null) _tree.NodeAdded -= BindAdded;
        _tree = null;
    }

    private void BindExisting(Node node)
    {
        if (node is Viewport) return;
        if (node is MeshInstance3D mesh)
            NativeNifMaterialEnvironment.Bind(mesh, _lighting, _unitsToMeters);
        foreach (var child in node.GetChildren()) BindExisting(child);
    }

    private void BindAdded(Node node)
    {
        if (node is MeshInstance3D mesh && _cell.IsAncestorOf(node) && node.GetViewport() == _cell.GetViewport())
            NativeNifMaterialEnvironment.Bind(mesh, _lighting, _unitsToMeters);
    }
}
