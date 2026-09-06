using Godot;
using OpenNV.Runtime.Formats.Gamebryo;

namespace OpenNV.Runtime.Presentation.Rendering;

// Reference colour varies independently even when its NIF materials are shared.
internal partial class RuntimeNativeReferenceEmittance : Node
{
    private Func<float[]> _sample = null!;
    private Vector3 _color;
    private Node _reference = null!;
    private SceneTree? _tree;
    private readonly HashSet<MeshInstance3D> _meshes = [];

    internal void Configure(Func<float[]> sample)
    {
        _sample = sample;
        _color = ReadColor();
    }

    public override void _EnterTree()
    {
        _reference = GetParent();
        _tree = GetTree();
        _color = ReadColor();
        BindExisting(_reference);
        _tree.NodeAdded += Added;
        _tree.NodeRemoved += Removed;
        PublishColor();
    }

    public override void _ExitTree()
    {
        if (_tree is not null) { _tree.NodeAdded -= Added; _tree.NodeRemoved -= Removed; }
        foreach (var mesh in _meshes) NativeNifEmittanceMaterial.Bind(mesh, null);
        _meshes.Clear();
        _tree = null;
    }

    public override void _Process(double delta)
    {
        try
        {
            var color = ReadColor();
            if (color == _color) return;
            _color = color;
            foreach (var mesh in _meshes) NativeNifEmittanceMaterial.Bind(mesh, color);
            PublishColor();
        }
        catch (Exception error)
        {
            SetMeta("opennv_emittance_unbound", error.Message);
            SetProcess(false);
            GetTree().Paused = true;
            GD.PushError($"OPENNV_MATERIAL_EMITTANCE_UNBOUND {_reference.Name}: {error.Message}");
        }
    }

    private Vector3 ReadColor()
    {
        var values = _sample();
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value) || value < 0))
            throw new InvalidDataException("Reference material emittance requires finite nonnegative RGB.");
        return new(values[0], values[1], values[2]);
    }

    private void PublishColor() => SetMeta("opennv_material_emittance_rgb", _color);

    private void BindExisting(Node node)
    {
        if (node is Viewport) return;
        if (node is MeshInstance3D mesh && NativeNifEmittanceMaterial.Accepts(mesh))
        {
            _meshes.Add(mesh);
            NativeNifEmittanceMaterial.Bind(mesh, _color);
        }
        foreach (var child in node.GetChildren()) BindExisting(child);
    }

    private void Added(Node node)
    {
        if (node is MeshInstance3D && _reference.IsAncestorOf(node) && node.GetViewport() == _reference.GetViewport())
            BindExisting(node);
    }

    private void Removed(Node node)
    {
        if (node is MeshInstance3D mesh && _meshes.Remove(mesh)) NativeNifEmittanceMaterial.Bind(mesh, null);
    }
}
