using Godot;
using OpenNV.Runtime.Content;

namespace OpenNV.Runtime.Formats.Gamebryo;

// The parsed static model and its actual reference instance are injected together.
// Index source nodes once; Godot names and native IDs are never cold identities.
// Replacement models require a new binding, not this source graph's adapters.
internal sealed class RuntimeNativeNifSoundEmitterBinding
{
    private readonly Node3D _root;
    private readonly FalloutFormKey _reference;
    private readonly string _path;
    private readonly FalloutNifFile _source;
    private readonly Dictionary<int, (Node3D Native, string Name)> _nodes = [];

    internal RuntimeNativeNifSoundEmitterBinding(Node3D root, FalloutFormKey reference, FalloutNifFile source)
    {
        _root = root; _reference = reference; _source = source;
        _path = FalloutBsaArchive.CanonicalPath(root.GetMeta("opennv_source_model", "").AsString());
        new FalloutAnimationSoundNodeEmitterSnapshot(reference, _path, source.Sha256, null, null).Validate(reference);
        RequireCurrentRoot();
        foreach (var node in SourceNodes(root))
        {
            if (node == root || !node.HasMeta("opennv_nif_block") || !node.HasMeta("opennv_nif_source_name")) continue;
            var block = node.GetMeta("opennv_nif_block").AsInt32();
            if (block < 0 || block >= source.Blocks.Count)
                throw new InvalidDataException("Source sound instance has an invalid original node block.");
            var name = source.ReadObject(block) switch
            {
                FalloutNifNode original => original.Name,
                FalloutNifGeometry original => original.Name,
                FalloutNifAmbientLight original => original.Name,
                FalloutNifPointLight original => original.Light.Name,
                FalloutNifParticleSystem original => original.Geometry.Name,
                _ => throw new InvalidDataException("Sound node block is not an original named scene object.")
            };
            if (node.GetMeta("opennv_nif_source_name").AsString() != name || !_nodes.TryAdd(block, (node, name)))
                throw new InvalidDataException("Source sound instance has an invalid or duplicate original node block.");
        }
    }

    internal FalloutAnimationSoundNodeEmitterSnapshot Capture(Node3D emitter)
    {
        RequireCurrentRoot();
        RuntimeNativeNifSoundEmitters.RequireNoAnimationObject(_root, emitter);
        if (emitter == _root) return new(_reference, _path, _source.Sha256, null, null);
        if (!GodotObject.IsInstanceValid(emitter) || !emitter.HasMeta("opennv_nif_block"))
            throw new NotSupportedException("Sound emitter is not an actual indexed source NIF node.");
        var block = emitter.GetMeta("opennv_nif_block").AsInt32();
        if (!_nodes.TryGetValue(block, out var indexed) || indexed.Native != emitter)
            throw new NotSupportedException("Sound emitter is not owned by this parsed reference model.");
        RequireCurrentNode(block, emitter);
        return new(_reference, _path, _source.Sha256, block, emitter.GetMeta("opennv_nif_source_name").AsString());
    }

    internal void RequireOwner(Node3D root, FalloutFormKey reference)
    {
        if (root != _root || reference != _reference)
            throw new InvalidDataException("Source sound binding belongs to a different actual reference instance.");
        RequireCurrentRoot();
    }

    internal Node3D Restore(FalloutAnimationSoundNodeEmitterSnapshot saved)
    {
        saved.Validate(_reference); RequireCurrentRoot();
        if (saved.ModelPath != _path || !saved.ModelSha256.Equals(_source.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Cold sound node differs from its winning original reference model.");
        if (saved.Block is not { } block)
        {
            RuntimeNativeNifSoundEmitters.RequireNoAnimationObject(_root, _root);
            return _root;
        }
        if (!_nodes.TryGetValue(block, out var indexed))
            throw new InvalidDataException("Cold sound model has no original emitter block.");
        var node = indexed.Native;
        RequireCurrentNode(block, node);
        RuntimeNativeNifSoundEmitters.RequireNoAnimationObject(_root, node);
        if (node.GetMeta("opennv_nif_source_name").AsString() != saved.Name)
            throw new InvalidDataException("Cold sound node differs from its original source name.");
        return node;
    }

    private void RequireCurrentRoot()
    {
        if (!GodotObject.IsInstanceValid(_root) || _root.HasMeta("opennv_nif_actor_skeleton") ||
            _root.GetMeta("opennv_reference_form_key", "").AsString() != _reference.ToString() ||
            FalloutBsaArchive.CanonicalPath(_root.GetMeta("opennv_source_model", "").AsString()) != _path ||
            !_root.GetMeta("opennv_nif_source_sha256", "").AsString().Equals(_source.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !_root.HasMeta("opennv_nif_fixed_strings") ||
            !_root.HasMeta("opennv_nif_source_root_blocks") ||
            !_root.GetMeta("opennv_nif_source_root_blocks").AsInt32Array().SequenceEqual(_source.Roots))
            throw new NotSupportedException("Sound node lost its actual static reference/source model owner.");
    }

    private void RequireCurrentNode(int block, Node3D node)
    {
        if (!GodotObject.IsInstanceValid(node) || node.GetMeta("opennv_nif_block", -1).AsInt32() != block ||
            !_nodes.TryGetValue(block, out var indexed) || indexed.Native != node ||
            node.GetMeta("opennv_nif_source_name", "").AsString() != indexed.Name)
            throw new NotSupportedException("Sound node lost its retained original source block.");
        RequireSourceAncestry(node);
    }

    private void RequireSourceAncestry(Node3D node)
    {
        for (Node? parent = node; parent is not null; parent = parent.GetParent())
        {
            if (parent == _root) return;
            if (parent.HasMeta("opennv_source_model") || parent.HasMeta("opennv_nif_source_root_blocks"))
                throw new NotSupportedException("Nested source models require their independent sound continuation binding.");
        }
        throw new NotSupportedException("Sound node left its actual original reference instance.");
    }

    private static IEnumerable<Node3D> SourceNodes(Node3D root)
    {
        var pending = new Stack<Node>(); pending.Push(root);
        while (pending.TryPop(out var node))
        {
            // A separate model/animation assembly has its own source identity.
            // Do not inspect or index its subtree under this model's blocks.
            if (node != root && (node.HasMeta("opennv_source_model") || node.HasMeta("opennv_nif_source_root_blocks"))) continue;
            if (node is Node3D sourceNode) yield return sourceNode;
            var children = node.GetChildren();
            for (var index = children.Count - 1; index >= 0; index--) pending.Push(children[index]);
        }
    }
}
