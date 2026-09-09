using Godot;

namespace OpenNV.Runtime.Formats.Gamebryo;

/// <summary>Publishes this NIF's live visual/physics bones to its hardware skin.</summary>
internal partial class RuntimeNifEmbeddedSkin : Skeleton3D
{
    // Relative paths, unlike C# references, remain valid in duplicated scenes.
    [Export] public NodePath SourceRoot { get; set; } = new();
    [Export] public Godot.Collections.Array<NodePath> SourceBones { get; set; } = [];
    private Node3D _root = null!;
    private Node3D[] _bones = [];

    internal void Bind(Node3D root, Node3D[] bones)
    {
        SourceRoot = GetPathTo(root);
        SourceBones = new(bones.Select(bone => GetPathTo(bone)));
        Resolve();
        for (var bone = 0; bone < _bones.Length; bone++) SetBoneRest(bone, Relative(_root, _bones[bone]));
        ResetBonePoses();
    }

    private void Resolve()
    {
        _root = GetNode<Node3D>(SourceRoot);
        _bones = SourceBones.Select(GetNode<Node3D>).ToArray();
        if (_bones.Length != GetBoneCount() || _bones.Length == 0)
            throw new InvalidDataException("Embedded skin has incomplete source bone bindings.");
    }

    public override void _Ready() { Resolve(); Publish(); }
    public override void _Process(double delta) => Publish();

    internal void Publish()
    {
        for (var bone = 0; bone < _bones.Length; bone++)
        {
            var pose = Relative(_root, _bones[bone]);
            SetBonePosePosition(bone, pose.Origin);
            SetBonePoseRotation(bone, pose.Basis.GetRotationQuaternion());
            SetBonePoseScale(bone, pose.Basis.Scale);
        }
    }

    internal static Transform3D Relative(Node3D root, Node3D bone)
    {
        var result = Transform3D.Identity;
        for (Node? node = bone; node != root; node = node.GetParent())
        {
            if (node is not Node3D spatial)
                throw new InvalidDataException("Embedded skin bone is not below its declared source root.");
            result = spatial.Transform * result;
        }
        return result;
    }
}
